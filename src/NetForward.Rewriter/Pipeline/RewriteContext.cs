using Microsoft.CodeAnalysis;
using NetForward.Core.Abstractions;

namespace NetForward.Rewriter.Pipeline;

/// <summary>
/// Immutable context threaded through the entire rewrite pipeline for one project.
/// Carries the Roslyn workspace, the current compilation for semantic queries,
/// and the compatibility catalog for looking up legacy type mappings.
/// </summary>
public sealed class RewriteContext
{
    public RewriteContext(
        Project roslynProject,
        Compilation compilation,
        ICompatibilityCatalog catalog,
        RewriteOptions options)
    {
        RoslynProject = roslynProject;
        Compilation = compilation;
        Catalog = catalog;
        Options = options;
    }

    /// <summary>The Roslyn project being rewritten.</summary>
    public Project RoslynProject { get; }

    /// <summary>
    /// Pre-built compilation for semantic queries.
    /// Use this to get SemanticModel instances — do NOT call GetSemanticModelAsync
    /// per-file inside rules, as it's expensive. The pipeline pre-builds this once.
    /// </summary>
    public Compilation Compilation { get; }

    /// <summary>Compatibility catalog for legacy → modern mappings.</summary>
    public ICompatibilityCatalog Catalog { get; }

    /// <summary>User-configured options for this run.</summary>
    public RewriteOptions Options { get; }

    /// <summary>
    /// Get the semantic model for a specific syntax tree.
    /// Thread-safe; Roslyn caches this internally.
    /// </summary>
    public SemanticModel GetSemanticModel(SyntaxTree syntaxTree)
        => Compilation.GetSemanticModel(syntaxTree);
}

/// <summary>
/// User-configurable options that affect how the rewriter behaves.
/// </summary>
public sealed record RewriteOptions
{
    /// <summary>When true, no files are written to disk. Results are returned in-memory only.</summary>
    public bool DryRun { get; init; }

    /// <summary>Highest tier to apply. Defaults to Tier2 (Tier3 is flag-only anyway).</summary>
    public RewriteTier MaxTier { get; init; } = RewriteTier.Tier2;

    /// <summary>Root output directory for side-by-side migration.</summary>
    public required string OutputRoot { get; init; }

    /// <summary>Suffix appended to the project folder name for the output. Default ".Core".</summary>
    public string OutputSuffix { get; init; } = ".Core";
}
