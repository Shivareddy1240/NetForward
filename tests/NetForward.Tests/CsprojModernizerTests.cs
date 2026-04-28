using FluentAssertions;
using NetForward.Analyzer;
using NetForward.Compatibility;
using NetForward.Modernizer;
using Xunit;

namespace NetForward.Tests;

public class CsprojModernizerTests
{
    [Fact]
    public async Task Produces_sdk_style_project_for_legacy_mvc()
    {
        var catalog = new YamlCompatibilityCatalog();
        var analyzer = new ProjectAnalyzer(catalog);
        var info = await analyzer.AnalyzeAsync(TestAssetPaths.LegacyMvcCsproj);

        var output = Path.Combine(Path.GetTempPath(), $"NetForwardTest_{Guid.NewGuid():N}.csproj");
        try
        {
            var modernizer = new CsprojModernizer();
            var result = modernizer.Modernize(TestAssetPaths.LegacyMvcCsproj, info.Type, output);

            result.ModernizedContent.Should().Contain("Microsoft.NET.Sdk.Web");
            result.ModernizedContent.Should().Contain("net8.0");
            result.ModernizedContent.Should().Contain("<PackageReference Include=\"EntityFramework\"");
            File.Exists(output).Should().BeTrue();
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [Fact]
    public async Task Includes_packages_from_packages_config_in_modernized_output()
    {
        var catalog = new YamlCompatibilityCatalog();
        var analyzer = new ProjectAnalyzer(catalog);
        var info = await analyzer.AnalyzeAsync(TestAssetPaths.LegacyMvcCsproj);

        var output = Path.Combine(Path.GetTempPath(), $"NetForwardTest_{Guid.NewGuid():N}.csproj");
        try
        {
            var modernizer = new CsprojModernizer();
            var result = modernizer.Modernize(TestAssetPaths.LegacyMvcCsproj, info.Type, output);

            result.ModernizedContent.Should().Contain("Newtonsoft.Json");
            result.ModernizedContent.Should().Contain("Microsoft.AspNet.WebApi");
            result.Notes.Should().Contain(n => n.Contains("packages.config"));
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }
}
