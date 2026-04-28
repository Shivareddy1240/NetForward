using NetForward.Core.Models;

namespace NetForward.Core.Abstractions;

/// <summary>
/// Analyzes a solution and produces a structured result. Implementations live in
/// NetForward.Analyzer; consumers depend only on this abstraction.
/// </summary>
public interface ISolutionAnalyzer
{
    /// <summary>
    /// Analyze the given solution file and produce a complete report.
    /// </summary>
    /// <param name="solutionPath">Absolute path to a .sln file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<SolutionAnalysisResult> AnalyzeAsync(
        string solutionPath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Single-project analyzer used internally by ISolutionAnalyzer.
/// </summary>
public interface IProjectAnalyzer
{
    Task<ProjectInfo> AnalyzeAsync(
        string projectPath,
        CancellationToken cancellationToken = default);
}
