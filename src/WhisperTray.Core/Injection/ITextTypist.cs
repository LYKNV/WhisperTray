namespace WhisperTray.Core.Injection;

/// <summary>Low-level keyboard simulation primitives — implemented via SendInput on Windows.</summary>
public interface ITextTypist
{
    /// <summary>Types the given text via simulated Unicode key events.</summary>
    void TypeUnicode(string text);

    /// <summary>
    /// Waits until no modifier key (Ctrl, Alt, Shift, Win) is physically held, then
    /// simulates Ctrl+V on the focused window. Bounded by <paramref name="timeout"/>.
    /// Returns true when the paste shortcut was issued; false if the timeout elapsed
    /// with a modifier still held — in that case the caller should not assume the
    /// transcription was pasted (typically: leave it on the clipboard and notify).
    /// </summary>
    /// <remarks>
    /// This guards against the case where the global hotkey modifier (e.g. Win in Win+Z)
    /// is still held when injection happens. Firing Ctrl+V at that moment would be combined
    /// with the held modifier (Win+Ctrl+V) and silently fail to paste.
    /// </remarks>
    bool TryPasteWhenModifiersReleased(TimeSpan timeout);
}
