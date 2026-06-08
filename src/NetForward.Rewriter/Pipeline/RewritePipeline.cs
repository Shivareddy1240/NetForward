using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetForward.Core.Abstractions;
using NetForward.Rewriter.Verification;
using NetForward.Core.Models;
using NetForward.Rewriter.Workspace;

namespace NetForward.Rewriter.Pipeline;

/// <summary>
/// Orchestrates the full rewrite pipeline for a solution or individual project.
///
/// Flow per project:
///   1. Open with MSBuildWorkspace to get a Roslyn Project + Compilation.
///   2. Filter to C# source files (skip generated, designer, resource files).
///   3. Apply rules tier-by-tier, in rule-ID order within each tier.
///   4. Write modified files to the side-by-side output directory.
///   5. Collect FileRewriteResult for every file (modified or not).
///   6. Return a MigrationResult.
/// </summary>
public sealed class RewritePipeline
{
    private readonly IReadOnlyList<IRewriteRule> _rules;
    private readonly ICompatibilityCatalog _catalog;
    private readonly ILogger<RewritePipeline> _logger;

    public RewritePipeline(
        IEnumerable<IRewriteRule> rules,
        ICompatibilityCatalog catalog,
        ILogger<RewritePipeline>? logger = null)
    {
        // Rules are applied in tier order, then by ID within each tier.
        _rules = rules
            .OrderBy(r => r.Tier)
            .ThenBy(r => r.RuleId)
            .ToList();

        _catalog = catalog;
        _logger = logger ?? NullLogger<RewritePipeline>.Instance;
    }

    /// <summary>
    /// Migrate all eligible (MVC/WebApi) projects in the solution.
    /// </summary>
    public async Task<MigrationResult> MigrateSolutionAsync(
        string solutionPath,
        RewriteOptions options,
        CancellationToken cancellationToken = default)
    {
        using var loader = new MSBuildWorkspaceLoader(_logger as ILogger<MSBuildWorkspaceLoader>);
        var solution = await loader.OpenSolutionAsync(solutionPath, cancellationToken);

        // Only migrate C# projects that target a framework we can work with.
        var eligibleProjects = solution.Projects
            .Where(p => p.Language == LanguageNames.CSharp)
            .Where(p => !IsAlreadyModern(p))
            .ToList();

        _logger.LogInformation("Found {Count} eligible project(s) to migrate.", eligibleProjects.Count);

        var projectResults = new List<ProjectRewriteResult>();
        foreach (var project in eligibleProjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await MigrateProjectAsync(project, options, cancellationToken);
            projectResults.Add(result);
        }

        return new MigrationResult
        {
            SolutionPath = solutionPath,
            MigratedAtUtc = DateTimeOffset.UtcNow,
            ToolVersion = GetToolVersion(),
            IsDryRun = options.DryRun,
            Projects = projectResults
        };
    }

    /// <summary>
    /// Migrate a single Roslyn project. Used both internally and by tests.
    /// </summary>
    public async Task<ProjectRewriteResult> MigrateProjectAsync(
        Project roslynProject,
        RewriteOptions options,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Migrating project: {Name}", roslynProject.Name);

        var compilation = await roslynProject.GetCompilationAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Could not compile project: {roslynProject.Name}");

        var context = new RewriteContext(roslynProject, compilation, _catalog, options);

        var outputDirectory = ComputeOutputDirectory(roslynProject, options);
        var fileResults = new List<FileRewriteResult>();
        var projectIssues = new List<MigrationIssue>();

        foreach (var document in roslynProject.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsMigratable(document))
            {
                _logger.LogDebug("Skipping non-migratable file: {Path}", document.FilePath);
                continue;
            }

            var fileResult = await RewriteDocumentAsync(document, context, outputDirectory, cancellationToken);
            fileResults.Add(fileResult);
        }

        _logger.LogInformation(
            "Project {Name}: {Modified} file(s) modified, {Unchanged} unchanged, {Issues} remaining issue(s).",
            roslynProject.Name,
            fileResults.Count(f => f.WasModified),
            fileResults.Count(f => !f.WasModified),
            fileResults.Sum(f => f.RemainingIssues.Count));

        var projectResult = new ProjectRewriteResult
        {
            ProjectName = roslynProject.Name,
            OriginalProjectPath = roslynProject.FilePath ?? "",
            OutputDirectory = outputDirectory,
            Files = fileResults,
            ProjectLevelIssues = projectIssues
        };

        // Run compilation verification unless dry-run or disabled.
        if (!options.DryRun && options.VerifyCompilation)
        {
            var verifier = new MigrationCompilationVerifier(
                _logger as ILogger<MigrationCompilationVerifier>);
            var buildResult = await verifier.VerifyAsync(
                outputDirectory, cancellationToken);

            projectResult = projectResult with
            {
                CompiledSuccessfully = buildResult.Succeeded,
                CompilerOutput = buildResult.RawOutput,
                CompileErrors = buildResult.Diagnostics
            };
        }

        return projectResult;
    }

    private async Task<FileRewriteResult> RewriteDocumentAsync(
        Document document,
        RewriteContext context,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var originalTree = await document.GetSyntaxTreeAsync(cancellationToken)
            ?? throw new InvalidOperationException($"No syntax tree for {document.FilePath}");
        var originalText = (await originalTree.GetTextAsync(cancellationToken)).ToString();

        var currentTree = originalTree;
        var allTransformations = new List<AppliedTransformation>();
        var allIssues = new List<MigrationIssue>();

        foreach (var rule in _rules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Tier 3 rules never modify; they only raise issues.
            if (rule.Tier == RewriteTier.Tier3 && context.Options.MaxTier < RewriteTier.Tier3)
            {
                continue;
            }

            if (!rule.IsApplicable(currentTree))
            {
                continue;
            }

            try
            {
                var ruleResult = await rule.ApplyAsync(context, currentTree, cancellationToken);
                currentTree = ruleResult.OutputTree;
                allTransformations.AddRange(ruleResult.Transformations);
                allIssues.AddRange(ruleResult.Issues);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Rule {RuleId} failed on {FilePath}. Skipping rule.",
                    rule.RuleId, document.FilePath);

                allIssues.Add(new MigrationIssue
                {
                    Id = rule.RuleId,
                    Title = $"Rule {rule.RuleId} failed: {rule.Name}",
                    Description = $"The rewriter encountered an error: {ex.Message}. This file section was left unchanged.",
                    Severity = IssueSeverity.Warning,
                    Category = IssueCategory.ProjectStructure,
                    FilePath = document.FilePath,
                    EffortHours = 1.0
                });
            }
        }

        var rewrittenContent = currentTree.ToString();

        // Write to disk unless dry-run.
        string? outputPath = null;
        if (!context.Options.DryRun && allTransformations.Count > 0)
        {
            outputPath = ComputeOutputFilePath(document.FilePath!, outputDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllTextAsync(outputPath, rewrittenContent, Encoding.UTF8, cancellationToken);
            _logger.LogDebug("Wrote: {Path}", outputPath);
        }
        else if (!context.Options.DryRun && allTransformations.Count == 0)
        {
            // File unchanged — copy as-is so the output project is complete.
            outputPath = ComputeOutputFilePath(document.FilePath!, outputDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllTextAsync(outputPath, originalText, Encoding.UTF8, cancellationToken);
        }

        return new FileRewriteResult
        {
            OriginalPath = document.FilePath ?? "",
            OutputPath = outputPath,
            OriginalContent = originalText,
            RewrittenContent = rewrittenContent,
            Transformations = allTransformations,
            RemainingIssues = allIssues
        };
    }

    private static bool IsMigratable(Document document)
    {
        var path = document.FilePath;
        if (string.IsNullOrWhiteSpace(path)) return false;

        // Skip generated, designer, resource, and migration files.
        var fileName = Path.GetFileName(path);
        if (fileName.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)) return false;
        if (fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)) return false;
        if (fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase)) return false;
        if (fileName.Contains("Migration", StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            && path.Contains("Migrations", StringComparison.OrdinalIgnoreCase)) return false;

        return path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAlreadyModern(Project project)
    {
        // Heuristic: modern projects use net5+ TFMs. We can't easily read the TFM
        // from the Roslyn model, so we check compilation options as a proxy.
        // Projects that have already been migrated will typically not define
        // NETFRAMEWORK conditional symbols.
        return project.ParseOptions is Microsoft.CodeAnalysis.CSharp.CSharpParseOptions opts
            && !opts.PreprocessorSymbolNames.Any(s => s.StartsWith("NET4", StringComparison.Ordinal)
                                                   || s == "NETFRAMEWORK");
    }

    private static string ComputeOutputDirectory(Project project, RewriteOptions options)
    {
        var projectDir = Path.GetDirectoryName(project.FilePath)
            ?? throw new InvalidOperationException("Project has no file path.");
        var projectName = project.Name;
        return Path.Combine(options.OutputRoot, projectName + options.OutputSuffix);
    }

    private static string ComputeOutputFilePath(string originalFilePath, string outputDirectory)
    {
        // Preserve directory structure relative to project root.
        // We use the filename only for flat projects, but most real projects
        // have subdirectories (Controllers/, Models/, etc.) that need preserving.
        var fileName = Path.GetFileName(originalFilePath);
        return Path.Combine(outputDirectory, fileName);
    }

    private static string GetToolVersion()
    {
        var asm = typeof(RewritePipeline).Assembly;
        return asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? asm.GetName().Version?.ToString()
               ?? "0.0.0";
    }
}
