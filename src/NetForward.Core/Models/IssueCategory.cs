namespace NetForward.Core.Models;

/// <summary>
/// High-level category an issue belongs to. Used for grouping in reports and filtering.
/// </summary>
public enum IssueCategory
{
    Unknown = 0,
    ProjectStructure,
    TargetFramework,
    PackageReference,
    WebConfiguration,
    AppConfiguration,
    AspNetApi,
    EntityFramework,
    Identity,
    Wcf,
    Logging,
    DependencyInjection,
    UiTechnology,
    ThirdPartyLibrary,
    BuildSystem
}
