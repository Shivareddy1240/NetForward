using NetForward.Core.Models;

namespace NetForward.Analyzer.Scoring;

/// <summary>
/// Computes a 0-100 readiness score from a list of issues.
/// Lower-severity issues subtract few points; blockers subtract many.
/// The formula is intentionally simple so reports can show the math.
/// </summary>
internal static class ReadinessScorer
{
    private static readonly Dictionary<IssueSeverity, int> SeverityWeights = new()
    {
        [IssueSeverity.Info] = 0,
        [IssueSeverity.AutoFixable] = 1,
        [IssueSeverity.Warning] = 3,
        [IssueSeverity.Error] = 8,
        [IssueSeverity.Blocker] = 20
    };

    /// <summary>
    /// Score for a single project. Returns 100 if no issues.
    /// </summary>
    public static int ScoreProject(IReadOnlyList<MigrationIssue> issues)
    {
        if (issues.Count == 0) return 100;

        var penalty = issues.Sum(i => SeverityWeights.TryGetValue(i.Severity, out var w) ? w : 0);
        return Math.Max(0, 100 - penalty);
    }

    /// <summary>
    /// Solution-level score: weighted average across projects, with an additional
    /// penalty for any blocker found anywhere.
    /// </summary>
    public static int ScoreSolution(IReadOnlyList<ProjectInfo> projects, IReadOnlyList<MigrationIssue> solutionIssues)
    {
        if (projects.Count == 0) return 0;

        var perProjectScores = projects.Select(p => ScoreProject(p.Issues)).ToList();
        var average = (int)Math.Round(perProjectScores.Average());

        var solutionPenalty = solutionIssues.Sum(i => SeverityWeights.TryGetValue(i.Severity, out var w) ? w : 0);

        return Math.Max(0, average - solutionPenalty);
    }
}
