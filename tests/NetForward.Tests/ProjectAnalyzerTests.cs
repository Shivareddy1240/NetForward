using FluentAssertions;
using NetForward.Analyzer;
using NetForward.Compatibility;
using NetForward.Core.Models;
using Xunit;

namespace NetForward.Tests;

public class ProjectAnalyzerTests
{
    private static ProjectAnalyzer BuildAnalyzer() =>
        new(new YamlCompatibilityCatalog());

    [Fact]
    public async Task Detects_legacy_csproj_format()
    {
        var analyzer = BuildAnalyzer();
        var info = await analyzer.AnalyzeAsync(TestAssetPaths.LegacyMvcCsproj);

        info.IsSdkStyle.Should().BeFalse();
        info.Issues.Should().Contain(i => i.Id == IssueIds.LegacyCsprojFormat);
    }

    [Fact]
    public async Task Classifies_legacy_mvc_app()
    {
        var analyzer = BuildAnalyzer();
        var info = await analyzer.AnalyzeAsync(TestAssetPaths.LegacyMvcCsproj);
        info.Type.Should().Be(ProjectType.AspNetMvc);
    }

    [Fact]
    public async Task Normalizes_legacy_target_framework_version()
    {
        var analyzer = BuildAnalyzer();
        var info = await analyzer.AnalyzeAsync(TestAssetPaths.LegacyMvcCsproj);
        info.TargetFramework.Should().Be("net48");
    }

    [Fact]
    public async Task Detects_packages_config_and_loads_packages()
    {
        var analyzer = BuildAnalyzer();
        var info = await analyzer.AnalyzeAsync(TestAssetPaths.LegacyMvcCsproj);

        info.UsesPackagesConfig.Should().BeTrue();
        info.Packages.Should().Contain(p => p.Id == "EntityFramework");
        info.Packages.Should().Contain(p => p.Id == "Microsoft.AspNet.Mvc");
        info.Issues.Should().Contain(i => i.Id == IssueIds.PackagesConfigPresent);
    }

    [Fact]
    public async Task Flags_legacy_packages()
    {
        var analyzer = BuildAnalyzer();
        var info = await analyzer.AnalyzeAsync(TestAssetPaths.LegacyMvcCsproj);

        var entityFramework = info.Packages.Single(p => p.Id == "EntityFramework");
        entityFramework.IsLegacy.Should().BeTrue();
        entityFramework.RecommendedReplacement.Should().Be("Microsoft.EntityFrameworkCore");
    }

    [Fact]
    public async Task Detects_web_config_and_global_asax()
    {
        var analyzer = BuildAnalyzer();
        var info = await analyzer.AnalyzeAsync(TestAssetPaths.LegacyMvcCsproj);

        info.HasWebConfig.Should().BeTrue();
        info.HasGlobalAsax.Should().BeTrue();
        info.Issues.Should().Contain(i => i.Id == IssueIds.WebConfigPresent);
        info.Issues.Should().Contain(i => i.Id == IssueIds.GlobalAsaxPresent);
    }

    [Fact]
    public async Task Recommends_aspnet_core_target()
    {
        var analyzer = BuildAnalyzer();
        var info = await analyzer.AnalyzeAsync(TestAssetPaths.LegacyMvcCsproj);
        info.RecommendedTarget.Should().Contain("ASP.NET Core MVC");
    }

    [Fact]
    public async Task Computes_positive_effort_estimate()
    {
        var analyzer = BuildAnalyzer();
        var info = await analyzer.AnalyzeAsync(TestAssetPaths.LegacyMvcCsproj);
        info.EstimatedEffortHours.Should().BeGreaterThan(0);
    }
}
