using System.IO;
using NAudio.Wave;
using WhisperTray.Core.Audio;

namespace WhisperTray.App.Adapters;

/// <summary>
/// Captures 16 kHz 16-bit mono PCM via WaveInEvent. The chosen sample format
/// lets us skip any resampling before handing bytes to the encoder.
/// </summary>
public sealed class NAudioRecorder : IAudioRecorder, IDisposable
{
    private const int SampleRate = 16_000;
    private const int BitsPerSample = 16;
    private const int Channels = 1;

    // 50 ms of audio: two meter readings per 100 ms capture buffer.
    private const int LevelChunkBytes = SampleRate / 20 * (BitsPerSample / 8) * Channels;

    private readonly object _gate = new();
    private WaveInEvent? _waveIn;
    private MemoryStream? _buffer;
    private bool _disposed;

    /// <summary>
    /// Raised on the capture thread with a 0..1 loudness for every ~50 ms of captured
    /// audio. Handlers must be cheap and must not touch UI objects directly.
    /// </summary>
    public event Action<float>? LevelMeasured;

    public bool IsRecording
    {
        get
        {
            lock (_gate)
            {
                return _waveIn is not null;
            }
        }
    }

    public void Start(string? deviceId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            if (_waveIn is not null)
            {
                throw new InvalidOperationException("Recorder is already recording.");
            }

            var deviceIndex = WaveInDeviceEnumerator.ResolveDeviceIndex(deviceId);

            _buffer = new MemoryStream();
            _waveIn = new WaveInEvent
            {
                DeviceNumber = deviceIndex,
                WaveFormat = new WaveFormat(SampleRate, BitsPerSample, Channels),
                BufferMilliseconds = 100,
                NumberOfBuffers = 3,
            };
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;
            _waveIn.StartRecording();
        }
    }

    public RecordedAudio Stop()
    {
        WaveInEvent? waveIn;
        MemoryStream? buffer;

        lock (_gate)
        {
            if (_waveIn is null || _buffer is null)
            {
                throw new InvalidOperationException("Recorder is not currently recording.");
            }
            waveIn = _waveIn;
            buffer = _buffer;
            _waveIn = null;
            _buffer = null;
        }

        waveIn.StopRecording();
        waveIn.DataAvailable -= OnDataAvailable;
        waveIn.RecordingStopped -= OnRecordingStopped;
        waveIn.Dispose();

        return new RecordedAudio(buffer.ToArray(), SampleRate);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_gate)
        {
            if (_waveIn is not null)
            {
                try
                {
                    _waveIn.StopRecording();
                }
                catch
                {
                    // best-effort on shutdown
                }
                _waveIn.Dispose();
                _waveIn = null;
            }
            _buffer?.Dispose();
            _buffer = null;
        }
        _disposed = true;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_gate)
        {
            _buffer?.Write(e.Buffer, 0, e.BytesRecorded);
        }

        PublishLevels(e.Buffer.AsSpan(0, e.BytesRecorded));
    }

    // Reads the same bytes that were just buffered for transcription; never modifies them.
    private void PublishLevels(ReadOnlySpan<byte> pcm)
    {
        var handler = LevelMeasured;
        if (handler is null)
        {
            return;
        }

        try
        {
            for (var offset = 0; offset < pcm.Length; offset += LevelChunkBytes)
            {
                var chunk = pcm.Slice(offset, Math.Min(LevelChunkBytes, pcm.Length - offset));
                handler(AudioLevelMeter.ComputeLevel(chunk));
            }
        }
        catch (Exception ex)
        {
            // A failing meter must never stop the recording itself.
            System.Diagnostics.Debug.WriteLine($"Level meter handler failed: {ex}");
        }
    }

    private static void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        // NAudio raises this on the capture thread after StopRecording completes;
        // we only hook it to surface fatal capture errors into the Debug log.
        if (e.Exception is not null)
        {
            System.Diagnostics.Debug.WriteLine($"WaveInEvent stopped with error: {e.Exception}");
        }
    }
}
