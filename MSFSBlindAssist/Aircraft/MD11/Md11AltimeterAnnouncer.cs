namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// Decides when the captain's altimeter setting is spoken. Pure: the definition supplies the
/// clock and does the speaking.
///
/// A knob wind delivers a run of values (the export rides the 1 Hz batch, so roughly one per
/// second) and a pilot wants the FINAL setting, not "29.93, 29.94, 29.95…". So a new value only
/// arms a pending sentence, and <see cref="Due"/> releases it once nothing new has arrived for
/// <see cref="SettleMs"/>. Baseline-first — the first value seen after connecting or a reset is
/// remembered, never spoken. What keeps a re-delivered UNCHANGED value quiet is the last-seen
/// guard in <see cref="OnUpdate"/>, which drops it before it can arm or re-stamp anything; the
/// sentence dedup in <see cref="Due"/> is a second net, and it alone would not do — an ignored
/// redelivery must not restart the settle clock, which is a question about arming, not about
/// words. The words are the B key's (<see cref="Md11Fcp.DescribeAltimeter"/>), so a pilot hears
/// one phrasing whether they asked or were told.
/// </summary>
public sealed class Md11AltimeterAnnouncer
{
    /// <summary>Quiet time after the last delivered value before the setting is spoken.</summary>
    public const int SettleMs = 1500;

    private bool _baselined;
    private double _lastSeen;
    private double _pending;
    private bool _hasPending;
    private long _pendingAtMs;
    private string? _lastSpoken;

    /// <summary>
    /// A value arrived. An unchanged redelivery is ignored outright — it neither arms nor
    /// re-stamps a pending settle. MainForm's status-display auto-refresh force-reads every
    /// display variable of an open panel about once a second, and a forced read of a
    /// batch-covered variable redelivers the same value even when nothing changed
    /// (SimConnectManager.VarCache still raises the update). Left unguarded, that redelivery
    /// would land inside the <see cref="SettleMs"/> window on every single delivery while the
    /// "Minimums and Altimeters" panel is open, so the settle would never expire: the setting
    /// would never be spoken while the panel is open, and would be spoken stale — describing a
    /// change that may be minutes old — the moment the pilot leaves it.
    /// </summary>
    public void OnUpdate(double value, long nowMs)
    {
        if (!_baselined)
        {
            _baselined = true;
            _lastSeen = value;
            _lastSpoken = Sentence(value);   // never spoken: connecting mid-flight is not a change
            return;
        }
        if (value == _lastSeen) return;      // unchanged redelivery: must not re-arm the settle
        _lastSeen = value;
        _pending = value;
        _hasPending = true;
        _pendingAtMs = nowMs;
    }

    /// <summary>Seeds the baseline when there is none (a flight load re-delivers only what changed); true when it did.</summary>
    public bool SeedIfEmpty(double value)
    {
        if (_baselined) return false;
        _baselined = true;
        _lastSeen = value;
        _lastSpoken = Sentence(value);
        return true;
    }

    /// <summary>True while a value is waiting out its settle.</summary>
    public bool HasPending => _hasPending;

    /// <summary>The sentence to speak now, or null: nothing pending, not yet settled, or the same words as last time.</summary>
    public string? Due(long nowMs)
    {
        if (!_hasPending || nowMs - _pendingAtMs < SettleMs) return null;
        _hasPending = false;
        var sentence = Sentence(_pending);
        if (sentence == _lastSpoken) return null;
        _lastSpoken = sentence;
        return sentence;
    }

    /// <summary>Forget everything: the next value is a baseline again (on the disconnect; an aircraft switch builds a fresh instance).</summary>
    public void Reset()
    {
        _baselined = false;
        _hasPending = false;
        _lastSpoken = null;
    }

    /// <summary>"Altimeter standard", or "Altimeter: 1013, 29.92" — the B key's words.</summary>
    public static string Sentence(double reading)
        => Md11Fcp.IsStandard(reading) ? "Altimeter standard" : $"Altimeter: {Md11Fcp.DescribeAltimeter(reading)}";
}
