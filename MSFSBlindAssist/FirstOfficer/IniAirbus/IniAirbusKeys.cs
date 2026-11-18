using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.FirstOfficer.IniAirbus;

/// <summary>
/// The control and state keys the iniBuilds A330-200 (MSFS 2024) First Officer uses, kept in one table
/// so the profile, flows and checklists name each control once. The generated map's controls are keyed
/// <c>A330_*</c> (its FS2024 input events, written by position through <c>A330ControlMap.InputFor</c>);
/// the definition's own controls (flaps, spoilers, gear, brakes, autobrake, V-speeds, fuel, altimeter)
/// are the <c>A330_*</c> constants at the bottom.
///
/// Every value below is in the READ domain (what the control's read-back shows), exactly as a panel
/// combo writes it: <c>HandleUIVariableSet</c> translates it into the input the aircraft takes. The
/// values are the generated map's measured positions (live differential probe, 2026-10-16:
/// tools/a330-gen), and IniAirbusFoTests pins each one against the map's own label.
///
/// Left to the Captain, so with no write key here at all: the fire handles and fire tests, gear,
/// parking brake, flaps and ground spoilers (the definition drives the last two with stock FLAPS_SET /
/// SPOILERS_ARM_ON/OFF events nothing measured on this aircraft), and the ADIRS (never probed; the First
/// Officer only reads them back).
///
/// ENGINE MASTERS AND MODE SELECTOR (2026-10-28): the First Officer starts and shuts down the engines.
/// These writes are NOT VERIFIED IN THE SIM (the aircraft was not loaded): the masters are presses of
/// their input event made only when INI_MIXTURE_RATIOn_HANDLE differs (tools/a330-gen). Every master
/// and selector write is read back, and one that does not take stops the flow.
/// </summary>
public sealed class IniAirbusKeys
{
    public required string Title { get; init; }

    // ---- Engines ----
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
    /// <summary>The external power pushbuttons: the First Officer switches the first ON and all OFF.</summary>
    public required IReadOnlyList<string> ExtPower { get; init; }           // 1 = On, 0 = Off
    /// <summary>The external power AVAIL lamp (1 lit).</summary>
    public required string ExtPowerAvailLamp { get; init; }

    // ---- APU ----
    public required string ApuMaster { get; init; }                          // 1 On, 0 Off
    public required string ApuStart { get; init; }                           // a press
    public required string ApuAvailLamp { get; init; }                       // INI_APU_AVAILABLE, 1 lit
    public required string ApuBleed { get; init; }                           // 1 On, 0 Off

    // ---- Fuel ----
    /// <summary>Every fuel pump pushbutton, with its read in ON and OFF: the wing pumps read their L:var
    /// (1 On, 0 Off), but the two centre-tank pumps move no L:var and are read from their own input
    /// event, where 0 = On and 1 = Off (measured, the map).</summary>
    public required IReadOnlyList<(string Key, int On, int Off)> FuelPumps { get; init; }

    // ---- Air ----
    public required IReadOnlyList<string> Packs { get; init; }               // 1 On
    public required string CrossBleed { get; init; }                         // 0 Shut, 1 Auto, 2 Open
    public required string PackFlow { get; init; }                           // 0 Low, 1 Normal, 2 High
    public required IReadOnlyList<string> HotAir { get; init; }              // 1 On
    /// <summary>The pressurization mode pushbutton (1 Auto).</summary>
    public required string PressMode { get; init; }

    // ---- ADIRS / oxygen ----
    /// <summary>The IRS mode selectors (0 Off, 1 Nav). READ ONLY: the generator did not probe them, so
    /// setting them is the Captain's and the First Officer reads them back.</summary>
    public required IReadOnlyList<string> IrsModeReads { get; init; }
    public required string CrewOxygen { get; init; }                         // 1 On, 0 Off

    // ---- Lights and signs ----
    public required string Strobe { get; init; }                             // 0 On, 1 Auto, 2 Off
    public required string Beacon { get; init; }                             // 1 On
    public required string WingLights { get; init; }                         // 1 On
    public required string NavLogo { get; init; }                            // 1 = system 1 on, 2 Off
    public required string RunwayTurnoff { get; init; }                      // 1 On
    public required string NoseLight { get; init; }                          // 0 T.O, 1 Taxi, 2 Off
    /// <summary>The landing lights: one switch for both (1 On, 0 Off).</summary>
    public required string LandingLight { get; init; }
    public required string SeatBelts { get; init; }                          // 1 On, 0 Off
    public required string NoSmoking { get; init; }                          // 0 On, 1 Auto, 2 Off
    public required string EmerExit { get; init; }                           // 0 On, 1 Armed, 2 Off
    public required string Dome { get; init; }                               // 0 Bright, 1 Dim, 2 Off
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
    /// <summary>The captain's altimeter unit (0 inches, 1 hectopascals).</summary>
    public required string CaptBaroUnit { get; init; }

    // ---- Transponder / weather ----
    public required string XpdrMode { get; init; }                           // 0 Stby, 1 Auto, 2 On
    public required string TcasMode { get; init; }                           // 0 Stby, 1 TA, 2 TA/RA
    public required string TcasTraffic { get; init; }                        // 1 All
    public required string AltReporting { get; init; }                       // 1 On
    public required string WxSystem { get; init; }                           // 0 system 1, 1 Off, 2 system 2
    public required string Pws { get; init; }                                // 0 Off, 1 Auto

    // ---- ECAM ----
    /// <summary>The system display's page read (INI_ECAM_ACTIVE_PAGE).</summary>
    public required string EcamPageRead { get; init; }
    /// <summary>Page key prefix: the ECP keys (<c>A330_ECAM_DOOR</c>, pushed).</summary>
    public required string EcamKeyPrefix { get; init; }

    // ---- Tests and calls ----
    /// <summary>The T.O CONFIG key: an ECAM-panel key in the probed set, so the First Officer's.</summary>
    public required string TakeoffConfigTest { get; init; }
    /// <summary>The cabin CALL ALL push key.</summary>
    public required string CabinCallAll { get; init; }

    /// <summary>What INI_AUTOBRAKE_LEVEL reads at MAX (0 off, 1 low, 2 medium, 3 max).</summary>
    public const int AutobrakeMax = 3;

    // ---- The definition's own controls ----
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

    /// <summary>The INI_ECAM_ACTIVE_PAGE number each page key selects (docs/ini-a330.md).</summary>
    public static readonly IReadOnlyDictionary<string, int> EcamPageNumber = new Dictionary<string, int>
    {
        ["ENG"] = 1, ["APU"] = 9, ["DOOR"] = 11, ["STS"] = 14,
    };

    private static string[] Of(params string[] s) => s.Select(x => "A330_" + x).ToArray();

    public static readonly IniAirbusKeys A330 = new()
    {
        Title = "First Officer — iniBuilds A330",
        // NOT VERIFIED IN THE SIM (see the class summary)
        EngineMasters = Of("ENG1_MASTER", "ENG2_MASTER"),
        EngineModeSelector = "A330_ENG_MODE",
        EngineAvail = new[] { Eng1Avail, Eng2Avail },
        // engine 2 first: the owner's A330 checklist
        StartOrder = new[] { 2, 1 },
        Batteries = Of("BAT1", "BAT2", "APU_BAT"),
        ExtPower = Of("EXT_A", "EXT_B"),
        ExtPowerAvailLamp = "A330_EXT_A_LAMP1",
        ApuMaster = "A330_APU_MASTER",
        ApuStart = "A330_APU_START",
        // APU START's lamps are START (1) and AVAILABLE (2)
        ApuAvailLamp = "A330_APU_START_LAMP2",
        ApuBleed = "A330_APU_BLEED",
        FuelPumps = Of("FUEL1L1", "FUEL1L2", "FUEL1LSTBY", "FUEL2R1", "FUEL2R2", "FUEL2RSTBY").Select(x => (x, 1, 0))
            .Concat(Of("FUEL_CTR_L", "FUEL_CTR_R").Select(x => (x, 0, 1))).ToArray(),
        Packs = Of("PACK1", "PACK2"),
        CrossBleed = "A330_XBLEED",
        PackFlow = "A330_PACK_FLOW",
        HotAir = Of("HOT_AIR1", "HOT_AIR2"),
        PressMode = "A330_PRESS_MODE",
        IrsModeReads = Of("IRS1_MODE", "IRS2_MODE", "IRS3_MODE"),
        CrewOxygen = "A330_CREW_OXY",
        Strobe = "A330_STROBE",
        Beacon = "A330_BEACON",
        WingLights = "A330_WING_LT",
        NavLogo = "A330_NAV_LOGO",
        RunwayTurnoff = "A330_RWY_TURNOFF",
        NoseLight = "A330_NOSE_LT",
        LandingLight = "A330_LANDING_LT",
        SeatBelts = "A330_SEAT_BELTS",
        NoSmoking = "A330_NO_SMOKING",
        EmerExit = "A330_EMER_EXIT_LT",
        Dome = "A330_DOME",
        AnnLights = "A330_ANN_LT",
        StbyCompassLight = "A330_STBY_COMPASS_LT",
        Wipers = Of("CPT_WIPER", "FO_WIPER"),
        EngAntiIce = Of("ENG1_ANTI_ICE", "ENG2_ANTI_ICE"),
        WingAntiIce = "A330_WING_ANTI_ICE",
        CaptFd = "A330_CPT_FD",
        FoFd = "A330_FO_FD",
        CaptLs = "A330_CPT_LS",
        FoLs = "A330_FO_LS",
        CaptBaroUnit = "A330_CPT_BARO_UNIT",
        XpdrMode = "A330_XPDR_MODE",
        TcasMode = "A330_TCAS_MODE",
        TcasTraffic = "A330_TCAS_TRAFFIC",
        AltReporting = "A330_XPDR_ALT_RPTG",
        WxSystem = "A330_WX_SYS",
        Pws = "A330_WX_PWS",
        EcamPageRead = "A330_ECAM_PAGE",
        EcamKeyPrefix = "A330_ECAM_",
        // The fire tests are the Captain's, so they have no key: AIRLINER_ENGINE_FIRE_TEST / _APU_AGENT_TEST
        // are in the fire-handle family the generator says it never probed (and HubHop has no preset), so
        // nothing shows a press only sounds the test and discharges nothing. T.O CONFIG
        // (AIRLINER_ECAM_TOCONFIG) is an ECAM-panel key, in the probed set (the not-probed list is masters,
        // start selector, ADIRS, IR/ADR, fire handles and agents, IDG, RAT, gear, parking brake).
        TakeoffConfigTest = "A330_TO_CONFIG",
        CabinCallAll = "A330_CALL_ALL",
    };

    /// <summary>Every key the First Officer may WRITE (IniAirbusFoTests: each is a registered, writable
    /// control of the definition).</summary>
    public IEnumerable<string> WriteKeys()
    {
        foreach (var k in EngineMasters) yield return k;
        yield return EngineModeSelector;
        foreach (var k in Batteries) yield return k;
        foreach (var k in ExtPower) yield return k;
        yield return ApuMaster; yield return ApuStart; yield return ApuBleed;
        foreach (var f in FuelPumps) yield return f.Key;
        foreach (var k in Packs) yield return k;
        yield return CrossBleed; yield return PackFlow;
        foreach (var k in HotAir) yield return k;
        yield return PressMode;
        yield return CrewOxygen;
        yield return Strobe; yield return Beacon; yield return WingLights; yield return NavLogo;
        yield return RunwayTurnoff; yield return NoseLight; yield return LandingLight;
        yield return SeatBelts; yield return NoSmoking; yield return EmerExit;
        yield return Dome; yield return AnnLights; yield return StbyCompassLight;
        foreach (var k in Wipers) yield return k;
        foreach (var k in EngAntiIce) yield return k;
        yield return WingAntiIce;
        yield return CaptFd; yield return FoFd; yield return CaptLs; yield return FoLs;
        yield return XpdrMode; yield return TcasMode; yield return TcasTraffic; yield return AltReporting;
        yield return WxSystem; yield return Pws;
        foreach (var page in EcamPageNumber.Keys) yield return EcamKey(page);
        yield return TakeoffConfigTest;
        yield return CabinCallAll;
        yield return Autobrake;
    }
}
