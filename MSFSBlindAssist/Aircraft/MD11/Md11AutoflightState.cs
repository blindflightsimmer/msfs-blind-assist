using System.Globalization;

namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// Every spoken autoflight string on the MD-11, composed in ONE place so the FCP window, the
/// Shift+H/S/A/V read-outs, the Ctrl+H/S/A/V dialogs and the Autoflight Status panel can never
/// describe the same window with different words.
///
/// WHAT IS KNOWABLE, AND FROM WHERE. The FCP button variables (MD11_CGS_NAV_BT and friends)
/// carry no state — measured 2026-09-06 in the cruise with NAV, PROF, FMS SPD, AP1 and the
/// autothrottle all engaged, every one of them read 0. The aircraft's FMA is drawn inside the
/// WASM and never exported. What IS exported is what the FCP itself shows a sighted pilot, and
/// TFDi's Systems Guide (Forward Panel page) documents the windows' engagement cues:
///   • IAS/MACH window "shows dashes when the AFS is controlling to the FMS flight plan speed"
///   • HDG/TRK window "is blank when the AFS is controlling to the FMS flight plan"
///   • V/S-FPA window "Display is blank if V/S or FPA are not engaged"
/// So a dashed speed window IS "FMS speed engaged", a dashed heading window IS "NAV engaged", and
/// a dashed V/S window is only "V/S or FPA not engaged" — PROF and ALT HOLD look identical there,
/// which is why nothing here ever claims PROF, and nothing claims APPR/LAND (FMA-only).
/// Autopilot and autothrottle engagement come from MD11_AP_STATE (documented 0/1/2/3) and
/// MD11_ATS_STATE (1 measured with the ATS engaged; 0 = off; 2 never observed, treated as on).
/// </summary>
public static class Md11AutoflightState
{
    /// <summary>A noun with its own lower-case form, so "FPA" is never string-lowered to "fPA".</summary>
    public sealed record Noun(string Capitalised, string Lower);

    public static readonly Noun Speed = new("Speed", "speed");
    public static readonly Noun Heading = new("Heading", "heading");
    public static readonly Noun Track = new("Track", "track");
    public static readonly Noun Altitude = new("Altitude", "altitude");
    public static readonly Noun VerticalSpeed = new("Vertical speed", "vertical speed");
    public static readonly Noun Fpa = new("FPA", "FPA");

    public static Noun HeadingNoun(bool track) => track ? Track : Heading;
    public static Noun VerticalNoun(bool fpa) => fpa ? Fpa : VerticalSpeed;

    // ---- engagement ----

    /// <summary>MD11_AP_STATE, documented 0=Off, 1=AP1, 2=AP2, 3=AP1+2.</summary>
    public static string Autopilot(double apState) => apState switch
    {
        >= 2.5 => "AP 1 and 2",
        >= 1.5 => "AP 2",
        >= 0.5 => "AP 1",
        _ => "off",
    };

    /// <summary>MD11_ATS_STATE: 0 off; 1 measured live with the autothrottle engaged; 2 unobserved, treated as on.</summary>
    public static bool AutothrottleOn(double atsState) => atsState >= 0.5;

    public static string Autothrottle(double atsState) => AutothrottleOn(atsState) ? "on" : "off";

    /// <summary>The AUTO FLIGHT button's state: TFDi say it engages "both ATs and one AP", so both halves are named.</summary>
    public static string Autoflight(double apState, double atsState)
    {
        var ap = Autopilot(apState);
        bool ats = AutothrottleOn(atsState);
        if (ap == "off" && !ats) return "off";
        return $"{(ap == "off" ? "AP off" : ap)}, ATS {(ats ? "on" : "off")}";
    }

    /// <summary>The heading window shows dashes while NAV flies the aircraft (TFDi, Forward Panel).</summary>
    public static bool NavEngaged(double afsHeading) => Md11Fcp.IsDashed(afsHeading);

    /// <summary>The speed window shows dashes while FMS speed is engaged (TFDi, Forward Panel).</summary>
    public static bool FmsSpeedEngaged(double afsSpeed) => Md11Fcp.IsDashed(afsSpeed);

    public static string Engaged(bool engaged) => engaged ? "engaged" : "not engaged";

    // ---- window values (no noun; the renderers add one) ----

    public static string SpeedValue(double afsSpeed, bool mach)
    {
        if (Md11Fcp.IsDashed(afsSpeed)) return "dashed, FMS speed engaged";
        // Mach is float32 (0.81999999 for 0.82): format, never compare.
        return mach
            ? $"Mach {afsSpeed.ToString("0.00", CultureInfo.InvariantCulture)}"
            : $"{afsSpeed.ToString("0", CultureInfo.InvariantCulture)} knots";
    }

    public static string HeadingValue(double afsHeading)
        => Md11Fcp.IsDashed(afsHeading)
            ? "dashed, NAV engaged"
            : afsHeading.ToString("000", CultureInfo.InvariantCulture);

    public static string AltitudeValue(double afsAltitude, bool metres)
        => Md11Fcp.IsDashed(afsAltitude)
            ? "dashed"
            : $"{afsAltitude.ToString("0", CultureInfo.InvariantCulture)} {(metres ? "metres" : "feet")}";

    /// <summary>A dashed window means V/S or FPA is not engaged — PROF or ALT HOLD, which cannot be told apart.</summary>
    public static string VerticalValue(double afsVerticalSpeed, bool fpa)
    {
        if (Md11Fcp.IsDashed(afsVerticalSpeed)) return "dashed, not engaged";
        return fpa
            ? $"{afsVerticalSpeed.ToString("0.0", CultureInfo.InvariantCulture)} degrees"
            : $"{afsVerticalSpeed.ToString("0", CultureInfo.InvariantCulture)} feet per minute";
    }

    // ---- renderers ----

    /// <summary>"Heading: dashed, NAV engaged" — the FCP window's status rows.</summary>
    public static string Row(Noun noun, string value) => $"{noun.Capitalised}: {value}";

    /// <summary>"Selected heading dashed, NAV engaged" — the Shift+H/S/A/V read-outs.</summary>
    public static string Selected(Noun noun, string value) => $"Selected {noun.Lower} {value}";

    /// <summary>
    /// The Autoflight Status panel's rows have FIXED display names ("Selected heading"), so the
    /// value carries the lower-cased noun only when the window is in its non-default mode:
    /// "track 123", "FPA -3.0 degrees"; a heading-mode 123 stays "123".
    /// </summary>
    public static string PanelValue(Noun defaultNoun, Noun noun, string value)
        => noun == defaultNoun ? value : $"{noun.Lower} {value}";
}
