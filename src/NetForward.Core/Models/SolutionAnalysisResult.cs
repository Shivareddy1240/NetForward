namespace NetForward.Core.Models;

/// <summary>
/// Top-level result of analyzing a solution. This is what the reporters consume.
/// </summary>
public sealed record SolutionAnalysisResult
{
    /// <summary>Path to the solution file analysed.</summary>
    public required string SolutionPath { get; init; }

    /// <summary>UTC timestamp the analysis completed.</summary>
    public required DateTimeOffset AnalysedAtUtc { get; init; }

    /// <summary>Version of NetForward that produced this report.</summary>
    public required string ToolVersion { get; init; }

    /// <summary>Per-project analysis results.</summary>
    public required IReadOnlyList<ProjectInfo> Projects { get; init; }

    /// <summary>Solution-level issues that don't belong to any single project.</summary>
    public IReadOnlyList<MigrationIssue> SolutionIssues { get; init; } = [];

    public int ProjectCount => Projects.Count;
    public int TotalIssueCount => Projects.Sum(p => p.Issues.Count) + SolutionIssues.Count;
    public double TotalEffortHours => Projects.Sum(p => p.EstimatedEffortHours);

    public int BlockerCount => Projects.SelectMany(p => p.Issues)
        .Concat(SolutionIssues)
        .Count(i => i.Severity == IssueSeverity.Blocker);

    /// <summary>
    /// Overall readiness score from 0 (cannot migrate) to 100 (already modern).
    /// Computed transparently from issue weights so the report can show the math.
    /// </summary>
    public int ReadinessScore { get; init; }
}
