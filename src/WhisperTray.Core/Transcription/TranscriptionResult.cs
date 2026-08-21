namespace WhisperTray.Core.Transcription;

public sealed record TranscriptionResult(
    string Text,
    string? DetectedLanguage = null,
    TranscriptionUsage? Usage = null);
