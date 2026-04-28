namespace NetForward.Core.Models;

/// <summary>
/// Aggregated facts about a single project after analysis.
/// </summary>
public sealed record ProjectInfo
{
    /// <summary>Project name (without extension).</summary>
    public required string Name { get; init; }

    /// <summary>Absolute path to the .csproj file.</summary>
    public required string ProjectFilePath { get; init; }

    public required ProjectType Type { get; init; }

    /// <summary>Target framework moniker (e.g. "net48", "net8.0").</summary>
    public required string TargetFramework { get; init; }

    /// <summary>True if the .csproj uses the modern SDK-style format.</summary>
    public required bool IsSdkStyle { get; init; }

    /// <summary>True if a legacy packages.config file is present.</summary>
    public bool UsesPackagesConfig { get; init; }

    /// <summary>True if a web.config file was detected.</summary>
    public bool HasWebConfig { get; init; }

    /// <summary>True if Global.asax was detected.</summary>
    public bool HasGlobalAsax { get; init; }

    /// <summary>NuGet packages referenced by this project.</summary>
    public IReadOnlyList<PackageReference> Packages { get; init; } = [];

    /// <summary>Project-to-project references (relative paths).</summary>
    public IReadOnlyList<string> ProjectReferences { get; init; } = [];

    /// <summary>Issues found during analysis.</summary>
    public IReadOnlyList<MigrationIssue> Issues { get; init; } = [];

    /// <summary>Total estimated manual effort in hours.</summary>
    public double EstimatedEffortHours => Issues.Sum(i => i.EffortHours);

    /// <summary>Recommended migration path, e.g. "ASP.NET Core MVC".</summary>
    public string? RecommendedTarget { get; init; }
}

/// <summary>
/// A single NuGet package reference, normalised across packages.config and PackageReference.
/// </summary>
public sealed record PackageReference
{
    public required string Id { get; init; }
    public required string Version { get; init; }

    /// <summary>True if this package is known to be deprecated or replaced in modern .NET.</summary>
    public bool IsLegacy { get; init; }

    /// <summary>Suggested replacement package, if any.</summary>
    public string? RecommendedReplacement { get; init; }
}
