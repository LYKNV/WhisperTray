namespace WhisperTray.Core.Audio;

/// <summary>
/// Fixed-size, thread-safe ring of the most recent meter levels. The capture thread
/// adds; the UI thread copies a snapshot ordered oldest to newest. Slots that have not
/// been filled yet read as 0, so a fresh recording starts with flat bars.
/// </summary>
public sealed class AudioLevelHistory
{
    private readonly object _gate = new();
    private readonly float[] _ring;
    private int _next;
    private int _count;

    public AudioLevelHistory(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _ring = new float[capacity];
    }

    public int Capacity => _ring.Length;

    public void Add(float level)
    {
        var clamped = float.IsNaN(level) ? 0f : Math.Clamp(level, 0f, 1f);

        lock (_gate)
        {
            _ring[_next] = clamped;
            _next = (_next + 1) % _ring.Length;
            if (_count < _ring.Length)
            {
                _count++;
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            Array.Clear(_ring);
            _next = 0;
            _count = 0;
        }
    }

    /// <summary>
    /// Writes exactly <see cref="Capacity"/> values, oldest first and newest last.
    /// While the ring is not yet full the leading slots are zero.
    /// </summary>
    public void CopyTo(Span<float> destination)
    {
        if (destination.Length != _ring.Length)
        {
            throw new ArgumentException(
                $"Destination must hold exactly {_ring.Length} values.", nameof(destination));
        }

        lock (_gate)
        {
            var missing = _ring.Length - _count;
            destination[..missing].Clear();

            var start = (_next - _count + _ring.Length) % _ring.Length;
            for (var i = 0; i < _count; i++)
            {
                destination[missing + i] = _ring[(start + i) % _ring.Length];
            }
        }
    }
}
