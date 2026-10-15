using System.Diagnostics;
using System.Windows.Interop;
using System.Windows.Threading;
using WhisperTray.App.Adapters;
using WhisperTray.Core.Audio;
using WhisperTray.Core.Orchestration;

namespace WhisperTray.App.Views;

/// <summary>
/// Drives <see cref="RecordingOverlayWindow"/> from orchestrator state and live
/// microphone levels. State changes arrive on the UI thread or the thread pool, levels on
/// the capture thread; everything that touches the window is posted with BeginInvoke, so
/// neither the orchestrator nor the recorder ever waits on the UI.
/// </summary>
public sealed class RecordingOverlayController : IDisposable
{
    // Injecting is usually over in a few milliseconds; only relabel the panel if it lasts,
    // so the text does not flash before the panel hides.
    private static readonly TimeSpan InjectingLabelDelay = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(50);

    private readonly Dispatcher _dispatcher;
    private readonly Orchestrator _orchestrator;
    private readonly NAudioRecorder _recorder;
    private readonly Func<string> _hotkey;
    private readonly AudioLevelHistory _levels = new(RecordingOverlayWindow.BarCount);
    private readonly float[] _snapshot = new float[RecordingOverlayWindow.BarCount];
    private readonly Stopwatch _elapsed = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _injectingTimer;
    private RecordingOverlayWindow? _window;
    private volatile bool _acceptLevels;
    private bool _disposed;

    public RecordingOverlayController(
        Dispatcher dispatcher,
        Orchestrator orchestrator,
        NAudioRecorder recorder,
        Func<string> hotkey)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentNullException.ThrowIfNull(hotkey);

        _dispatcher = dispatcher;
        _orchestrator = orchestrator;
        _recorder = recorder;
        _hotkey = hotkey;

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Render, dispatcher) { Interval = RefreshInterval };
        _refreshTimer.Tick += OnRefreshTick;
        _injectingTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = InjectingLabelDelay };
        _injectingTimer.Tick += OnInjectingTimerTick;

        _orchestrator.StateChanged += OnStateChanged;
        _recorder.LevelMeasured += OnLevelMeasured;
    }

    /// <summary>Must be called on the dispatcher thread (App.OnExit).</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        _orchestrator.StateChanged -= OnStateChanged;
        _recorder.LevelMeasured -= OnLevelMeasured;
        _acceptLevels = false;
        _refreshTimer.Stop();
        _injectingTimer.Stop();
        _window?.Close();
        _window = null;
    }

    private void OnLevelMeasured(float level)
    {
        if (_acceptLevels)
        {
            _levels.Add(level);
        }
    }

    private void OnStateChanged(object? sender, OrchestratorState state) =>
        _dispatcher.BeginInvoke(() => Apply(state));

    private void Apply(OrchestratorState state)
    {
        if (_disposed)
        {
            return;
        }

        _injectingTimer.Stop();

        switch (state)
        {
            case OrchestratorState.Recording:
                StartRecordingVisuals();
                break;

            case OrchestratorState.Transcribing:
                StopRecordingVisuals();
                _window?.ShowProcessing("Распознавание…");
                break;

            case OrchestratorState.Injecting:
                StopRecordingVisuals();
                _injectingTimer.Start();
                break;

            default:
                StopRecordingVisuals();
                _window?.HideOverlay();
                break;
        }
    }

    private void StartRecordingVisuals()
    {
        _levels.Clear();
        _acceptLevels = true;
        _elapsed.Restart();

        var window = EnsureWindow();
        window.ShowRecording(_hotkey());
        // The overlay never activates, so the foreground window is still the dictation target.
        window.PlaceOnMonitorOf(NativeMethods.GetForegroundWindow());
        window.Show();
        _refreshTimer.Start();
    }

    private void StopRecordingVisuals()
    {
        _acceptLevels = false;
        _refreshTimer.Stop();
        _elapsed.Stop();
    }

    private RecordingOverlayWindow EnsureWindow()
    {
        if (_window is null)
        {
            _window = new RecordingOverlayWindow();
            // Create the HWND up front: applies the no-activate/click-through styles before
            // the first Show and lets PlaceOnMonitorOf convert device pixels to DIPs.
            _ = new WindowInteropHelper(_window).EnsureHandle();
        }
        return _window;
    }

    private void OnRefreshTick(object? sender, EventArgs e)
    {
        if (_window is null)
        {
            return;
        }

        _levels.CopyTo(_snapshot);
        _window.UpdateRecording(RecordingClock.Format(_elapsed.Elapsed), _snapshot);
    }

    private void OnInjectingTimerTick(object? sender, EventArgs e)
    {
        _injectingTimer.Stop();
        if (_orchestrator.State == OrchestratorState.Injecting)
        {
            _window?.ShowProcessing("Вставка текста…");
        }
    }
}
