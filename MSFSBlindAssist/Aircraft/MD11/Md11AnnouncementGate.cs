namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// Decides whether a composed-state text is spoken (spec §3.6–3.7). Pure; the definition
/// supplies the clock. Two channels share it: BACKGROUND lamp changes (dedup on text, silent
/// inside the echo window of the pilot's own press) and PRESS FEEDBACK (always spoken, and it
/// seeds the dedup so the lamps that press lights do not repeat it).
///
/// The echo window runs from the press until the feedback speaks, and at most
/// <see cref="EchoWindowMs"/>. It is closed by <see cref="Feedback"/> deliberately: a guarded
/// press spends 550 ms lifting its cover before the button is even written, so its lamp can land
/// AFTER the feedback has already spoken the old state. With the window still open that later
/// lamp — carrying a DIFFERENT text, i.e. the correction — was swallowed and never spoken again,
/// leaving the pilot told "Off" for a control that had just come on. Once the feedback has been
/// heard there is nothing left to echo, and the text dedup below still keeps the SAME state from
/// being said twice.
///
/// Every call lands on the UI thread — <c>HandleLampUpdate</c> runs there as the event-batch
/// consumer (a WinForms timer, so the same thread that dispatches every SimVar update), and
/// <c>TFDiMD11Definition</c> marshals <c>PressFeedbackAsync</c>'s tail back to it (via its
/// captured <c>SynchronizationContext</c>) after that method's ConfigureAwait(false) delays — so
/// the plain <see cref="Dictionary{TKey,TValue}"/> fields below need no lock.
/// </summary>
public sealed class Md11AnnouncementGate
{
    /// <summary>The press's own lamp echoes land within the 1 Hz batch plus settle; anything inside this window after a press, and before its feedback speaks, is the echo.</summary>
    public const int EchoWindowMs = 2500;

    private readonly Dictionary<string, string> _lastSpoken = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _pressedAt = new(StringComparer.Ordinal);

    public void NotePress(string owner, long nowMs) => _pressedAt[owner] = nowMs;

    public bool IsInEchoWindow(string owner, long nowMs)
        => _pressedAt.TryGetValue(owner, out var t) && nowMs - t < EchoWindowMs;

    public bool ShouldSpeakBackground(string owner, string text, long nowMs)
    {
        if (IsInEchoWindow(owner, nowMs)) return false;
        if (_lastSpoken.TryGetValue(owner, out var prev) && prev == text) return false;
        _lastSpoken[owner] = text;
        return true;
    }

    public string Feedback(string owner, string text)
    {
        _lastSpoken[owner] = text;
        _pressedAt.Remove(owner);   // the feedback IS the confirmation — nothing left to echo
        return text;
    }

    public void Reset()
    {
        _lastSpoken.Clear();
        _pressedAt.Clear();
    }
}
