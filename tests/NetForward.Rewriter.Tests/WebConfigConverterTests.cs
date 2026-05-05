using FluentAssertions;
using NetForward.Converters.WebConfig;
using Xunit;

namespace NetForward.Rewriter.Tests;

public class WebConfigConverterTests : IDisposable
{
    private readonly string _tempDir;

    public WebConfigConverterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"NetForwardConverterTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
    }

    private string WriteWebConfig(string xml)
    {
        var path = Path.Combine(_tempDir, "Web.config");
        File.WriteAllText(path, xml);
        return path;
    }

    [Fact]
    public void Converts_appSettings_to_camelCase_json_properties()
    {
        var webConfig = WriteWebConfig(@"<?xml version=""1.0""?>
<configuration>
  <appSettings>
    <add key=""ApiBaseUrl"" value=""https://api.example.com"" />
    <add key=""MaxRetries"" value=""3"" />
  </appSettings>
</configuration>");

        var outDir = Path.Combine(_tempDir, "output");
        var result = new WebConfigConverter().Convert(webConfig, outDir);

        // Keys are camelCased: ApiBaseUrl → apiBaseUrl, MaxRetries → maxRetries
        result.AppSettingsContent.Should().Contain("apiBaseUrl");
        result.AppSettingsContent.Should().Contain("https://api.example.com");
        result.AppSettingsContent.Should().Contain("maxRetries");
        result.AppSettingsContent.Should().Contain("3");
        File.Exists(result.AppSettingsPath).Should().BeTrue();
    }

    [Fact]
    public void Converts_connectionStrings_keeping_PascalCase_key()
    {
        var webConfig = WriteWebConfig(@"<?xml version=""1.0""?>
<configuration>
  <connectionStrings>
    <add name=""DefaultConnection"" connectionString=""Server=.;Database=MyDb;Integrated Security=true"" />
  </connectionStrings>
</configuration>");

        var outDir = Path.Combine(_tempDir, "output");
        var result = new WebConfigConverter().Convert(webConfig, outDir);

        // "ConnectionStrings" stays PascalCase — IConfiguration.GetConnectionString() requires it.
        result.AppSettingsContent.Should().Contain("ConnectionStrings");
        result.AppSettingsContent.Should().Contain("DefaultConnection");
        result.DevSettingsContent.Should().Contain("DefaultConnection");
    }

    [Fact]
    public void Writes_both_appsettings_and_dev_settings_files()
    {
        var webConfig = WriteWebConfig(@"<?xml version=""1.0""?>
<configuration>
  <appSettings>
    <add key=""Foo"" value=""bar"" />
  </appSettings>
</configuration>");

        var outDir = Path.Combine(_tempDir, "output");
        var result = new WebConfigConverter().Convert(webConfig, outDir);

        File.Exists(result.AppSettingsPath).Should().BeTrue();
        File.Exists(result.DevSettingsPath).Should().BeTrue();
    }

    [Fact]
    public void Adds_TODO_stub_for_unknown_sections()
    {
        var webConfig = WriteWebConfig(@"<?xml version=""1.0""?>
<configuration>
  <myCustomSection foo=""bar"" />
</configuration>");

        var outDir = Path.Combine(_tempDir, "output");
        var result = new WebConfigConverter().Convert(webConfig, outDir);

        result.AppSettingsContent.Should().Contain("TODO_myCustomSection");
        result.Notes.Should().Contain(n => n.Contains("myCustomSection"));
    }

    [Fact]
    public void Notes_contain_migration_summary()
    {
        var webConfig = WriteWebConfig(@"<?xml version=""1.0""?>
<configuration>
  <appSettings>
    <add key=""Key1"" value=""val1"" />
  </appSettings>
</configuration>");

        var outDir = Path.Combine(_tempDir, "output");
        var result = new WebConfigConverter().Convert(webConfig, outDir);

        result.Notes.Should().Contain(n => n.Contains("appSettings"));
    }

    // ---- ToCamelCase unit tests ----

    [Theory]
    [InlineData("ApiBaseUrl", "apiBaseUrl")]
    [InlineData("MaxRetries", "maxRetries")]
    [InlineData("myKey", "myKey")]
    [InlineData("my_key", "myKey")]
    [InlineData("api_base_url", "apiBaseUrl")]
    [InlineData("foo", "foo")]
    [InlineData("Foo", "foo")]
    public void ToCamelCase_converts_correctly(string input, string expected)
    {
        WebConfigConverter.ToCamelCase(input).Should().Be(expected);
    }
}
