using System.Runtime.InteropServices;

namespace WhisperTray.Core.Audio;

/// <summary>
/// Turns a chunk of 16-bit little-endian mono PCM into a 0..1 loudness value for UI
/// meters. RMS is mapped onto a dBFS window so a quiet room sits near zero and normal
/// speech fills most of the range. Pure and allocation-free, so it is safe to call on
/// the capture thread.
/// </summary>
public static class AudioLevelMeter
{
    /// <summary>RMS at or below this level reads as 0 (room noise, idle microphone).</summary>
    public const double FloorDbfs = -55.0;

    /// <summary>RMS at or above this level reads as 1 (loud, close speech).</summary>
    public const double CeilingDbfs = -10.0;

    public static float ComputeLevel(ReadOnlySpan<byte> pcm16)
    {
        // A trailing odd byte cannot form a sample; ignore it rather than throw.
        var samples = MemoryMarshal.Cast<byte, short>(pcm16[..(pcm16.Length & ~1)]);
        if (samples.IsEmpty)
        {
            return 0f;
        }

        double sumOfSquares = 0;
        foreach (var sample in samples)
        {
            double value = sample;
            sumOfSquares += value * value;
        }

        var rms = Math.Sqrt(sumOfSquares / samples.Length) / 32768.0;
        if (rms <= 0)
        {
            return 0f;
        }

        var dbfs = 20.0 * Math.Log10(rms);
        var normalized = (dbfs - FloorDbfs) / (CeilingDbfs - FloorDbfs);
        return (float)Math.Clamp(normalized, 0.0, 1.0);
    }
}
