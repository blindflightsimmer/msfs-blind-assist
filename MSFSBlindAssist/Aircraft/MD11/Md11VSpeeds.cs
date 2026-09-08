namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// The MD-11's five FMS take-off speed exports and how they are spoken when the FMS sets them —
/// "V1 145, VR 150, V2 158, slat retraction speed 190, flap retraction speed 210 knots" — so a
/// blind pilot hears the speeds appear the moment the take-off performance is entered, the way
/// the PMDGs and the iFly announce theirs. Until 2026-09-08 these were silent read-outs: the
/// V-Speeds panel showed them and nothing said so. The same five keys carry the Ctrl+M rows that
/// mute the announcement and, for V1 / VR / V2, the take-off roll callouts
/// (<see cref="Md11TakeoffCallouts"/>).
/// </summary>
public static class Md11VSpeeds
{
    /// <summary>Export key → the word spoken before the value.</summary>
    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["MD11_V1"] = "V1",
        ["MD11_VR"] = "VR",
        ["MD11_V2"] = "V2",
        ["MD11_VSR"] = "slat retraction speed",
        ["MD11_VFR"] = "flap retraction speed",
    };

    public static bool IsKey(string varName) => Labels.ContainsKey(varName);

    /// <summary>
    /// The five keys in SPEAKING order. The batch delivers them in ordinal name order — V1, V2,
    /// VFR, VR, VSR — which is not how a pilot thinks of them, so the sentence is composed in this
    /// order whatever order they arrived in.
    /// </summary>
    public static readonly string[] Keys = { "MD11_V1", "MD11_VR", "MD11_V2", "MD11_VSR", "MD11_VFR" };
}

/// <summary>
/// Speaks the take-off speeds when the FMS SETS or CHANGES them, as ONE sentence. Pure: the
/// definition feeds it the export deliveries on the UI thread, supplies the clock, and does the
/// speaking after the settle.
///
/// A computed set arrives as five deliveries in one batch; spoken one by one they would be five
/// queued sentences in the batch's alphabetical order (V2 before VR, the retraction speeds split
/// around it). So a change only ARMS a pending sentence, and <see cref="Due"/> releases it once
/// nothing new has arrived for <see cref="SettleMs"/>, in <see cref="Md11VSpeeds.Keys"/> order —
/// the house "one utterance" remedy.
///
/// Baseline-first per speed: the first value seen after connecting is remembered, never spoken
/// (connecting with speeds already entered is not a change). An unchanged redelivery (the V-Speeds
/// panel's per-second force-read) is silent. A speed the FMS has not computed, or has cleared,
/// reads 0 or TFDi's dashed sentinel and is remembered but never spoken — "V1 0 knots" is not
/// information — while the next real value after a clear IS spoken: the speeds came back.
///
/// Deliberately NO Reset for a reconnect: the reconnect's first batch has already delivered the
/// five (with the cache cleared, every var re-fires once) before the definition's
/// ResetAnnouncementBaselines runs, so a reset there would throw away the baseline just taken and
/// the next genuine change — the pilot's perf entry — would be eaten as a fresh baseline. An
/// aircraft switch constructs a new definition, and with it a fresh announcer.
/// </summary>
public sealed class Md11VSpeedAnnouncer
{
    /// <summary>Quiet time after the last changed speed before the sentence is spoken — a batch's five land within one dispatch.</summary>
    public const int SettleMs = 300;

    private readonly Dictionary<string, double> _last = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _pending = new(StringComparer.Ordinal);
    private long _pendingAtMs;

    /// <summary>True while a changed speed is waiting out its settle.</summary>
    public bool HasPending => _pending.Count > 0;

    /// <summary>
    /// A delivery arrived. Returns true when it armed or re-stamped a pending sentence (the caller
    /// then schedules a <see cref="Due"/> check), false when there is nothing to say for it.
    /// </summary>
    public bool OnUpdate(string varName, double value, long nowMs)
    {
        if (!Md11VSpeeds.IsKey(varName)) return false;
        bool had = _last.TryGetValue(varName, out var previous);
        _last[varName] = value;
        if (!had) return false;                                  // baseline: connecting is not a change
        if (Math.Abs(value - previous) <= 0.5) return false;     // redelivered unchanged
        if (value <= 0) return false;                            // cleared: nothing worth saying
        _pending[varName] = value;
        _pendingAtMs = nowMs;
        return true;
    }

    /// <summary>
    /// The sentence to speak now, or null: nothing pending, not yet settled, or every pending
    /// speed muted. <paramref name="isMuted"/> is the Ctrl+M row check, applied per speed here
    /// because the sentence is spoken from a timer, outside MainForm's Suppressed wrap.
    /// </summary>
    public string? Due(long nowMs, Func<string, bool> isMuted)
    {
        if (!HasPending || nowMs - _pendingAtMs < SettleMs) return null;
        var parts = new List<string>(Md11VSpeeds.Keys.Length);
        foreach (var key in Md11VSpeeds.Keys)
            if (_pending.TryGetValue(key, out var value) && !isMuted(key))
                parts.Add($"{Md11VSpeeds.Labels[key]} {Math.Round(value).ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        _pending.Clear();
        return parts.Count == 0 ? null : string.Join(", ", parts) + " knots";
    }
}
