using WhisperTray.Core.Injection;

namespace WhisperTray.Core.Tests.TestInfrastructure;

public sealed class RecordingTextTypist : ITextTypist
{
    public List<string> TypedSegments { get; } = new();

    /// <summary>Number of paste-shortcut presses actually emitted (i.e. wait did not time out).</summary>
    public int PasteShortcutCount { get; private set; }

    /// <summary>Number of paste attempts, regardless of whether they succeeded or timed out.</summary>
    public int PasteAttemptCount { get; private set; }

    /// <summary>Timeout last passed to <see cref="TryPasteWhenModifiersReleased"/>, or null if never called.</summary>
    public TimeSpan? LastPasteTimeout { get; private set; }

    /// <summary>
    /// Toggle to simulate a modifier-release timeout. Defaults to true so existing tests that
    /// expect the keyboard shortcut to fire keep doing so without ceremony.
    /// </summary>
    public bool PasteWillSucceed { get; set; } = true;

    public void TypeUnicode(string text) => TypedSegments.Add(text);

    public bool TryPasteWhenModifiersReleased(TimeSpan timeout)
    {
        PasteAttemptCount++;
        LastPasteTimeout = timeout;
        if (!PasteWillSucceed)
        {
            return false;
        }
        PasteShortcutCount++;
        return true;
    }
}
