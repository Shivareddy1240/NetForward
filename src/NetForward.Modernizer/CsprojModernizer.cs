using System.Text;
using NetForward.Analyzer.Parsing;
using NetForward.Core.Models;

namespace NetForward.Modernizer;

/// <summary>
/// Converts a legacy verbose .csproj to SDK-style format. Non-destructive:
/// writes the result to a side-by-side path or to a caller-supplied output path.
/// </summary>
public sealed class CsprojModernizer
{
    /// <summary>
    /// Result of a modernize operation.
    /// </summary>
    public sealed record Result(
        string OriginalPath,
        string ModernizedPath,
        string ModernizedContent,
        IReadOnlyList<string> Notes);

    /// <summary>
    /// Convert the given .csproj to SDK-style. The new file is written next to the
    /// original with a `.modernized.csproj` suffix, unless <paramref name="outputPath"/> is provided.
    /// </summary>
    public Result Modernize(string projectPath, ProjectType projectType, string? outputPath = null)
    {
        var parsed = CsprojParser.Parse(projectPath);
        var notes = new List<string>();

        if (parsed.IsSdkStyle)
        {
            notes.Add("Project is already SDK-style; output is identical to input.");
            return new Result(projectPath, projectPath, File.ReadAllText(projectPath), notes);
        }

        var sdkName = ChooseSdk(projectType, out var note);
        if (note is not null) notes.Add(note);

        var newTfm = ChooseModernTfm(projectType, parsed.TargetFramework, out var tfmNote);
        if (tfmNote is not null) notes.Add(tfmNote);

        // Pick up packages.config too, since legacy projects often have both.
        var packagesConfigPath = PackagesConfigParser.FindAlongside(projectPath);
        var packagesFromConfig = packagesConfigPath is not null
            ? PackagesConfigParser.Parse(packagesConfigPath)
            : Array.Empty<ParsedPackageReference>();

        var allPackages = parsed.PackageReferences
            .Concat(packagesFromConfig)
            .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (packagesFromConfig.Count > 0)
        {
            notes.Add($"Migrated {packagesFromConfig.Count} entries from packages.config to <PackageReference>. Delete packages.config after verifying.");
        }

        var sb = new StringBuilder();
        sb.AppendLine($"""<Project Sdk="{sdkName}">""");
        sb.AppendLine();
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine($"    <TargetFramework>{newTfm}</TargetFramework>");

        if (projectType == ProjectType.WinForms)
        {
            sb.AppendLine("    <UseWindowsForms>true</UseWindowsForms>");
        }
        if (projectType == ProjectType.Wpf)
        {
            sb.AppendLine("    <UseWPF>true</UseWPF>");
        }

        if (parsed.OutputType is { Length: > 0 })
        {
            sb.AppendLine($"    <OutputType>{parsed.OutputType}</OutputType>");
        }

        sb.AppendLine("    <Nullable>enable</Nullable>");
        sb.AppendLine("    <ImplicitUsings>enable</ImplicitUsings>");
        sb.AppendLine("  </PropertyGroup>");

        if (allPackages.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("  <ItemGroup>");
            foreach (var pkg in allPackages)
            {
                if (string.IsNullOrEmpty(pkg.Version))
                {
                    sb.AppendLine($"""    <PackageReference Include="{Escape(pkg.Id)}" />""");
                }
                else
                {
                    sb.AppendLine($"""    <PackageReference Include="{Escape(pkg.Id)}" Version="{Escape(pkg.Version)}" />""");
                }
            }
            sb.AppendLine("  </ItemGroup>");
        }

        if (parsed.ProjectReferences.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("  <ItemGroup>");
            foreach (var projRef in parsed.ProjectReferences)
            {
                sb.AppendLine($"""    <ProjectReference Include="{Escape(projRef)}" />""");
            }
            sb.AppendLine("  </ItemGroup>");
        }

        sb.AppendLine();
        sb.AppendLine("</Project>");

        var content = sb.ToString();
        var resolvedOutput = outputPath ?? Path.ChangeExtension(projectPath, ".modernized.csproj");

        File.WriteAllText(resolvedOutput, content, Encoding.UTF8);
        notes.Add($"Modernized csproj written to: {resolvedOutput}");
        notes.Add("Review and rename to replace the original after verifying.");

        return new Result(projectPath, resolvedOutput, content, notes);
    }

    private static string ChooseSdk(ProjectType type, out string? note)
    {
        note = null;
        return type switch
        {
            ProjectType.AspNetMvc or ProjectType.AspNetWebApi or ProjectType.Asmx
                => "Microsoft.NET.Sdk.Web",
            ProjectType.WebForms => SetNote(out note, "WebForms cannot run on modern SDK; using Microsoft.NET.Sdk.Web as a placeholder. UI must be rewritten.")
                ?? "Microsoft.NET.Sdk.Web",
            _ => "Microsoft.NET.Sdk"
        };
    }

    private static string ChooseModernTfm(ProjectType type, string legacyTfm, out string? note)
    {
        note = null;
        return type switch
        {
            ProjectType.WinForms or ProjectType.Wpf
                => SetNote(out note, "Desktop UI requires the Windows-specific TFM; using net8.0-windows.")
                    ?? "net8.0-windows",
            ProjectType.Wcf
                => SetNote(out note, "WCF server-side does not run on net8.0; consider CoreWCF. TFM set to net8.0 for now.")
                    ?? "net8.0",
            _ => "net8.0"
        };
    }

    private static string? SetNote(out string? note, string value)
    {
        note = value;
        return null;
    }

    private static string Escape(string raw)
    {
        // Manual XML attribute-value escape. Package ids and version strings
        // never legitimately contain these characters, but be defensive.
        return raw
            .Replace("&", "&amp;")
            .Replace("\"", "&quot;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }
}
