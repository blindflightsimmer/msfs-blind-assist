using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// iniBuilds A321LR (MSFS 2024 — the A321 shipped with the sim, a streamed and ENCRYPTED package, title "A321").
///
/// MECHANISM MAP (everything measured live 2026-10-16; docs/ini-a321.md):
///  * The same iniBuilds code base as the A330 (1,834 of its 2,732 panel-state L:vars share the A330's names,
///    the autoflight is the A300's: same FCU dials, press flags and FMA numbers) — so this is the A330
///    definition with the A321's own map, names and hardware.
///  * Controls are NOT the A330's: the A321's FS2024 input events do nothing when set or pushed from outside
///    (240 of them probed both ways). The INI_* L:vars ARE the inputs, as on the A300: the panels come from
///    Resources/a321_control_map.json (tools/a321-gen), every control a direct L:var write proven by a
///    write-and-read-back probe that also recorded what moved downstream.
///  * The FCU knob pulls and pushes are the exception: their INSTRUMENT_FCU_* input events DO answer a value
///    press (heading pull engaged HDG, V/S pull V/S, measured). AP 1/2, LOC and APPR are the A330's L:var
///    press flags; A/THR is AP8_BUTTON (the A300's numbering — a press on the ground arms nothing, as on the
///    real aircraft, so it is unverified until flown); EXPED and V/S push are the A321's own press flags.
///  * MCDU: no text export (iniAirbusMCDU_1 is absent here too): pixel-read through its own pop-out profile
///    (views 12/13 like the A330, a SQUARE pop-out), grid and glyph library (tools/a321-cdu-ocr).
///  * EFB: "iniEfbA321LR", the A330's tablet page for page — read by the A330's agent unchanged.
/// </summary>
public sealed class IniA321Definition : IniA330Definition
{
    public IniA321Definition() : base(A330ControlMap.A321) { }

    public override string AircraftName => "iniBuilds A321LR";
    public override string AircraftCode => "INI_A321";
    internal override string ShortName => "A321";

    internal override HashSet<string> DisabledMonitorSet => Settings.SettingsManager.Current.A321DisabledMonitorVariablesSet;
    internal override List<string> DisabledMonitorList => Settings.SettingsManager.Current.A321DisabledMonitorVariables;

    /// <summary>The A321's FCU: no ALT button (an A330 key); an EXPED key; the knob pulls/pushes on their input
    /// events (value press, measured); V/S push = level off.</summary>
    internal static readonly (string key, string label, string? pulse, string? inputEvent)[] A321Buttons =
    {
        ("A330_AP1", "AP 1", "INI_AP1_BUTTON", null),
        ("A330_AP2", "AP 2", "INI_AP2_BUTTON", null),
        ("A330_ATHR", "A/THR", "AP8_BUTTON", null),
        ("A330_SPD_PULL", "Speed knob pull (selected speed)", null, "INSTRUMENT_FCU_SPD_PULL"),
        ("A330_SPD_PUSH", "Speed knob push (managed speed)", null, "INSTRUMENT_FCU_SPD_PUSH"),
        ("A330_HDG_PULL", "Heading knob pull (selected heading)", null, "INSTRUMENT_FCU_HDG_PULL"),
        ("A330_HDG_PUSH", "Heading knob push (managed, NAV)", null, "INSTRUMENT_FCU_HDG_PUSH"),
        ("A330_ALT_PULL", "Altitude knob pull (open climb or descent)", null, "INSTRUMENT_FCU_ALT_PULL"),
        ("A330_ALT_PUSH", "Altitude knob push (managed climb or descent)", null, "INSTRUMENT_FCU_ALT_PUSH"),
        ("A330_VS_PULL", "V/S knob pull (selected vertical speed)", null, "INSTRUMENT_FCU_VS_PULL"),
        ("A330_VS_PUSH", "V/S knob push (level off)", "INI_FCU_LEVEL_OFF_COMMAND", null),
        ("A330_LOC", "LOC", "INI_LOCALIZER_BUTTON", null),
        ("A330_APPR", "APPR", "INI_APPROACH_BUTTON", null),
        (ExpedButtonKey, "EXPED", "A320_FCU_EXPEDITE_COMMAND", null),
    };

    internal override IEnumerable<(string key, string label, string? pulse, string? inputEvent)> Buttons => A321Buttons;

    /// <summary>The 100/1000 ft selector is not reachable from outside (INI_ALTITUDE_STATE reverts a write and its
    /// input event does nothing), so it is not offered.</summary>
    internal static readonly string[] A321SelectKeys = { "A330_SPD_MACH", "A330_TRK_FPA", "A330_METRIC" };

    internal override IReadOnlyCollection<string> SelectKeys => A321SelectKeys;

    internal override string? Ev(string a330Event) => a330Event switch
    {
        "AIRLINER_PARKINGBRAKE" => "LANDING_GEAR_PARKINGBRAKE",
        "AIRLINER_MCU_ALTSELECT" => null,
        _ => a330Event,
    };

    /// <summary>LANDING_GEAR_PARKINGBRAKE read 100 with the brake set (the dump of 2026-10-16).</summary>
    internal override double ParkingBrakeInput(bool set) => set ? 100 : 0;

    protected override bool SetOwnEvent(SimConnectManager sim, ScreenReaderAnnouncer announcer, string what, string a330Event, double value)
    {
        switch (a330Event)
        {
            case "AIRLINER_SPDMACH":
                // a press flag that toggles (measured: one write turned 100 knots into Mach 0.151; a second back);
                // callers ask only when the units should change
                if (CanCalc(sim, announcer, what)) Pulse(sim, "INI_SPD_MACH_BUTTON");
                return true;
            case "AIRLINER_METRIC":
                // the state L:var takes a direct write (held, measured)
                if (CanCalc(sim, announcer, what)) Write(sim, $"{(value > 0.5 ? 1 : 0)} (>L:INI_FCU_METRIC_STATE)");
                return true;
        }
        return base.SetOwnEvent(sim, announcer, what, a330Event, value);
    }

    protected override bool AutobrakeWritesCalc => true;

    protected override void SetAutobrake(SimConnectManager sim, int level, double? current)
    {
        // MAX is a toggling key (measured: A320_AUTOBRAKE_MAX_COMMAND took the level 0 -> 3 and back);
        // LO and MED have no key of their own here, so the level L:var is written (held, measured)
        bool maxNow = current is double c && Math.Round(c) == 3;
        if (level == 3) { if (!maxNow) Pulse(sim, "A320_AUTOBRAKE_MAX_COMMAND"); return; }
        if (maxNow) Pulse(sim, "A320_AUTOBRAKE_MAX_COMMAND");
        Write(sim, $"{Math.Clamp(level, 0, 2)} (>L:INI_AUTOBRAKE_LEVEL)");
    }

    internal override (string greenDot, string s, string f) SpeedLvars => ("FMGEC_GD_SPD", "FCPC_S_SPEED", "FCPC_F_SPEED");

    /// <summary>The A321's tanks in kilograms: both wings and the centre; the LR's additional centre tanks are not
    /// published and come out of the sim's total (see FuelSummary).</summary>
    internal static readonly (string key, string lvar, string name)[] A321Tanks =
    {
        ("A330_FUEL_LI", "INI_FUEL_WEIGHT_LEFT_INNER", "left wing"),
        ("A330_FUEL_CTR_KG", "INI_FUEL_WEIGHT_CENTER", "centre"),
        ("A330_FUEL_RI", "INI_FUEL_WEIGHT_RIGHT_INNER", "right wing"),
    };

    internal override (string key, string lvar, string name)[] Tanks => A321Tanks;
    internal override bool CentreTankFromSim => false;

    /// <summary>Instrument views (0-based, swept 2026-10-16): 3 frames both ECAM screens, 23 the captain's PFD and ND
    /// head-on (view 10, the A330's analogue, shows them small below the glareshield).</summary>
    internal override int EcamViewIndex => 3;
    internal override int PfdNdViewIndex => 23;

    internal override Forms.IniA300.CduFormProfile CduProfile() => Forms.IniA300.CduFormProfile.A321(A300CduGlyphLibrary.A321);

    internal override CoherentPmdgEfbClient CreateEfbClient() => CoherentPmdgEfbClient.ForIniA321();
}
