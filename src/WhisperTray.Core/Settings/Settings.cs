namespace WhisperTray.Core.Configuration;

public sealed record Settings
{
    public string Hotkey { get; init; } = "Win+Z";
    public bool Autostart { get; init; }
    public string? AudioDeviceId { get; init; }
    public TranscriptionProvider Provider { get; init; } = TranscriptionProvider.OpenAi;
    public string BaseUrl { get; init; } = "https://api.openai.com/v1";
    public string Model { get; init; } = "gpt-4o-transcribe";
    public string? ApiKey { get; init; }
    public string? Language { get; init; }
    public string PromptHint { get; init; } = "";
    public AudioFormat AudioFormat { get; init; } = AudioFormat.OggOpus;
    public InjectionMode InjectionMode { get; init; } = InjectionMode.Auto;
    public int TimeoutSeconds { get; init; } = 120;

    public static Settings Default { get; } = new();
}
