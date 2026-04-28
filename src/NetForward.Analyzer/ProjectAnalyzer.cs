using NetForward.Analyzer.Classification;
using NetForward.Analyzer.Parsing;
using NetForward.Core.Abstractions;
using NetForward.Core.Models;

namespace NetForward.Analyzer;

/// <summary>
/// Default project analyzer. Examines a single .csproj and produces a <see cref="ProjectInfo"/>.
/// </summary>
public sealed class ProjectAnalyzer : IProjectAnalyzer
{
    private readonly ICompatibilityCatalog _catalog;

    public ProjectAnalyzer(ICompatibilityCatalog catalog)
    {
        _catalog = catalog;
    }

    public Task<ProjectInfo> AnalyzeAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var parsed = CsprojParser.Parse(projectPath);
        var projectType = ProjectClassifier.Classify(parsed);
        var issues = new List<MigrationIssue>();

        // Detect packages.config and merge package lists
        var packagesConfigPath = PackagesConfigParser.FindAlongside(projectPath);
        var usesPackagesConfig = packagesConfigPath is not null;
        var legacyPackages = usesPackagesConfig
            ? PackagesConfigParser.Parse(packagesConfigPath!)
            : Array.Empty<ParsedPackageReference>();

        var allPackages = parsed.PackageReferences.Concat(legacyPackages)
            .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        // Resolve packages against the compatibility catalog
        var resolvedPackages = allPackages
            .Select(p =>
            {
                var mapping = _catalog.FindPackage(p.Id);
                return new PackageReference
                {
                    Id = p.Id,
                    Version = p.Version,
                    IsLegacy = mapping is not null,
                    RecommendedReplacement = mapping?.ModernId
                };
            })
            .ToList();

        // === Issue detection ====================================================

        if (!parsed.IsSdkStyle && projectType != ProjectType.AlreadyModern)
        {
            issues.Add(new MigrationIssue
            {
                Id = IssueIds.LegacyCsprojFormat,
                Title = "Legacy (non-SDK-style) project format",
                Description = "This project uses the verbose pre-2017 csproj format. The modernizer can convert it to SDK-style format automatically.",
                Severity = IssueSeverity.AutoFixable,
                Category = IssueCategory.ProjectStructure,
                Recommendation = "Run `netforward modernize-csproj` to convert.",
                FilePath = parsed.ProjectFilePath,
                EffortHours = 0.5
            });
        }

        if (usesPackagesConfig)
        {
            issues.Add(new MigrationIssue
            {
                Id = IssueIds.PackagesConfigPresent,
                Title = "packages.config is present",
                Description = "This project uses the legacy packages.config format. Modern .NET projects use <PackageReference> in the csproj.",
                Severity = IssueSeverity.AutoFixable,
                Category = IssueCategory.ProjectStructure,
                Recommendation = "The modernizer migrates packages.config to PackageReference automatically.",
                FilePath = packagesConfigPath,
                EffortHours = 0.25
            });
        }

        if (IsLegacyTfm(parsed.TargetFramework))
        {
            issues.Add(new MigrationIssue
            {
                Id = IssueIds.LegacyTargetFramework,
                Title = $"Targets legacy framework ({parsed.TargetFramework})",
                Description = $"This project targets {parsed.TargetFramework}, which is .NET Framework. Modern .NET 8 is the recommended target.",
                Severity = IssueSeverity.Warning,
                Category = IssueCategory.TargetFramework,
                Recommendation = "Plan to retarget to net8.0 (or net8.0-windows for desktop).",
                FilePath = parsed.ProjectFilePath,
                EffortHours = 1.0
            });
        }

        DetectFileSystemMarkers(projectPath, issues, out var hasWebConfig, out var hasGlobalAsax);

        // Project-type-specific blockers
        switch (projectType)
        {
            case ProjectType.WebForms:
                issues.Add(new MigrationIssue
                {
                    Id = IssueIds.WebFormsProject,
                    Title = "Web Forms (.aspx) project",
                    Description = "ASP.NET Web Forms is not supported on modern .NET. Migration requires UI rewrite to MVC, Razor Pages, or Blazor.",
                    Severity = IssueSeverity.Blocker,
                    Category = IssueCategory.UiTechnology,
                    Recommendation = "Plan a UI rewrite. Blazor is the closest paradigm; MVC/Razor Pages is the closest server-rendered alternative.",
                    EffortHours = 40
                });
                break;

            case ProjectType.Wcf:
                issues.Add(new MigrationIssue
                {
                    Id = IssueIds.WcfProject,
                    Title = "WCF service",
                    Description = "WCF server-side hosting is not supported on modern .NET.",
                    Severity = IssueSeverity.Blocker,
                    Category = IssueCategory.Wcf,
                    Recommendation = "Use CoreWCF (community port) for SOAP compatibility, or migrate to gRPC / minimal APIs.",
                    EffortHours = 24
                });
                break;

            case ProjectType.WinForms:
                issues.Add(new MigrationIssue
                {
                    Id = IssueIds.WinFormsProject,
                    Title = "WinForms application",
                    Description = "WinForms runs on modern .NET (net8.0-windows) but the .csproj must be modernized first.",
                    Severity = IssueSeverity.Warning,
                    Category = IssueCategory.UiTechnology,
                    Recommendation = "Retarget to net8.0-windows with <UseWindowsForms>true</UseWindowsForms>.",
                    EffortHours = 4
                });
                break;

            case ProjectType.Wpf:
                issues.Add(new MigrationIssue
                {
                    Id = IssueIds.WpfProject,
                    Title = "WPF application",
                    Description = "WPF runs on modern .NET (net8.0-windows) but the .csproj must be modernized first.",
                    Severity = IssueSeverity.Warning,
                    Category = IssueCategory.UiTechnology,
                    Recommendation = "Retarget to net8.0-windows with <UseWPF>true</UseWPF>.",
                    EffortHours = 4
                });
                break;
        }

        // Per-package issues
        foreach (var pkg in resolvedPackages.Where(p => p.IsLegacy))
        {
            var mapping = _catalog.FindPackage(pkg.Id);
            if (mapping is null) continue;

            if (mapping.RemovedInModern)
            {
                issues.Add(new MigrationIssue
                {
                    Id = IssueIds.RemovedInModernPackage,
                    Title = $"Package '{pkg.Id}' has no modern equivalent",
                    Description = mapping.Notes ?? $"'{pkg.Id}' is not available on modern .NET.",
                    Severity = IssueSeverity.Error,
                    Category = mapping.Category,
                    Recommendation = mapping.Notes,
                    EffortHours = 4
                });
            }
            else
            {
                issues.Add(new MigrationIssue
                {
                    Id = IssueIds.LegacyPackageReference,
                    Title = $"Legacy package: {pkg.Id}",
                    Description = mapping.Notes ?? $"'{pkg.Id}' has a recommended modern replacement.",
                    Severity = IssueSeverity.Warning,
                    Category = mapping.Category,
                    Recommendation = pkg.RecommendedReplacement is not null
                        ? $"Replace with {pkg.RecommendedReplacement}."
                        : null,
                    EffortHours = 1.0
                });
            }
        }

        var info = new ProjectInfo
        {
            Name = Path.GetFileNameWithoutExtension(projectPath),
            ProjectFilePath = parsed.ProjectFilePath,
            Type = projectType,
            TargetFramework = parsed.TargetFramework,
            IsSdkStyle = parsed.IsSdkStyle,
            UsesPackagesConfig = usesPackagesConfig,
            HasWebConfig = hasWebConfig,
            HasGlobalAsax = hasGlobalAsax,
            Packages = resolvedPackages,
            ProjectReferences = parsed.ProjectReferences,
            Issues = issues,
            RecommendedTarget = RecommendTarget(projectType)
        };

        return Task.FromResult(info);
    }

    private static void DetectFileSystemMarkers(string projectPath, List<MigrationIssue> issues, out bool hasWebConfig, out bool hasGlobalAsax)
    {
        hasWebConfig = false;
        hasGlobalAsax = false;

        var dir = Path.GetDirectoryName(projectPath);
        if (dir is null) return;

        var webConfig = Path.Combine(dir, "Web.config");
        if (File.Exists(webConfig))
        {
            hasWebConfig = true;
            issues.Add(new MigrationIssue
            {
                Id = IssueIds.WebConfigPresent,
                Title = "Web.config present",
                Description = "Modern .NET uses appsettings.json + Program.cs configuration. Web.config sections must be migrated.",
                Severity = IssueSeverity.Warning,
                Category = IssueCategory.WebConfiguration,
                Recommendation = "The modernizer extracts <appSettings> and <connectionStrings> into appsettings.json automatically. Custom sections require manual review.",
                FilePath = webConfig,
                EffortHours = 2
            });
        }

        var globalAsax = Path.Combine(dir, "Global.asax");
        if (File.Exists(globalAsax))
        {
            hasGlobalAsax = true;
            issues.Add(new MigrationIssue
            {
                Id = IssueIds.GlobalAsaxPresent,
                Title = "Global.asax present",
                Description = "Application_Start, Session_Start, etc. must be migrated to Program.cs / Startup.cs equivalents.",
                Severity = IssueSeverity.Warning,
                Category = IssueCategory.WebConfiguration,
                Recommendation = "Application_Start logic moves to Program.cs WebApplicationBuilder configuration.",
                FilePath = globalAsax,
                EffortHours = 2
            });
        }
    }

    private static bool IsLegacyTfm(string tfm)
    {
        if (string.IsNullOrWhiteSpace(tfm) || tfm == "unknown") return false;
        if (!tfm.StartsWith("net", StringComparison.OrdinalIgnoreCase)) return false;
        var rest = tfm[3..];
        // Modern TFMs have a dot ("net8.0"). Legacy ones don't ("net48").
        return !rest.Contains('.') && !rest.StartsWith("standard", StringComparison.OrdinalIgnoreCase)
                                   && !rest.StartsWith("coreapp", StringComparison.OrdinalIgnoreCase);
    }

    private static string? RecommendTarget(ProjectType type) => type switch
    {
        ProjectType.AspNetMvc => "ASP.NET Core MVC (net8.0)",
        ProjectType.AspNetWebApi => "ASP.NET Core Web API (net8.0)",
        ProjectType.WebForms => "ASP.NET Core MVC/Razor Pages or Blazor (net8.0) — UI rewrite required",
        ProjectType.Wcf => "CoreWCF or gRPC (net8.0)",
        ProjectType.Asmx => "ASP.NET Core Web API (net8.0)",
        ProjectType.WinForms => "WinForms on net8.0-windows",
        ProjectType.Wpf => "WPF on net8.0-windows",
        ProjectType.ClassLibrary => "Class library on net8.0",
        ProjectType.Console => "Console app on net8.0",
        ProjectType.Test => "xUnit on net8.0",
        ProjectType.AlreadyModern => null,
        _ => "net8.0"
    };
}
