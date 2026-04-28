using System.Xml.Linq;

namespace NetForward.Analyzer.Parsing;

/// <summary>
/// Parses both legacy (verbose, ToolsVersion-based) and SDK-style csproj files.
/// Returns a normalized view of just the facts the analyzer needs.
/// </summary>
public static class CsprojParser
{
    public static ParsedCsproj Parse(string projectFilePath)
    {
        if (!File.Exists(projectFilePath))
        {
            throw new FileNotFoundException($"Project file not found: {projectFilePath}", projectFilePath);
        }

        var doc = XDocument.Load(projectFilePath);
        var root = doc.Root ?? throw new InvalidOperationException($"Empty project file: {projectFilePath}");

        // SDK-style projects have an Sdk attribute on the root <Project> element.
        var isSdkStyle = root.Attribute("Sdk") is not null
            || root.Element(XmlNs("Project") + "Sdk") is not null;

        // Legacy projects have an xmlns; SDK-style usually don't. We handle both
        // by stripping namespace from element names when searching.
        var targetFramework = FindFirstValue(root, "TargetFramework")
            ?? FindFirstValue(root, "TargetFrameworkVersion")
            ?? FindFirstValue(root, "TargetFrameworks");

        var outputType = FindFirstValue(root, "OutputType");

        // Detect known project flags
        var useWindowsForms = ParseBool(FindFirstValue(root, "UseWindowsForms"));
        var useWpf = ParseBool(FindFirstValue(root, "UseWPF"));

        // Project SDK type (Microsoft.NET.Sdk, Microsoft.NET.Sdk.Web, etc.)
        var sdk = root.Attribute("Sdk")?.Value
            ?? root.Element(XmlNs("Project") + "Sdk")?.Attribute("Name")?.Value;

        // Collect package references (SDK-style) and project references
        var packageRefs = root
            .Descendants()
            .Where(e => string.Equals(e.Name.LocalName, "PackageReference", StringComparison.Ordinal))
            .Select(e => new ParsedPackageReference(
                Id: e.Attribute("Include")?.Value ?? "",
                Version: e.Attribute("Version")?.Value
                    ?? e.Element(e.Name.Namespace + "Version")?.Value
                    ?? ""))
            .Where(p => !string.IsNullOrWhiteSpace(p.Id))
            .ToList();

        var projectRefs = root
            .Descendants()
            .Where(e => string.Equals(e.Name.LocalName, "ProjectReference", StringComparison.Ordinal))
            .Select(e => e.Attribute("Include")?.Value ?? "")
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();

        // Detect signal references that imply project type for legacy projects
        // (legacy projects list every assembly via <Reference Include="..." />)
        var assemblyRefs = root
            .Descendants()
            .Where(e => string.Equals(e.Name.LocalName, "Reference", StringComparison.Ordinal))
            .Select(e => e.Attribute("Include")?.Value ?? "")
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(StripStrongName)
            .ToList();

        return new ParsedCsproj
        {
            ProjectFilePath = Path.GetFullPath(projectFilePath),
            IsSdkStyle = isSdkStyle,
            Sdk = sdk,
            TargetFramework = NormalizeTfm(targetFramework),
            OutputType = outputType,
            UseWindowsForms = useWindowsForms,
            UseWpf = useWpf,
            PackageReferences = packageRefs,
            ProjectReferences = projectRefs,
            AssemblyReferences = assemblyRefs
        };
    }

    private static string? FindFirstValue(XElement root, string localName)
    {
        return root
            .Descendants()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, localName, StringComparison.Ordinal))
            ?.Value;
    }

    private static bool ParseBool(string? raw)
        => bool.TryParse(raw, out var value) && value;

    /// <summary>
    /// Normalize legacy TargetFrameworkVersion values like "v4.8" to "net48".
    /// SDK-style values like "net8.0" pass through unchanged.
    /// </summary>
    private static string NormalizeTfm(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "unknown";

        var trimmed = raw.Trim();

        // Already SDK-style?
        if (trimmed.StartsWith("net", StringComparison.OrdinalIgnoreCase) && !trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.ToLowerInvariant();
        }

        // Legacy format: "v4.8" -> "net48", "v4.7.2" -> "net472"
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            var version = trimmed[1..].Replace(".", "");
            return $"net{version}";
        }

        return trimmed.ToLowerInvariant();
    }

    private static string StripStrongName(string assemblyRef)
    {
        // "System.Web.Mvc, Version=5.2.7.0, Culture=neutral, PublicKeyToken=..."  -> "System.Web.Mvc"
        var commaIndex = assemblyRef.IndexOf(',');
        return commaIndex >= 0 ? assemblyRef[..commaIndex].Trim() : assemblyRef.Trim();
    }

    private static XNamespace XmlNs(string _) => XNamespace.None;
}

public sealed record ParsedCsproj
{
    public required string ProjectFilePath { get; init; }
    public required bool IsSdkStyle { get; init; }
    public string? Sdk { get; init; }
    public required string TargetFramework { get; init; }
    public string? OutputType { get; init; }
    public bool UseWindowsForms { get; init; }
    public bool UseWpf { get; init; }
    public IReadOnlyList<ParsedPackageReference> PackageReferences { get; init; } = [];
    public IReadOnlyList<string> ProjectReferences { get; init; } = [];
    public IReadOnlyList<string> AssemblyReferences { get; init; } = [];
}

public sealed record ParsedPackageReference(string Id, string Version);
