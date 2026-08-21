namespace WhisperTray.Core.Analytics;

/// <summary>
/// Sink for usage telemetry. Implementations must never throw: a failure to record
/// must degrade to a missing row, never to a failed dictation.
/// </summary>
public interface IUsageAnalytics
{
    void Record(UsageRecord record);
}
