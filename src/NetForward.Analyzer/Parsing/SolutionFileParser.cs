using System.Text.RegularExpressions;

namespace NetForward.Analyzer.Parsing;

/// <summary>
/// Lightweight .sln parser. Extracts only what we need (project entries) without
/// pulling in the full Microsoft.Build dependency tree.
/// </summary>
internal static class SolutionFileParser
{
    // Project("{TYPE-GUID}") = "Name", "relative\path\Project.csproj", "{PROJECT-GUID}"
    private static readonly Regex ProjectLineRegex = new(
        @"^Project\(""\{(?<typeGuid>[A-F0-9\-]+)\}""\)\s*=\s*""(?<projectName>[^""]+)""\s*,\s*""(?<path>[^""]+)""\s*,\s*""\{(?<projectGuid>[A-F0-9\-]+)\}""\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Solution folders use this type GUID and have no .csproj on disk.
    private const string SolutionFolderTypeGuid = "2150E333-8FDC-42A3-9474-1A3956D46DE8";

    public static IReadOnlyList<SolutionProjectEntry> Parse(string solutionPath)
    {
        if (!File.Exists(solutionPath))
        {
            throw new FileNotFoundException($"Solution file not found: {solutionPath}", solutionPath);
        }

        var solutionDirectory = Path.GetDirectoryName(Path.GetFullPath(solutionPath))
            ?? throw new InvalidOperationException("Could not determine solution directory.");

        var entries = new List<SolutionProjectEntry>();

        foreach (var line in File.ReadLines(solutionPath))
        {
            var match = ProjectLineRegex.Match(line);
            if (!match.Success) continue;

            var typeGuid = match.Groups["typeGuid"].Value;
            if (string.Equals(typeGuid, SolutionFolderTypeGuid, StringComparison.OrdinalIgnoreCase))
            {
                continue; // skip solution folders
            }

            var relative = match.Groups["path"].Value.Replace('\\', Path.DirectorySeparatorChar);
            var absolute = Path.GetFullPath(Path.Combine(solutionDirectory, relative));

            // Phase 1 only handles .csproj. Other types (.vbproj, .fsproj, .vcxproj)
            // get reported as unsupported in a later release.
            if (!absolute.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            entries.Add(new SolutionProjectEntry(
                Name: match.Groups["projectName"].Value,
                AbsolutePath: absolute,
                ProjectGuid: match.Groups["projectGuid"].Value));
        }

        return entries;
    }
}

internal sealed record SolutionProjectEntry(string Name, string AbsolutePath, string ProjectGuid);
