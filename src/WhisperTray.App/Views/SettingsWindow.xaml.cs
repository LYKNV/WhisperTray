using System.Windows;
using System.Windows.Controls;
using WhisperTray.Core.Configuration;
using WhisperTray.Core.Transcription;
using WpfInput = System.Windows.Input;

namespace WhisperTray.App.Views;

/// <summary>
/// Modal editor for <see cref="Settings"/>. Opens with the current snapshot, lets the
/// user change every field, and exposes the edited result via <see cref="SavedSettings"/>
/// when the user confirms. The composition root owns persisting the returned object and
/// re-registering the hotkey.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly Settings _initial;
    private bool _baseUrlTouchedByUser;
    private bool _modelTouchedByUser;

    public SettingsWindow(Settings initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        InitializeComponent();
        _initial = initial;

        PopulateProviderCombo();
        PopulateAudioFormatCombo();
        PopulateInjectionModeCombo();
        PopulateFromSettings(initial);

        // Attach user-intent watchers AFTER seeding so that PopulateFromSettings itself
        // doesn't flip the "touched" flags.
        BaseUrlBox.TextChanged += (_, _) => _baseUrlTouchedByUser = true;
        ModelCombo.AddHandler(
            System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler((_, _) => _modelTouchedByUser = true));
    }

    public Settings? SavedSettings { get; private set; }

    private void PopulateProviderCombo()
    {
        ProviderCombo.ItemsSource = Enum.GetValues<TranscriptionProvider>();
    }

    private void PopulateAudioFormatCombo()
    {
        AudioFormatCombo.ItemsSource = Enum.GetValues<AudioFormat>();
    }

    private void PopulateInjectionModeCombo()
    {
        InjectionModeCombo.ItemsSource = Enum.GetValues<InjectionMode>();
    }

    private void PopulateFromSettings(Settings s)
    {
        HotkeyBox.Text = s.Hotkey;
        AutostartCheck.IsChecked = s.Autostart;
        ProviderCombo.SelectedItem = s.Provider;
        BaseUrlBox.Text = s.BaseUrl;
        PopulateModelCombo(s.Provider, s.Model);
        ApiKeyBox.Password = s.ApiKey ?? string.Empty;
        LanguageBox.Text = s.Language ?? string.Empty;
        PromptBox.Text = s.PromptHint;
        AudioFormatCombo.SelectedItem = s.AudioFormat;
        InjectionModeCombo.SelectedItem = s.InjectionMode;
        TimeoutBox.Text = s.TimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private void PopulateModelCombo(TranscriptionProvider provider, string currentModel)
    {
        var models = ProviderModelCatalog.ModelsFor(provider);
        ModelCombo.ItemsSource = models;
        ModelCombo.Text = currentModel;
    }

    private void ProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProviderCombo.SelectedItem is not TranscriptionProvider provider)
        {
            return;
        }

        // When the user picks a different provider, reset fields they haven't manually
        // edited yet. If they *have* edited BaseUrl/Model by hand we preserve their text.
        if (!_baseUrlTouchedByUser)
        {
            BaseUrlBox.Text = ProviderDefaults.DefaultBaseUrl(provider);
            _baseUrlTouchedByUser = false;
        }

        var wasTouched = _modelTouchedByUser;
        var nextModel = wasTouched ? ModelCombo.Text : ProviderDefaults.DefaultModel(provider);
        PopulateModelCombo(provider, nextModel);
        _modelTouchedByUser = wasTouched;
    }

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        HotkeyBox.SelectAll();
    }

    private void HotkeyBox_PreviewKeyDown(object sender, WpfInput.KeyEventArgs e)
    {
        e.Handled = true;

        // Ignore pure modifier presses — wait for a real base key.
        var key = e.Key == WpfInput.Key.System ? e.SystemKey : e.Key;
        if (IsModifierOnly(key))
        {
            return;
        }

        if (key == WpfInput.Key.Escape)
        {
            HotkeyBox.Text = _initial.Hotkey;
            return;
        }

        var parts = new List<string>(4);
        if ((WpfInput.Keyboard.Modifiers & WpfInput.ModifierKeys.Control) != 0)
        {
            parts.Add("Ctrl");
        }
        if ((WpfInput.Keyboard.Modifiers & WpfInput.ModifierKeys.Alt) != 0)
        {
            parts.Add("Alt");
        }
        if ((WpfInput.Keyboard.Modifiers & WpfInput.ModifierKeys.Shift) != 0)
        {
            parts.Add("Shift");
        }
        if ((WpfInput.Keyboard.Modifiers & WpfInput.ModifierKeys.Windows) != 0)
        {
            parts.Add("Win");
        }

        var keyName = MapKeyToHotkeyToken(key);
        if (keyName is null || parts.Count == 0)
        {
            // Need at least one modifier + a known base key.
            return;
        }

        parts.Add(keyName);
        HotkeyBox.Text = string.Join("+", parts);
    }

    private static bool IsModifierOnly(WpfInput.Key key) => key
        is WpfInput.Key.LeftCtrl or WpfInput.Key.RightCtrl
        or WpfInput.Key.LeftAlt or WpfInput.Key.RightAlt
        or WpfInput.Key.LeftShift or WpfInput.Key.RightShift
        or WpfInput.Key.LWin or WpfInput.Key.RWin;

    private static string? MapKeyToHotkeyToken(WpfInput.Key key)
    {
        // Letters A–Z and F1–F24 map verbatim via ToString().
        if (key >= WpfInput.Key.A && key <= WpfInput.Key.Z)
        {
            return key.ToString();
        }
        if (key >= WpfInput.Key.F1 && key <= WpfInput.Key.F24)
        {
            return key.ToString();
        }
        // Digits: Key.D0..Key.D9 → "0".."9".
        if (key >= WpfInput.Key.D0 && key <= WpfInput.Key.D9)
        {
            return ((int)(key - WpfInput.Key.D0)).ToString();
        }
        if (key >= WpfInput.Key.NumPad0 && key <= WpfInput.Key.NumPad9)
        {
            return "Numpad" + (int)(key - WpfInput.Key.NumPad0);
        }
        return key switch
        {
            WpfInput.Key.Space => "Space",
            WpfInput.Key.Tab => "Tab",
            WpfInput.Key.Enter => "Enter",
            WpfInput.Key.Back => "Backspace",
            WpfInput.Key.Insert => "Insert",
            WpfInput.Key.Delete => "Delete",
            WpfInput.Key.Home => "Home",
            WpfInput.Key.End => "End",
            WpfInput.Key.PageUp => "PageUp",
            WpfInput.Key.PageDown => "PageDown",
            WpfInput.Key.Left => "Left",
            WpfInput.Key.Right => "Right",
            WpfInput.Key.Up => "Up",
            WpfInput.Key.Down => "Down",
            WpfInput.Key.PrintScreen => "PrintScreen",
            WpfInput.Key.Pause => "Pause",
            WpfInput.Key.CapsLock => "CapsLock",
            WpfInput.Key.NumLock => "NumLock",
            WpfInput.Key.Scroll => "ScrollLock",
            WpfInput.Key.Multiply => "NumpadMultiply",
            WpfInput.Key.Add => "NumpadAdd",
            WpfInput.Key.Subtract => "NumpadSubtract",
            WpfInput.Key.Decimal => "NumpadDecimal",
            WpfInput.Key.Divide => "NumpadDivide",
            _ => null,
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var edited = BuildSettingsFromForm();
        var errors = SettingsValidator.Validate(edited);
        if (errors.Count > 0)
        {
            ErrorText.Text = string.Join(Environment.NewLine, errors);
            ErrorPanel.Visibility = Visibility.Visible;
            return;
        }

        SavedSettings = edited;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        SavedSettings = null;
        DialogResult = false;
        Close();
    }

    private Settings BuildSettingsFromForm()
    {
        var language = string.IsNullOrWhiteSpace(LanguageBox.Text) ? null : LanguageBox.Text.Trim();
        var provider = ProviderCombo.SelectedItem is TranscriptionProvider picked ? picked : _initial.Provider;
        var audioFormat = AudioFormatCombo.SelectedItem is AudioFormat fmt ? fmt : _initial.AudioFormat;
        var injection = InjectionModeCombo.SelectedItem is InjectionMode mode ? mode : _initial.InjectionMode;
        var timeoutSeconds = int.TryParse(TimeoutBox.Text.Trim(), out var parsedTimeout) ? parsedTimeout : -1;

        return _initial with
        {
            Hotkey = HotkeyBox.Text,
            Autostart = AutostartCheck.IsChecked ?? false,
            Provider = provider,
            BaseUrl = BaseUrlBox.Text.Trim(),
            Model = (ModelCombo.Text ?? string.Empty).Trim(),
            ApiKey = string.IsNullOrEmpty(ApiKeyBox.Password) ? null : ApiKeyBox.Password,
            Language = language,
            PromptHint = PromptBox.Text,
            AudioFormat = audioFormat,
            InjectionMode = injection,
            TimeoutSeconds = timeoutSeconds,
        };
    }
}
