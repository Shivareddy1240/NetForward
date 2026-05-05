using System.CommandLine;
using Microsoft.Extensions.Logging;
using NetForward.Compatibility;
using NetForward.Converters.Startup;
using NetForward.Converters.WebConfig;
using NetForward.Core.Models;
using NetForward.Rewriter.Pipeline;
using NetForward.Rewriter.Workspace;

namespace NetForward.Cli;

/// <summary>
/// Builds and handles the `netforward migrate` command.
/// </summary>
internal static class MigrateCommand
{
    public static Command Build()
    {
        var solutionArg = new Argument<FileInfo>(
            name: "solution",
            description: "Path to the .sln file to migrate.");

        var outputDirOption = new Option<DirectoryInfo>(
            aliases: new[] { "--output", "-o" },
            getDefaultValue: () => new DirectoryInfo(
                Path.Combine(Directory.GetCurrentDirectory(), "netforward-migrated")),
            description: "Root output directory for side-by-side migrated projects.");

        var suffixOption = new Option<string>(
            name: "--suffix",
            getDefaultValue: () => ".Core",
            description: "Suffix appended to each migrated project folder name.");

        var dryRunOption = new Option<bool>(
            name: "--dry-run",
            description: "Show what would change without writing files to disk.");

        var tierOption = new Option<int>(
            name: "--tier",
            getDefaultValue: () => 2,
            description: "Maximum rewriter tier to apply (1 = safe only, 2 = with warnings, 3 = include advisories).");

        var verboseOption = new Option<bool>("--verbose", "Verbose logging.");

        var command = new Command("migrate",
            "Migrate all MVC projects in a solution to ASP.NET Core (side-by-side).");

        command.AddArgument(solutionArg);
        command.AddOption(outputDirOption);
        command.AddOption(suffixOption);
        command.AddOption(dryRunOption);
        command.AddOption(tierOption);
        command.AddOption(verboseOption);

        command.SetHandler(
            async (FileInfo sln, DirectoryInfo outDir, string suffix, bool dryRun, int tier, bool verbose) =>
            {
                Environment.ExitCode = await RunAsync(sln, outDir, suffix, dryRun, tier, verbose);
            },
            solutionArg, outputDirOption, suffixOption, dryRunOption, tierOption, verboseOption);

        return command;
    }

    private static async Task<int> RunAsync(
        FileInfo solution,
        DirectoryInfo outputDir,
        string suffix,
        bool dryRun,
        int tier,
        bool verbose)
    {
        // CRITICAL: MSBuild.Locator must be initialized before ANY Microsoft.Build types load.
        MSBuildWorkspaceLoader.EnsureInitialized();

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSimpleConsole(opts =>
            {
                opts.SingleLine = true;
                opts.TimestampFormat = "HH:mm:ss ";
            });
            builder.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information);
        });

        var logger = loggerFactory.CreateLogger("migrate");

        if (!solution.Exists)
        {
            logger.LogError("Solution file not found: {Path}", solution.FullName);
            return 1;
        }

        var options = new RewriteOptions
        {
            DryRun = dryRun,
            OutputRoot = outputDir.FullName,
            OutputSuffix = suffix,
            MaxTier = tier switch
            {
                1 => RewriteTier.Tier1,
                3 => RewriteTier.Tier3,
                _ => RewriteTier.Tier2
            }
        };

        if (dryRun)
        {
            logger.LogInformation("DRY RUN — no files will be written.");
        }

        var catalog = new YamlCompatibilityCatalog();
        var pipeline = RewriterFactory.CreateDefault(catalog, loggerFactory);

        MigrationResult result;
        try
        {
            result = await pipeline.MigrateSolutionAsync(solution.FullName, options);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Migration failed.");
            return 2;
        }

        // Run config converters for each project with a web.config / Global.asax
        await RunConfigConvertersAsync(solution.FullName, outputDir.FullName, suffix, logger);

        PrintSummary(result, dryRun);

        return result.TotalRemainingIssues > 0 ? 11 : 0;
    }

    private static async Task RunConfigConvertersAsync(
        string solutionPath,
        string outputRoot,
        string suffix,
        ILogger logger)
    {
        var solutionDir = Path.GetDirectoryName(solutionPath)!;
        var webConfigConverter = new WebConfigConverter();
        var globalAsaxConverter = new GlobalAsaxConverter();

        // Find all web.config and Global.asax.cs files in the solution tree.
        foreach (var webConfig in Directory.GetFiles(solutionDir, "Web.config", SearchOption.AllDirectories))
        {
            var projectDir = Path.GetDirectoryName(webConfig)!;
            var projectName = Path.GetFileName(projectDir);
            var outDir = Path.Combine(outputRoot, projectName + suffix);

            try
            {
                var result = webConfigConverter.Convert(webConfig, outDir);
                logger.LogInformation("web.config → appsettings.json: {Path}", result.AppSettingsPath);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "web.config conversion failed for: {Path}", webConfig);
            }
        }

        foreach (var globalAsax in Directory.GetFiles(solutionDir, "Global.asax.cs", SearchOption.AllDirectories))
        {
            var projectDir = Path.GetDirectoryName(globalAsax)!;
            var projectName = Path.GetFileName(projectDir);
            var outDir = Path.Combine(outputRoot, projectName + suffix);

            try
            {
                var result = globalAsaxConverter.Convert(globalAsax, outDir);
                logger.LogInformation("Global.asax.cs → Program.cs: {Path}", result.OutputPath);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Global.asax.cs conversion failed for: {Path}", globalAsax);
            }
        }

        await Task.CompletedTask;
    }

    private static void PrintSummary(MigrationResult result, bool dryRun)
    {
        Console.WriteLine();
        Console.WriteLine(dryRun ? "=== DRY RUN SUMMARY ===" : "=== MIGRATION SUMMARY ===");
        Console.WriteLine($"Projects migrated   : {result.Projects.Count}");
        Console.WriteLine($"Files modified      : {result.TotalFilesModified}");
        Console.WriteLine($"Files unchanged     : {result.TotalFilesUnchanged}");
        Console.WriteLine($"Remaining issues    : {result.TotalRemainingIssues}");
        Console.WriteLine();

        foreach (var project in result.Projects)
        {
            Console.WriteLine($"  {project.ProjectName}");
            Console.WriteLine($"    Modified  : {project.FilesModified}");
            Console.WriteLine($"    Unchanged : {project.FilesUnchanged}");
            Console.WriteLine($"    Issues    : {project.RemainingIssueCount}");

            if (project.RemainingIssueCount > 0)
            {
                Console.WriteLine("    Remaining issues:");
                foreach (var file in project.Files.Where(f => f.RemainingIssues.Count > 0))
                {
                    foreach (var issue in file.RemainingIssues)
                    {
                        Console.WriteLine($"      [{issue.Id}] {issue.Title}");
                        if (issue.FilePath is not null)
                        {
                            Console.WriteLine($"        at {issue.FilePath}:{issue.LineNumber}");
                        }
                    }
                }
            }

            if (!result.IsDryRun)
            {
                Console.WriteLine($"    Output    : {project.OutputDirectory}");
            }
        }
    }
}
