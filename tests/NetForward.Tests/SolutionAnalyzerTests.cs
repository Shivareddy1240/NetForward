using FluentAssertions;
using NetForward.Analyzer;
using NetForward.Compatibility;
using NetForward.Core.Models;
using Xunit;

namespace NetForward.Tests;

public class SolutionAnalyzerTests
{
    private static SolutionAnalyzer BuildAnalyzer()
    {
        var catalog = new YamlCompatibilityCatalog();
        var projectAnalyzer = new ProjectAnalyzer(catalog);
        return new SolutionAnalyzer(projectAnalyzer);
    }

    [Fact]
    public async Task Analyzes_legacy_solution_end_to_end()
    {
        var analyzer = BuildAnalyzer();
        var result = await analyzer.AnalyzeAsync(TestAssetPaths.LegacyMvcSolution);

        result.Projects.Should().HaveCount(1);
        result.Projects[0].Type.Should().Be(ProjectType.AspNetMvc);
        result.SolutionIssues.Should().BeEmpty();
    }

    [Fact]
    public async Task Computes_readiness_score_below_perfect_for_legacy_solution()
    {
        var analyzer = BuildAnalyzer();
        var result = await analyzer.AnalyzeAsync(TestAssetPaths.LegacyMvcSolution);

        result.ReadinessScore.Should().BeInRange(0, 99);
    }

    [Fact]
    public async Task Reports_total_issue_count_matching_per_project_sum()
    {
        var analyzer = BuildAnalyzer();
        var result = await analyzer.AnalyzeAsync(TestAssetPaths.LegacyMvcSolution);

        var expected = result.Projects.Sum(p => p.Issues.Count) + result.SolutionIssues.Count;
        result.TotalIssueCount.Should().Be(expected);
    }

    [Fact]
    public async Task Stamps_tool_version_and_timestamp()
    {
        var analyzer = BuildAnalyzer();
        var result = await analyzer.AnalyzeAsync(TestAssetPaths.LegacyMvcSolution);

        result.ToolVersion.Should().NotBeNullOrEmpty();
        result.AnalysedAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }
}
