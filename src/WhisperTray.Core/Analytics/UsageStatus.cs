namespace WhisperTray.Core.Analytics;

/// <summary>Outcome of a single dictation session, as recorded in the usage log.</summary>
public enum UsageStatus
{
    Ok,

    /// <summary>The provider answered successfully but the transcript was blank.</summary>
    Empty,

    Error,
}
