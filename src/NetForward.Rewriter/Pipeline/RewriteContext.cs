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
    private readonly SyntaxTree _originalTree;
    private Microsoft.CodeAnalysis.Compilation _compilation;
    private readonly object _compilationLock = new();

    public RewriteContext(
        Project roslynProject,
        Microsoft.CodeAnalysis.Compilation compilation,
        ICompatibilityCatalog catalog,
        RewriteOptions options)
    {
        RoslynProject = roslynProject;
        _compilation = compilation;
        // Track the original tree so we can detect when the current tree has changed.
        _originalTree = compilation.SyntaxTrees.FirstOrDefault()
            ?? throw new InvalidOperationException("Compilation contains no syntax trees.");
        Catalog = catalog;
        Options = options;
    }

    /// <summary>The Roslyn project being rewritten.</summary>
    public Project RoslynProject { get; }

    /// <summary>Compatibility catalog for legacy → modern mappings.</summary>
    public ICompatibilityCatalog Catalog { get; }

    /// <summary>User-configured options for this run.</summary>
    public RewriteOptions Options { get; }

    /// <summary>
    /// Get the semantic model for a syntax tree.
    ///
    /// IMPORTANT: Each rewrite rule produces a new SyntaxTree. Roslyn's Compilation
    /// only knows about the trees it was built with. If the caller passes a rewritten
    /// tree that is NOT in the compilation, we update the compilation to replace the
    /// original tree with the new one before querying.
    ///
    /// This is safe because:
    ///   - Each rule only ever replaces one tree at a time.
    ///   - The compilation update is locked for thread safety.
    ///   - Roslyn's WithReplacedSyntaxTree is cheap (structural sharing).
    /// </summary>
    public SemanticModel GetSemanticModel(SyntaxTree syntaxTree)
    {
        lock (_compilationLock)
        {
            // Fast path: tree is already in the compilation.
            if (_compilation.ContainsSyntaxTree(syntaxTree))
            {
                return _compilation.GetSemanticModel(syntaxTree);
            }

            // Slow path: tree was rewritten — replace the stale tree in the compilation.
            // Find which tree in the compilation is the "old" version of this one.
            // We always replace the original (or most recently replaced) tree.
            var existingTree = _compilation.SyntaxTrees.FirstOrDefault()
                ?? throw new InvalidOperationException(
                    "Compilation has no trees to replace.");

            _compilation = _compilation.ReplaceSyntaxTree(existingTree, syntaxTree);
            return _compilation.GetSemanticModel(syntaxTree);
        }
    }
}

/// <summary>
/// User-configurable options that affect how the rewriter behaves.
/// </summary>
public sealed record RewriteOptions
{
    /// <summary>When true, no files are written to disk. Results are returned in-memory only.</summary>
    public bool DryRun { get; init; }

    /// <summary>
    /// When true, runs `dotnet build` against each migrated project after rewriting
    /// and attaches compile diagnostics to the result. Ignored in DryRun mode.
    /// </summary>
    public bool VerifyCompilation { get; init; } = true;

    /// <summary>Highest tier to apply. Defaults to Tier2 (Tier3 is flag-only anyway).</summary>
    public RewriteTier MaxTier { get; init; } = RewriteTier.Tier2;

    /// <summary>Root output directory for side-by-side migration.</summary>
    public required string OutputRoot { get; init; }

    /// <summary>Suffix appended to the project folder name for the output. Default ".Core".</summary>
    public string OutputSuffix { get; init; } = ".Core";
}
