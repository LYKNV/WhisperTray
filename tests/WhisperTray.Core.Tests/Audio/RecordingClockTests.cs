using FluentAssertions;
using WhisperTray.Core.Audio;

namespace WhisperTray.Core.Tests.Audio;

public class RecordingClockTests
{
    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(12, "00:12")]
    [InlineData(59 * 60 + 59, "59:59")]
    [InlineData(60 * 60, "1:00:00")]
    [InlineData(2 * 3600 + 5 * 60 + 7, "2:05:07")]
    [InlineData(30 * 3600, "30:00:00")]
    public void Format_RendersElapsedTime(int seconds, string expected)
    {
        RecordingClock.Format(TimeSpan.FromSeconds(seconds)).Should().Be(expected);
    }

    [Fact]
    public void Format_DropsFractionalSeconds()
    {
        RecordingClock.Format(TimeSpan.FromMilliseconds(12_999)).Should().Be("00:12");
    }

    [Fact]
    public void Format_ForNegativeElapsed_IsZero()
    {
        RecordingClock.Format(TimeSpan.FromSeconds(-5)).Should().Be("00:00");
    }
}
