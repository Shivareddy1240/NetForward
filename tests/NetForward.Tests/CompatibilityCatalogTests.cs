using FluentAssertions;
using NetForward.Compatibility;
using Xunit;

namespace NetForward.Tests;

public class CompatibilityCatalogTests
{
    [Fact]
    public void Loads_packages_from_yaml()
    {
        var catalog = new YamlCompatibilityCatalog();
        catalog.AllPackages.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("Microsoft.AspNet.Mvc", "Microsoft.AspNetCore.Mvc")]
    [InlineData("EntityFramework", "Microsoft.EntityFrameworkCore")]
    [InlineData("Newtonsoft.Json", "System.Text.Json")]
    public void Returns_modern_id_for_known_legacy_package(string legacy, string expectedModern)
    {
        var catalog = new YamlCompatibilityCatalog();
        var mapping = catalog.FindPackage(legacy);
        mapping.Should().NotBeNull();
        mapping!.ModernId.Should().Be(expectedModern);
    }

    [Fact]
    public void Flags_packages_removed_in_modern_dotnet()
    {
        var catalog = new YamlCompatibilityCatalog();
        var mapping = catalog.FindPackage("Microsoft.Owin");
        mapping.Should().NotBeNull();
        mapping!.RemovedInModern.Should().BeTrue();
    }

    [Fact]
    public void Returns_null_for_unknown_package()
    {
        var catalog = new YamlCompatibilityCatalog();
        catalog.FindPackage("Some.Unknown.Package.12345").Should().BeNull();
    }

    [Fact]
    public void Lookup_is_case_insensitive()
    {
        var catalog = new YamlCompatibilityCatalog();
        catalog.FindPackage("microsoft.aspnet.mvc").Should().NotBeNull();
        catalog.FindPackage("MICROSOFT.ASPNET.MVC").Should().NotBeNull();
    }

    [Fact]
    public void Loads_api_mappings()
    {
        var catalog = new YamlCompatibilityCatalog();
        catalog.AllApis.Should().NotBeEmpty();
        catalog.FindApi("System.Web.HttpContext").Should().NotBeNull();
    }
}
