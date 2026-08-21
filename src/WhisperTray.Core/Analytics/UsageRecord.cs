using WhisperTray.Core.Configuration;
using WhisperTray.Core.Transcription;

namespace WhisperTray.Core.Analytics;

/// <summary>
/// One row of the usage log. Carries no timestamp — the sink stamps the row from
/// its own clock.
/// </summary>
public sealed record UsageRecord
{
    public required UsageStatus Status { get; init; }

    /// <summary>Null unless <see cref="Status"/> is <see cref="UsageStatus.Error"/>.</summary>
    public UsageErrorKind? Error { get; init; }

    public required TimeSpan AudioDuration { get; init; }
    public required TranscriptionProvider Provider { get; init; }
    public required string Model { get; init; }

    /// <summary>Null when the provider reported no consumption data.</summary>
    public TranscriptionUsage? Usage { get; init; }
}
