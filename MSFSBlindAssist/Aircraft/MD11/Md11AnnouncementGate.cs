namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// Decides whether a composed-state text is spoken (spec §3.6–3.7). Pure; the definition
/// supplies the clock. Two channels share it: BACKGROUND lamp changes (dedup on text, silent
/// inside the echo window of the pilot's own press) and PRESS FEEDBACK (always spoken, and it
/// seeds the dedup so the lamps that press lights do not repeat it).
/// </summary>
public sealed class Md11AnnouncementGate
{
    /// <summary>A press's own lamp echoes land within the 1 Hz batch plus settle; anything inside this window after a press is the echo.</summary>
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
        return text;
    }

    public void Reset()
    {
        _lastSpoken.Clear();
        _pressedAt.Clear();
    }
}
