namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// Decides when the captain's altimeter setting is spoken. Pure: the definition supplies the
/// clock and does the speaking.
///
/// A knob wind delivers a run of values (the export rides the 1 Hz batch, so roughly one per
/// second) and a pilot wants the FINAL setting, not "29.93, 29.94, 29.95…". So a new value only
/// arms a pending sentence, and <see cref="Due"/> releases it once nothing new has arrived for
/// <see cref="SettleMs"/>. Baseline-first — the first value seen after connecting or a reset is
/// remembered, never spoken — and deduplicated on the sentence, so a re-delivered unchanged
/// value stays quiet. The words are the B key's (<see cref="Md11Fcp.DescribeAltimeter"/>), so a
/// pilot hears one phrasing whether they asked or were told.
/// </summary>
public sealed class Md11AltimeterAnnouncer
{
    /// <summary>Quiet time after the last delivered value before the setting is spoken.</summary>
    public const int SettleMs = 1500;

    private bool _baselined;
    private double _pending;
    private bool _hasPending;
    private long _pendingAtMs;
    private string? _lastSpoken;

    /// <summary>A value arrived (any value: the batch re-delivers unchanged ones too).</summary>
    public void OnUpdate(double value, long nowMs)
    {
        if (!_baselined)
        {
            _baselined = true;
            _lastSpoken = Sentence(value);   // never spoken: connecting mid-flight is not a change
            return;
        }
        _pending = value;
        _hasPending = true;
        _pendingAtMs = nowMs;
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

    /// <summary>Forget everything: the next value is a baseline again (reconnect, aircraft switch).</summary>
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
