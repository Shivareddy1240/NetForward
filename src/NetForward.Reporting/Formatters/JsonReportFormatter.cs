using System.Text.Json;
using System.Text.Json.Serialization;
using NetForward.Core.Abstractions;
using NetForward.Core.Models;

namespace NetForward.Reporting.Formatters;

/// <summary>
/// JSON formatter. Output is the canonical machine-readable form of the analysis result.
/// Uses camelCase for property names, matching common JSON conventions and what the
/// dashboard and any downstream consumer would expect.
/// </summary>
public sealed class JsonReportFormatter : IReportFormatter
{
    public string FormatId => "json";
    public string FileExtension => "json";
    public string DisplayName => "JSON";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task WriteAsync(SolutionAnalysisResult result, string outputPath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.Create(outputPath);
        await JsonSerializer.SerializeAsync(stream, result, Options, cancellationToken);
    }
}
