using FluentAssertions;
using WhisperTray.Core.Configuration;
using WhisperTray.Core.Injection;
using WhisperTray.Core.Tests.TestInfrastructure;

namespace WhisperTray.Core.Tests.Injection;

public class CascadingTextInjectorTests
{
    private static ForegroundWindowInfo NormalTarget() =>
        new(WindowHandle: 0x1234, ProcessName: "notepad", IsOwnProcess: false, RequiresElevation: false);

    private static ForegroundWindowInfo OwnProcessTarget() =>
        new(WindowHandle: 0x2222, ProcessName: "whispertray", IsOwnProcess: true, RequiresElevation: false);

    private static ForegroundWindowInfo ElevatedTarget() =>
        new(WindowHandle: 0x3333, ProcessName: "cmd", IsOwnProcess: false, RequiresElevation: true);

    private sealed class Harness
    {
        public FakeClipboardService Clipboard { get; } = new(initialText: "prior clipboard");
        public RecordingTextTypist Typist { get; } = new();
        public ManualDelayedExecutor Delay { get; } = new();
        public CascadingTextInjector Sut { get; }

        public Harness()
        {
            Sut = new CascadingTextInjector(Clipboard, Typist, Delay);
        }
    }

    [Fact]
    public void Inject_ModeClipboardOnly_SetsClipboardAndSkipsKeyboard()
    {
        var h = new Harness();

        var result = h.Sut.Inject("hello", NormalTarget(), InjectionMode.ClipboardOnly);

        result.Outcome.Should().Be(InjectionOutcome.ClipboardOnly);
        h.Clipboard.Text.Should().Be("hello");
        h.Typist.TypedSegments.Should().BeEmpty();
        h.Typist.PasteShortcutCount.Should().Be(0);
        h.Delay.Scheduled.Should().BeEmpty();
    }

    [Fact]
    public void Inject_ModeType_CallsTypistDoesNotTouchClipboard()
    {
        var h = new Harness();

        var result = h.Sut.Inject("hello", NormalTarget(), InjectionMode.Type);

        result.Outcome.Should().Be(InjectionOutcome.Typed);
        h.Typist.TypedSegments.Should().ContainSingle().Which.Should().Be("hello");
        h.Clipboard.Text.Should().Be("prior clipboard");
        h.Clipboard.SetCount.Should().Be(0);
        h.Typist.PasteShortcutCount.Should().Be(0);
    }

    [Fact]
    public void Inject_ModePaste_SetsClipboardPressesCtrlVSchedulesRestore()
    {
        var h = new Harness();

        var result = h.Sut.Inject("hello", NormalTarget(), InjectionMode.Paste);

        result.Outcome.Should().Be(InjectionOutcome.Pasted);
        h.Clipboard.Text.Should().Be("hello");
        h.Typist.PasteShortcutCount.Should().Be(1);
        h.Delay.Scheduled.Should().ContainSingle()
            .Which.Delay.Should().Be(CascadingTextInjector.ClipboardRestoreDelay);
    }

    [Fact]
    public void Inject_ModePaste_RestoreScheduleActuallyRestoresPreviousClipboard()
    {
        var h = new Harness();

        h.Sut.Inject("hello", NormalTarget(), InjectionMode.Paste);
        h.Delay.FireAll();

        h.Clipboard.Text.Should().Be("prior clipboard");
    }

    [Fact]
    public void Inject_ModePaste_PassesConfiguredTimeoutToTypist()
    {
        var h = new Harness();

        h.Sut.Inject("hello", NormalTarget(), InjectionMode.Paste);

        h.Typist.LastPasteTimeout.Should().Be(CascadingTextInjector.ModifierReleaseTimeout);
    }

    [Fact]
    public void Inject_ModePaste_TimeoutWaitingForModifiers_FallsBackToClipboardOnly()
    {
        var h = new Harness();
        h.Typist.PasteWillSucceed = false;

        var result = h.Sut.Inject("hello", NormalTarget(), InjectionMode.Paste);

        result.Outcome.Should().Be(InjectionOutcome.ClipboardOnly);
        result.Reason.Should().NotBeNullOrEmpty();
        h.Typist.PasteAttemptCount.Should().Be(1);
        h.Typist.PasteShortcutCount.Should().Be(0, "Ctrl+V is not fired when a modifier is still held");
        h.Clipboard.Text.Should().Be("hello", "transcription must stay in clipboard so the user can paste it manually");
        h.Delay.Scheduled.Should().BeEmpty("clipboard must NOT be restored — otherwise the transcription would vanish before the user pastes");
    }

    [Fact]
    public void Inject_ModePaste_PreviousClipboardEmpty_SkipsRestore()
    {
        var clipboard = new FakeClipboardService(initialText: null);
        var typist = new RecordingTextTypist();
        var delay = new ManualDelayedExecutor();
        var sut = new CascadingTextInjector(clipboard, typist, delay);

        var result = sut.Inject("hello", NormalTarget(), InjectionMode.Paste);

        result.Outcome.Should().Be(InjectionOutcome.Pasted);
        delay.Scheduled.Should().BeEmpty("nothing to restore when previous clipboard was empty");
        clipboard.Text.Should().Be("hello");
    }

    [Fact]
    public void Inject_Auto_NormalTarget_BehavesLikePaste()
    {
        var h = new Harness();

        var result = h.Sut.Inject("hello", NormalTarget(), InjectionMode.Auto);

        result.Outcome.Should().Be(InjectionOutcome.Pasted);
        result.Reason.Should().BeNull();
        h.Typist.PasteShortcutCount.Should().Be(1);
    }

    [Fact]
    public void Inject_Auto_TargetIsOurOwnProcess_FallsBackToClipboardOnlyWithReason()
    {
        var h = new Harness();

        var result = h.Sut.Inject("hello", OwnProcessTarget(), InjectionMode.Auto);

        result.Outcome.Should().Be(InjectionOutcome.ClipboardOnly);
        result.Reason.Should().NotBeNullOrEmpty();
        h.Typist.PasteShortcutCount.Should().Be(0);
        h.Clipboard.Text.Should().Be("hello");
    }

    [Fact]
    public void Inject_Auto_TargetRequiresElevation_FallsBackToClipboardOnlyWithReason()
    {
        var h = new Harness();

        var result = h.Sut.Inject("hello", ElevatedTarget(), InjectionMode.Auto);

        result.Outcome.Should().Be(InjectionOutcome.ClipboardOnly);
        result.Reason.Should().NotBeNullOrEmpty();
        result.Reason.Should().Contain("elevated");
        h.Typist.PasteShortcutCount.Should().Be(0);
    }

    [Fact]
    public void Inject_EmptyText_SkipsEverything()
    {
        var h = new Harness();

        var result = h.Sut.Inject(string.Empty, NormalTarget(), InjectionMode.Auto);

        result.Outcome.Should().Be(InjectionOutcome.Skipped);
        h.Typist.TypedSegments.Should().BeEmpty();
        h.Typist.PasteShortcutCount.Should().Be(0);
        h.Clipboard.SetCount.Should().Be(0);
    }

    [Fact]
    public void Inject_ExplicitPaste_WorksEvenWhenTargetIsOurOwnProcess()
    {
        // Explicit mode overrides Auto's safety check. Users may opt in to this
        // consciously (e.g., testing) via the settings UI.
        var h = new Harness();

        var result = h.Sut.Inject("hello", OwnProcessTarget(), InjectionMode.Paste);

        result.Outcome.Should().Be(InjectionOutcome.Pasted);
    }

    [Fact]
    public void Inject_ExplicitType_IgnoresElevationHint()
    {
        var h = new Harness();

        var result = h.Sut.Inject("hello", ElevatedTarget(), InjectionMode.Type);

        result.Outcome.Should().Be(InjectionOutcome.Typed);
    }

    [Fact]
    public void Constructor_RejectsNullDependencies()
    {
        var clipboard = new FakeClipboardService();
        var typist = new RecordingTextTypist();
        var delay = new ManualDelayedExecutor();

        ((Action)(() => new CascadingTextInjector(null!, typist, delay))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new CascadingTextInjector(clipboard, null!, delay))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new CascadingTextInjector(clipboard, typist, null!))).Should().Throw<ArgumentNullException>();
    }
}
