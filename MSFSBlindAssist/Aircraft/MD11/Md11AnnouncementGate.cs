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

    /// <summary>
    /// What a lit→dark lamp transition says, decided at the moment its deferral FIRES rather than
    /// when the lamp went out (spec §3.7, amended 2026-09-05). Returns the sentence to speak, or
    /// null for silence.
    ///
    /// Two things can only be known late. First, POWER: the 528 continuous lamps ride two
    /// SimConnect batches, and the DC bus 1 lamp the gate reads sits in a different batch from the
    /// hydraulic and pneumatic lamps, so at shutdown a lamp can go dark a beat before the gate
    /// learns the busses died. Speaking on the spot narrated the whole panel losing power one
    /// control at a time — "Tank 1 Fuel Pumps: On", "Pack 1: On", … — which is exactly the
    /// per-control power narration §3.7 forbids. Second, the CURRENT STATE: a control with several
    /// legends may still have one lit, and the answer worth speaking is what it reads NOW, not
    /// which lamp moved.
    ///
    /// So the caller re-composes at fire time and hands the result here. Unpowered drops it;
    /// otherwise the normal background dedup decides, which is what keeps a lamp that relit inside
    /// the deferral from being announced twice.
    /// </summary>
    public string? SpeakDarkTransition(string owner, string? composedNow, bool poweredNow, long nowMs)
    {
        if (!poweredNow) return null;                        // the panel lost power, not the system
        if (string.IsNullOrEmpty(composedNow)) return null;   // nothing to say about this control
        return ShouldSpeakBackground(owner, composedNow, nowMs) ? composedNow : null;
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
