namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// The MD-11's five FMS take-off speed exports and how each is spoken when the FMS sets it —
/// "V1 145 knots", the PMDG wording — so a blind pilot hears the speeds appear the moment the
/// take-off performance is entered, the way the PMDGs and the iFly announce theirs. Until
/// 2026-09-08 these were silent read-outs: the V-Speeds panel showed them and nothing said so.
/// The same five keys carry the Ctrl+M rows that mute the announcement (through MainForm's
/// Suppressed wrap) and, for V1 / VR / V2, the take-off roll callouts (<see cref="Md11TakeoffCallouts"/>).
/// </summary>
public static class Md11VSpeeds
{
    /// <summary>Export key → the word spoken before the value.</summary>
    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["MD11_V1"] = "V1",
        ["MD11_VR"] = "VR",
        ["MD11_V2"] = "V2",
        ["MD11_VSR"] = "Slat retraction speed",
        ["MD11_VFR"] = "Flap retraction speed",
    };

    public static bool IsKey(string varName) => Labels.ContainsKey(varName);

    /// <summary>The five keys, in speaking order — they arrive together when the FMS computes a set.</summary>
    public static readonly string[] Keys = { "MD11_V1", "MD11_VR", "MD11_V2", "MD11_VSR", "MD11_VFR" };
}

/// <summary>
/// Speaks a take-off speed when the FMS SETS or CHANGES it. Pure: the definition feeds it the
/// export deliveries on the UI thread and does the speaking.
///
/// Baseline-first per speed: the first value seen after connecting is remembered, never spoken
/// (connecting with speeds already entered is not a change). An unchanged redelivery (the
/// V-Speeds panel's per-second force-read) is silent. A speed the FMS has not computed, or has
/// cleared, reads 0 or TFDi's dashed sentinel and is remembered but never spoken — "V1 0 knots"
/// is not information — while the next real value after a clear IS spoken: the speeds came back.
/// </summary>
public sealed class Md11VSpeedAnnouncer
{
    private readonly Dictionary<string, double> _last = new(StringComparer.Ordinal);

    /// <summary>A delivery arrived: "V1 145 knots" when it is a set or a change worth speaking, else null.</summary>
    public string? OnUpdate(string varName, double value)
    {
        if (!Md11VSpeeds.Labels.TryGetValue(varName, out var label)) return null;
        bool had = _last.TryGetValue(varName, out var previous);
        _last[varName] = value;
        if (!had) return null;                                   // baseline: connecting is not a change
        if (Math.Abs(value - previous) <= 0.5) return null;      // redelivered unchanged
        if (value <= 0) return null;                             // cleared: nothing worth saying
        return $"{label} {Math.Round(value)} knots";
    }

    /// <summary>Forget everything: the next value of each speed is a baseline again (reconnect, aircraft switch).</summary>
    public void Reset() => _last.Clear();
}
