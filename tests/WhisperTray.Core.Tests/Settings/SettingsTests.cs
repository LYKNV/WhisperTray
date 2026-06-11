using FluentAssertions;
using WhisperTray.Core.Configuration;

namespace WhisperTray.Core.Tests;

public class SettingsTests
{
    [Fact]
    public void Default_HasExpectedDefaults()
    {
        var defaults = Settings.Default;

        defaults.Hotkey.Should().Be("Win+Z");
        defaults.Autostart.Should().BeFalse();
        defaults.Provider.Should().Be(TranscriptionProvider.OpenAi);
        defaults.BaseUrl.Should().Be("https://api.openai.com/v1");
        defaults.Model.Should().Be("gpt-4o-transcribe");
        defaults.Language.Should().BeNull();
        defaults.PromptHint.Should().Be(string.Empty);
        defaults.AudioFormat.Should().Be(AudioFormat.OggOpus);
        defaults.InjectionMode.Should().Be(InjectionMode.Auto);
        defaults.TimeoutSeconds.Should().Be(120);
        defaults.AudioDeviceId.Should().BeNull();
        defaults.ApiKey.Should().BeNull();
    }

    [Fact]
    public void Record_Equality_WorksByValue()
    {
        var a = Settings.Default with { Hotkey = "Ctrl+Shift+D" };
        var b = Settings.Default with { Hotkey = "Ctrl+Shift+D" };

        a.Should().Be(b);
    }
}
