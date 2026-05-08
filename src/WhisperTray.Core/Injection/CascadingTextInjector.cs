using WhisperTray.Core.Configuration;

namespace WhisperTray.Core.Injection;

/// <summary>
/// Combines clipboard + keyboard-simulation primitives into a single injection
/// decision tree based on the user's InjectionMode. Pure logic — all OS calls
/// live behind IClipboardService / ITextTypist / IDelayedExecutor.
/// </summary>
public sealed class CascadingTextInjector : ITextInjector
{
    public static readonly TimeSpan ClipboardRestoreDelay = TimeSpan.FromSeconds(2);

    // Upper bound for waiting out a still-held hotkey modifier before firing Ctrl+V.
    // Generous on purpose — most users release within ~100 ms; the long tail is just to
    // keep the experience robust if the user is e.g. mid-chord with Win held intentionally.
    public static readonly TimeSpan ModifierReleaseTimeout = TimeSpan.FromSeconds(5);

    private readonly IClipboardService _clipboard;
    private readonly ITextTypist _typist;
    private readonly IDelayedExecutor _delay;

    public CascadingTextInjector(IClipboardService clipboard, ITextTypist typist, IDelayedExecutor delay)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(typist);
        ArgumentNullException.ThrowIfNull(delay);
        _clipboard = clipboard;
        _typist = typist;
        _delay = delay;
    }

    public InjectionResult Inject(string text, ForegroundWindowInfo target, InjectionMode mode)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(target);

        if (string.IsNullOrEmpty(text))
        {
            return new InjectionResult(InjectionOutcome.Skipped, "Empty transcription text.");
        }

        var (effectiveMode, reason) = Resolve(mode, target);

        return effectiveMode switch
        {
            InjectionMode.ClipboardOnly => ExecuteClipboardOnly(text, reason),
            InjectionMode.Type => ExecuteType(text, reason),
            InjectionMode.Paste => ExecutePaste(text, reason),
            _ => throw new InvalidOperationException($"Unhandled injection mode {effectiveMode}."),
        };
    }

    private static (InjectionMode mode, string? reason) Resolve(InjectionMode requested, ForegroundWindowInfo target)
    {
        if (requested != InjectionMode.Auto)
        {
            return (requested, null);
        }

        if (target.IsOwnProcess)
        {
            return (InjectionMode.ClipboardOnly, "Foreground window belongs to WhisperTray; copied to clipboard instead.");
        }

        if (target.RequiresElevation)
        {
            return (InjectionMode.ClipboardOnly, "Foreground window runs elevated; copied to clipboard instead.");
        }

        return (InjectionMode.Paste, null);
    }

    private InjectionResult ExecuteClipboardOnly(string text, string? reason)
    {
        _clipboard.SetText(text);
        return new InjectionResult(InjectionOutcome.ClipboardOnly, reason);
    }

    private InjectionResult ExecuteType(string text, string? reason)
    {
        _typist.TypeUnicode(text);
        return new InjectionResult(InjectionOutcome.Typed, reason);
    }

    private InjectionResult ExecutePaste(string text, string? reason)
    {
        var previous = _clipboard.GetText();
        _clipboard.SetText(text);

        if (!_typist.TryPasteWhenModifiersReleased(ModifierReleaseTimeout))
        {
            // Modifier still held when the timeout fired. Firing Ctrl+V now would be
            // combined with that modifier (e.g. Win+Ctrl+V) and silently drop. Leave
            // the transcription on the clipboard for manual paste and skip the restore
            // — otherwise we'd swap the text back to the previous clipboard before the
            // user has a chance to use it.
            return new InjectionResult(
                InjectionOutcome.ClipboardOnly,
                $"A modifier key was still held after {ModifierReleaseTimeout.TotalSeconds:0}s; transcription left in clipboard for manual paste.");
        }

        if (previous is not null)
        {
            _delay.Schedule(ClipboardRestoreDelay, () => _clipboard.SetText(previous));
        }

        return new InjectionResult(InjectionOutcome.Pasted, reason);
    }
}
