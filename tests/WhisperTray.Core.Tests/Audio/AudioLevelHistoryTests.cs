using FluentAssertions;
using WhisperTray.Core.Audio;

namespace WhisperTray.Core.Tests.Audio;

public class AudioLevelHistoryTests
{
    [Fact]
    public void Constructor_WithNonPositiveCapacity_Throws()
    {
        var act = () => new AudioLevelHistory(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void CopyTo_WhenEmpty_IsAllZero()
    {
        var history = new AudioLevelHistory(4);
        var snapshot = new float[4];

        history.CopyTo(snapshot);

        snapshot.Should().Equal(0f, 0f, 0f, 0f);
    }

    [Fact]
    public void CopyTo_WhenPartiallyFilled_RightAlignsNewestLast()
    {
        var history = new AudioLevelHistory(4);
        history.Add(0.1f);
        history.Add(0.2f);
        var snapshot = new float[4];

        history.CopyTo(snapshot);

        snapshot.Should().Equal(0f, 0f, 0.1f, 0.2f);
    }

    [Fact]
    public void CopyTo_AfterWrapAround_KeepsMostRecentInOrder()
    {
        var history = new AudioLevelHistory(3);
        foreach (var level in new[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f })
        {
            history.Add(level);
        }
        var snapshot = new float[3];

        history.CopyTo(snapshot);

        snapshot.Should().Equal(0.3f, 0.4f, 0.5f);
    }

    [Fact]
    public void Add_ClampsOutOfRangeAndNaN()
    {
        var history = new AudioLevelHistory(3);
        history.Add(-1f);
        history.Add(2f);
        history.Add(float.NaN);
        var snapshot = new float[3];

        history.CopyTo(snapshot);

        snapshot.Should().Equal(0f, 1f, 0f);
    }

    [Fact]
    public void Clear_ResetsToEmpty()
    {
        var history = new AudioLevelHistory(2);
        history.Add(0.7f);
        history.Add(0.8f);

        history.Clear();
        history.Add(0.5f);
        var snapshot = new float[2];
        history.CopyTo(snapshot);

        snapshot.Should().Equal(0f, 0.5f);
    }

    [Fact]
    public void CopyTo_WithWrongDestinationLength_Throws()
    {
        var history = new AudioLevelHistory(3);

        var act = () => history.CopyTo(new float[2]);

        act.Should().Throw<ArgumentException>();
    }
}
