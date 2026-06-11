using System.Text.Json;
using System.Text.Json.Serialization;

namespace WhisperTray.Core.Configuration;

/// <summary>
/// Persists <see cref="Settings"/> to a JSON file under %APPDATA%.
/// If an <see cref="ISecretProtector"/> is supplied the <c>ApiKey</c> is
/// round-tripped through it and serialised as <c>apiKeyProtected</c>. Files
/// that still hold a plaintext <c>apiKey</c> (the MVP boot state) are picked
/// up on Load and automatically migrated on the next Save.
/// </summary>
public sealed class JsonFileSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private readonly ISecretProtector? _protector;

    public JsonFileSettingsStore(string path, ISecretProtector? protector = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _path = path;
        _protector = protector;
    }

    public Settings Load()
    {
        if (!File.Exists(_path))
        {
            return Settings.Default;
        }

        string json;
        try
        {
            json = File.ReadAllText(_path);
        }
        catch (IOException)
        {
            return Settings.Default;
        }

        PersistedSettings? persisted;
        try
        {
            persisted = JsonSerializer.Deserialize<PersistedSettings>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return Settings.Default;
        }

        return persisted is null
            ? Settings.Default
            : FromPersisted(persisted);
    }

    public void Save(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var persisted = ToPersisted(settings);
        var json = JsonSerializer.Serialize(persisted, JsonOptions);
        File.WriteAllText(_path, json);
    }

    private Settings FromPersisted(PersistedSettings p) => new()
    {
        Hotkey = p.Hotkey ?? Settings.Default.Hotkey,
        Autostart = p.Autostart ?? Settings.Default.Autostart,
        AudioDeviceId = p.AudioDeviceId,
        Provider = p.Provider ?? Settings.Default.Provider,
        BaseUrl = p.BaseUrl ?? Settings.Default.BaseUrl,
        Model = p.Model ?? Settings.Default.Model,
        ApiKey = ResolveApiKey(p),
        Language = p.Language,
        PromptHint = p.PromptHint ?? Settings.Default.PromptHint,
        AudioFormat = p.AudioFormat ?? Settings.Default.AudioFormat,
        InjectionMode = p.InjectionMode ?? Settings.Default.InjectionMode,
        TimeoutSeconds = p.TimeoutSeconds is > 0 ? p.TimeoutSeconds.Value : Settings.Default.TimeoutSeconds,
    };

    private string? ResolveApiKey(PersistedSettings p)
    {
        // Prefer an encrypted blob when we have a protector and the file is already migrated.
        if (_protector is not null && !string.IsNullOrEmpty(p.ApiKeyProtected))
        {
            var unprotected = _protector.Unprotect(p.ApiKeyProtected);
            if (unprotected is not null)
            {
                return unprotected;
            }
            // Unprotect failed (tampered / copied profile) — fall through to plaintext so the
            // user can at least start the app and re-enter the key via Settings window.
        }
        return p.ApiKey;
    }

    private PersistedSettings ToPersisted(Settings s)
    {
        var persisted = new PersistedSettings
        {
            Hotkey = s.Hotkey,
            Autostart = s.Autostart,
            AudioDeviceId = s.AudioDeviceId,
            Provider = s.Provider,
            BaseUrl = s.BaseUrl,
            Model = s.Model,
            Language = s.Language,
            PromptHint = s.PromptHint,
            AudioFormat = s.AudioFormat,
            InjectionMode = s.InjectionMode,
            TimeoutSeconds = s.TimeoutSeconds,
        };

        if (string.IsNullOrEmpty(s.ApiKey))
        {
            return persisted;
        }

        if (_protector is not null)
        {
            // Migrated / already-encrypted form: only apiKeyProtected is written.
            return persisted with { ApiKeyProtected = _protector.Protect(s.ApiKey) };
        }

        return persisted with { ApiKey = s.ApiKey };
    }

    private sealed record PersistedSettings
    {
        public string? Hotkey { get; init; }
        public bool? Autostart { get; init; }
        public string? AudioDeviceId { get; init; }
        public TranscriptionProvider? Provider { get; init; }
        public string? BaseUrl { get; init; }
        public string? Model { get; init; }
        public string? ApiKey { get; init; }
        public string? ApiKeyProtected { get; init; }
        public string? Language { get; init; }
        public string? PromptHint { get; init; }
        public AudioFormat? AudioFormat { get; init; }
        public InjectionMode? InjectionMode { get; init; }
        public int? TimeoutSeconds { get; init; }
    }
}
