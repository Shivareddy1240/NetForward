using System.Text;
using NetForward.Core.Abstractions;
using NetForward.Core.Models;

namespace NetForward.Reporting.Formatters;

/// <summary>
/// Markdown formatter. Produces a human-readable report suitable for committing
/// alongside the source repo or pasting into a PR description.
/// </summary>
public sealed class MarkdownReportFormatter : IReportFormatter
{
    public string FormatId => "markdown";
    public string FileExtension => "md";
    public string DisplayName => "Markdown";

    public async Task WriteAsync(SolutionAnalysisResult result, string outputPath, CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# NetForward Migration Readiness Report");
        sb.AppendLine();
        sb.AppendLine($"**Solution**: `{result.SolutionPath}`  ");
        sb.AppendLine($"**Analyzed**: {result.AnalysedAtUtc:u}  ");
        sb.AppendLine($"**Tool version**: {result.ToolVersion}");
        sb.AppendLine();

        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"- **Readiness score**: **{result.ReadinessScore}/100**");
        sb.AppendLine($"- **Projects**: {result.ProjectCount}");
        sb.AppendLine($"- **Total issues**: {result.TotalIssueCount}");
        sb.AppendLine($"- **Blockers**: {result.BlockerCount}");
        sb.AppendLine($"- **Estimated manual effort**: {result.TotalEffortHours:F1} hours");
        sb.AppendLine();

        sb.AppendLine("## Projects");
        sb.AppendLine();
        sb.AppendLine("| Name | Type | Target Framework | SDK-style | Issues | Effort (h) | Recommended Target |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var project in result.Projects)
        {
            sb.AppendLine($"| {Escape(project.Name)} | {project.Type} | `{project.TargetFramework}` | {(project.IsSdkStyle ? "Yes" : "No")} | {project.Issues.Count} | {project.EstimatedEffortHours:F1} | {Escape(project.RecommendedTarget ?? "-")} |");
        }
        sb.AppendLine();

        if (result.SolutionIssues.Count > 0)
        {
            sb.AppendLine("## Solution-level issues");
            sb.AppendLine();
            WriteIssues(sb, result.SolutionIssues);
        }

        foreach (var project in result.Projects)
        {
            sb.AppendLine($"## {Escape(project.Name)}");
            sb.AppendLine();
            sb.AppendLine($"- **Path**: `{project.ProjectFilePath}`");
            sb.AppendLine($"- **Type**: {project.Type}");
            sb.AppendLine($"- **Target Framework**: `{project.TargetFramework}`");
            sb.AppendLine($"- **SDK-style**: {project.IsSdkStyle}");
            sb.AppendLine($"- **packages.config**: {project.UsesPackagesConfig}");
            sb.AppendLine($"- **Web.config**: {project.HasWebConfig}");
            sb.AppendLine($"- **Global.asax**: {project.HasGlobalAsax}");
            if (project.RecommendedTarget is not null)
            {
                sb.AppendLine($"- **Recommended target**: {project.RecommendedTarget}");
            }
            sb.AppendLine();

            if (project.Packages.Any(p => p.IsLegacy))
            {
                sb.AppendLine("### Legacy packages");
                sb.AppendLine();
                sb.AppendLine("| Package | Version | Recommended replacement |");
                sb.AppendLine("|---|---|---|");
                foreach (var pkg in project.Packages.Where(p => p.IsLegacy))
                {
                    sb.AppendLine($"| `{Escape(pkg.Id)}` | {Escape(pkg.Version)} | {Escape(pkg.RecommendedReplacement ?? "(none)")} |");
                }
                sb.AppendLine();
            }

            if (project.Issues.Count > 0)
            {
                sb.AppendLine("### Issues");
                sb.AppendLine();
                WriteIssues(sb, project.Issues);
            }
            else
            {
                sb.AppendLine("_No issues detected._");
                sb.AppendLine();
            }
        }

        await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8, cancellationToken);
    }

    private static void WriteIssues(StringBuilder sb, IReadOnlyList<MigrationIssue> issues)
    {
        foreach (var issue in issues.OrderByDescending(i => i.Severity))
        {
            sb.AppendLine($"#### `{issue.Id}` — {Escape(issue.Title)}");
            sb.AppendLine();
            sb.AppendLine($"**Severity**: {issue.Severity}  ");
            sb.AppendLine($"**Category**: {issue.Category}  ");
            sb.AppendLine($"**Effort**: {issue.EffortHours:F2} h");
            if (issue.FilePath is not null)
            {
                sb.AppendLine($"  ");
                sb.AppendLine($"**File**: `{issue.FilePath}`");
            }
            sb.AppendLine();
            sb.AppendLine(Escape(issue.Description));
            sb.AppendLine();
            if (issue.Recommendation is not null)
            {
                sb.AppendLine($"> **Recommendation**: {Escape(issue.Recommendation)}");
                sb.AppendLine();
            }
        }
    }

    private static string Escape(string s) => s.Replace("|", "\\|");
}
