using FluentAssertions;
using WhisperTray.Core.Analytics;
using WhisperTray.Core.Configuration;

namespace WhisperTray.Core.Tests.Analytics;

public class NullUsageAnalyticsTests
{
    [Fact]
    public void Record_SwallowsEveryRecord()
    {
        var record = new UsageRecord
        {
            Status = UsageStatus.Ok,
            AudioDuration = TimeSpan.FromSeconds(12.34),
            Provider = TranscriptionProvider.OpenAi,
            Model = "gpt-4o-transcribe",
        };

        var act = () => NullUsageAnalytics.Instance.Record(record);

        act.Should().NotThrow();
    }
}
