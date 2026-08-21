namespace WhisperTray.Core.Analytics;

/// <summary>Discards every record. Used by tests and by compositions that want analytics off.</summary>
public sealed class NullUsageAnalytics : IUsageAnalytics
{
    public static NullUsageAnalytics Instance { get; } = new();

    private NullUsageAnalytics()
    {
    }

    public void Record(UsageRecord record)
    {
    }
}
