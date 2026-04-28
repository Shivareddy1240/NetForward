using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using NetForward.Core.Abstractions;
using NetForward.Core.Models;

namespace NetForward.Reporting.Formatters;

/// <summary>
/// Word (.docx) formatter built on DocumentFormat.OpenXml. Produces a
/// stakeholder-ready document with cover page, summary, project details, and issues.
/// </summary>
public sealed class WordReportFormatter : IReportFormatter
{
    public string FormatId => "word";
    public string FileExtension => "docx";
    public string DisplayName => "Word";

    public Task WriteAsync(SolutionAnalysisResult result, string outputPath, CancellationToken cancellationToken = default)
    {
        using var doc = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = mainPart.Document.AppendChild(new Body());

        AddStyles(mainPart);

        body.AppendChild(Heading("NetForward Migration Readiness Report", "Heading1"));
        body.AppendChild(Paragraph($"Solution: {result.SolutionPath}", italic: true));
        body.AppendChild(Paragraph($"Analyzed at: {result.AnalysedAtUtc:u}", italic: true));
        body.AppendChild(Paragraph($"Tool version: {result.ToolVersion}", italic: true));

        // Summary
        body.AppendChild(Heading("Summary", "Heading2"));
        body.AppendChild(Paragraph($"Readiness score: {result.ReadinessScore} / 100", bold: true));
        body.AppendChild(Paragraph($"Projects: {result.ProjectCount}"));
        body.AppendChild(Paragraph($"Total issues: {result.TotalIssueCount}"));
        body.AppendChild(Paragraph($"Blockers: {result.BlockerCount}"));
        body.AppendChild(Paragraph($"Estimated manual effort: {result.TotalEffortHours:F1} hours"));

        // Projects table
        body.AppendChild(Heading("Projects", "Heading2"));
        body.AppendChild(BuildProjectsTable(result.Projects));

        // Solution-level issues
        if (result.SolutionIssues.Count > 0)
        {
            body.AppendChild(Heading("Solution-level issues", "Heading2"));
            foreach (var issue in result.SolutionIssues.OrderByDescending(i => i.Severity))
            {
                AppendIssue(body, issue);
            }
        }

        // Per-project sections
        foreach (var project in result.Projects)
        {
            body.AppendChild(Heading(project.Name, "Heading2"));
            body.AppendChild(Paragraph($"Path: {project.ProjectFilePath}"));
            body.AppendChild(Paragraph($"Type: {project.Type}"));
            body.AppendChild(Paragraph($"Target framework: {project.TargetFramework}"));
            body.AppendChild(Paragraph($"SDK-style: {project.IsSdkStyle}"));
            if (project.RecommendedTarget is not null)
            {
                body.AppendChild(Paragraph($"Recommended target: {project.RecommendedTarget}"));
            }

            if (project.Issues.Count == 0)
            {
                body.AppendChild(Paragraph("No issues detected.", italic: true));
                continue;
            }

            body.AppendChild(Heading("Issues", "Heading3"));
            foreach (var issue in project.Issues.OrderByDescending(i => i.Severity))
            {
                AppendIssue(body, issue);
            }
        }

        mainPart.Document.Save();
        return Task.CompletedTask;
    }

    private static void AppendIssue(Body body, MigrationIssue issue)
    {
        body.AppendChild(Paragraph($"[{issue.Id}] {issue.Title}", bold: true));
        body.AppendChild(Paragraph($"Severity: {issue.Severity}  ·  Category: {issue.Category}  ·  Effort: {issue.EffortHours:F2}h"));
        if (issue.FilePath is not null)
        {
            body.AppendChild(Paragraph($"File: {issue.FilePath}", italic: true));
        }
        body.AppendChild(Paragraph(issue.Description));
        if (issue.Recommendation is not null)
        {
            body.AppendChild(Paragraph($"Recommendation: {issue.Recommendation}", italic: true));
        }
    }

    private static Table BuildProjectsTable(IReadOnlyList<ProjectInfo> projects)
    {
        var table = new Table();

        var props = new TableProperties(
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4U },
                new BottomBorder { Val = BorderValues.Single, Size = 4U },
                new LeftBorder { Val = BorderValues.Single, Size = 4U },
                new RightBorder { Val = BorderValues.Single, Size = 4U },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4U },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4U })
        );
        table.AppendChild(props);

        table.AppendChild(BuildRow(true, "Name", "Type", "Target", "SDK", "Issues", "Effort", "Recommended"));

        foreach (var project in projects)
        {
            table.AppendChild(BuildRow(false,
                project.Name,
                project.Type.ToString(),
                project.TargetFramework,
                project.IsSdkStyle ? "Yes" : "No",
                project.Issues.Count.ToString(),
                $"{project.EstimatedEffortHours:F1}h",
                project.RecommendedTarget ?? "-"));
        }

        return table;
    }

    private static TableRow BuildRow(bool header, params string[] cells)
    {
        var row = new TableRow();
        foreach (var cellText in cells)
        {
            var cell = new TableCell();
            var paragraph = Paragraph(cellText, bold: header);
            cell.AppendChild(paragraph);
            row.AppendChild(cell);
        }
        return row;
    }

    private static Paragraph Heading(string text, string styleId)
    {
        var paragraph = new Paragraph();
        var pPr = new ParagraphProperties();
        pPr.AppendChild(new ParagraphStyleId { Val = styleId });
        paragraph.AppendChild(pPr);
        paragraph.AppendChild(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        return paragraph;
    }

    private static Paragraph Paragraph(string text, bool bold = false, bool italic = false)
    {
        var paragraph = new Paragraph();
        var run = new Run();
        var rPr = new RunProperties();
        if (bold) rPr.AppendChild(new Bold());
        if (italic) rPr.AppendChild(new Italic());
        if (rPr.HasChildren) run.AppendChild(rPr);
        run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        paragraph.AppendChild(run);
        return paragraph;
    }

    private static void AddStyles(MainDocumentPart mainPart)
    {
        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        var styles = new Styles();

        styles.Append(BuildStyle("Heading1", "heading 1", 32, true));
        styles.Append(BuildStyle("Heading2", "heading 2", 28, true));
        styles.Append(BuildStyle("Heading3", "heading 3", 24, true));

        stylesPart.Styles = styles;
        stylesPart.Styles.Save();
    }

    private static Style BuildStyle(string id, string name, int halfPointSize, bool bold)
    {
        var style = new Style { Type = StyleValues.Paragraph, StyleId = id };
        style.Append(new StyleName { Val = name });
        style.Append(new BasedOn { Val = "Normal" });
        style.Append(new NextParagraphStyle { Val = "Normal" });
        var rPr = new StyleRunProperties();
        rPr.Append(new FontSize { Val = halfPointSize.ToString() });
        if (bold) rPr.Append(new Bold());
        style.Append(rPr);
        return style;
    }
}
