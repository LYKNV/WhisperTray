using WhisperTray.Core.Analytics;
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
    private readonly IUsageAnalytics _analytics;
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
        IUsageAnalytics analytics,
        Func<Settings> settings)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentNullException.ThrowIfNull(encoderFactory);
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(injector);
        ArgumentNullException.ThrowIfNull(foreground);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(analytics);
        ArgumentNullException.ThrowIfNull(settings);

        _recorder = recorder;
        _encoderFactory = encoderFactory;
        _clientFactory = clientFactory;
        _injector = injector;
        _foreground = foreground;
        _notifier = notifier;
        _analytics = analytics;
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

        // Defaults describe the worst case, so an unannotated exit path is logged as
        // an unexpected error rather than silently as a success.
        var status = UsageStatus.Error;
        var errorKind = UsageErrorKind.Unexpected;
        TranscriptionUsage? usage = null;

        try
        {
            if (target is null)
            {
                errorKind = UsageErrorKind.NoTarget;
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

            usage = result.Usage;

            if (string.IsNullOrWhiteSpace(result.Text))
            {
                status = UsageStatus.Empty;
                _notifier.NotifyInfo("No speech detected", "The recording produced an empty transcription.");
                return;
            }

            SetState(OrchestratorState.Injecting);
            var injection = _injector.Inject(result.Text, target, settings.InjectionMode);
            status = UsageStatus.Ok;
            if (!string.IsNullOrEmpty(injection.Reason))
            {
                _notifier.NotifyInfo("Text routed to clipboard", injection.Reason);
            }
        }
        catch (TranscriptionAuthException ex)
        {
            errorKind = UsageErrorKind.Auth;
            _notifier.NotifyError("Authentication failed", ex.Message);
        }
        catch (TranscriptionRateLimitException ex)
        {
            errorKind = UsageErrorKind.RateLimit;
            _notifier.NotifyWarning("Rate limited", ex.Message);
        }
        catch (TranscriptionRequestException ex)
        {
            errorKind = UsageErrorKind.Request;
            _notifier.NotifyError("Transcription failed", ex.Message);
        }
        catch (TranscriptionServerException ex)
        {
            errorKind = UsageErrorKind.Server;
            _notifier.NotifyError("Transcription failed", ex.Message);
        }
        catch (TranscriptionException ex)
        {
            errorKind = UsageErrorKind.Unexpected;
            _notifier.NotifyError("Transcription failed", ex.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            errorKind = UsageErrorKind.Timeout;
            _notifier.NotifyError(
                "Transcription timed out",
                $"The provider did not respond within {settings.TimeoutSeconds} seconds. "
                + "The timeout can be changed in Settings.");
        }
        catch (OperationCanceledException)
        {
            errorKind = UsageErrorKind.Cancelled;
            _notifier.NotifyInfo("Cancelled", "Transcription was cancelled.");
        }
        catch (Exception ex)
        {
            errorKind = UsageErrorKind.Unexpected;
            _notifier.NotifyError("Unexpected error", ex.Message);
        }
        finally
        {
            _analytics.Record(new UsageRecord
            {
                Status = status,
                Error = status == UsageStatus.Error ? errorKind : null,
                AudioDuration = audio.Duration,
                Provider = settings.Provider,
                Model = settings.Model,
                Usage = usage,
            });

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
