using FluentAssertions;
using WhisperTray.Core.Audio;
using WhisperTray.Core.Tests.TestInfrastructure;

namespace WhisperTray.Core.Tests.Audio;

public class AudioLevelMeterTests
{
    // 1 kHz at 16 kHz is exactly 16 samples per period, so 0.1 s holds whole periods
    // and the RMS of the synthesized sine matches the analytic value.
    private static byte[] Sine(double amplitude) =>
        AudioSamples.SamplesToPcmBytes(AudioSamples.SynthesizeSine(16_000, 1_000, 0.1, amplitude));

    [Fact]
    public void ComputeLevel_ForSilence_IsZero()
    {
        var silence = new byte[3_200];

        AudioLevelMeter.ComputeLevel(silence).Should().Be(0f);
    }

    [Fact]
    public void ComputeLevel_ForEmptyBuffer_IsZero()
    {
        AudioLevelMeter.ComputeLevel(ReadOnlySpan<byte>.Empty).Should().Be(0f);
    }

    [Fact]
    public void ComputeLevel_ForOddByteCount_IgnoresTrailingByte()
    {
        var pcm = AudioSamples.SamplesToPcmBytes(new short[] { 1_000, -1_000 });
        var odd = pcm.Concat(new byte[] { 0x7F }).ToArray();

        AudioLevelMeter.ComputeLevel(odd).Should().Be(AudioLevelMeter.ComputeLevel(pcm));
    }

    [Fact]
    public void ComputeLevel_ForFullScaleSine_IsOne()
    {
        AudioLevelMeter.ComputeLevel(Sine(1.0)).Should().Be(1f);
    }

    [Fact]
    public void ComputeLevel_BelowFloor_IsZero()
    {
        // RMS ≈ -63 dBFS, under the -55 dBFS floor.
        AudioLevelMeter.ComputeLevel(Sine(0.001)).Should().Be(0f);
    }

    [Fact]
    public void ComputeLevel_AtMinus30Dbfs_MapsLinearlyInsideWindow()
    {
        // RMS of a sine is amplitude / sqrt(2); this amplitude gives -30 dBFS.
        var amplitude = Math.Pow(10, -30.0 / 20) * Math.Sqrt(2);
        var expected = (-30.0 - AudioLevelMeter.FloorDbfs) / (AudioLevelMeter.CeilingDbfs - AudioLevelMeter.FloorDbfs);

        AudioLevelMeter.ComputeLevel(Sine(amplitude)).Should().BeApproximately((float)expected, 0.01f);
    }

    [Fact]
    public void ComputeLevel_LouderSignal_ReadsHigher()
    {
        var quiet = AudioLevelMeter.ComputeLevel(Sine(0.01));
        var loud = AudioLevelMeter.ComputeLevel(Sine(0.1));

        loud.Should().BeGreaterThan(quiet);
    }
}
