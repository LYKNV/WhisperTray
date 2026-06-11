using FluentAssertions;
using WhisperTray.Core.Configuration;

namespace WhisperTray.Core.Tests;

public class SettingsValidatorTests
{
    private static Settings ValidSample() => Settings.Default with { ApiKey = "sk-x" };

    [Fact]
    public void Validate_ValidSettings_ReturnsNoErrors()
    {
        SettingsValidator.Validate(ValidSample()).Should().BeEmpty();
    }

    [Fact]
    public void Validate_UnparseableHotkey_ReportsError()
    {
        var bad = ValidSample() with { Hotkey = "just-space" };

        SettingsValidator.Validate(bad).Should().Contain(e => e.StartsWith("Hotkey:"));
    }

    [Fact]
    public void Validate_BaseUrlNotAbsolute_ReportsError()
    {
        var bad = ValidSample() with { BaseUrl = "api.openai.com" };

        SettingsValidator.Validate(bad).Should().Contain(e => e.StartsWith("Base URL:"));
    }

    [Fact]
    public void Validate_BaseUrlWithUnsupportedScheme_ReportsError()
    {
        var bad = ValidSample() with { BaseUrl = "ftp://legacy.example/v1" };

        SettingsValidator.Validate(bad).Should().Contain(e => e.StartsWith("Base URL:"));
    }

    [Fact]
    public void Validate_EmptyModel_ReportsError()
    {
        var bad = ValidSample() with { Model = "   " };

        SettingsValidator.Validate(bad).Should().Contain(e => e.StartsWith("Model:"));
    }

    [Fact]
    public void Validate_EmptyApiKey_ReportsError()
    {
        var bad = ValidSample() with { ApiKey = null };

        SettingsValidator.Validate(bad).Should().Contain(e => e.StartsWith("API key:"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(9)]
    [InlineData(601)]
    public void Validate_TimeoutOutOfRange_ReportsError(int seconds)
    {
        var bad = ValidSample() with { TimeoutSeconds = seconds };

        SettingsValidator.Validate(bad).Should().Contain(e => e.StartsWith("Timeout:"));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(120)]
    [InlineData(600)]
    public void Validate_TimeoutWithinRange_NoError(int seconds)
    {
        var ok = ValidSample() with { TimeoutSeconds = seconds };

        SettingsValidator.Validate(ok).Should().BeEmpty();
    }

    [Fact]
    public void IsValid_MirrorsValidate()
    {
        SettingsValidator.IsValid(ValidSample()).Should().BeTrue();
        SettingsValidator.IsValid(ValidSample() with { ApiKey = null }).Should().BeFalse();
    }
}
