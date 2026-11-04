using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.FirstOfficer.IniAirbus;

/// <summary>
/// The control and state keys the iniBuilds A330-200 / A321LR / A340-300 (MSFS 2024) First Officer uses,
/// per airframe. The definitions share one code base (<see cref="IniA321Definition"/> and
/// IniA340Definition derive from <see cref="IniA330Definition"/>), so the profile, flows and
/// checklists are shared and only these keys differ: the A330's generated map is keyed <c>A330_*</c> (its
/// FS2024 input events, written by position through <c>A330ControlMap.InputFor</c>), the A321's
/// <c>A321_*</c> (direct <c>INI_*</c> L:var writes — its input events do nothing from outside), the A340's
/// <c>A340_*</c> = "A340_" + the cockpit input event's name without "AIRLINER_" (tools/a340-gen: the A340's
/// behaviour code is readable, so each event's L:var and positions come from its own code). The
/// definition's own controls (flaps, spoilers, gear, brakes, autobrake, V-speeds, fuel, altimeter) are
/// <c>A330_*</c> on all three.
///
/// Every value below is in the READ domain (what the control's read-back shows), exactly as a panel
/// combo writes it: <c>HandleUIVariableSet</c> translates it into the input the aircraft takes. The A330
/// and A321 values are the generated maps' measured positions (live differential probe, 2026-10-16:
/// tools/a330-gen, tools/a321-gen) and IniAirbusFoTests pins each one against the map's own label; the
/// A340's come from its cockpit code, tooltips and panel states (docs/first-officer.md, iniBuilds A340).
///
/// A null key (or empty list) means this airframe has no control the First Officer may drive for that
/// line: the line is a Captain reminder instead (never a guessed write). Left to the Captain on every
/// airframe: the fire handles, gear, parking brake, flaps and ground spoilers (the definition drives the
/// last two with stock FLAPS_SET / SPOILERS_ARM_ON/OFF events nothing measured on these aircraft); the
/// ADIRS on the A330 and A321 (never probed / not in the map) — no write key here at all.
///
/// ENGINE MASTERS AND MODE SELECTOR (2026-10-28): the First Officer starts and shuts down the engines on
/// all three airframes. On the A340 the controls come from its readable cockpit code; on the A330 and the
/// A321 they are NOT VERIFIED IN THE SIM (neither aircraft was loaded): the A330's masters are presses of
/// their input event made only when INI_MIXTURE_RATIOn_HANDLE differs (tools/a330-gen), the A321's direct
/// writes of that L:var (tools/a321-gen). Every master and selector write is read back, and one that does
/// not take stops the flow.
/// </summary>
public sealed class IniAirbusKeys
{
    /// <summary>"A330", "A321" or "A340", for messages and the window title.</summary>
    public required string Airframe { get; init; }
    public required string Title { get; init; }

    // ---- Engines ----
    public required int EngineCount { get; init; }
    /// <summary>The engine masters, index 0 = engine 1 (1 On, 0 Off).</summary>
    public required IReadOnlyList<string> EngineMasters { get; init; }
    /// <summary>The engine mode / start selector (0 Crank, 1 Norm, 2 IGN START).</summary>
    public required string EngineModeSelector { get; init; }
    /// <summary>The aircraft's own ENGINE_n_AVAIL reads, index 0 = engine 1 (1 = the E/WD's green AVAIL).</summary>
    public required IReadOnlyList<string> EngineAvail { get; init; }
    /// <summary>The order the First Officer starts the engines in, one at a time.</summary>
    public required IReadOnlyList<int> StartOrder { get; init; }

    public const int ModeCrank = 0, ModeNorm = 1, ModeIgnStart = 2;

    // ---- Electrical ----
    public required IReadOnlyList<string> Batteries { get; init; }          // 1 = Auto (on), 0 = Off
    /// <summary>The external power pushbutton(s) the First Officer switches ON (the first) and OFF (all).</summary>
    public required IReadOnlyList<string> ExtPower { get; init; }           // 1 = On, 0 = Off
    /// <summary>The external power AVAIL lamp (1 lit), or null.</summary>
    public string? ExtPowerAvailLamp { get; init; }

    // ---- APU ----
    public required string ApuMaster { get; init; }                          // 1 On, 0 Off
    public required string ApuStart { get; init; }                           // a press
    public required string ApuAvailLamp { get; init; }                       // INI_APU_AVAILABLE, 1 lit
    public required string ApuBleed { get; init; }                           // 1 On, 0 Off
    /// <summary>The APU START press flag the First Officer must read before pressing (the A321: its START
    /// is a bare `1 (>L:INI_APU_START_BUTTON)` write the panel state shows staying 1 while the APU runs, so
    /// a press over a 1 would do nothing and is never made — the Captain starts the APU instead; the A340
    /// reads the same flag). Null on the A330, whose START is its input event's own _Push/_Release.</summary>
    public string? ApuStartPressRead { get; init; }

    // ---- Fuel ----
    /// <summary>Every fuel pump pushbutton the aircraft has on this airframe, with its read in ON and
    /// OFF: the wing pumps read their L:var (1 On, 0 Off), but the A330's two centre-tank pumps move no
    /// L:var and are read from their own input event, where 0 = On and 1 = Off (measured, the map).</summary>
    public required IReadOnlyList<(string Key, int On, int Off)> FuelPumps { get; init; }

    // ---- Air ----
    public required IReadOnlyList<string> Packs { get; init; }               // 1 On
    public required string CrossBleed { get; init; }                         // 0 Shut, 1 Auto, 2 Open
    public required string PackFlow { get; init; }                           // 0 Low, 1 Normal, 2 High
    public required IReadOnlyList<string> HotAir { get; init; }              // 1 On
    /// <summary>The pressurisation mode pushbutton, or null (the A340: its MODE SEL writes a press flag and
    /// no readable state, so the line is left to the Captain).</summary>
    public string? PressMode { get; init; }
    /// <summary>The pressurisation mode's read in AUTO: A330 1 (its pushbutton reads 1 in), A321 0
    /// (<c>INI_CABIN_MAN_PRESS</c> reads 1 in MANUAL).</summary>
    public int PressModeAuto { get; init; }

    // ---- ADIRS / oxygen ----
    /// <summary>The IRS mode selectors (0 Off, 1 Nav). READ ONLY on the A330 (the generator did not probe
    /// them); written by the First Officer on the A340 (<see cref="IrsWritable"/>: its cockpit code is
    /// readable). Empty on the A321, whose map carries none.</summary>
    public required IReadOnlyList<string> IrsModeReads { get; init; }
    /// <summary>True when the First Officer may set the IRS selectors (the A340).</summary>
    public bool IrsWritable { get; init; }
    public required string CrewOxygen { get; init; }                         // 1 On, 0 Off

    // ---- Lights and signs ----
    public required string Strobe { get; init; }                             // 0 On, 1 Auto, 2 Off
    public required string Beacon { get; init; }                             // 1 On
    public required string WingLights { get; init; }                         // 1 On
    public required string NavLogo { get; init; }                            // 1 = system 1 on, 2 Off
    public required string RunwayTurnoff { get; init; }                      // 1 On
    public required string NoseLight { get; init; }                          // 0 T.O, 1 Taxi, 2 Off
    /// <summary>Landing lights: each key with its On / Off / stowed value. The A330 and A340 have one
    /// switch (1 On, 0 Off); the A321 one per side (0 On, 1 Off, 2 Retract).</summary>
    public required IReadOnlyList<(string Key, int On, int Off, int Stowed)> LandingLights { get; init; }
    public required string SeatBelts { get; init; }
    /// <summary>Seat belts read ON / OFF: A330/A321 1 / 0 (two positions), A340 0 / 2 (ON, AUTO, OFF).</summary>
    public required int SeatBeltsOn { get; init; }
    public required int SeatBeltsOff { get; init; }
    public required string NoSmoking { get; init; }                          // 0 On, 1 Auto, 2 Off
    public required string EmerExit { get; init; }                           // 0 On, 1 Armed, 2 Off
    /// <summary>0 Bright, 1 Dim, 2 Off; null on the A340 (a two-position switch there, "as required").</summary>
    public string? Dome { get; init; }
    public required string AnnLights { get; init; }                          // 0 Test, 1 Bright, 2 Dim
    public required string StbyCompassLight { get; init; }                   // 1 On
    public required IReadOnlyList<string> Wipers { get; init; }              // 0 Off

    // ---- Anti-ice ----
    /// <summary>Engine anti-ice, index 0 = engine 1 (1 On).</summary>
    public required IReadOnlyList<string> EngAntiIce { get; init; }
    public required string WingAntiIce { get; init; }

    // ---- EFIS / FCU ----
    public required string CaptFd { get; init; }                             // 1 On
    public required string FoFd { get; init; }
    public required string CaptLs { get; init; }                             // 1 On
    public required string FoLs { get; init; }
    /// <summary>The captain's altimeter unit (0 inches, 1 hectopascals), or null (the A321 and A340 have
    /// none the First Officer can read; their QNH is spoken in hectopascals).</summary>
    public string? CaptBaroUnit { get; init; }

    // ---- Transponder / weather ----
    public required string XpdrMode { get; init; }                           // 0 Stby, 1 Auto, 2 On
    public required string TcasMode { get; init; }                           // 0 Stby, 1 TA, 2 TA/RA
    public required string TcasTraffic { get; init; }                        // 1 All
    public string? AltReporting { get; init; }                               // 1 On
    public required string WxSystem { get; init; }                           // 0 system 1, 1 Off, 2 system 2
    public required string Pws { get; init; }                                // 0 Off, 1 Auto

    // ---- ECAM ----
    /// <summary>The system display's page read (INI_ECAM_ACTIVE_PAGE).</summary>
    public required string EcamPageRead { get; init; }
    /// <summary>Page key prefix: the A330's ECP keys (<c>A330_ECAM_DOOR</c>, pushed), the A321's page
    /// writes (<c>A321_ECAM_DOOR</c>, a direct page-number write) or the A340's ECP keys.</summary>
    public required string EcamKeyPrefix { get; init; }

    // ---- Tests and calls (null = Captain) ----
    public string? EngFireTest { get; init; }
    public string? ApuFireTest { get; init; }
    public string? TakeoffConfigTest { get; init; }
    public string? CabinCallAll { get; init; }
    /// <summary>True when the autobrake MAX key may be pressed only from OFF (the A321: only 0 → 3 was
    /// measured on A320_AUTOBRAKE_MAX_COMMAND; over LO or MED it is the Captain's).</summary>
    public bool AutobrakeMaxOnlyFromOff { get; init; }
    /// <summary>What INI_AUTOBRAKE_LEVEL reads at MAX: 3 on the A330/A321, 4 on the A340
    /// (IniA340Definition.AbMax: 0 off, 5 LO, 3 MED, 4 MAX).</summary>
    public int AutobrakeMax { get; init; } = 3;
    /// <summary>The raw INI_AUTOBRAKE_LEVEL to the 0 off / 1 low / 2 medium / 3 max read-back scale, or null
    /// when the raw value already is that scale (the A330/A321).</summary>
    public IReadOnlyDictionary<int, int>? AutobrakeRawToLevel { get; init; }

    /// <summary>The definition's own OnRequest reads this airframe adds for the First Officer, polled by the
    /// window (the A340's engine 3/4 AVAIL, APU and ground-power AVAIL and the ECAM page).</summary>
    public IReadOnlyList<string> ExtraPollReads { get; init; } = Array.Empty<string>();

    // ---- The definition's own controls, the same key on every airframe ----
    public const string Flaps = "A330_FLAPS";                 // FLAPS HANDLE INDEX 0 Up .. 4 Full (read only)
    public const string SpoilersArmed = "A330_SPLR_ARM";      // SPOILERS ARMED (read only)
    public const string GearLever = "A330_GEAR";              // INI_GEAR_HANDLE_STATUS 1 Down (read only)
    public const string GearNose = "A330_GEAR_POS";           // GEAR CENTER POSITION, percent
    public const string GearLeft = IniA330Definition.FoGearLeftKey;
    public const string GearRight = IniA330Definition.FoGearRightKey;
    public const string ParkingBrake = "A330_PARKBRK";        // INI_PARKING_BRAKE_STATUS 1 Set (read only)
    public const string Autobrake = "A330_AUTOBRAKE";         // INI_AUTOBRAKE_LEVEL (see AutobrakeMax)
    public const string V1 = IniA330Definition.TakeoffV1Key, VR = IniA330Definition.TakeoffVrKey, V2 = IniA330Definition.TakeoffV2Key;
    public const string FuelTotalLb = "A330_FUEL_TOTAL_LB";
    public const string WeightMetric = "A330_IS_METRIC";      // INI_IS_METRIC, 1 kilograms
    public const string BaroMb = "A330_BARO_FRESH";           // KOHLSMAN SETTING MB:1
    public const string BaroMode = "A330_BARO_MODE_FRESH";    // XMLVAR_Baro1_Mode, 2/3 = STD
    public const string Eng1Avail = IniA330Definition.FoEng1AvailKey, Eng2Avail = IniA330Definition.FoEng2AvailKey;
    public const string ApuFlap = IniA330Definition.FoApuFlapKey;

    /// <summary>The key of a system-display page (ENG, APU, DOOR, STS).</summary>
    public string EcamKey(string page) => EcamKeyPrefix + page;

    /// <summary>Engine <paramref name="n"/>'s master key (1-based).</summary>
    public string Master(int n) => EngineMasters[n - 1];

    /// <summary>"engine 1 and 2", "engines 1 to 4": for spoken lines.</summary>
    public string EnginesPhrase => EngineCount == 2 ? "engine 1 and engine 2" : $"engines 1 to {EngineCount}";

    /// <summary>The INI_ECAM_ACTIVE_PAGE number each page key selects (docs/ini-a330.md, and the
    /// A321's direct page writes). The A340 is the same code base; its numbers are UNVERIFIED (the worst
    /// a wrong one does is a second press, which returns the display to automatic).</summary>
    public static readonly IReadOnlyDictionary<string, int> EcamPageNumber = new Dictionary<string, int>
    {
        ["ENG"] = 1, ["APU"] = 9, ["DOOR"] = 11, ["STS"] = 14,
    };

    private static string[] Of(string p, params string[] s) => s.Select(x => p + x).ToArray();

    public static readonly IniAirbusKeys A330 = Build("A330", "First Officer — iniBuilds A330");
    public static readonly IniAirbusKeys A321 = Build("A321", "First Officer — iniBuilds A321LR");

    private static IniAirbusKeys Build(string airframe, string title)
    {
        bool a330 = airframe == "A330";
        string p = a330 ? "A330_" : "A321_";
        return new IniAirbusKeys
        {
            Airframe = airframe,
            Title = title,
            EngineCount = 2,
            // NOT VERIFIED IN THE SIM (see the class summary)
            EngineMasters = Of(p, "ENG1_MASTER", "ENG2_MASTER"),
            EngineModeSelector = p + "ENG_MODE",
            EngineAvail = new[] { Eng1Avail, Eng2Avail },
            // engine 2 first: the owner's A330 checklist, and the A320-family FCOM order
            StartOrder = new[] { 2, 1 },
            Batteries = a330 ? Of(p, "BAT1", "BAT2", "APU_BAT") : Of(p, "BAT1", "BAT2"),
            ExtPower = a330 ? Of(p, "EXT_A", "EXT_B") : Of(p, "EXT_PWR"),
            ExtPowerAvailLamp = a330 ? "A330_EXT_A_LAMP1" : "A321_EXT_PWR_LAMP1",
            ApuMaster = p + "APU_MASTER",
            ApuStart = p + "APU_START",
            // A330: APU START's lamps are START (1) and AVAILABLE (2); A321: AVAILABLE only
            ApuAvailLamp = a330 ? "A330_APU_START_LAMP2" : "A321_APU_START_LAMP1",
            ApuBleed = p + "APU_BLEED",
            ApuStartPressRead = a330 ? null : IniA330Definition.FoApuStartPbKey,
            FuelPumps = a330
                ? Of(p, "FUEL1L1", "FUEL1L2", "FUEL1LSTBY", "FUEL2R1", "FUEL2R2", "FUEL2RSTBY").Select(x => (x, 1, 0))
                    .Concat(Of(p, "FUEL_CTR_L", "FUEL_CTR_R").Select(x => (x, 0, 1))).ToArray()
                // the A321 map also carries A321_FUEL1LSTBY/2RSTBY (the A330's L:var names exist in its
                // panel state, reading 0 ready for take-off): the A321 has no standby pumps, so they are
                // never touched
                : Of(p, "FUEL1L1", "FUEL1L2", "FUEL2R1", "FUEL2R2", "FUEL_CTR1", "FUEL_CTR2").Select(x => (x, 1, 0)).ToArray(),
            Packs = Of(p, "PACK1", "PACK2"),
            CrossBleed = p + "XBLEED",
            PackFlow = p + "PACK_FLOW",
            HotAir = a330 ? Of(p, "HOT_AIR1", "HOT_AIR2") : Of(p, "HOT_AIR1"),
            PressMode = p + "PRESS_MODE",
            PressModeAuto = a330 ? 1 : 0,
            IrsModeReads = a330 ? Of(p, "IRS1_MODE", "IRS2_MODE", "IRS3_MODE") : Array.Empty<string>(),
            CrewOxygen = p + "CREW_OXY",
            Strobe = p + "STROBE",
            Beacon = p + "BEACON",
            WingLights = p + "WING_LT",
            NavLogo = p + "NAV_LOGO",
            RunwayTurnoff = p + "RWY_TURNOFF",
            NoseLight = p + "NOSE_LT",
            LandingLights = a330
                ? new[] { ("A330_LANDING_LT", 1, 0, 0) }
                : new[] { ("A321_LDG_LT_L", 0, 1, 2), ("A321_LDG_LT_R", 0, 1, 2) },
            SeatBelts = p + "SEAT_BELTS",
            SeatBeltsOn = 1,
            SeatBeltsOff = 0,
            NoSmoking = p + "NO_SMOKING",
            EmerExit = p + "EMER_EXIT_LT",
            Dome = p + "DOME",
            AnnLights = p + "ANN_LT",
            StbyCompassLight = p + "STBY_COMPASS_LT",
            Wipers = Of(p, "CPT_WIPER", "FO_WIPER"),
            EngAntiIce = Of(p, "ENG1_ANTI_ICE", "ENG2_ANTI_ICE"),
            WingAntiIce = p + "WING_ANTI_ICE",
            CaptFd = p + "CPT_FD",
            FoFd = p + "FO_FD",
            CaptLs = p + "CPT_LS",
            FoLs = p + "FO_LS",
            CaptBaroUnit = a330 ? "A330_CPT_BARO_UNIT" : null,
            XpdrMode = p + "XPDR_MODE",
            TcasMode = p + "TCAS_MODE",
            TcasTraffic = p + "TCAS_TRAFFIC",
            AltReporting = a330 ? "A330_XPDR_ALT_RPTG" : null,
            WxSystem = p + "WX_SYS",
            Pws = p + "WX_PWS",
            EcamPageRead = p + "ECAM_PAGE",
            EcamKeyPrefix = p + "ECAM_",
            // Fire tests: the Captain's on BOTH. The A330's are AIRLINER_ENGINE_FIRE_TEST / _APU_AGENT_TEST in
            // the fire-handle family the generator says it never probed (and HubHop has no preset), so nothing
            // shows a press only sounds the test and discharges nothing; the A321 map has no fire key at all.
            // T.O CONFIG: the A330's AIRLINER_ECAM_TOCONFIG is an ECAM-panel key, in the probed set (the
            // docstring's not-probed list is masters, start selector, ADIRS, IR/ADR, fire handles and agents,
            // IDG, RAT, gear, parking brake), so it stays the First Officer's; the A321 map has none.
            // Cabin call: the A330's push key; the A321's is a bare `1 (>L:INI_CALL_ALL_BUTTON)` write nothing
            // confirms is released again — a call key left at 1 could be an endless chime — so the Captain's.
            EngFireTest = null,
            ApuFireTest = null,
            TakeoffConfigTest = a330 ? "A330_TO_CONFIG" : null,
            CabinCallAll = a330 ? "A330_CALL_ALL" : null,
            AutobrakeMaxOnlyFromOff = !a330,
            AutobrakeMax = 3,
        };
    }

    /// <summary>Every key the First Officer may WRITE on this airframe (IniAirbusFoTests: each is a
    /// registered, writable control of the definition).</summary>
    public IEnumerable<string> WriteKeys()
    {
        foreach (var k in EngineMasters) yield return k;
        yield return EngineModeSelector;
        if (IrsWritable) foreach (var k in IrsModeReads) yield return k;
        foreach (var k in Batteries) yield return k;
        foreach (var k in ExtPower) yield return k;
        yield return ApuMaster; yield return ApuStart; yield return ApuBleed;
        foreach (var f in FuelPumps) yield return f.Key;
        foreach (var k in Packs) yield return k;
        yield return CrossBleed; yield return PackFlow;
        foreach (var k in HotAir) yield return k;
        if (PressMode != null) yield return PressMode;
        yield return CrewOxygen;
        yield return Strobe; yield return Beacon; yield return WingLights; yield return NavLogo;
        yield return RunwayTurnoff; yield return NoseLight;
        foreach (var l in LandingLights) yield return l.Key;
        yield return SeatBelts; yield return NoSmoking; yield return EmerExit;
        if (Dome != null) yield return Dome;
        yield return AnnLights; yield return StbyCompassLight;
        foreach (var k in Wipers) yield return k;
        foreach (var k in EngAntiIce) yield return k;
        yield return WingAntiIce;
        yield return CaptFd; yield return FoFd; yield return CaptLs; yield return FoLs;
        yield return XpdrMode; yield return TcasMode; yield return TcasTraffic;
        if (AltReporting != null) yield return AltReporting;
        yield return WxSystem; yield return Pws;
        foreach (var page in EcamPageNumber.Keys) yield return EcamKey(page);
        if (EngFireTest != null) yield return EngFireTest;
        if (ApuFireTest != null) yield return ApuFireTest;
        if (TakeoffConfigTest != null) yield return TakeoffConfigTest;
        if (CabinCallAll != null) yield return CabinCallAll;
        yield return Autobrake;
    }
}
