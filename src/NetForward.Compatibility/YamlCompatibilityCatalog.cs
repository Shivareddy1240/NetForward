using System.Reflection;
using NetForward.Core.Abstractions;
using NetForward.Core.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NetForward.Compatibility;

/// <summary>
/// Loads package and API mappings from embedded YAML resources.
/// Constructed once and queried many times; lookups are case-insensitive.
/// </summary>
public sealed class YamlCompatibilityCatalog : ICompatibilityCatalog
{
    private readonly Dictionary<string, PackageMapping> _packages;
    private readonly Dictionary<string, ApiMapping> _apis;

    public YamlCompatibilityCatalog()
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var packagesYaml = ReadEmbeddedResource("NetForward.Compatibility.Data.packages.yaml");
        var apisYaml = ReadEmbeddedResource("NetForward.Compatibility.Data.apis.yaml");

        var packageRoot = deserializer.Deserialize<PackageYamlRoot>(packagesYaml)
                          ?? new PackageYamlRoot();
        var apiRoot = deserializer.Deserialize<ApiYamlRoot>(apisYaml)
                      ?? new ApiYamlRoot();

        _packages = (packageRoot.Packages ?? new())
            .Select(p => new PackageMapping
            {
                LegacyId = p.LegacyId,
                ModernId = p.ModernId,
                Notes = p.Notes,
                RemovedInModern = p.RemovedInModern,
                Category = ParseCategory(p.Category, IssueCategory.PackageReference)
            })
            .ToDictionary(p => p.LegacyId, StringComparer.OrdinalIgnoreCase);

        _apis = (apiRoot.Apis ?? new())
            .Select(a => new ApiMapping
            {
                LegacyFullName = a.LegacyFullName,
                ModernFullName = a.ModernFullName,
                Notes = a.Notes,
                Category = ParseCategory(a.Category, IssueCategory.AspNetApi)
            })
            .ToDictionary(a => a.LegacyFullName, StringComparer.OrdinalIgnoreCase);
    }

    public PackageMapping? FindPackage(string legacyPackageId)
        => _packages.TryGetValue(legacyPackageId, out var m) ? m : null;

    public ApiMapping? FindApi(string legacyFullTypeName)
        => _apis.TryGetValue(legacyFullTypeName, out var m) ? m : null;

    public IReadOnlyCollection<PackageMapping> AllPackages => _packages.Values;
    public IReadOnlyCollection<ApiMapping> AllApis => _apis.Values;

    private static string ReadEmbeddedResource(string resourceName)
    {
        var asm = typeof(YamlCompatibilityCatalog).Assembly;
        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static IssueCategory ParseCategory(string? raw, IssueCategory fallback)
        => Enum.TryParse<IssueCategory>(raw, ignoreCase: true, out var parsed) ? parsed : fallback;

    // YAML DTOs --------------------------------------------------------------

    private sealed class PackageYamlRoot
    {
        public List<PackageYaml>? Packages { get; set; }
    }

    private sealed class PackageYaml
    {
        public string LegacyId { get; set; } = "";
        public string? ModernId { get; set; }
        public string? Notes { get; set; }
        public bool RemovedInModern { get; set; }
        public string? Category { get; set; }
    }

    private sealed class ApiYamlRoot
    {
        public List<ApiYaml>? Apis { get; set; }
    }

    private sealed class ApiYaml
    {
        public string LegacyFullName { get; set; } = "";
        public string? ModernFullName { get; set; }
        public string? Notes { get; set; }
        public string? Category { get; set; }
    }
}
