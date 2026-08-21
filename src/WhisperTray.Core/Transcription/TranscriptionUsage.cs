namespace WhisperTray.Core.Transcription;

/// <summary>
/// Consumption reported by the provider for one transcription: either token counts
/// or billed audio duration. Most providers report nothing, in which case the whole
/// instance is null rather than zero-filled.
/// </summary>
public sealed record TranscriptionUsage(
    int? InputTokens = null,
    int? OutputTokens = null,
    int? TotalTokens = null,
    double? BilledSeconds = null);
