using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetForward.Core.Models;

namespace NetForward.Rewriter.Pipeline;

/// <summary>
/// Tier classification for a rewrite rule.
/// Drives whether transformations are auto-applied or only flagged.
/// </summary>
public enum RewriteTier
{
    /// <summary>Auto-apply. >95% safe, no human review needed.</summary>
    Tier1 = 1,

    /// <summary>Apply with warning. 80-95% safe, surfaced in report for review.</summary>
    Tier2 = 2,

    /// <summary>Flag only. Too risky or complex to auto-transform; raises an issue with guidance.</summary>
    Tier3 = 3
}

/// <summary>
/// A single rewrite rule. Each rule is responsible for one specific AST-level transformation.
/// Rules are applied in ID order within each tier by the RewritePipeline.
/// </summary>
public interface IRewriteRule
{
    /// <summary>Stable rule ID (e.g. "R001"). Never reused.</summary>
    string RuleId { get; }

    /// <summary>Human-readable name shown in reports.</summary>
    string Name { get; }

    /// <summary>Which tier this rule belongs to.</summary>
    RewriteTier Tier { get; }

    /// <summary>
    /// Quick check: does this file likely need this rule applied?
    /// Used to skip the full Roslyn rewrite for files that clearly don't need it.
    /// Returning true doesn't guarantee changes will be made; false skips the rule entirely.
    /// </summary>
    bool IsApplicable(SyntaxTree syntaxTree);

    /// <summary>
    /// Apply the transformation. Returns a (possibly identical) new syntax tree,
    /// the list of transformations applied, and any issues raised.
    /// </summary>
    Task<RuleResult> ApplyAsync(
        RewriteContext context,
        SyntaxTree syntaxTree,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The result of applying a single rule to a single file.
/// </summary>
public sealed record RuleResult
{
    public required SyntaxTree OutputTree { get; init; }
    public IReadOnlyList<AppliedTransformation> Transformations { get; init; } = [];
    public IReadOnlyList<MigrationIssue> Issues { get; init; } = [];
}
