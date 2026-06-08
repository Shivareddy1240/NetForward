using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetForward.Core.Models;

namespace NetForward.Rewriter.Verification;

/// <summary>
/// Runs `dotnet build` against the migrated side-by-side output and
/// parses the compiler diagnostics back into <see cref="MigrationIssue"/> instances.
///
/// Namespace is NetForward.Rewriter.Verification (not .Compilation) to avoid
/// clashing with Microsoft.CodeAnalysis.Compilation which is used throughout
/// the Rewriter assembly.
/// </summary>
public sealed class MigrationCompilationVerifier
{
    private readonly ILogger<MigrationCompilationVerifier> _logger;

    // MSBuild error/warning line format:
    // path/to/File.cs(10,5): error CS0246: The type or namespace name 'Foo' ...
    private static readonly Regex DiagnosticLineRegex = new(
        @"^(?<file>.+?)\((?<line>\d+),(?<col>\d+)\):\s+(?<severity>error|warning)\s+(?<code>CS\d+):\s+(?<message>.+)$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // Solution/project-level errors (no file info)
    private static readonly Regex ProjectErrorRegex = new(
        @"^.*error\s+(?<code>MSB\d+|NETSDK\d+):\s+(?<message>.+)$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public MigrationCompilationVerifier(ILogger<MigrationCompilationVerifier>? logger = null)
    {
        _logger = logger ?? NullLogger<MigrationCompilationVerifier>.Instance;
    }

    /// <summary>
    /// Verify a migrated project by running `dotnet build` against its output directory.
    /// </summary>
    public async Task<BuildVerificationResult> VerifyAsync(
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        var csprojFiles = Directory.GetFiles(outputDirectory, "*.csproj",
            SearchOption.TopDirectoryOnly);

        if (csprojFiles.Length == 0)
        {
            _logger.LogWarning("No .csproj found in output directory: {Dir}", outputDirectory);
            return BuildVerificationResult.NotApplicable(outputDirectory,
                "No .csproj file found in the migrated output directory.");
        }

        var csprojPath = csprojFiles[0];
        _logger.LogInformation("Running dotnet build on: {Path}", csprojPath);

        var (exitCode, stdout, stderr) = await RunDotnetBuildAsync(csprojPath, cancellationToken);

        var allOutput = stdout + "\n" + stderr;
        var diagnostics = ParseDiagnostics(allOutput);
        var succeeded = exitCode == 0;

        _logger.LogInformation(
            "Build {Result}: {Errors} error(s), {Warnings} warning(s).",
            succeeded ? "SUCCEEDED" : "FAILED",
            diagnostics.Count(d => d.Severity == IssueSeverity.Error || d.Severity == IssueSeverity.Blocker),
            diagnostics.Count(d => d.Severity == IssueSeverity.Warning));

        return new BuildVerificationResult
        {
            ProjectPath = csprojPath,
            Succeeded = succeeded,
            ExitCode = exitCode,
            RawOutput = allOutput,
            Diagnostics = diagnostics
        };
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunDotnetBuildAsync(
        string csprojPath,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{csprojPath}\" --nologo -v minimal",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(csprojPath)!
        };

        var stdoutSb = new StringBuilder();
        var stderrSb = new StringBuilder();

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdoutSb.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderrSb.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(); } catch { /* best effort */ }
            throw;
        }

        return (process.ExitCode, stdoutSb.ToString(), stderrSb.ToString());
    }

    private static IReadOnlyList<MigrationIssue> ParseDiagnostics(string output)
    {
        var issues = new List<MigrationIssue>();

        // File-level errors (with line info) — skip warnings
        foreach (Match match in DiagnosticLineRegex.Matches(output))
        {
            var severityText = match.Groups["severity"].Value;
            if (severityText == "warning") continue;

            var code = match.Groups["code"].Value;
            var message = match.Groups["message"].Value.Trim();
            var file = match.Groups["file"].Value.Trim();
            var line = int.TryParse(match.Groups["line"].Value, out var l) ? l : (int?)null;

            issues.Add(new MigrationIssue
            {
                Id = $"NF5{code[2..]}",
                Title = $"Compile error {code}: {Truncate(message, 80)}",
                Description = $"The migrated project did not compile. {code}: {message}",
                Severity = IssueSeverity.Error,
                Category = IssueCategory.ProjectStructure,
                FilePath = file,
                LineNumber = line,
                EffortHours = 0.5,
                Recommendation = CompileErrorGuidance(code),
                HelpUrl = $"https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/{code.ToLowerInvariant()}"
            });
        }

        // Project-level MSBuild errors (no file info)
        foreach (Match match in ProjectErrorRegex.Matches(output))
        {
            var code = match.Groups["code"].Value;
            if (code.StartsWith("CS", StringComparison.Ordinal)) continue; // already captured above

            var message = match.Groups["message"].Value.Trim();
            issues.Add(new MigrationIssue
            {
                Id = "NF500",
                Title = $"Build error {code}",
                Description = $"MSBuild/SDK error: {message}",
                Severity = IssueSeverity.Error,
                Category = IssueCategory.BuildSystem,
                EffortHours = 1.0,
                Recommendation =
                    "Run `dotnet restore` in the output directory, then retry `netforward migrate`."
            });
        }

        return issues;
    }

    private static string? CompileErrorGuidance(string code) => code switch
    {
        "CS0246" => "Type or namespace not found. Check NuGet references and that R001 updated all using directives.",
        "CS0103" => "Name not in scope. Likely a legacy helper (HtmlHelper, UrlHelper) — inject via DI instead.",
        "CS1061" => "Member not found. May have been renamed or moved in ASP.NET Core.",
        "CS0115" => "No method to override. Base class method signature changed between MVC 5 and Core.",
        "CS0029" => "Cannot implicitly convert. Check action return types — use IActionResult or ActionResult<T>.",
        "CS1503" => "Argument type mismatch. Check method overloads in ASP.NET Core docs.",
        _ => null
    };

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "…";
}

/// <summary>
/// Result of running dotnet build against a migrated project.
/// Named BuildVerificationResult (not CompilationResult) to avoid
/// confusion with Roslyn's own result types.
/// </summary>
public sealed record BuildVerificationResult
{
    public required string ProjectPath { get; init; }
    public required bool Succeeded { get; init; }
    public required int ExitCode { get; init; }
    public required string RawOutput { get; init; }
    public required IReadOnlyList<MigrationIssue> Diagnostics { get; init; }

    public bool IsNotApplicable { get; init; }
    public string? NotApplicableReason { get; init; }

    public int ErrorCount => Diagnostics.Count(d =>
        d.Severity is IssueSeverity.Error or IssueSeverity.Blocker);

    public static BuildVerificationResult NotApplicable(string projectPath, string reason) =>
        new()
        {
            ProjectPath = projectPath,
            Succeeded = false,
            ExitCode = -1,
            RawOutput = "",
            Diagnostics = [],
            IsNotApplicable = true,
            NotApplicableReason = reason
        };
}
