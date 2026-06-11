using WhisperTray.Core.Hotkeys;

namespace WhisperTray.Core.Configuration;

/// <summary>
/// Pure validation for the editable <see cref="Settings"/> surface. Returns a
/// human-readable list of errors; the settings window blocks Save on any error
/// and surfaces them next to the offending field when possible.
/// </summary>
public static class SettingsValidator
{
    public static IReadOnlyList<string> Validate(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var errors = new List<string>();

        if (!HotkeyCombo.TryParse(settings.Hotkey, out _, out var hotkeyError))
        {
            errors.Add($"Hotkey: {hotkeyError ?? "invalid combination."}");
        }

        if (string.IsNullOrWhiteSpace(settings.BaseUrl)
            || !Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add("Base URL: must be an absolute http(s) URL.");
        }

        if (string.IsNullOrWhiteSpace(settings.Model))
        {
            errors.Add("Model: cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            errors.Add("API key: cannot be empty.");
        }

        if (settings.TimeoutSeconds is < 10 or > 600)
        {
            errors.Add("Timeout: must be a whole number between 10 and 600 seconds.");
        }

        return errors;
    }

    public static bool IsValid(Settings settings) => Validate(settings).Count == 0;
}
