using NetForward.Core.Models;

namespace NetForward.Core.Abstractions;

/// <summary>
/// AI augmentation layer for ambiguous cases. Phase 1 ships a no-op stub
/// (<see cref="NullAiAdvisor"/>); Phase 4 wires this to the Anthropic API.
/// </summary>
public interface IAiAdvisor
{
    /// <summary>True if a real backend is configured. Stub returns false.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Produce a plain-English explanation of why an issue exists and how to remediate it.
    /// </summary>
    Task<string?> ExplainIssueAsync(MigrationIssue issue, CancellationToken cancellationToken = default);

    /// <summary>
    /// Suggest a refactor for a code snippet that the deterministic rewriter cannot handle.
    /// </summary>
    Task<string?> SuggestRefactorAsync(string codeSnippet, string context, CancellationToken cancellationToken = default);
}

/// <summary>
/// No-op IAiAdvisor used when AI augmentation is disabled.
/// </summary>
public sealed class NullAiAdvisor : IAiAdvisor
{
    public bool IsAvailable => false;

    public Task<string?> ExplainIssueAsync(MigrationIssue issue, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    public Task<string?> SuggestRefactorAsync(string codeSnippet, string context, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);
}
