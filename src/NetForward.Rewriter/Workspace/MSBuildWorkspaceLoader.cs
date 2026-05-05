using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace NetForward.Rewriter.Workspace;

/// <summary>
/// Wraps MSBuildWorkspace with proper Microsoft.Build.Locator initialization.
///
/// CRITICAL: MSBuildLocator.RegisterDefaults() MUST be called before any
/// Microsoft.Build types are loaded into the AppDomain. This class is the
/// single place that initialization happens. Call EnsureInitialized() once
/// at startup (before any Roslyn workspace operations).
/// </summary>
public sealed class MSBuildWorkspaceLoader : IDisposable
{
    private static int _initialized;
    private readonly MSBuildWorkspace _workspace;
    private readonly ILogger<MSBuildWorkspaceLoader> _logger;

    public MSBuildWorkspaceLoader(ILogger<MSBuildWorkspaceLoader>? logger = null)
    {
        _logger = logger ?? NullLogger<MSBuildWorkspaceLoader>.Instance;
        EnsureInitialized();
        _workspace = MSBuildWorkspace.Create();

        _workspace.WorkspaceFailed += (_, e) =>
        {
            // Log but don't throw — workspace failures are often non-fatal
            // (e.g. missing optional targets, SDK components not installed).
            var level = e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure
                ? LogLevel.Warning
                : LogLevel.Debug;
            _logger.Log(level, "MSBuildWorkspace diagnostic: {Message}", e.Diagnostic.Message);
        };
    }

    /// <summary>
    /// Initializes the MSBuild locator. Safe to call multiple times;
    /// initialization is idempotent.
    /// </summary>
    public static void EnsureInitialized()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 0)
        {
            if (!MSBuildLocator.IsRegistered)
            {
                MSBuildLocator.RegisterDefaults();
            }
        }
    }

    /// <summary>
    /// Open a solution and return all C# projects in it.
    /// </summary>
    public async Task<Solution> OpenSolutionAsync(
        string solutionPath,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Opening solution with MSBuildWorkspace: {Path}", solutionPath);
        var solution = await _workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken);
        _logger.LogInformation("Loaded {Count} project(s) from solution.", solution.Projects.Count());
        return solution;
    }

    /// <summary>
    /// Open a single project.
    /// </summary>
    public async Task<Project> OpenProjectAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Opening project with MSBuildWorkspace: {Path}", projectPath);
        return await _workspace.OpenProjectAsync(projectPath, cancellationToken: cancellationToken);
    }

    public void Dispose() => _workspace.Dispose();
}
