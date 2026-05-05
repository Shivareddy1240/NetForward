using System.CommandLine;
using Microsoft.Extensions.Logging;
using NetForward.Analyzer;
using NetForward.Compatibility;
using NetForward.Core.Abstractions;
using NetForward.Core.Models;
using NetForward.Modernizer;
using NetForward.Reporting.Formatters;

namespace NetForward.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Arguments and options shared between commands
        var verboseOption = new Option<bool>("--verbose", "Verbose logging.");

        // analyze
        var solutionArg = new Argument<FileInfo>(
            name: "solution",
            description: "Path to the .sln file to analyze.");

        var outputDirOption = new Option<DirectoryInfo>(
            aliases: new[] { "--output", "-o" },
            getDefaultValue: () => new DirectoryInfo(Path.Combine(Directory.GetCurrentDirectory(), "netforward-report")),
            description: "Output directory for reports.");

        var formatsOption = new Option<string[]>(
            aliases: new[] { "--format", "-f" },
            getDefaultValue: () => new[] { "json", "markdown", "html", "word" },
            description: "Report formats to produce. Repeat or comma-separate.")
        {
            AllowMultipleArgumentsPerToken = true
        };

        var analyzeCommand = new Command("analyze", "Analyze a .NET solution and produce a migration readiness report.");
        analyzeCommand.AddArgument(solutionArg);
        analyzeCommand.AddOption(outputDirOption);
        analyzeCommand.AddOption(formatsOption);
        analyzeCommand.AddOption(verboseOption);
        analyzeCommand.SetHandler(async (FileInfo sln, DirectoryInfo outDir, string[] formats, bool verbose) =>
        {
            Environment.ExitCode = await RunAnalyzeAsync(sln, outDir, formats, verbose);
        }, solutionArg, outputDirOption, formatsOption, verboseOption);

        // modernize-csproj
        var projectArg = new Argument<FileInfo>(
            name: "project",
            description: "Path to the legacy .csproj to modernize.");

        var modernizeOutputOption = new Option<FileInfo?>(
            aliases: new[] { "--out" },
            description: "Output path. Defaults to <project>.modernized.csproj.");

        var modernizeCommand = new Command("modernize-csproj", "Convert a legacy .csproj to SDK-style format.");
        modernizeCommand.AddArgument(projectArg);
        modernizeCommand.AddOption(modernizeOutputOption);
        modernizeCommand.AddOption(verboseOption);
        modernizeCommand.SetHandler((FileInfo proj, FileInfo? output, bool verbose) =>
        {
            Environment.ExitCode = RunModernizeCsproj(proj, output, verbose);
        }, projectArg, modernizeOutputOption, verboseOption);

        var root = new RootCommand("NetForward — analyze and migrate legacy .NET projects.")
        {
            analyzeCommand,
            modernizeCommand,
            MigrateCommand.Build()
        };

        return await root.InvokeAsync(args);
    }

    private static async Task<int> RunAnalyzeAsync(FileInfo solution, DirectoryInfo outputDir, string[] formats, bool verbose)
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSimpleConsole(opts =>
            {
                opts.SingleLine = true;
                opts.TimestampFormat = "HH:mm:ss ";
            });
            builder.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information);
        });
        var logger = loggerFactory.CreateLogger<SolutionAnalyzer>();

        if (!solution.Exists)
        {
            logger.LogError("Solution file not found: {Path}", solution.FullName);
            return 1;
        }

        ICompatibilityCatalog catalog = new YamlCompatibilityCatalog();
        IProjectAnalyzer projectAnalyzer = new ProjectAnalyzer(catalog);
        ISolutionAnalyzer solutionAnalyzer = new SolutionAnalyzer(projectAnalyzer, logger);

        SolutionAnalysisResult result;
        try
        {
            result = await solutionAnalyzer.AnalyzeAsync(solution.FullName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Analysis failed.");
            return 2;
        }

        outputDir.Create();

        var requestedFormats = NormalizeFormats(formats);
        var formatters = BuildFormatters(requestedFormats);
        if (formatters.Count == 0)
        {
            logger.LogError("No valid report formats requested. Supported: json, markdown, html, word.");
            return 3;
        }

        var stem = Path.GetFileNameWithoutExtension(solution.Name);
        foreach (var formatter in formatters)
        {
            var outputPath = Path.Combine(outputDir.FullName, $"{stem}.{formatter.FileExtension}");
            try
            {
                await formatter.WriteAsync(result, outputPath);
                logger.LogInformation("Wrote {Format} report: {Path}", formatter.DisplayName, outputPath);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to write {Format} report.", formatter.DisplayName);
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Readiness score : {result.ReadinessScore}/100");
        Console.WriteLine($"Projects        : {result.ProjectCount}");
        Console.WriteLine($"Total issues    : {result.TotalIssueCount}");
        Console.WriteLine($"Blockers        : {result.BlockerCount}");
        Console.WriteLine($"Estimated effort: {result.TotalEffortHours:F1}h");

        return result.BlockerCount > 0 ? 10 : 0;
    }

    private static int RunModernizeCsproj(FileInfo project, FileInfo? outputPath, bool verbose)
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSimpleConsole(opts => opts.SingleLine = true);
            builder.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information);
        });
        var logger = loggerFactory.CreateLogger("modernize-csproj");

        if (!project.Exists)
        {
            logger.LogError("Project file not found: {Path}", project.FullName);
            return 1;
        }

        var catalog = new YamlCompatibilityCatalog();
        var analyzer = new ProjectAnalyzer(catalog);
        var info = analyzer.AnalyzeAsync(project.FullName).GetAwaiter().GetResult();

        var modernizer = new CsprojModernizer();
        var result = modernizer.Modernize(project.FullName, info.Type, outputPath?.FullName);

        Console.WriteLine($"Modernized csproj written to: {result.ModernizedPath}");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        foreach (var note in result.Notes)
        {
            Console.WriteLine($"  - {note}");
        }
        return 0;
    }

    private static List<string> NormalizeFormats(IEnumerable<string> raw)
    {
        return raw
            .SelectMany(s => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(s => s.ToLowerInvariant())
            .Distinct()
            .ToList();
    }

    private static List<IReportFormatter> BuildFormatters(IEnumerable<string> requested)
    {
        var result = new List<IReportFormatter>();
        foreach (var id in requested)
        {
            IReportFormatter? formatter = id switch
            {
                "json" => new JsonReportFormatter(),
                "markdown" or "md" => new MarkdownReportFormatter(),
                "html" => new HtmlReportFormatter(),
                "word" or "docx" => new WordReportFormatter(),
                _ => null
            };
            if (formatter is not null) result.Add(formatter);
        }
        return result;
    }
}
