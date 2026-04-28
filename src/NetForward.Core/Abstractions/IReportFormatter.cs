using NetForward.Core.Models;

namespace NetForward.Core.Abstractions;

/// <summary>
/// Renders a <see cref="SolutionAnalysisResult"/> to a specific output format.
/// One implementation per format (JSON, Markdown, Word, HTML).
/// </summary>
public interface IReportFormatter
{
    /// <summary>Stable identifier for this formatter, e.g. "json", "markdown".</summary>
    string FormatId { get; }

    /// <summary>File extension produced (without leading dot), e.g. "json".</summary>
    string FileExtension { get; }

    /// <summary>Human-readable display name, e.g. "JSON".</summary>
    string DisplayName { get; }

    /// <summary>
    /// Write the report to the given output path.
    /// </summary>
    Task WriteAsync(
        SolutionAnalysisResult result,
        string outputPath,
        CancellationToken cancellationToken = default);
}
