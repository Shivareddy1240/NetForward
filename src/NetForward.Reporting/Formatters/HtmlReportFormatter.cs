using System.Net;
using System.Text;
using NetForward.Core.Abstractions;
using NetForward.Core.Models;

namespace NetForward.Reporting.Formatters;

/// <summary>
/// HTML formatter. Produces a self-contained single-file dashboard
/// (no external dependencies) suitable for sharing with stakeholders.
/// </summary>
public sealed class HtmlReportFormatter : IReportFormatter
{
    public string FormatId => "html";
    public string FileExtension => "html";
    public string DisplayName => "HTML";

    public async Task WriteAsync(SolutionAnalysisResult result, string outputPath, CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\"><head>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine($"<title>NetForward Report — {Encode(Path.GetFileName(result.SolutionPath))}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine(EmbeddedCss);
        sb.AppendLine("</style></head><body>");

        sb.AppendLine("<header><h1>NetForward Migration Readiness Report</h1>");
        sb.AppendLine($"<p class=\"meta\">Solution: <code>{Encode(result.SolutionPath)}</code></p>");
        sb.AppendLine($"<p class=\"meta\">Analyzed at: {result.AnalysedAtUtc:u} &middot; Tool v{Encode(result.ToolVersion)}</p>");
        sb.AppendLine("</header>");

        sb.AppendLine("<section class=\"summary\">");
        sb.AppendLine($"<div class=\"score score-{ScoreClass(result.ReadinessScore)}\"><span>{result.ReadinessScore}</span><small>/ 100</small><p>Readiness</p></div>");
        sb.AppendLine($"<div class=\"stat\"><span>{result.ProjectCount}</span><p>Projects</p></div>");
        sb.AppendLine($"<div class=\"stat\"><span>{result.TotalIssueCount}</span><p>Issues</p></div>");
        sb.AppendLine($"<div class=\"stat stat-warn\"><span>{result.BlockerCount}</span><p>Blockers</p></div>");
        sb.AppendLine($"<div class=\"stat\"><span>{result.TotalEffortHours:F0}h</span><p>Est. effort</p></div>");
        sb.AppendLine("</section>");

        sb.AppendLine("<section><h2>Projects</h2><table><thead><tr><th>Name</th><th>Type</th><th>Target</th><th>SDK</th><th>Issues</th><th>Effort</th><th>Recommended</th></tr></thead><tbody>");
        foreach (var project in result.Projects)
        {
            sb.AppendLine($"<tr><td><a href=\"#{Encode(project.Name)}\">{Encode(project.Name)}</a></td><td>{project.Type}</td><td><code>{Encode(project.TargetFramework)}</code></td><td>{(project.IsSdkStyle ? "✓" : "—")}</td><td>{project.Issues.Count}</td><td>{project.EstimatedEffortHours:F1}h</td><td>{Encode(project.RecommendedTarget ?? "-")}</td></tr>");
        }
        sb.AppendLine("</tbody></table></section>");

        if (result.SolutionIssues.Count > 0)
        {
            sb.AppendLine("<section><h2>Solution-level issues</h2>");
            WriteIssues(sb, result.SolutionIssues);
            sb.AppendLine("</section>");
        }

        foreach (var project in result.Projects)
        {
            sb.AppendLine($"<section id=\"{Encode(project.Name)}\"><h2>{Encode(project.Name)}</h2>");
            sb.AppendLine("<dl class=\"facts\">");
            sb.AppendLine($"<dt>Path</dt><dd><code>{Encode(project.ProjectFilePath)}</code></dd>");
            sb.AppendLine($"<dt>Type</dt><dd>{project.Type}</dd>");
            sb.AppendLine($"<dt>Target Framework</dt><dd><code>{Encode(project.TargetFramework)}</code></dd>");
            sb.AppendLine($"<dt>SDK-style</dt><dd>{project.IsSdkStyle}</dd>");
            sb.AppendLine($"<dt>packages.config</dt><dd>{project.UsesPackagesConfig}</dd>");
            sb.AppendLine($"<dt>Web.config</dt><dd>{project.HasWebConfig}</dd>");
            sb.AppendLine($"<dt>Global.asax</dt><dd>{project.HasGlobalAsax}</dd>");
            if (project.RecommendedTarget is not null)
            {
                sb.AppendLine($"<dt>Recommended target</dt><dd>{Encode(project.RecommendedTarget)}</dd>");
            }
            sb.AppendLine("</dl>");

            if (project.Issues.Count > 0)
            {
                WriteIssues(sb, project.Issues);
            }
            else
            {
                sb.AppendLine("<p class=\"empty\">No issues detected.</p>");
            }
            sb.AppendLine("</section>");
        }

        sb.AppendLine("</body></html>");
        await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8, cancellationToken);
    }

    private static void WriteIssues(StringBuilder sb, IReadOnlyList<MigrationIssue> issues)
    {
        sb.AppendLine("<ul class=\"issues\">");
        foreach (var issue in issues.OrderByDescending(i => i.Severity))
        {
            sb.AppendLine($"<li class=\"issue sev-{issue.Severity.ToString().ToLowerInvariant()}\">");
            sb.AppendLine($"<header><code class=\"id\">{Encode(issue.Id)}</code> <strong>{Encode(issue.Title)}</strong> <span class=\"badge\">{issue.Severity}</span> <span class=\"badge cat\">{issue.Category}</span> <span class=\"effort\">{issue.EffortHours:F2}h</span></header>");
            sb.AppendLine($"<p>{Encode(issue.Description)}</p>");
            if (issue.Recommendation is not null)
            {
                sb.AppendLine($"<blockquote><strong>Recommendation:</strong> {Encode(issue.Recommendation)}</blockquote>");
            }
            if (issue.FilePath is not null)
            {
                sb.AppendLine($"<p class=\"path\"><code>{Encode(issue.FilePath)}</code></p>");
            }
            sb.AppendLine("</li>");
        }
        sb.AppendLine("</ul>");
    }

    private static string ScoreClass(int score) => score switch
    {
        >= 80 => "good",
        >= 50 => "warn",
        _ => "bad"
    };

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private const string EmbeddedCss = """
        :root {
          --bg: #f6f7f9;
          --panel: #ffffff;
          --ink: #1f2937;
          --muted: #6b7280;
          --border: #e5e7eb;
          --good: #16a34a;
          --warn: #d97706;
          --bad: #dc2626;
          --accent: #2563eb;
        }
        * { box-sizing: border-box; }
        body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", system-ui, sans-serif;
               margin: 0; background: var(--bg); color: var(--ink); line-height: 1.5; }
        header, section { max-width: 1100px; margin: 0 auto; padding: 24px; }
        header { border-bottom: 1px solid var(--border); background: var(--panel); }
        h1 { margin: 0 0 8px 0; font-size: 28px; }
        h2 { margin-top: 0; border-bottom: 1px solid var(--border); padding-bottom: 8px; }
        .meta { margin: 4px 0; color: var(--muted); font-size: 14px; }
        code { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; background: #f3f4f6; padding: 1px 6px; border-radius: 4px; font-size: 0.9em; }
        section { background: var(--panel); margin-top: 16px; border-radius: 8px; border: 1px solid var(--border); }
        .summary { display: grid; grid-template-columns: repeat(auto-fit, minmax(160px, 1fr)); gap: 16px; }
        .score, .stat { padding: 16px; border-radius: 8px; background: #fafafa; border: 1px solid var(--border); text-align: center; }
        .score span, .stat span { font-size: 36px; font-weight: 700; display: block; }
        .stat-warn span { color: var(--warn); }
        .score-good span { color: var(--good); }
        .score-warn span { color: var(--warn); }
        .score-bad span { color: var(--bad); }
        .score small { color: var(--muted); font-size: 14px; }
        .score p, .stat p { margin: 4px 0 0; color: var(--muted); font-size: 13px; text-transform: uppercase; letter-spacing: 0.5px; }
        table { width: 100%; border-collapse: collapse; margin-top: 8px; }
        th, td { text-align: left; padding: 10px 12px; border-bottom: 1px solid var(--border); }
        th { background: #fafafa; font-weight: 600; font-size: 13px; color: var(--muted); }
        a { color: var(--accent); text-decoration: none; }
        a:hover { text-decoration: underline; }
        dl.facts { display: grid; grid-template-columns: max-content 1fr; gap: 6px 16px; margin: 0 0 16px; }
        dl.facts dt { color: var(--muted); font-size: 13px; }
        dl.facts dd { margin: 0; }
        ul.issues { list-style: none; padding: 0; }
        li.issue { border-left: 4px solid var(--border); padding: 12px 16px; margin-bottom: 12px; background: #fafafa; border-radius: 0 6px 6px 0; }
        li.sev-blocker { border-left-color: var(--bad); }
        li.sev-error { border-left-color: var(--bad); }
        li.sev-warning { border-left-color: var(--warn); }
        li.sev-autofixable { border-left-color: var(--accent); }
        li.sev-info { border-left-color: var(--muted); }
        li.issue header { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; margin-bottom: 8px; }
        .id { font-weight: 600; }
        .badge { background: #e5e7eb; padding: 2px 8px; border-radius: 4px; font-size: 12px; text-transform: uppercase; letter-spacing: 0.5px; }
        .badge.cat { background: #dbeafe; color: #1e40af; }
        .effort { color: var(--muted); font-size: 13px; margin-left: auto; }
        blockquote { border-left: 3px solid var(--accent); margin: 8px 0; padding: 4px 12px; background: #eff6ff; }
        .path { color: var(--muted); font-size: 13px; }
        .empty { color: var(--muted); font-style: italic; }
        """;
}
