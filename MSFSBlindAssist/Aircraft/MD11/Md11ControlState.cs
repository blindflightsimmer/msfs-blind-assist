namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// Composes the spoken state of an MD-11 control from its legend lamps, its latch and the
/// DC-power gate. Pure and order-sensitive (spec §3.3):
///   1. every LIT legend, in lamp order, joined with ", " — a lamp is the system's own answer;
///   2. else the proven latch position — valid even when the aircraft is unpowered;
///   3. else "unpowered" when the DC gate is off — every lamp reads 0 cold and dark, and
///      calling that "normal" would lie about a dark panel (measured 2026-09-05);
///   4. else the dark meaning ("On" for an OFF-legend button, "Not available" for EXT PWR).
/// Returns null when there is nothing to say, which MainForm renders as a bare label.
/// </summary>
public static class Md11ControlState
{
    /// <summary>Stock <c>ELECTRICAL MAIN BUS VOLTAGE</c> at or above this counts as DC power present (24 V measured on battery or external power; 0 V cold and dark).</summary>
    public const double PoweredVolts = 20.0;

    /// <summary>A lamp var above this is lit (they are 0/1 booleans; brightness is a separate var).</summary>
    public const double LitThreshold = 0.5;

    public const string Unpowered = "unpowered";

    public static bool IsPowered(double? volts) => volts is >= PoweredVolts;

    public static string? Compose(Md11StateSpec? spec, Func<string, double?> read, bool powered)
    {
        if (spec == null) return null;

        var lit = new List<string>();
        foreach (var lamp in spec.Lamps)
        {
            if (read(lamp.Var) is > LitThreshold && !string.IsNullOrEmpty(lamp.Lit) && !lit.Contains(lamp.Lit))
                lit.Add(lamp.Lit);
        }
        if (lit.Count > 0) return string.Join(", ", lit);

        if (spec.Latch != null && read(spec.Latch.Var) is { } position)
            return position > LitThreshold ? spec.Latch.On : spec.Latch.Off;

        if (!powered && (spec.Lamps.Count > 0 || !string.IsNullOrEmpty(spec.Dark))) return Unpowered;

        return string.IsNullOrEmpty(spec.Dark) ? null : spec.Dark;
    }
}
