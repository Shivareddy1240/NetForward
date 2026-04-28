namespace NetForward.Core.Models;

/// <summary>
/// Severity of a migration finding. Drives effort scoring and report sorting.
/// </summary>
public enum IssueSeverity
{
    /// <summary>Informational — no action required, just visibility.</summary>
    Info = 0,

    /// <summary>Will be auto-handled by the modernizer; surfaced for transparency.</summary>
    AutoFixable = 1,

    /// <summary>Manual review recommended but a default fix exists.</summary>
    Warning = 2,

    /// <summary>Manual rewrite required — no automatic conversion is safe.</summary>
    Error = 3,

    /// <summary>Cannot be migrated without architectural redesign.</summary>
    Blocker = 4
}
