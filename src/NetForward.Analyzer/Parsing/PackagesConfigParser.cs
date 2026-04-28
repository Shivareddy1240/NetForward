using System.Xml.Linq;

namespace NetForward.Analyzer.Parsing;

/// <summary>
/// Parses the legacy packages.config file (pre-PackageReference NuGet format).
/// </summary>
public static class PackagesConfigParser
{
    public static IReadOnlyList<ParsedPackageReference> Parse(string packagesConfigPath)
    {
        if (!File.Exists(packagesConfigPath)) return Array.Empty<ParsedPackageReference>();

        try
        {
            var doc = XDocument.Load(packagesConfigPath);
            var root = doc.Root;
            if (root is null) return Array.Empty<ParsedPackageReference>();

            return root
                .Elements()
                .Where(e => string.Equals(e.Name.LocalName, "package", StringComparison.OrdinalIgnoreCase))
                .Select(e => new ParsedPackageReference(
                    Id: e.Attribute("id")?.Value ?? "",
                    Version: e.Attribute("version")?.Value ?? ""))
                .Where(p => !string.IsNullOrWhiteSpace(p.Id))
                .ToList();
        }
        catch (System.Xml.XmlException)
        {
            return Array.Empty<ParsedPackageReference>();
        }
    }

    /// <summary>
    /// Find the packages.config file alongside a project, if present.
    /// </summary>
    public static string? FindAlongside(string projectFilePath)
    {
        var directory = Path.GetDirectoryName(projectFilePath);
        if (directory is null) return null;

        var candidate = Path.Combine(directory, "packages.config");
        return File.Exists(candidate) ? candidate : null;
    }
}
