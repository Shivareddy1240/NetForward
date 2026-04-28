using NetForward.Core.Models;

namespace NetForward.Core.Abstractions;

/// <summary>
/// Looks up modern .NET equivalents for legacy APIs, packages, and config sections.
/// Implementation in NetForward.Compatibility.
/// </summary>
public interface ICompatibilityCatalog
{
    /// <summary>Find a mapping for a legacy NuGet package id, or null if unknown.</summary>
    PackageMapping? FindPackage(string legacyPackageId);

    /// <summary>Find a mapping for a legacy fully-qualified type name (e.g. "System.Web.HttpContext").</summary>
    ApiMapping? FindApi(string legacyFullTypeName);

    /// <summary>All package mappings (read-only).</summary>
    IReadOnlyCollection<PackageMapping> AllPackages { get; }

    /// <summary>All API mappings (read-only).</summary>
    IReadOnlyCollection<ApiMapping> AllApis { get; }
}

public sealed record PackageMapping
{
    public required string LegacyId { get; init; }
    public string? ModernId { get; init; }
    public string? Notes { get; init; }
    public bool RemovedInModern { get; init; }
    public IssueCategory Category { get; init; } = IssueCategory.PackageReference;
}

public sealed record ApiMapping
{
    public required string LegacyFullName { get; init; }
    public string? ModernFullName { get; init; }
    public string? Notes { get; init; }
    public IssueCategory Category { get; init; } = IssueCategory.AspNetApi;
}
