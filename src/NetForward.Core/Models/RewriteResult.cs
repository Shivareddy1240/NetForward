namespace NetForward.Core.Models;

/// <summary>
/// The result of rewriting a single source file.
/// </summary>
public sealed record FileRewriteResult
{
    /// <summary>Original absolute path of the file.</summary>
    public required string OriginalPath { get; init; }

    /// <summary>
    /// Absolute path where the rewritten file was written.
    /// Null when running in dry-run mode.
    /// </summary>
    public string? OutputPath { get; init; }

    /// <summary>The rewritten source text.</summary>
    public required string RewrittenContent { get; init; }

    /// <summary>The original source text (kept for diffing).</summary>
    public required string OriginalContent { get; init; }

    /// <summary>Individual transformations applied to this file.</summary>
    public IReadOnlyList<AppliedTransformation> Transformations { get; init; } = [];

    /// <summary>Issues raised during rewriting that require manual attention.</summary>
    public IReadOnlyList<MigrationIssue> RemainingIssues { get; init; } = [];

    /// <summary>True if any transformations were applied.</summary>
    public bool WasModified => Transformations.Count > 0;

    /// <summary>True if the file compiled cleanly after rewrite (set by compilation step).</summary>
    public bool? CompilesAfterRewrite { get; init; }
}

/// <summary>
/// A single transformation applied to a file by a rewrite rule.
/// </summary>
public sealed record AppliedTransformation
{
    /// <summary>The rule that applied this transformation (e.g. "R001").</summary>
    public required string RuleId { get; init; }

    /// <summary>Short description of what changed.</summary>
    public required string Description { get; init; }

    /// <summary>1-based line number where the change occurred (if applicable).</summary>
    public int? LineNumber { get; init; }
}

/// <summary>
/// The result of rewriting a single project.
/// </summary>
public sealed record ProjectRewriteResult
{
    public required string ProjectName { get; init; }
    public required string OriginalProjectPath { get; init; }

    /// <summary>
    /// Root of the side-by-side output directory for this project.
    /// e.g. C:\src\MyApp.Core\
    /// </summary>
    public required string OutputDirectory { get; init; }

    public required IReadOnlyList<FileRewriteResult> Files { get; init; }

    public int FilesModified => Files.Count(f => f.WasModified);
    public int FilesUnchanged => Files.Count(f => !f.WasModified);
    public int RemainingIssueCount => Files.Sum(f => f.RemainingIssues.Count);

    /// <summary>Issues raised during rewriting that are not attributable to a single file.</summary>
    public IReadOnlyList<MigrationIssue> ProjectLevelIssues { get; init; } = [];

    /// <summary>
    /// True if dotnet build succeeded on the migrated output. Null if verification
    /// was skipped (dry-run mode or VerifyCompilation=false).
    /// </summary>
    public bool? CompiledSuccessfully { get; init; }

    /// <summary>Raw compiler output from the verification step.</summary>
    public string? CompilerOutput { get; init; }

    /// <summary>Compile errors raised during the verification step.</summary>
    public IReadOnlyList<MigrationIssue> CompileErrors { get; init; } = [];
}

/// <summary>
/// The top-level result of running the migration pipeline against a solution.
/// </summary>
public sealed record MigrationResult
{
    public required string SolutionPath { get; init; }
    public required DateTimeOffset MigratedAtUtc { get; init; }
    public required string ToolVersion { get; init; }
    public required bool IsDryRun { get; init; }

    public required IReadOnlyList<ProjectRewriteResult> Projects { get; init; }

    public int TotalFilesModified => Projects.Sum(p => p.FilesModified);
    public int TotalFilesUnchanged => Projects.Sum(p => p.FilesUnchanged);
    public int TotalRemainingIssues => Projects.Sum(p => p.RemainingIssueCount);
}
