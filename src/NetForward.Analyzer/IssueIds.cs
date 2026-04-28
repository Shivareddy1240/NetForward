namespace NetForward.Analyzer;

/// <summary>
/// Stable identifiers for every issue NetForward can raise. Format: NF### or NFC### (compatibility).
/// IDs are never reused. Add new ones at the end of each section.
/// </summary>
public static class IssueIds
{
    // Project structure (NF001-NF099)
    public const string LegacyCsprojFormat = "NF001";
    public const string PackagesConfigPresent = "NF002";
    public const string LegacyTargetFramework = "NF003";
    public const string MissingTargetFramework = "NF004";

    // Web configuration (NF100-NF199)
    public const string WebConfigPresent = "NF100";
    public const string GlobalAsaxPresent = "NF101";
    public const string CustomHttpModule = "NF102";
    public const string CustomHttpHandler = "NF103";

    // Package references (NF200-NF299)
    public const string LegacyPackageReference = "NF200";
    public const string RemovedInModernPackage = "NF201";

    // Project type specific (NF300-NF399)
    public const string WebFormsProject = "NF300";
    public const string WcfProject = "NF301";
    public const string AsmxProject = "NF302";
    public const string WinFormsProject = "NF303";
    public const string WpfProject = "NF304";
}
