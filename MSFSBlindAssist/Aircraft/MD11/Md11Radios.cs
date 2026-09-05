using System.Globalization;

namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// The MD-11's COM radios, read from the simulator's own COM variables. TFDi's radio panel
/// drives them (COM1 read 135.500 on a live aircraft, 2026-09-06, through the calculator path —
/// the plain data-definition read the probing tool used returned 0 for the same variable, so a
/// zero from that tool proves nothing here). Wording mirrors the FBW and HS787 definitions:
/// "COM 1 active 135.500", standby included so a tuner click and an XFER both read back.
/// </summary>
public static class Md11Radios
{
    /// <summary>The VHF airband; anything outside it is an unpowered or garbage value and stays silent.</summary>
    public const double AirbandLowKhz = 118000;
    public const double AirbandHighKhz = 137000;

    /// <summary>Definition keys, active then standby per radio — the order the Radios read-out panel lists them.</summary>
    public static readonly string[] Keys =
    {
        "COM_ACTIVE_FREQUENCY:1", "COM_STANDBY_FREQUENCY:1",
        "COM_ACTIVE_FREQUENCY:2", "COM_STANDBY_FREQUENCY:2",
        "COM_ACTIVE_FREQUENCY:3", "COM_STANDBY_FREQUENCY:3",
    };

    public static bool IsComKey(string key)
        => key.StartsWith("COM_ACTIVE_FREQUENCY:", StringComparison.Ordinal)
        || key.StartsWith("COM_STANDBY_FREQUENCY:", StringComparison.Ordinal);

    /// <summary>The stock SimVar name behind a key: "COM_ACTIVE_FREQUENCY:1" → "COM ACTIVE FREQUENCY:1".</summary>
    public static string SimVarName(string key) => key.Replace('_', ' ');

    public static string Radio(string key)
        => key.EndsWith(":2", StringComparison.Ordinal) ? "COM 2"
         : key.EndsWith(":3", StringComparison.Ordinal) ? "COM 3" : "COM 1";

    public static string Kind(string key)
        => key.Contains("ACTIVE", StringComparison.Ordinal) ? "active" : "standby";

    /// <summary>"COM 1 Active Frequency" — the panel row and Ctrl+M label.</summary>
    public static string DisplayName(string key)
        => $"{Radio(key)} {(Kind(key) == "active" ? "Active" : "Standby")} Frequency";

    public static bool InAirband(double khz) => khz >= AirbandLowKhz && khz <= AirbandHighKhz;

    /// <summary>kHz → "135.500", invariant culture (spoken, so never a comma decimal).</summary>
    public static string FormatMhz(double khz) => (khz / 1000.0).ToString("0.000", CultureInfo.InvariantCulture);

    /// <summary>The read-out row's value: the frequency, or dashes while the radio reads nothing sensible.</summary>
    public static string Display(double khz) => InAirband(khz) ? FormatMhz(khz) : "--";

    public static string Describe(string key, double khz) => $"{Radio(key)} {Kind(key)} {FormatMhz(khz)}";
}

/// <summary>
/// Baseline-first change detector for the COM keys: the first sample of each key seeds silently
/// (connecting must not read the whole radio stack aloud), and a later change of more than half
/// a kHz inside the airband is spoken. Reset on reconnect so the first delivery re-seeds.
/// </summary>
public sealed class Md11ComAnnouncer
{
    private readonly Dictionary<string, double> _last = new(StringComparer.Ordinal);

    /// <summary>The sentence to speak for this update, or null when nothing should be said.</summary>
    public string? OnUpdate(string key, double khz)
    {
        bool seeded = _last.TryGetValue(key, out double prev);
        _last[key] = khz;
        if (!seeded) return null;
        if (Math.Abs(khz - prev) <= 0.5) return null;
        if (!Md11Radios.InAirband(khz)) return null;
        return Md11Radios.Describe(key, khz);
    }

    public void Reset() => _last.Clear();
}
