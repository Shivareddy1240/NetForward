using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetForward.Compatibility;
using NetForward.Rewriter.Pipeline;

namespace NetForward.Rewriter.Tests;

/// <summary>
/// Test helpers for running individual rules against synthetic code snippets.
/// Uses CSharpSyntaxTree directly — no MSBuild, no disk I/O, runs in milliseconds.
/// </summary>
internal static class RuleTestHelpers
{
    /// <summary>
    /// Parse a C# source string into a SyntaxTree with a minimal compilation context.
    /// </summary>
    public static SyntaxTree ParseSource(string source)
    {
        return CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12));
    }

    /// <summary>
    /// Build a minimal Compilation from one or more syntax trees.
    /// Good enough for SemanticModel queries in rule unit tests.
    /// </summary>
    public static Compilation BuildCompilation(params SyntaxTree[] trees)
    {
        return CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: trees,
            references: GetBasicReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    /// <summary>
    /// Build a <see cref="RewriteContext"/> for unit testing.
    /// Uses NullAiAdvisor and the real compatibility catalog.
    /// </summary>
    public static RewriteContext BuildContext(SyntaxTree tree, RewriteOptions? options = null)
    {
        var compilation = BuildCompilation(tree);
        var catalog = new YamlCompatibilityCatalog();
        var opts = options ?? new RewriteOptions
        {
            DryRun = true,
            OutputRoot = Path.GetTempPath(),
            MaxTier = RewriteTier.Tier2
        };

        // Use the first project from a temporary workspace-less context.
        // For unit tests we construct a Project manually.
        var workspace = new AdhocWorkspace();
        var projectInfo = ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Create(),
            name: "TestProject",
            assemblyName: "TestProject",
            language: LanguageNames.CSharp);

        var project = workspace.AddProject(projectInfo);
        return new RewriteContext(project, compilation, catalog, opts);
    }

    /// <summary>
    /// Apply a single rule to a source string and return the rewritten source.
    /// </summary>
    public static async Task<(string Output, RuleResult Result)> ApplyRuleAsync(
        IRewriteRule rule,
        string source)
    {
        var tree = ParseSource(source);
        var context = BuildContext(tree);
        var result = await rule.ApplyAsync(context, tree);
        return (result.OutputTree.ToString(), result);
    }

    private static IReadOnlyList<MetadataReference> GetBasicReferences()
    {
        var assemblies = new[]
        {
            typeof(object).Assembly.Location,
            typeof(System.Collections.Generic.List<>).Assembly.Location,
            typeof(System.Linq.Enumerable).Assembly.Location,
        };

        return assemblies
            .Distinct()
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToList<MetadataReference>();
    }
}
