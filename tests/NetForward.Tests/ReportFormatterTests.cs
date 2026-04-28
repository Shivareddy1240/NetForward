using FluentAssertions;
using NetForward.Analyzer;
using NetForward.Compatibility;
using NetForward.Reporting.Formatters;
using Xunit;

namespace NetForward.Tests;

public class ReportFormatterTests : IAsyncLifetime
{
    private string _outDir = "";

    public Task InitializeAsync()
    {
        _outDir = Path.Combine(Path.GetTempPath(), $"NetForwardReports_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_outDir);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_outDir)) Directory.Delete(_outDir, recursive: true);
        return Task.CompletedTask;
    }

    private async Task<NetForward.Core.Models.SolutionAnalysisResult> GetSampleResultAsync()
    {
        var catalog = new YamlCompatibilityCatalog();
        var analyzer = new SolutionAnalyzer(new ProjectAnalyzer(catalog));
        return await analyzer.AnalyzeAsync(TestAssetPaths.LegacyMvcSolution);
    }

    [Fact]
    public async Task Json_formatter_produces_valid_json()
    {
        var result = await GetSampleResultAsync();
        var path = Path.Combine(_outDir, "report.json");

        var formatter = new JsonReportFormatter();
        await formatter.WriteAsync(result, path);

        File.Exists(path).Should().BeTrue();
        var contents = await File.ReadAllTextAsync(path);
        contents.Should().Contain("LegacyMvcApp");
        contents.Should().Contain("\"readinessScore\"");

        // Round-trip parse to verify JSON validity
        using var doc = System.Text.Json.JsonDocument.Parse(contents);
        doc.RootElement.GetProperty("projects").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Markdown_formatter_produces_readable_output()
    {
        var result = await GetSampleResultAsync();
        var path = Path.Combine(_outDir, "report.md");

        var formatter = new MarkdownReportFormatter();
        await formatter.WriteAsync(result, path);

        var contents = await File.ReadAllTextAsync(path);
        contents.Should().Contain("# NetForward Migration Readiness Report");
        contents.Should().Contain("LegacyMvcApp");
        contents.Should().Contain("Readiness score");
    }

    [Fact]
    public async Task Html_formatter_produces_self_contained_document()
    {
        var result = await GetSampleResultAsync();
        var path = Path.Combine(_outDir, "report.html");

        var formatter = new HtmlReportFormatter();
        await formatter.WriteAsync(result, path);

        var contents = await File.ReadAllTextAsync(path);
        contents.Should().StartWith("<!DOCTYPE html>");
        contents.Should().Contain("<style>");
        contents.Should().Contain("LegacyMvcApp");
    }

    [Fact]
    public async Task Word_formatter_produces_valid_docx()
    {
        var result = await GetSampleResultAsync();
        var path = Path.Combine(_outDir, "report.docx");

        var formatter = new WordReportFormatter();
        await formatter.WriteAsync(result, path);

        File.Exists(path).Should().BeTrue();
        new FileInfo(path).Length.Should().BeGreaterThan(1000);

        // Sanity check: docx is a zip with [Content_Types].xml at the root
        using var zip = System.IO.Compression.ZipFile.OpenRead(path);
        zip.Entries.Should().Contain(e => e.FullName == "[Content_Types].xml");
    }
}
