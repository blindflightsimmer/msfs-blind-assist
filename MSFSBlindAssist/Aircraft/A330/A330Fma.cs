using MSFSBlindAssist.Aircraft.A300;

namespace MSFSBlindAssist.Aircraft.A330;

/// <summary>
/// The A330's flight-mode annunciator, composed from the same <c>INI_*</c> mode L:vars the A300 uses
/// (<c>INI_PITCH_MODE</c>, <c>INI_ROLL_MODE</c>, <c>INI_AT_MODE</c>, their ARM companions): the A330 is
/// the same iniBuilds autoflight code base, measured live 2026-10-16 on the ground — HDG pull set
/// roll mode 2, the HDG-V/S/TRK-FPA push set roll mode 11 and TRACK_FPA_STATE, the ALT knob pull set
/// pitch mode 2 with ALT (6) armed, V/S pull set pitch mode 8, the ALT push set pitch mode 29.
///
/// The VALUES are the A300's (decoded from that aircraft's binary, A300Fma); the WORDS are the A330's
/// FMA vocabulary (OP CLB for the A300's LVL CH speed mode, CLB/DES for its profile modes, TRACK/FPA
/// in TRK-FPA). Unlike the A300 this is NOT decoded from a binary (the A330 package is encrypted):
/// a value this table does not know is spoken as its number ("vertical mode 17") rather than
/// guessed, so a gap is audible and can be filled from the log. Pure — pinned by A330FmaTests.
/// </summary>
internal static class A330Fma
{
    public static string Lateral(int roll, bool trackFpa) => roll switch
    {
        0 => "",
        1 => "NAV",
        2 => trackFpa ? "TRACK" : "HDG",
        3 => trackFpa ? "TRACK" : "HDG",
        4 => "RWY",
        5 => "LOC",
        6 => "LOC star",
        7 => "VOR",
        8 => "VOR star",
        11 => "TRACK",
        _ => $"lateral mode {roll}",
    };

    public static string LateralArmed(int arm) => arm switch
    {
        0 => "", 1 => "NAV", 5 => "LOC", 7 => "VOR", _ => "",
    };

    /// <param name="climbing">FCU altitude above the aircraft: an open mode is OP CLB, else OP DES.</param>
    public static string Vertical(int pitch, bool mach, bool trackFpa, bool climbing) => pitch switch
    {
        0 => "",
        1 => "SRS",
        2 or 4 => climbing ? "OP CLB" : "OP DES",
        3 or 24 or 27 => "CLB",
        5 or 13 or 16 or 25 => "DES",
        6 or 28 or 29 => "ALT",
        7 => "G S",
        8 => trackFpa ? "FPA" : "V S",
        9 or 22 => "ALT star",
        10 => "G S star",
        12 or 14 or 15 or 20 or 26 => "ALT CRZ",
        _ => $"vertical mode {pitch}",
    };

    public static string VerticalArmed(int arm, int arm2, int activePitch)
    {
        var parts = new List<string>();
        string a = arm switch
        {
            3 => "CLB", 5 or 13 or 21 => "DES",
            6 => activePitch is 3 or 5 or 16 or 18 or 24 or 25 ? "" : "ALT",
            7 => "G S", 22 => "ALT CRZ",
            _ => "",
        };
        if (a.Length > 0) parts.Add(a);
        string b = arm2 switch { 7 => "G S", 13 => "DES", _ => "" };
        if (b.Length > 0 && b != a) parts.Add(b);
        return string.Join(" ", parts);
    }

    public static string Approach(int mode) => mode switch
    {
        1 => "LAND", 2 => "FLARE", 3 => "ROLL OUT", 4 => "GO AROUND", _ => "",
    };

    public static string Thrust(int at, int pitch, bool mach, bool thrustLock)
    {
        if (thrustLock) return "THR LK";
        return at switch
        {
            0 => "",
            1 => mach ? "MACH" : "SPEED",
            2 or 9 => "THR IDLE",
            3 or 12 => pitch is 1 ? "MAN FLX" : "THR CLB",
            5 => "THR CLB",
            13 => "THR LK",
            _ => $"thrust mode {at}",
        };
    }

    public static string Capability(int cap) => cap switch
    {
        1 => "CAT 1", 2 => "CAT 2", 3 => "CAT 3", 4 => "CAT 3 dual", _ => "",
    };

    public static A330FmaText Compose(A330FmaInputs i)
    {
        string thrust = Thrust(i.Thrust, i.Pitch, i.SpeedIsMach, i.ThrustLock);
        string approach = Approach(i.Approach);
        string vertical, lateral;
        if (approach.Length > 0) { vertical = approach; lateral = ""; }
        else
        {
            vertical = Vertical(i.Pitch, i.SpeedIsMach, i.TrackFpa, i.Climbing);
            lateral = Lateral(i.Roll, i.TrackFpa);
        }
        var armed = new List<string>();
        string va = VerticalArmed(i.PitchArm, i.PitchArm2, i.Pitch);
        if (va.Length > 0) armed.Add(va);
        string la = LateralArmed(i.RollArm);
        if (la.Length > 0 && approach.Length == 0) armed.Add(la);
        string cap = approach.Length > 0 || la == "LOC" || va.Contains("G S") ? Capability(i.Capability) : "";
        return new A330FmaText(thrust, vertical, lateral, string.Join(" ", armed), cap);
    }
}

internal readonly record struct A330FmaInputs(
    int Thrust, int Pitch, int PitchArm, int PitchArm2, int Roll, int RollArm, int Approach,
    bool SpeedIsMach, bool TrackFpa, bool ThrustLock, int Capability, bool Climbing);

internal sealed record A330FmaText(string Thrust, string Vertical, string Lateral, string Armed, string Capability)
{
    public string Summary()
    {
        var parts = new List<string>();
        if (Thrust.Length > 0) parts.Add(Thrust);
        if (Vertical.Length > 0) parts.Add(Vertical);
        if (Lateral.Length > 0) parts.Add(Lateral);
        if (Armed.Length > 0) parts.Add("armed " + Armed);
        if (Capability.Length > 0) parts.Add(Capability);
        return parts.Count > 0 ? string.Join(", ", parts) : "No flight modes";
    }
}
