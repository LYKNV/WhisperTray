using FluentAssertions;
using WhisperTray.Core.Analytics;
using WhisperTray.Core.Audio;
using WhisperTray.Core.Configuration;
using WhisperTray.Core.Injection;
using WhisperTray.Core.Orchestration;
using WhisperTray.Core.Tests.TestInfrastructure;
using WhisperTray.Core.Transcription;

namespace WhisperTray.Core.Tests.Orchestration;

public class OrchestratorTests
{
    private sealed class Harness
    {
        public InMemoryFakeRecorder Recorder { get; }
        public StaticAudioEncoderFactory EncoderFactory { get; } = new(new WavPassthroughEncoder());
        public FakeTranscriptionClient TranscriptionClient { get; } = new();
        public FakeTranscriptionClientFactory ClientFactory { get; }
        public FakeClipboardService Clipboard { get; } = new(initialText: null);
        public RecordingTextTypist Typist { get; } = new();
        public ManualDelayedExecutor Delay { get; } = new();
        public CascadingTextInjector Injector { get; }
        public ForegroundWindowInfo ForegroundTarget { get; set; } =
            new(WindowHandle: 0x42, ProcessName: "notepad", IsOwnProcess: false, RequiresElevation: false);
        public FakeForegroundWindowService Foreground { get; }
        public RecordingNotificationService Notifier { get; } = new();
        public FakeUsageAnalytics Analytics { get; } = new();
        public Settings Settings { get; set; } = Settings.Default with
        {
            ApiKey = "sk-test",
            Model = "whisper-1",
            Provider = TranscriptionProvider.OpenAi,
            AudioFormat = AudioFormat.Wav,
            InjectionMode = InjectionMode.Paste,
        };
        public Orchestrator Sut { get; }

        public Harness(RecordedAudio? capturedAudio = null)
        {
            capturedAudio ??= new RecordedAudio(new byte[] { 0x01, 0x00, 0x02, 0x00 }, 16_000);
            Recorder = new InMemoryFakeRecorder(capturedAudio);
            ClientFactory = new FakeTranscriptionClientFactory(TranscriptionClient);
            Injector = new CascadingTextInjector(Clipboard, Typist, Delay);
            Foreground = new FakeForegroundWindowService(() => ForegroundTarget);
            Sut = new Orchestrator(
                Recorder,
                EncoderFactory,
                ClientFactory,
                Injector,
                Foreground,
                Notifier,
                Analytics,
                () => Settings);
        }
    }

    private sealed class FakeForegroundWindowService : IForegroundWindowService
    {
        private readonly Func<ForegroundWindowInfo> _provider;
        public int CaptureCount { get; private set; }

        public FakeForegroundWindowService(Func<ForegroundWindowInfo> provider)
        {
            _provider = provider;
        }

        public ForegroundWindowInfo Capture()
        {
            CaptureCount++;
            return _provider();
        }
    }

    [Fact]
    public async Task ToggleAsync_FromIdle_CapturesForegroundAndStartsRecording()
    {
        var h = new Harness();

        await h.Sut.ToggleAsync();

        h.Sut.State.Should().Be(OrchestratorState.Recording);
        h.Foreground.CaptureCount.Should().Be(1);
        h.Recorder.IsRecording.Should().BeTrue();
    }

    [Fact]
    public async Task ToggleAsync_FromRecording_RunsFullPipelineAndReturnsToIdle()
    {
        var h = new Harness();
        h.TranscriptionClient.ResultToReturn = new TranscriptionResult("hello world");

        await h.Sut.ToggleAsync();  // start
        await h.Sut.ToggleAsync();  // stop + process

        h.Sut.State.Should().Be(OrchestratorState.Idle);
        h.Recorder.IsRecording.Should().BeFalse();
        h.TranscriptionClient.RequestsReceived.Should().ContainSingle();
        h.Typist.PasteShortcutCount.Should().Be(1);
        h.Clipboard.Text.Should().Be("hello world");
    }

    [Fact]
    public async Task ToggleAsync_TranscriptionRequest_CarriesSettingsFields()
    {
        var h = new Harness();
        h.Settings = h.Settings with
        {
            Language = "ru",
            PromptHint = "mixed ru/en",
            Model = "gpt-4o-transcribe",
        };

        await h.Sut.ToggleAsync();
        await h.Sut.ToggleAsync();

        var request = h.TranscriptionClient.RequestsReceived.Single();
        request.Model.Should().Be("gpt-4o-transcribe");
        request.Language.Should().Be("ru");
        request.Prompt.Should().Be("mixed ru/en");
    }

    [Fact]
    public async Task ToggleAsync_EmptyPromptHint_PassesNullPrompt()
    {
        var h = new Harness();
        h.Settings = h.Settings with { PromptHint = "" };

        await h.Sut.ToggleAsync();
        await h.Sut.ToggleAsync();

        h.TranscriptionClient.RequestsReceived.Single().Prompt.Should().BeNull();
    }

    [Fact]
    public async Task ToggleAsync_EncoderPickedFromSettings()
    {
        var h = new Harness();
        h.Settings = h.Settings with { AudioFormat = AudioFormat.OggOpus };

        await h.Sut.ToggleAsync();
        await h.Sut.ToggleAsync();

        h.EncoderFactory.RequestedFormats.Should().Contain(AudioFormat.OggOpus);
    }

    [Fact]
    public async Task ToggleAsync_TranscriptionAuthFailure_EmitsErrorAndReturnsToIdle()
    {
        var h = new Harness();
        h.TranscriptionClient.ExceptionToThrow = new TranscriptionAuthException("bad key");

        await h.Sut.ToggleAsync();
        await h.Sut.ToggleAsync();

        h.Sut.State.Should().Be(OrchestratorState.Idle);
        h.Typist.PasteShortcutCount.Should().Be(0);
        h.Notifier.Notifications.Should().ContainSingle(n =>
            n.Severity == RecordingNotificationService.Severity.Error);
    }

    [Fact]
    public async Task ToggleAsync_TranscriptionRateLimited_EmitsWarning()
    {
        var h = new Harness();
        h.TranscriptionClient.ExceptionToThrow = new TranscriptionRateLimitException("slow down");

        await h.Sut.ToggleAsync();
        await h.Sut.ToggleAsync();

        h.Notifier.Notifications.Should().ContainSingle(n =>
            n.Severity == RecordingNotificationService.Severity.Warning);
    }

    [Fact]
    public async Task ToggleAsync_EmptyTranscription_SkipsInjectionWithInfoNotification()
    {
        var h = new Harness();
        h.TranscriptionClient.ResultToReturn = new TranscriptionResult(string.Empty);

        await h.Sut.ToggleAsync();
        await h.Sut.ToggleAsync();

        h.Typist.PasteShortcutCount.Should().Be(0);
        h.Clipboard.SetCount.Should().Be(0);
        h.Notifier.Notifications.Should().ContainSingle(n =>
            n.Severity == RecordingNotificationService.Severity.Info
            && n.Title.Contains("speech", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ToggleAsync_WhitespaceOnlyTranscription_Skipped()
    {
        var h = new Harness();
        h.TranscriptionClient.ResultToReturn = new TranscriptionResult("   \n\t  ");

        await h.Sut.ToggleAsync();
        await h.Sut.ToggleAsync();

        h.Typist.PasteShortcutCount.Should().Be(0);
    }

    [Fact]
    public async Task ToggleAsync_DuringTranscription_IgnoredWithInfoNotification()
    {
        var h = new Harness();
        h.TranscriptionClient.Delay = TimeSpan.FromMilliseconds(200);

        await h.Sut.ToggleAsync();          // start
        var pipeline = h.Sut.ToggleAsync(); // stop + process (slow)
        // Fire a third toggle while the pipeline is mid-flight.
        await h.Sut.ToggleAsync();

        await pipeline;

        h.Notifier.Notifications.Should().Contain(n =>
            n.Severity == RecordingNotificationService.Severity.Info
            && n.Title.Contains("Still processing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ToggleAsync_OwnProcessForeground_AutoDowngradesToClipboardOnly()
    {
        var h = new Harness();
        h.ForegroundTarget = h.ForegroundTarget with { IsOwnProcess = true };
        h.Settings = h.Settings with { InjectionMode = InjectionMode.Auto };
        h.TranscriptionClient.ResultToReturn = new TranscriptionResult("hi");

        await h.Sut.ToggleAsync();
        await h.Sut.ToggleAsync();

        h.Typist.PasteShortcutCount.Should().Be(0);
        h.Clipboard.Text.Should().Be("hi");
        h.Notifier.Notifications.Should().Contain(n =>
            n.Severity == RecordingNotificationService.Severity.Info
            && n.Title.Contains("clipboard", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ToggleAsync_ElevatedForeground_AutoDowngradesToClipboardOnly()
    {
        var h = new Harness();
        h.ForegroundTarget = h.ForegroundTarget with { RequiresElevation = true };
        h.Settings = h.Settings with { InjectionMode = InjectionMode.Auto };

        await h.Sut.ToggleAsync();
        await h.Sut.ToggleAsync();

        h.Typist.PasteShortcutCount.Should().Be(0);
        h.Notifier.Notifications.Should().Contain(n =>
            n.Severity == RecordingNotificationService.Severity.Info);
    }

    [Fact]
    public async Task ToggleAsync_StateChangedEvent_FiresThroughFullSequence()
    {
        var h = new Harness();
        var states = new List<OrchestratorState>();
        h.Sut.StateChanged += (_, s) => states.Add(s);

        await h.Sut.ToggleAsync();
        await h.Sut.ToggleAsync();

        states.Should().ContainInOrder(
            OrchestratorState.Recording,
            OrchestratorState.Transcribing,
            OrchestratorState.Injecting,
            OrchestratorState.Idle);
    }

    [Fact]
    public async Task ToggleAsync_CapturesForegroundOnceBeforeShowingAnyUi()
    {
        // Regression guard: if we ever accidentally re-capture after showing a
        // notification or the settings window, the captured target would be ours.
        var h = new Harness();

        await h.Sut.ToggleAsync();   // capture happens here
        await h.Sut.ToggleAsync();   // pipeline runs, no recapture

        h.Foreground.CaptureCount.Should().Be(1);
    }

    [Fact]
    public async Task ToggleAsync_UnexpectedExceptionInClient_NotifiedAsError()
    {
        var h = new Harness();
        h.TranscriptionClient.ExceptionToThrow = new InvalidOperationException("oops");

        await h.Sut.ToggleAsync();
        await h.Sut.ToggleAsync();

        h.Sut.State.Should().Be(OrchestratorState.Idle);
        h.Notifier.Notifications.Should().ContainSingle(n =>
            n.Severity == RecordingNotificationService.Severity.Error);
    }

    [Fact]
    public async Task ToggleAsync_TranscriptionExceedsTimeout_EmitsTimeoutErrorAndReturnsToIdle()
    {
        var h = new Harness();
        h.Settings = h.Settings with { TimeoutSeconds = 1 };
        h.TranscriptionClient.Delay = TimeSpan.FromSeconds(30);

        await h.Sut.ToggleAsync();
        await h.Sut.ToggleAsync();

        h.Sut.State.Should().Be(OrchestratorState.Idle);
        h.Typist.PasteShortcutCount.Should().Be(0);
        h.Notifier.Notifications.Should().ContainSingle(n =>
            n.Severity == RecordingNotificationService.Severity.Error
            && n.Title.Contains("timed out", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ToggleAsync_UserCancellation_ReportsCancelledNotTimeout()
    {
        var h = new Harness();
        h.Settings = h.Settings with { TimeoutSeconds = 60 };
        h.TranscriptionClient.Delay = TimeSpan.FromSeconds(30);
        using var cts = new CancellationTokenSource();

        await h.Sut.ToggleAsync();
        var pipeline = h.Sut.ToggleAsync(cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));
        await pipeline;

        h.Notifier.Notifications.Should().ContainSingle(n =>
            n.Severity == RecordingNotificationService.Severity.Info
            && n.Title.Contains("Cancelled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Constructor_RejectsNullDependencies()
    {
        var recorder = new InMemoryFakeRecorder(new RecordedAudio(new byte[4], 16_000));
        var encF = new StaticAudioEncoderFactory(new WavPassthroughEncoder());
        var cliF = new FakeTranscriptionClientFactory(new FakeTranscriptionClient());
        var injector = new CascadingTextInjector(
            new FakeClipboardService(), new RecordingTextTypist(), new ManualDelayedExecutor());
        var fg = new FakeForegroundWindowService(() =>
            new ForegroundWindowInfo(0, "x", false, false));
        var notif = new RecordingNotificationService();
        var analytics = new FakeUsageAnalytics();
        Func<Settings> s = () => Settings.Default;

        ((Action)(() => new Orchestrator(null!, encF, cliF, injector, fg, notif, analytics, s))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new Orchestrator(recorder, null!, cliF, injector, fg, notif, analytics, s))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new Orchestrator(recorder, encF, null!, injector, fg, notif, analytics, s))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new Orchestrator(recorder, encF, cliF, null!, fg, notif, analytics, s))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new Orchestrator(recorder, encF, cliF, injector, null!, notif, analytics, s))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new Orchestrator(recorder, encF, cliF, injector, fg, null!, analytics, s))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new Orchestrator(recorder, encF, cliF, injector, fg, notif, null!, s))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new Orchestrator(recorder, encF, cliF, injector, fg, notif, analytics, null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ToggleAsync_SuccessfulDictation_RecordsSingleOkUsageRow()
    {
        // 16000 samples of 16-bit mono at 16 kHz = 1.00 s of audio.
        var h = new Harness(new RecordedAudio(new byte[16_000 * 2], 16_000));
        h.Settings = h.Settings with
        {
            Provider = TranscriptionProvider.Lemonfox,
            Model = "whisper-large-v3",
        };
        h.TranscriptionClient.ResultToReturn = new TranscriptionResult(
            "hello world",
            DetectedLanguage: "english",
            Usage: new TranscriptionUsage(InputTokens: 412, OutputTokens: 48, TotalTokens: 460));

        await h.Sut.ToggleAsync();
        await h.Sut.ToggleAsync();

        h.Analytics.Records.Should().ContainSingle();
        var row = h.Analytics.Records[0];
        row.Status.Should().Be(UsageStatus.Ok);
        row.Error.Should().BeNull();
        row.AudioDuration.TotalSeconds.Should().BeApproximately(1.0, 0.001);
        row.Provider.Should().Be(TranscriptionProvider.Lemonfox);
        row.Model.Should().Be("whisper-large-v3");
        row.Usage.Should().Be(new TranscriptionUsage(412, 48, 460));
    }

    [Fact]
    public async Task ToggleAsync_EmptyTranscription_RecordsEmptyStatus()
    {
        var h = new Harness();
        h.TranscriptionClient.ResultToReturn = new TranscriptionResult("   ");

        await h.Sut.ToggleAsync();
        await h.Sut.ToggleAsync();

        h.Analytics.Records.Should().ContainSingle();
        h.Analytics.Records[0].Status.Should().Be(UsageStatus.Empty);
        h.Analytics.Records[0].Error.Should().BeNull();
    }

    [Theory]
    [InlineData(nameof(TranscriptionAuthException), UsageErrorKind.Auth)]
    [InlineData(nameof(TranscriptionRateLimitException), UsageErrorKind.RateLimit)]
    [InlineData(nameof(TranscriptionServerException), UsageErrorKind.Server)]
    [InlineData(nameof(TranscriptionRequestException), UsageErrorKind.Request)]
    [InlineData("Generic", UsageErrorKind.Unexpected)]
    public async Task ToggleAsync_TranscriptionFailure_RecordsMatchingErrorKind(
        string exceptionName, UsageErrorKind expected)
    {
        var h = new Harness();
        h.TranscriptionClient.ExceptionToThrow = exceptionName switch
        {
            nameof(TranscriptionAuthException) => new TranscriptionAuthException("bad key"),
            nameof(TranscriptionRateLimitException) => new TranscriptionRateLimitException("slow down"),
            nameof(TranscriptionServerException) =>
                new TranscriptionServerException(System.Net.HttpStatusCode.BadGateway, "upstream down"),
            nameof(TranscriptionRequestException) =>
                new TranscriptionRequestException(System.Net.HttpStatusCode.BadRequest, "bad request"),
            _ => new InvalidOperationException("boom"),
        };

        await h.Sut.ToggleAsync();
        await h.Sut.ToggleAsync();

        h.Analytics.Records.Should().ContainSingle();
        h.Analytics.Records[0].Status.Should().Be(UsageStatus.Error);
        h.Analytics.Records[0].Error.Should().Be(expected);
        h.Analytics.Records[0].Usage.Should().BeNull();
    }

    [Fact]
    public async Task ToggleAsync_Timeout_RecordsTimeoutErrorKind()
    {
        var h = new Harness();
        h.Settings = h.Settings with { TimeoutSeconds = 1 };
        h.TranscriptionClient.Delay = TimeSpan.FromSeconds(30);

        await h.Sut.ToggleAsync();
        await h.Sut.ToggleAsync();

        h.Analytics.Records.Should().ContainSingle();
        h.Analytics.Records[0].Status.Should().Be(UsageStatus.Error);
        h.Analytics.Records[0].Error.Should().Be(UsageErrorKind.Timeout);
    }

    [Fact]
    public async Task ToggleAsync_UserCancellation_RecordsCancelledErrorKind()
    {
        var h = new Harness();
        h.Settings = h.Settings with { TimeoutSeconds = 60 };
        h.TranscriptionClient.Delay = TimeSpan.FromSeconds(30);
        using var cts = new CancellationTokenSource();

        await h.Sut.ToggleAsync();
        var pipeline = h.Sut.ToggleAsync(cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));
        await pipeline;

        h.Analytics.Records.Should().ContainSingle();
        h.Analytics.Records[0].Error.Should().Be(UsageErrorKind.Cancelled);
    }

    [Fact]
    public async Task ToggleAsync_TwoDictations_RecordsExactlyTwoRows()
    {
        var h = new Harness();

        await h.Sut.ToggleAsync();
        await h.Sut.ToggleAsync();
        await h.Sut.ToggleAsync();
        await h.Sut.ToggleAsync();

        h.Analytics.Records.Should().HaveCount(2);
    }

    [Fact]
    public async Task ToggleAsync_IgnoredPressWhileTranscribing_DoesNotAddExtraRow()
    {
        var h = new Harness();
        h.TranscriptionClient.Delay = TimeSpan.FromMilliseconds(300);

        await h.Sut.ToggleAsync();          // start recording
        var pipeline = h.Sut.ToggleAsync(); // stop + transcribe
        await h.Sut.ToggleAsync();          // ignored: still transcribing
        await pipeline;

        h.Analytics.Records.Should().ContainSingle();
    }
}
