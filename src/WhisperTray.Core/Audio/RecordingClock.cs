using System.Globalization;

namespace WhisperTray.Core.Audio;

/// <summary>Formats elapsed recording time for the overlay: MM:SS, or H:MM:SS from one hour.</summary>
public static class RecordingClock
{
    public static string Format(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        var hours = (long)elapsed.TotalHours;
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{elapsed.Minutes:00}:{elapsed.Seconds:00}");
    }
}
