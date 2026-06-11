using WhisperTray.Core.Audio;
using WhisperTray.Core.Configuration;
using WhisperTray.Core.Injection;
using WhisperTray.Core.Transcription;

namespace WhisperTray.Core.Orchestration;

/// <summary>
/// Drives the record-transcribe-inject lifecycle. Each hotkey press is a call to
/// <see cref="ToggleAsync"/>:
///   - From Idle       -> captures foreground, starts the recorder, enters Recording.
///   - From Recording  -> stops the recorder, runs the async pipeline
///                        (encode -> transcribe -> inject), returns to Idle.
///   - From Transcribing / Injecting -> ignored with a notification.
/// All transcription errors are converted into notifications; the state machine
/// always settles back to Idle.
/// </summary>
public sealed class Orchestrator
{
    private readonly IAudioRecorder _recorder;
    private readonly IAudioEncoderFactory _encoderFactory;
    private readonly ITranscriptionClientFactory _clientFactory;
    private readonly ITextInjector _injector;
    private readonly IForegroundWindowService _foreground;
    private readonly INotificationService _notifier;
    private readonly Func<Settings> _settings;
    private readonly object _gate = new();

    private ForegroundWindowInfo? _capturedTarget;

    public Orchestrator(
        IAudioRecorder recorder,
        IAudioEncoderFactory encoderFactory,
        ITranscriptionClientFactory clientFactory,
        ITextInjector injector,
        IForegroundWindowService foreground,
        INotificationService notifier,
        Func<Settings> settings)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentNullException.ThrowIfNull(encoderFactory);
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(injector);
        ArgumentNullException.ThrowIfNull(foreground);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(settings);

        _recorder = recorder;
        _encoderFactory = encoderFactory;
        _clientFactory = clientFactory;
        _injector = injector;
        _foreground = foreground;
        _notifier = notifier;
        _settings = settings;
    }

    public OrchestratorState State { get; private set; } = OrchestratorState.Idle;

    public event EventHandler<OrchestratorState>? StateChanged;

    public async Task ToggleAsync(CancellationToken cancellationToken = default)
    {
        Task? pipeline = null;

        lock (_gate)
        {
            switch (State)
            {
                case OrchestratorState.Idle:
                    StartRecording();
                    break;

                case OrchestratorState.Recording:
                    pipeline = StopAndRunPipeline(cancellationToken);
                    break;

                default:
                    _notifier.NotifyInfo(
                        "Still processing",
                        "The previous dictation is still being transcribed.");
                    break;
            }
        }

        if (pipeline is not null)
        {
            await pipeline.ConfigureAwait(false);
        }
    }

    private void StartRecording()
    {
        var settings = _settings();
        _capturedTarget = _foreground.Capture();
        _recorder.Start(settings.AudioDeviceId);
        SetState(OrchestratorState.Recording);
    }

    private Task StopAndRunPipeline(CancellationToken cancellationToken)
    {
        var audio = _recorder.Stop();
        var target = _capturedTarget;
        SetState(OrchestratorState.Transcribing);
        return RunPipelineAsync(audio, target, cancellationToken);
    }

    private async Task RunPipelineAsync(RecordedAudio audio, ForegroundWindowInfo? target, CancellationToken cancellationToken)
    {
        var settings = _settings();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (settings.TimeoutSeconds > 0)
        {
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        }

        try
        {
            if (target is null)
            {
                _notifier.NotifyError("Internal error", "No foreground window was captured for the session.");
                return;
            }

            var encoder = _encoderFactory.Create(settings.AudioFormat);
            var encoded = encoder.Encode(audio.Samples, audio.SampleRate);

            var client = _clientFactory.Create(settings);
            var result = await client.TranscribeAsync(
                new TranscriptionRequest
                {
                    AudioBytes = encoded,
                    ContentType = encoder.ContentType,
                    FileName = "audio" + encoder.FileExtension,
                    Model = settings.Model,
                    Language = settings.Language,
                    Prompt = string.IsNullOrWhiteSpace(settings.PromptHint) ? null : settings.PromptHint,
                },
                timeoutCts.Token).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(result.Text))
            {
                _notifier.NotifyInfo("No speech detected", "The recording produced an empty transcription.");
                return;
            }

            SetState(OrchestratorState.Injecting);
            var injection = _injector.Inject(result.Text, target, settings.InjectionMode);
            if (!string.IsNullOrEmpty(injection.Reason))
            {
                _notifier.NotifyInfo("Text routed to clipboard", injection.Reason);
            }
        }
        catch (TranscriptionAuthException ex)
        {
            _notifier.NotifyError("Authentication failed", ex.Message);
        }
        catch (TranscriptionRateLimitException ex)
        {
            _notifier.NotifyWarning("Rate limited", ex.Message);
        }
        catch (TranscriptionException ex)
        {
            _notifier.NotifyError("Transcription failed", ex.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _notifier.NotifyError(
                "Transcription timed out",
                $"The provider did not respond within {settings.TimeoutSeconds} seconds. "
                + "The timeout can be changed in Settings.");
        }
        catch (OperationCanceledException)
        {
            _notifier.NotifyInfo("Cancelled", "Transcription was cancelled.");
        }
        catch (Exception ex)
        {
            _notifier.NotifyError("Unexpected error", ex.Message);
        }
        finally
        {
            SetState(OrchestratorState.Idle);
            _capturedTarget = null;
        }
    }

    private void SetState(OrchestratorState next)
    {
        if (State == next)
        {
            return;
        }
        State = next;
        StateChanged?.Invoke(this, next);
    }
}
