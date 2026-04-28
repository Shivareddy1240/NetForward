using NetForward.Analyzer.Parsing;
using NetForward.Core.Models;

namespace NetForward.Analyzer.Classification;

/// <summary>
/// Determines the <see cref="ProjectType"/> of a parsed project using a layered set of signals:
/// SDK type, OutputType, package references, assembly references, and file system markers.
/// First match wins; rules are ordered from strongest signal to weakest.
/// </summary>
internal static class ProjectClassifier
{
    public static ProjectType Classify(ParsedCsproj parsed)
    {
        // 1. SDK-style projects: the SDK name is decisive.
        if (parsed.IsSdkStyle)
        {
            // Modern SDKs map cleanly to project types. We treat them as AlreadyModern
            // because Phase 1's job is to surface what needs migrating; SDK-style
            // projects targeting netcore/net5+ don't need our help.
            if (IsModernTfm(parsed.TargetFramework))
            {
                return ProjectType.AlreadyModern;
            }

            // SDK-style projects targeting net48 etc. exist (multi-target libraries).
            // They still classify by SDK type.
            return ClassifyBySdkType(parsed);
        }

        // 2. Legacy projects: combine OutputType, packages, and assembly refs.
        var hasMvc = HasPackageOrAssembly(parsed, "Microsoft.AspNet.Mvc", "System.Web.Mvc");
        var hasWebApi = HasPackageOrAssembly(parsed, "Microsoft.AspNet.WebApi.Core", "System.Web.Http");
        var hasWebForms = HasAssembly(parsed, "System.Web") && HasAssembly(parsed, "System.Web.ApplicationServices")
                          && !hasMvc && !hasWebApi;
        var hasWcf = HasAssembly(parsed, "System.ServiceModel");
        var hasWinForms = HasAssembly(parsed, "System.Windows.Forms");
        var hasWpf = HasAssembly(parsed, "PresentationCore") || HasAssembly(parsed, "PresentationFramework");
        var isTest = parsed.PackageReferences.Any(p =>
            p.Id.StartsWith("xunit", StringComparison.OrdinalIgnoreCase)
            || p.Id.StartsWith("MSTest.", StringComparison.OrdinalIgnoreCase)
            || p.Id.StartsWith("NUnit", StringComparison.OrdinalIgnoreCase));

        if (isTest) return ProjectType.Test;
        if (hasMvc) return ProjectType.AspNetMvc;
        if (hasWebApi) return ProjectType.AspNetWebApi;
        if (hasWebForms) return ProjectType.WebForms;
        if (hasWcf) return ProjectType.Wcf;
        if (hasWinForms) return ProjectType.WinForms;
        if (hasWpf) return ProjectType.Wpf;

        // OutputType-based fallbacks
        return parsed.OutputType?.ToLowerInvariant() switch
        {
            "exe" or "winexe" => ProjectType.Console,
            "library" => ProjectType.ClassLibrary,
            _ => ProjectType.Unknown
        };
    }

    private static ProjectType ClassifyBySdkType(ParsedCsproj parsed)
    {
        if (string.Equals(parsed.Sdk, "Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase))
        {
            // Modern web project. Distinguish MVC vs API via package references.
            if (parsed.PackageReferences.Any(p => p.Id.StartsWith("Microsoft.AspNetCore.Mvc", StringComparison.OrdinalIgnoreCase)))
            {
                return ProjectType.AspNetMvc;
            }
            return ProjectType.AspNetWebApi;
        }

        if (parsed.UseWindowsForms) return ProjectType.WinForms;
        if (parsed.UseWpf) return ProjectType.Wpf;

        return parsed.OutputType?.ToLowerInvariant() switch
        {
            "exe" or "winexe" => ProjectType.Console,
            _ => ProjectType.ClassLibrary
        };
    }

    private static bool HasPackageOrAssembly(ParsedCsproj parsed, string packageId, string assemblyName)
        => HasPackage(parsed, packageId) || HasAssembly(parsed, assemblyName);

    private static bool HasPackage(ParsedCsproj parsed, string packageId)
        => parsed.PackageReferences.Any(p => string.Equals(p.Id, packageId, StringComparison.OrdinalIgnoreCase));

    private static bool HasAssembly(ParsedCsproj parsed, string assemblyName)
        => parsed.AssemblyReferences.Any(a => string.Equals(a, assemblyName, StringComparison.OrdinalIgnoreCase));

    /// <summary>True if the TFM represents .NET (Core) 5 or later.</summary>
    private static bool IsModernTfm(string tfm)
    {
        // net5.0, net6.0, net7.0, net8.0, net9.0, netcoreapp3.1, netstandard2.x ...
        if (tfm.StartsWith("netcoreapp", StringComparison.OrdinalIgnoreCase)) return true;
        if (tfm.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase)) return true;
        if (!tfm.StartsWith("net", StringComparison.OrdinalIgnoreCase)) return false;

        // After "net": "48" = legacy; "5.0", "8.0" = modern.
        var versionPart = tfm[3..];
        // Modern TFMs always contain a '.' (net5.0). Legacy never does (net48, net472).
        return versionPart.Contains('.');
    }
}
