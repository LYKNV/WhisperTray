namespace WhisperTray.Core.Analytics;

/// <summary>Why a dictation session failed. Mirrors the catch arms of the orchestrator pipeline.</summary>
public enum UsageErrorKind
{
    Auth,
    RateLimit,
    Request,
    Server,
    Timeout,
    Cancelled,

    /// <summary>No foreground window was captured, so there was nowhere to inject.</summary>
    NoTarget,

    Unexpected,
}
