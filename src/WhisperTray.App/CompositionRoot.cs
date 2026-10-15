using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using WhisperTray.App.Adapters;
using WhisperTray.App.Tray;
using WhisperTray.App.Views;
using WhisperTray.Core.Analytics;
using WhisperTray.Core.Configuration;
using WhisperTray.Core.Hotkeys;
using WhisperTray.Core.Injection;
using WhisperTray.Core.Orchestration;
using WpfApplication = System.Windows.Application;

namespace WhisperTray.App;

/// <summary>
/// Composes every service in the app. Lives for the duration of the process,
/// disposed on App.OnExit. Mutable current-settings snapshot is updated when
/// the Settings window saves (Phase 9); for now Settings are read from disk
/// once at startup.
/// </summary>
public sealed class CompositionRoot : IDisposable
{
    private readonly HttpClient _http;
    private readonly GlobalHotkeyService _hotkey;
    private readonly NAudioRecorder _recorder;
    private readonly TrayAppHost _tray;
    private readonly Orchestrator _orchestrator;
    private readonly ISettingsStore _settingsStore;
    private readonly IAutostartService _autostart;
    private readonly RecordingOverlayController _overlay;
    private Settings _currentSettings;

    public CompositionRoot(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WhisperTray");
        var settingsPath = Path.Combine(configDir, "settings.json");
        // DPAPI-backed per-user encryption is active from Phase 9 onward. The store
        // transparently migrates any pre-existing plaintext apiKey on the first Save
        // driven by the Settings window.
        var protector = new DpapiSecretProtector();
        _settingsStore = new JsonFileSettingsStore(settingsPath, protector);
        _currentSettings = _settingsStore.Load();

        _autostart = new RegistryAutostartService(new HkcuRunRegistryKey());

        var analytics = new CsvUsageAnalytics(Path.Combine(configDir, "analytics"));

        _recorder = new NAudioRecorder();
        var encoderFactory = new DefaultAudioEncoderFactory();
        var clientFactory = new HttpTranscriptionClientFactory(_http);
        var clipboard = new WpfClipboardService(dispatcher);
        var typist = new SendInputTextTypist();
        var delayExecutor = new TaskDelayedExecutor();
        var injector = new CascadingTextInjector(clipboard, typist, delayExecutor);
        var foreground = new Win32ForegroundWindowService();

        _tray = new TrayAppHost(dispatcher);
        _hotkey = new GlobalHotkeyService(dispatcher);

        _orchestrator = new Orchestrator(
            _recorder,
            encoderFactory,
            clientFactory,
            injector,
            foreground,
            _tray,
            analytics,
            () => _currentSettings);

        _orchestrator.StateChanged += (_, state) => _tray.UpdateState(state);
        _overlay = new RecordingOverlayController(dispatcher, _orchestrator, _recorder, () => _currentSettings.Hotkey);
        _hotkey.Toggled += OnHotkeyToggled;
        _tray.SettingsRequested += OnSettingsRequested;
        _tray.QuitRequested += (_, _) => WpfApplication.Current.Shutdown();
    }

    public void Start()
    {
        if (HotkeyCombo.TryParse(_currentSettings.Hotkey, out var combo, out var hotkeyError) && combo is not null)
        {
            try
            {
                _hotkey.Register(combo);
            }
            catch (Exception ex)
            {
                _tray.NotifyError("Hotkey registration failed", ex.Message);
            }
        }
        else
        {
            _tray.NotifyWarning("Invalid hotkey", hotkeyError ?? "Hotkey could not be parsed.");
        }

        // Keep the Run-key entry in sync with the saved Autostart preference. This also
        // self-heals: if the exe was moved between sessions, the next startup rewrites
        // the entry with the current path.
        SyncAutostart(_currentSettings.Autostart);

        if (string.IsNullOrWhiteSpace(_currentSettings.ApiKey))
        {
            _tray.NotifyInfo(
                "Configure API key",
                "Right-click the tray icon and open Settings to finish setup.");
        }
    }

    private void SyncAutostart(bool enabled)
    {
        try
        {
            if (enabled)
            {
                var exePath = Environment.ProcessPath
                    ?? Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath))
                {
                    _tray.NotifyWarning(
                        "Autostart unavailable",
                        "Could not determine the WhisperTray executable path.");
                    return;
                }
                _autostart.Enable(exePath);
            }
            else
            {
                _autostart.Disable();
            }
        }
        catch (Exception ex)
        {
            _tray.NotifyError("Autostart update failed", ex.Message);
        }
    }

    public void Dispose()
    {
        _hotkey.Toggled -= OnHotkeyToggled;
        _hotkey.Dispose();
        _overlay.Dispose();
        _recorder.Dispose();
        _tray.Dispose();
        _http.Dispose();
    }

    private async void OnHotkeyToggled(object? sender, EventArgs e)
    {
        try
        {
            await _orchestrator.ToggleAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _tray.NotifyError("Unexpected failure", ex.Message);
        }
    }

    private void OnSettingsRequested(object? sender, EventArgs e)
    {
        var window = new SettingsWindow(_currentSettings)
        {
            Owner = WpfApplication.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsVisible),
        };

        var confirmed = window.ShowDialog();
        if (confirmed != true || window.SavedSettings is null)
        {
            return;
        }

        ApplySavedSettings(window.SavedSettings);
    }

    private void ApplySavedSettings(Settings updated)
    {
        var previousHotkey = _currentSettings.Hotkey;

        try
        {
            _settingsStore.Save(updated);
        }
        catch (Exception ex)
        {
            _tray.NotifyError("Settings not saved", ex.Message);
            return;
        }

        var previousAutostart = _currentSettings.Autostart;
        _currentSettings = updated;

        if (updated.Autostart != previousAutostart)
        {
            SyncAutostart(updated.Autostart);
        }

        // Hotkey may have changed — re-register. Even when unchanged, re-registering is
        // harmless and keeps the code path simple.
        if (!string.Equals(previousHotkey, updated.Hotkey, StringComparison.Ordinal))
        {
            if (HotkeyCombo.TryParse(updated.Hotkey, out var combo, out var parseError) && combo is not null)
            {
                try
                {
                    _hotkey.Unregister();
                    _hotkey.Register(combo);
                }
                catch (Exception ex)
                {
                    _tray.NotifyError("Hotkey registration failed", ex.Message);
                }
            }
            else
            {
                _tray.NotifyWarning("Invalid hotkey", parseError ?? "Hotkey could not be parsed.");
            }
        }

        _tray.NotifyInfo("Settings saved", "New preferences take effect immediately.");
    }
}
