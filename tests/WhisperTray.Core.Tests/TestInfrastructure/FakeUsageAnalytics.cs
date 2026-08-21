using WhisperTray.Core.Analytics;

namespace WhisperTray.Core.Tests.TestInfrastructure;

public sealed class FakeUsageAnalytics : IUsageAnalytics
{
    public List<UsageRecord> Records { get; } = new();

    public void Record(UsageRecord record) => Records.Add(record);
}
