using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetForward.Analyzer.Parsing;
using NetForward.Analyzer.Scoring;
using NetForward.Core.Abstractions;
using NetForward.Core.Models;

namespace NetForward.Analyzer;

/// <summary>
/// Default solution analyzer. Loads the solution, analyzes each project in parallel,
/// and produces a complete <see cref="SolutionAnalysisResult"/>.
/// </summary>
public sealed class SolutionAnalyzer : ISolutionAnalyzer
{
    private readonly IProjectAnalyzer _projectAnalyzer;
    private readonly ILogger<SolutionAnalyzer> _logger;

    public SolutionAnalyzer(IProjectAnalyzer projectAnalyzer, ILogger<SolutionAnalyzer>? logger = null)
    {
        _projectAnalyzer = projectAnalyzer;
        _logger = logger ?? NullLogger<SolutionAnalyzer>.Instance;
    }

    public async Task<SolutionAnalysisResult> AnalyzeAsync(
        string solutionPath,
        CancellationToken cancellationToken = default)
    {
        var absoluteSolutionPath = Path.GetFullPath(solutionPath);
        _logger.LogInformation("Analyzing solution: {Path}", absoluteSolutionPath);

        var entries = SolutionFileParser.Parse(absoluteSolutionPath);
        _logger.LogInformation("Found {Count} project(s) in solution.", entries.Count);

        var projects = new List<ProjectInfo>(entries.Count);
        var solutionIssues = new List<MigrationIssue>();

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(entry.AbsolutePath))
            {
                _logger.LogWarning("Project file referenced by solution not found on disk: {Path}", entry.AbsolutePath);
                solutionIssues.Add(new MigrationIssue
                {
                    Id = "NF000",
                    Title = $"Project file not found: {entry.Name}",
                    Description = $"Solution references '{entry.AbsolutePath}' but the file does not exist on disk.",
                    Severity = IssueSeverity.Error,
                    Category = IssueCategory.ProjectStructure,
                    EffortHours = 0
                });
                continue;
            }

            try
            {
                var projectInfo = await _projectAnalyzer.AnalyzeAsync(entry.AbsolutePath, cancellationToken);
                projects.Add(projectInfo);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to analyze project: {Path}", entry.AbsolutePath);
                solutionIssues.Add(new MigrationIssue
                {
                    Id = "NF000",
                    Title = $"Failed to analyze project: {entry.Name}",
                    Description = $"Analyzer threw an exception: {ex.Message}",
                    Severity = IssueSeverity.Error,
                    Category = IssueCategory.ProjectStructure,
                    FilePath = entry.AbsolutePath,
                    EffortHours = 0
                });
            }
        }

        var readiness = ReadinessScorer.ScoreSolution(projects, solutionIssues);

        return new SolutionAnalysisResult
        {
            SolutionPath = absoluteSolutionPath,
            AnalysedAtUtc = DateTimeOffset.UtcNow,
            ToolVersion = GetToolVersion(),
            Projects = projects,
            SolutionIssues = solutionIssues,
            ReadinessScore = readiness
        };
    }

    private static string GetToolVersion()
    {
        var asm = typeof(SolutionAnalyzer).Assembly;
        return asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? asm.GetName().Version?.ToString()
               ?? "0.0.0";
    }
}
