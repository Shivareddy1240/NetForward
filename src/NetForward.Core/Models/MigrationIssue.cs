namespace NetForward.Core.Models;

/// <summary>
/// A single finding produced by the analyzer. Every issue has a stable ID
/// (e.g. NF001) so reports are diff-able across runs and users can suppress them.
/// </summary>
public sealed record MigrationIssue
{
    /// <summary>Stable identifier (e.g. "NF001"). Never reused.</summary>
    public required string Id { get; init; }

    /// <summary>Short human-readable title.</summary>
    public required string Title { get; init; }

    /// <summary>Detailed explanation of what was found and why it matters.</summary>
    public required string Description { get; init; }

    public required IssueSeverity Severity { get; init; }

    public required IssueCategory Category { get; init; }

    /// <summary>Suggested remediation, if known.</summary>
    public string? Recommendation { get; init; }

    /// <summary>File path the issue was found in (relative to project root, if applicable).</summary>
    public string? FilePath { get; init; }

    /// <summary>1-based line number, if applicable.</summary>
    public int? LineNumber { get; init; }

    /// <summary>1-based column number, if applicable.</summary>
    public int? ColumnNumber { get; init; }

    /// <summary>Effort estimate in hours to manually resolve this issue.</summary>
    public double EffortHours { get; init; }

    /// <summary>Optional URL to documentation.</summary>
    public string? HelpUrl { get; init; }
}
