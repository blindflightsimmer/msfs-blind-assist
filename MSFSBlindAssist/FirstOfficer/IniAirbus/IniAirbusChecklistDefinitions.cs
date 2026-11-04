using MSFSBlindAssist.FirstOfficer.Airbus;
using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.FirstOfficer.IniAirbus;

using Item = Models.ChecklistItem<IniAirbusActionExecutor, IniAirbusStateEvaluator>;
using Group = Models.ChecklistGroup<IniAirbusActionExecutor, IniAirbusStateEvaluator>;
using CheckFn = System.Func<IniAirbusActionExecutor, IniAirbusStateEvaluator, System.Threading.Tasks.Task>;
using S = IniAirbusStateEvaluator;

/// <summary>
/// iniBuilds A330-200 / A321LR / A340-300 First Officer checklists: the action groups that mirror
/// <see cref="IniAirbusFlowDefinitions"/> (a line's CheckAction fires the SAME write its flow step
/// does, through <see cref="IniAirbusActionExecutor"/>), interleaved with the action-free read-backs
/// (<c>*_CL</c>, FO-3).
///
/// READ-BACK SOURCE: the Airbus normal checklist — the A320-family card of November 2021, which Airbus
/// harmonised with the A330 card line for line (Cockpit Preparation, Before Start, After Start, Taxi with
/// the T.O memo, Line-up, Approach, Landing with the LDG memo, After Landing, Parking, Securing) — in the
/// exact ids, order and words the Headwind A330 / A32NX / Fenix profiles carry (FOA-8,
/// docs/invariants/first-officer-airbus.md, with the owner's choices recorded there). Where the iniBuilds
/// aircraft publishes no value the First Officer can read, the line is a plain Reminder (rudder trim,
/// engine mode, minimums) instead of an auto-detected one. The same cards serve the four-engine A340
/// (its own iniBuilds procedure names the same phases); a line whose control the airframe lacks is absent.
/// </summary>
public static class IniAirbusChecklistDefinitions
{
    public static List<Group> Build(IniAirbusKeys k) => new()
    {
        BuildElectricalPowerUp(k),
        BuildPreflight(k),
        BuildCockpitPrepCL(k),
        BuildBeforeStart(k),
        BuildBeforeStartCL(k),
        BuildEngineStart(k),
        BuildAfterStart(k),
        BuildAfterStartCL(k),
        BuildTaxi(k),
        BuildTaxiCL(k),
        BuildLineup(k),
        BuildLineupCL(k),
        BuildAfterTakeoff(k),
        BuildDescent(k),
        BuildApproach(k),
        BuildApproachCL(k),
        BuildLanding(k),
        BuildLandingCL(k),
        BuildAfterLanding(k),
        BuildAfterLandingCL(k),
        BuildShutdown(k),
        BuildParkingCL(k),
        BuildSecure(k),
        BuildSecuringCL(k),
    };

    // An item, a run of items (one per engine) or null (a line this airframe does not have).
    private static List<Item> Items(params object?[] items) =>
        items.SelectMany(o => o switch { Item it => new[] { it }, Item[] many => many, _ => Array.Empty<Item>() }).ToList();

    /// <summary>One item per engine, engine 1 first.</summary>
    private static Item[] PerEngine(IniAirbusKeys k, Func<int, Item> item) => Enumerable.Range(1, k.EngineCount).Select(item).ToArray();

    /// <summary>One item per engine in the start order.</summary>
    private static Item[] InStartOrder(IniAirbusKeys k, Func<int, Item> item) => k.StartOrder.Select(item).ToArray();

    private static CheckFn SetAll(IEnumerable<string> keys, int value) => async (e, _) =>
    {
        foreach (var key in keys) await e.Set(key, value);
    };

    private static CheckFn SetPumps(IniAirbusKeys k, bool on) => async (e, _) =>
    {
        foreach (var (key, onV, offV) in k.FuelPumps) await e.Set(key, on ? onV : offV);
    };

    // -----------------------------------------------------------------------
    private static Group BuildElectricalPowerUp(IniAirbusKeys k) => new()
    {
        Id = "ELEC_POWER_UP", Name = "Electrical Power Up",
        Items = Items(
            // cold and dark: a tick switches a master off only on the ground with every engine stopped
            PerEngine(k, n => LeaveAlone(Auto($"EPU_CHK_MASTER{n}", "ELEC_POWER_UP", $"Engine {n} master: OFF", k.Master(n), v => v < 0.5,
                    (e, _) => e.Set(k.Master(n), 0)),
                s => NotOnGround(s) || EnginesNotStopped(s), "Left as it is: not on the ground with every engine stopped.")),
            Auto("EPU_CHK_MODE", "ELEC_POWER_UP", "Engine mode selector: NORM", k.EngineModeSelector,
                v => Math.Abs(v - IniAirbusKeys.ModeNorm) < 0.5, (e, _) => e.Set(k.EngineModeSelector, IniAirbusKeys.ModeNorm)),
            Auto("EPU_CHK_GEAR", "ELEC_POWER_UP", "Gear lever: DOWN", IniAirbusKeys.GearLever, v => v > 0.5, null),
            Auto("EPU_CHK_PARK", "ELEC_POWER_UP", "Parking brake: SET", IniAirbusKeys.ParkingBrake, v => v > 0.5, null),
            Auto("EPU_CHK_WIPERS", "ELEC_POWER_UP", "Wipers: OFF", S.WipersOff, v => v > 0.5, SetAll(k.Wipers, 0)),
            Auto("EPU_CHK_WXR", "ELEC_POWER_UP", "Weather radar: OFF", k.WxSystem,
                v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set(k.WxSystem, 1)),
            Auto("EPU_BATS", "ELEC_POWER_UP", "Batteries: ON", S.BatteriesOn, v => v > 0.5, SetAll(k.Batteries, 1)),
            LeaveAlone(Auto("EPU_EXTPWR", "ELEC_POWER_UP", "External power: ON (if available)", S.ExtPowerOn, v => v > 0.5,
                    (e, _) => e.Set(k.ExtPower[0], 1)),
                s => s.GetValue(S.ExtPowerAvailable) is var a && !double.IsNaN(a) && a < 0.5,
                "No external power available. External power left off."),
            Auto("EPU_NAVLOGO", "ELEC_POWER_UP", "Nav and logo lights: ON", k.NavLogo, v => v < 1.5,
                (e, _) => e.Set(k.NavLogo, 1)),
            Auto("EPU_ANN", "ELEC_POWER_UP", "Annunciator lights: BRIGHT", k.AnnLights,
                v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set(k.AnnLights, 1)),
            k.Dome is { } dome ? Auto("EPU_DOME", "ELEC_POWER_UP", "Dome light: BRIGHT", dome, v => Math.Abs(v) < 0.5, (e, _) => e.Set(dome, 0)) : null,
            Auto("EPU_STBYCOMPASS", "ELEC_POWER_UP", "Standby compass light: ON", k.StbyCompassLight,
                v => v > 0.5, (e, _) => e.Set(k.StbyCompassLight, 1)))
    };

    // -----------------------------------------------------------------------
    private static Group BuildPreflight(IniAirbusKeys k) => new()
    {
        Id = "PREFLIGHT", Name = "Preflight",
        Items = Items(
            // ADIRS: the A340's are the First Officer's; the Captain's selector on the A330 (not probed) and
            // the A321 (absent from its map)
            k.IrsWritable
                ? Auto("PF_IRS", "PREFLIGHT", "IRS 1, 2 and 3: NAV", S.IrsNav, v => v > 0.5, SetAll(k.IrsModeReads, 1))
                : k.IrsModeReads.Count > 0
                    ? Auto("PF_IRS", "PREFLIGHT", "Set IRS 1, 2 and 3 to NAV", S.IrsNav, v => v > 0.5, null)
                    : Reminder("PF_IRS", "PREFLIGHT", "Set IRS 1, 2 and 3 to NAV"),
            Auto("PF_OXY", "PREFLIGHT", "Crew oxygen supply: ON", k.CrewOxygen, v => v > 0.5, (e, _) => e.Set(k.CrewOxygen, 1)),
            k.ApuFireTest != null
                ? ActionManual("PF_FIRE_APU", "PREFLIGHT", "APU fire test: listen for the fire warning", (e, _) => e.Set(k.ApuFireTest, 1))
                : Reminder("PF_FIRE_APU", "PREFLIGHT", "APU fire test"),
            k.EngFireTest != null
                ? ActionManual("PF_FIRE_ENG", "PREFLIGHT", "Engine fire test: listen for the fire warning", (e, _) => e.Set(k.EngFireTest, 1))
                : Reminder("PF_FIRE_ENG", "PREFLIGHT", k.EngineCount == 2 ? "Engine 1 and 2 fire tests" : "Engine fire tests"),
            ActionManual("PF_ECAMDOOR", "PREFLIGHT", "ECAM page: door", (e, _) => e.Set(IniAirbusActionExecutor.EcamPagePrefix + "DOOR", 1)),
            Auto("PF_PACKS", "PREFLIGHT", "Packs 1 and 2: ON", S.PacksOn, v => v > 0.5, SetAll(k.Packs, 1)),
            Auto("PF_XBLEED", "PREFLIGHT", "Crossbleed: AUTO", k.CrossBleed, v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set(k.CrossBleed, 1)),
            Auto("PF_PACKFLOW", "PREFLIGHT", "Pack flow: NORMAL", k.PackFlow, v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set(k.PackFlow, 1)),
            Auto("PF_HOTAIR", "PREFLIGHT", "Hot air: ON", S.HotAirOn, v => v > 0.5, SetAll(k.HotAir, 1)),
            k.PressMode is { } press
                ? Auto("PF_PRESSMODE", "PREFLIGHT", "Cabin pressure mode: AUTO", press,
                    v => Math.Abs(v - k.PressModeAuto) < 0.5, (e, _) => e.Set(press, k.PressModeAuto))
                : null,
            Auto("PF_STROBE", "PREFLIGHT", "Strobes: AUTO", k.Strobe, v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set(k.Strobe, 1)),
            Auto("PF_WING_LT", "PREFLIGHT", "Wing lights: OFF", k.WingLights, v => v < 0.5, (e, _) => e.Set(k.WingLights, 0)),
            Auto("PF_NOSMOKE", "PREFLIGHT", "No smoking: AUTO", k.NoSmoking, v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set(k.NoSmoking, 1)),
            Auto("PF_EMEREXIT", "PREFLIGHT", "Emergency exit lights: ARM", k.EmerExit, v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set(k.EmerExit, 1)),
            k.AltReporting != null
                ? Auto("PF_ALTRPTG", "PREFLIGHT", "Altitude reporting: ON", k.AltReporting, v => v > 0.5, (e, _) => e.Set(k.AltReporting, 1))
                : null,
            Auto("PF_TCASTRAFFIC", "PREFLIGHT", "TCAS traffic: ALL", k.TcasTraffic, v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set(k.TcasTraffic, 1)),
            Auto("PF_FDS", "PREFLIGHT", "Flight directors: ON", S.FdsOn, v => v > 0.5, SetAll(new[] { k.CaptFd, k.FoFd }, 1)),
            Reminder("PF_BARO", "PREFLIGHT", "Set QNH on both altimeters and the standby altimeter"),
            Reminder("PF_FCUALT", "PREFLIGHT", "Set the initial cleared altitude on the FCU"),
            Reminder("PF_SQUAWK", "PREFLIGHT", "Set the squawk code"),
            Reminder("PF_EFB", "PREFLIGHT", "EFB setup — load fuel and payload"),
            Reminder("PF_MCDU", "PREFLIGHT", "MCDU setup — INIT, flight plan and PERF pages"))
    };

    // -----------------------------------------------------------------------
    private static Group BuildBeforeStart(IniAirbusKeys k) => new()
    {
        Id = "BEFORE_START", Name = "Before Start",
        Items = Items(
            ActionManual("BS_ECAMAPU", "BEFORE_START", "ECAM page: APU", (e, _) => e.Set(IniAirbusActionExecutor.EcamPagePrefix + "APU", 1)),
            Auto("BS_APU", "BEFORE_START", "APU: ON and available", k.ApuAvailLamp,
                IniAirbusActionExecutor.IsApuAvailable, (e, s) => e.StartApuAsync(s)),
            Auto("BS_APUBLEED", "BEFORE_START", "APU bleed: ON", k.ApuBleed, v => v > 0.5, (e, _) => e.Set(k.ApuBleed, 1)),
            Auto("BS_FUELPUMPS", "BEFORE_START", "Fuel pumps: ALL ON", S.FuelPumpsOn, v => v > 0.5, SetPumps(k, on: true)),
            // never onto the batteries: a tick switches external power off only with the APU available
            LeaveAlone(Auto("BS_EXTPWR_OFF", "BEFORE_START", "External power: OFF", S.ExtPowerOff, v => v > 0.5,
                    SetAll(k.ExtPower, 0)),
                s => !IniAirbusActionExecutor.IsApuAvailable(s.GetValue(k.ApuAvailLamp)),
                "External power left on: the APU is not available."),
            Auto("BS_SEATBELTS", "BEFORE_START", "Seat belts: ON", S.SeatBeltsOn, v => v > 0.5, (e, _) => e.SetSeatbeltSign(true)),
            Auto("BS_BEACON", "BEFORE_START", "Beacon: ON", k.Beacon, v => v > 0.5, (e, _) => e.Set(k.Beacon, 1)),
            Reminder("BS_FCU", "BEFORE_START", "Set the FCU: cleared altitude, speed and heading managed"),
            Reminder("BS_DOORS", "BEFORE_START", "Close doors and remove ground services on the EFB"),
            Reminder("BS_THRLEVERS", "BEFORE_START", "Confirm thrust levers idle"),
            Reminder("BS_CLEARANCE", "BEFORE_START", "Obtain pushback and start clearance"))
    };

    // -----------------------------------------------------------------------
    private static Group BuildEngineStart(IniAirbusKeys k) => new()
    {
        Id = "ENGINE_START", Name = "Engine Start",
        Items = Items(
            ActionManual("ES_ECAMENG", "ENGINE_START", "ECAM page: engine", (e, _) => e.Set(IniAirbusActionExecutor.EcamPagePrefix + "ENG", 1)),
            LeaveAlone(Auto("ES_MODE", "ENGINE_START", "Engine mode selector: IGN START", k.EngineModeSelector,
                    v => Math.Abs(v - IniAirbusKeys.ModeIgnStart) < 0.5, (e, _) => e.Set(k.EngineModeSelector, IniAirbusKeys.ModeIgnStart)),
                NotOnGround, NotOnGroundText),
            // a master tick opens fuel only on the ground with the selector at IGN START, over an engine not
            // already running (a toggling master pressed there would cut it); the running lines read the
            // aircraft's own engine AVAIL
            InStartOrder(k, n => LeaveAlone(Auto($"ES_ENG{n}", "ENGINE_START", $"Engine {n} master: ON", k.Master(n), v => v > 0.5,
                    (e, _) => e.Set(k.Master(n), 1)),
                s => NotOnGround(s) || !s.IsPosition(k.EngineModeSelector, IniAirbusKeys.ModeIgnStart)
                    || !(s.GetValue(S.RunningField(n)) < 0.5),
                $"Engine {n} master left as it is: the engine mode selector is not at IGN START, the engine is running, or the aircraft is not on the ground.")),
            InStartOrder(k, n => Auto($"ES_ENG{n}_RUN", "ENGINE_START", $"Engine {n}: running", S.RunningField(n), v => v > 0.5, null)))
    };

    // -----------------------------------------------------------------------
    private static Group BuildAfterStart(IniAirbusKeys k) => new()
    {
        Id = "AFTER_START", Name = "After Start",
        Items = Items(
            Auto("AS_MODE_NORM", "AFTER_START", "Engine mode selector: NORM", k.EngineModeSelector,
                v => Math.Abs(v - IniAirbusKeys.ModeNorm) < 0.5, (e, _) => e.Set(k.EngineModeSelector, IniAirbusKeys.ModeNorm)),
            Auto("AS_APUBLEED_OFF", "AFTER_START", "APU bleed: OFF", k.ApuBleed, v => v < 0.5, (e, _) => e.Set(k.ApuBleed, 0)),
            Auto("AS_APUMASTER_OFF", "AFTER_START", "APU master: OFF", k.ApuMaster, v => v < 0.5, (e, _) => e.Set(k.ApuMaster, 0)),
            // read only: the Captain arms the spoilers and sets the flaps
            Auto("AS_SPOILERS_ARM", "AFTER_START", "Ground spoilers: ARMED", IniAirbusKeys.SpoilersArmed, v => v > 0.5, null),
            Auto("AS_FLAPS", "AFTER_START", "Flaps: takeoff setting", IniAirbusKeys.Flaps, S.IsTakeoffFlaps, null),
            Auto("AS_NOSE_TAXI", "AFTER_START", "Nose light: TAXI", k.NoseLight, v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set(k.NoseLight, 1)),
            Reminder("AS_ANTIICE", "AFTER_START", "Set engine and wing anti-ice as required"),
            Reminder("AS_PITCHTRIM", "AFTER_START", "Set pitch trim per the loadsheet"),
            Auto("AS_ANN", "AFTER_START", "Annunciator lights: DIM", k.AnnLights, v => Math.Abs(v - 2) < 0.5, (e, _) => e.Set(k.AnnLights, 2)),
            k.Dome is { } dome ? Auto("AS_DOME", "AFTER_START", "Dome light: OFF", dome, v => Math.Abs(v - 2) < 0.5, (e, _) => e.Set(dome, 2)) : null,
            ActionManual("AS_ECAMSTS", "AFTER_START", "ECAM page: status", (e, _) => e.Set(IniAirbusActionExecutor.EcamPagePrefix + "STS", 1)))
    };

    // -----------------------------------------------------------------------
    private static Group BuildTaxi(IniAirbusKeys k) => new()
    {
        Id = "TAXI", Name = "Taxi",
        Items = Items(
            // MAX toggles: a tick presses only over a level read and not already MAX
            LeaveAlone(Auto("TX_AUTOBRAKE", "TAXI", "Autobrake: MAX", S.AutobrakeMax,
                    v => v > 0.5, (e, _) => e.Set(IniAirbusKeys.Autobrake, k.AutobrakeMax)),
                s => double.IsNaN(s.GetValue(IniAirbusKeys.Autobrake)) || s.AutobrakeMaxLeftToCaptain(),
                "Autobrake left as it is: set MAX yourself."),
            Auto("TX_WXR", "TAXI", "Weather radar: SYSTEM 1", k.WxSystem, v => Math.Abs(v) < 0.5, (e, _) => e.Set(k.WxSystem, 0)),
            Auto("TX_PWS", "TAXI", "Predictive windshear: AUTO", k.Pws, v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set(k.Pws, 1)),
            Auto("TX_XPDRAUTO", "TAXI", "Transponder: AUTO", k.XpdrMode, v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set(k.XpdrMode, 1)),
            k.TakeoffConfigTest != null
                ? ActionManual("TX_CONFIG", "TAXI", "Takeoff config test", (e, _) => e.Set(k.TakeoffConfigTest, 1))
                : Reminder("TX_CONFIG", "TAXI", "Takeoff config test"))
    };

    // -----------------------------------------------------------------------
    private static Group BuildLineup(IniAirbusKeys k) => new()
    {
        Id = "LINEUP", Name = "Line-up",
        Items = Items(
            k.CabinCallAll != null
                ? ActionManual("LU_CABIN", "LINEUP", "Advise the cabin crew for takeoff (call all)", (e, _) => e.Set(k.CabinCallAll, 1))
                : Reminder("LU_CABIN", "LINEUP", "Advise the cabin crew for takeoff"),
            Auto("LU_TCAS", "LINEUP", "TCAS: TA/RA", k.TcasMode, v => Math.Abs(v - 2) < 0.5, (e, _) => e.Set(k.TcasMode, 2)),
            Auto("LU_STROBE", "LINEUP", "Strobes: ON", k.Strobe, v => Math.Abs(v) < 0.5, (e, _) => e.Set(k.Strobe, 0)),
            Auto("LU_TURNOFF", "LINEUP", "Runway turn-off lights: ON", k.RunwayTurnoff, v => v > 0.5, (e, _) => e.Set(k.RunwayTurnoff, 1)),
            Auto("LU_NOSE_TO", "LINEUP", "Nose light: TAKEOFF", k.NoseLight, v => Math.Abs(v) < 0.5, (e, _) => e.Set(k.NoseLight, 0)),
            Auto("LU_LANDING_LT", "LINEUP", "Landing lights: ON", S.LandingLightsOn, v => v > 0.5,
                (e, _) => e.SetLandingLights(IniAirbusActionExecutor.LandingOn)),
            Reminder("LU_CLEARANCE", "LINEUP", "Obtain takeoff clearance"))
    };

    // -----------------------------------------------------------------------
    private static Group BuildAfterTakeoff(IniAirbusKeys k) => new()
    {
        Id = "AFTER_TAKEOFF", Name = "After Takeoff",
        Items = Items(
            Reminder("AT_SPOILERS_CAPT", "AFTER_TAKEOFF", "Ground spoilers: DISARM"),
            Auto("AT_PACKS", "AFTER_TAKEOFF", "Packs 1 and 2: ON", S.PacksOn, v => v > 0.5, SetAll(k.Packs, 1)),
            Auto("AT_TURNOFF_OFF", "AFTER_TAKEOFF", "Runway turn-off lights: OFF", k.RunwayTurnoff, v => v < 0.5,
                (e, _) => e.Set(k.RunwayTurnoff, 0)))
    };

    // -----------------------------------------------------------------------
    private static Group BuildDescent(IniAirbusKeys k) => new()
    {
        Id = "DESCENT", Name = "Descent",
        Items = Items(
            Reminder("DC_AUTOBRAKE", "DESCENT", "Set the landing autobrake — Main panel section, Brakes panel"),
            Auto("DC_SEATBELTS", "DESCENT", "Seat belts: ON", S.SeatBeltsOn, v => v > 0.5, (e, _) => e.SetSeatbeltSign(true)),
            Reminder("DC_MCDU", "DESCENT",
                "Descent preparation: MCDU PERF APPR set — QNH, temperature, wind and minimums; landing configuration reviewed"))
    };

    // -----------------------------------------------------------------------
    private static Group BuildApproach(IniAirbusKeys k) => new()
    {
        Id = "APPROACH", Name = "Approach",
        Items = Items(
            Auto("AP_LS", "APPROACH", "LS captain and first officer: ON", S.LsOn, v => v > 0.5, SetAll(new[] { k.CaptLs, k.FoLs }, 1)),
            Auto("AP_SEATBELTS", "APPROACH", "Seat belts: ON", S.SeatBeltsOn, v => v > 0.5, (e, _) => e.SetSeatbeltSign(true)),
            k.CabinCallAll != null
                ? ActionManual("AP_CABIN", "APPROACH", "Notify the cabin crew for landing (call all)", (e, _) => e.Set(k.CabinCallAll, 1))
                : Reminder("AP_CABIN", "APPROACH", "Notify the cabin crew for landing"),
            Reminder("AP_BARO", "APPROACH", "Set the altimeters to the local QNH when cleared"),
            Reminder("AP_MINIMUMS", "APPROACH", "Check minimums set on the MCDU approach page"),
            Reminder("AP_ENGMODE", "APPROACH", "Set engine mode selector as required"))
    };

    // -----------------------------------------------------------------------
    private static Group BuildLanding(IniAirbusKeys k) => new()
    {
        Id = "LANDING", Name = "Landing",
        Items = Items(
            Auto("LD_SPOILERS_ARM", "LANDING", "Ground spoilers: ARMED", IniAirbusKeys.SpoilersArmed, v => v > 0.5, null))
    };

    // -----------------------------------------------------------------------
    private static Group BuildAfterLanding(IniAirbusKeys k) => new()
    {
        Id = "AFTER_LANDING", Name = "After Landing",
        Items = Items(
            Reminder("AL_SPOILERS_CAPT", "AFTER_LANDING", "Ground spoilers: DISARM"),
            Reminder("AL_FLAPS_CAPT", "AFTER_LANDING", "Flaps: UP"),
            Auto("AL_WXR_OFF", "AFTER_LANDING", "Weather radar: OFF", k.WxSystem, v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set(k.WxSystem, 1)),
            Auto("AL_PWS_OFF", "AFTER_LANDING", "Predictive windshear: OFF", k.Pws, v => v < 0.5, (e, _) => e.Set(k.Pws, 0)),
            Auto("AL_STROBE_AUTO", "AFTER_LANDING", "Strobes: AUTO", k.Strobe, v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set(k.Strobe, 1)),
            Auto("AL_LANDING_OFF", "AFTER_LANDING", "Landing lights: OFF", S.LandingLightsOff, v => v > 0.5,
                (e, _) => e.SetLandingLights(IniAirbusActionExecutor.LandingStowed)),
            Auto("AL_NOSE_TAXI", "AFTER_LANDING", "Nose light: TAXI", k.NoseLight, v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set(k.NoseLight, 1)),
            Auto("AL_APU", "AFTER_LANDING", "APU: ON and available", k.ApuAvailLamp,
                IniAirbusActionExecutor.IsApuAvailable, (e, s) => e.StartApuAsync(s)),
            Auto("AL_ANTIICE_OFF", "AFTER_LANDING", "Engine and wing anti-ice: OFF", S.AntiIceOff, v => v > 0.5,
                SetAll(k.EngAntiIce.Append(k.WingAntiIce), 0)))
    };

    // -----------------------------------------------------------------------
    private static Group BuildShutdown(IniAirbusKeys k) => new()
    {
        Id = "SHUTDOWN", Name = "Shutdown",
        Items = Items(
            Reminder("SD_PARKBRAKE_CAPT", "SHUTDOWN", "Parking brake: SET"),
            Auto("SD_PARKBRAKE", "SHUTDOWN", "Parking brake: SET (confirmed)", IniAirbusKeys.ParkingBrake, v => v > 0.5, null),
            Auto("SD_APUBLEED_ON", "SHUTDOWN", "APU bleed: ON", k.ApuBleed, v => v > 0.5, (e, _) => e.Set(k.ApuBleed, 1)),
            // a master tick cuts fuel only on the ground with the parking brake read SET
            PerEngine(k, n => LeaveAlone(Auto($"SD_ENG{n}_OFF", "SHUTDOWN", $"Engine {n} master: OFF", k.Master(n), v => v < 0.5,
                    (e, _) => e.Set(k.Master(n), 0)),
                s => NotOnGround(s) || !s.IsOn(IniAirbusKeys.ParkingBrake),
                $"Engine {n} master left on: not on the ground with the parking brake set.")),
            Auto("SD_ENGINES_OFF", "SHUTDOWN", "Engines: stopped", S.EnginesOff, v => v > 0.5, null),
            Auto("SD_XPDR_STBY", "SHUTDOWN", "Transponder: STANDBY", k.XpdrMode, v => v < 0.5, (e, _) => e.Set(k.XpdrMode, 0)),
            Auto("SD_TCAS_STBY", "SHUTDOWN", "TCAS: STANDBY", k.TcasMode, v => v < 0.5, (e, _) => e.Set(k.TcasMode, 0)),
            Auto("SD_LS_OFF", "SHUTDOWN", "LS captain and first officer: OFF", S.LsOff, v => v > 0.5, SetAll(new[] { k.CaptLs, k.FoLs }, 0)),
            Auto("SD_SEATBELTS_OFF", "SHUTDOWN", "Seat belts: OFF", S.SeatBeltsOff, v => v > 0.5, (e, _) => e.SetSeatbeltSign(false)),
            // beacon and fuel pumps off on a tick only once both engines read stopped (FO-25)
            LeaveAlone(Auto("SD_BEACON_OFF", "SHUTDOWN", "Beacon: OFF", k.Beacon, v => v < 0.5, (e, _) => e.Set(k.Beacon, 0)),
                EnginesNotStopped, EnginesRunningText),
            Auto("SD_WING_LT_OFF", "SHUTDOWN", "Wing lights: OFF", k.WingLights, v => v < 0.5, (e, _) => e.Set(k.WingLights, 0)),
            // ...and the fuel pumps never airborne either: engines out in flight, they feed a relight
            LeaveAlone(Auto("SD_FUELPUMPS_OFF", "SHUTDOWN", "Fuel pumps: ALL OFF", S.FuelPumpsOff, v => v > 0.5, SetPumps(k, on: false)),
                s => NotOnGround(s) || EnginesNotStopped(s), "Fuel pumps left on: not on the ground with every engine stopped."),
            Auto("SD_NOSE_OFF", "SHUTDOWN", "Nose light: OFF", k.NoseLight, v => Math.Abs(v - 2) < 0.5, (e, _) => e.Set(k.NoseLight, 2)),
            Auto("SD_TURNOFF_OFF", "SHUTDOWN", "Runway turn-off lights: OFF", k.RunwayTurnoff, v => v < 0.5, (e, _) => e.Set(k.RunwayTurnoff, 0)),
            Auto("SD_ANN", "SHUTDOWN", "Annunciator lights: BRIGHT", k.AnnLights, v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set(k.AnnLights, 1)),
            k.Dome is { } dome ? Auto("SD_DOME", "SHUTDOWN", "Dome light: BRIGHT", dome, v => Math.Abs(v) < 0.5, (e, _) => e.Set(dome, 0)) : null,
            ActionManual("SD_ECAMDOOR", "SHUTDOWN", "ECAM page: door", (e, _) => e.Set(IniAirbusActionExecutor.EcamPagePrefix + "DOOR", 1)))
    };

    // -----------------------------------------------------------------------
    private static Group BuildSecure(IniAirbusKeys k) => new()
    {
        Id = "SECURE", Name = "Securing",
        Items = Items(
            // the A340's IRS: a tick switches them off only on the ground with every engine stopped (FO-25)
            k.IrsWritable
                ? LeaveAlone(Auto("SC_ADIRS", "SECURE", "IRS 1, 2 and 3: OFF", S.IrsOff, v => v > 0.5, SetAll(k.IrsModeReads, 0)),
                    s => NotOnGround(s) || EnginesNotStopped(s), "IRS left on: not on the ground with every engine stopped.")
                : k.IrsModeReads.Count > 0
                    ? Auto("SC_ADIRS", "SECURE", "Set IRS 1, 2 and 3 to OFF", S.IrsOff, v => v > 0.5, null)
                    : Reminder("SC_ADIRS", "SECURE", "Set IRS 1, 2 and 3 to OFF"),
            LeaveAlone(Auto("SC_OXY", "SECURE", "Crew oxygen supply: OFF", k.CrewOxygen, v => v < 0.5, (e, _) => e.Set(k.CrewOxygen, 0)),
                NotOnGround, NotOnGroundText),
            LeaveAlone(Auto("SC_EMEREXIT", "SECURE", "Emergency exit lights: OFF", k.EmerExit, v => Math.Abs(v - 2) < 0.5,
                    (e, _) => e.Set(k.EmerExit, 2)),
                NotOnGround, NotOnGroundText),
            Auto("SC_NOSMOKE", "SECURE", "No smoking: OFF", k.NoSmoking, v => Math.Abs(v - 2) < 0.5, (e, _) => e.Set(k.NoSmoking, 2)),
            Auto("SC_APUBLEED", "SECURE", "APU bleed: OFF", k.ApuBleed, v => v < 0.5, (e, _) => e.Set(k.ApuBleed, 0)),
            LeaveAlone(Auto("SC_APUMASTER", "SECURE", "APU master: OFF", k.ApuMaster, v => v < 0.5, (e, _) => e.Set(k.ApuMaster, 0)),
                NotOnGround, NotOnGroundText),
            // like the flow (FOA-10): the APU flap closes on APU or external power, so the tick waits for it
            // (150 s, then carries on as the flow does) before switching external power off
            Auto("SC_EXTPWR_OFF", "SECURE", "External power: OFF", S.ExtPowerOff, v => v > 0.5, async (e, s) =>
            {
                for (int waited = 0; waited < ApuFlapWaitMs && !(s.GetValue(IniAirbusKeys.ApuFlap) < 0.05); waited += 1000)
                    await Task.Delay(1000);
                await SetAll(k.ExtPower, 0)(e, s);
            }),
            // batteries off on a tick only on the ground with both engines stopped (FO-25)
            LeaveAlone(Auto("SC_BATS", "SECURE", "Batteries: OFF", S.BatteriesOff, v => v > 0.5, SetAll(k.Batteries, 0)),
                s => !s.IsPosition("SIM_ON_GROUND", 1) || EnginesNotStopped(s),
                "Batteries left on: not on the ground with every engine stopped."),
            k.Dome is { } dome ? Auto("SC_DOME", "SECURE", "Dome light: OFF", dome, v => Math.Abs(v - 2) < 0.5, (e, _) => e.Set(dome, 2)) : null,
            Auto("SC_STBYCOMPASS", "SECURE", "Standby compass light: OFF", k.StbyCompassLight, v => v < 0.5,
                (e, _) => e.Set(k.StbyCompassLight, 0)))
    };

    // -----------------------------------------------------------------------
    // Read-backs: the Airbus normal checklist (FOA-8 ids, order and words; FO-3 action-free)
    // -----------------------------------------------------------------------

    private static Group BuildCockpitPrepCL(IniAirbusKeys k) => new()
    {
        Id = "COCKPIT_PREP_CL", Name = "Cockpit Preparation Checklist",
        Items = Items(
            Reminder("CPC_PINS", "COCKPIT_PREP_CL", "Gear pins and covers: REMOVED"),
            Reminder("CPC_FUEL", "COCKPIT_PREP_CL", "Fuel quantity: CHECKED", s => s.FuelText()),
            Auto("CPC_SEATBELTS", "COCKPIT_PREP_CL", "Seat belts: ON", S.SeatBeltsOn, v => v > 0.5, null),
            k.IrsModeReads.Count > 0
                ? Auto("CPC_ADIRS", "COCKPIT_PREP_CL", "ADIRS: NAV", S.IrsNav, v => v > 0.5, null)
                : Reminder("CPC_ADIRS", "COCKPIT_PREP_CL", "ADIRS: NAV"),
            Reminder("CPC_BARO", "COCKPIT_PREP_CL", "Baro reference: SET", s => s.BaroText()))
    };

    private static Group BuildBeforeStartCL(IniAirbusKeys k) => new()
    {
        Id = "BEFORE_START_CL", Name = "Before Start Checklist",
        Items = Items(
            Reminder("BSC_PARKBRK", "BEFORE_START_CL", "Parking brake: SET",
                s => AirbusReadbackFormat.OnOff(s.GetValue(IniAirbusKeys.ParkingBrake))),
            Reminder("BSC_TOSPEEDS", "BEFORE_START_CL", "Takeoff speeds and thrust: SET",
                s => AirbusReadbackFormat.TakeoffSpeeds(s.GetValue(IniAirbusKeys.V1), s.GetValue(IniAirbusKeys.VR),
                    s.GetValue(IniAirbusKeys.V2), double.NaN)),
            Reminder("BSC_WINDOWS", "BEFORE_START_CL", "Windows: CLOSED"),
            Auto("BSC_BEACON", "BEFORE_START_CL", "Beacon: ON", k.Beacon, v => v > 0.5, null),
            Reminder("BSC_ACARS", "BEFORE_START_CL", "Start ACARS"))
    };

    private static Group BuildAfterStartCL(IniAirbusKeys k) => new()
    {
        Id = "AFTER_START_CL", Name = "After Start Checklist",
        Items = Items(
            Reminder("ASC_ANTIICE", "AFTER_START_CL", "Anti-ice: SET",
                s => s.AntiIceText()),
            Reminder("ASC_ECAMSTS", "AFTER_START_CL", "ECAM status: CHECKED"),
            Reminder("ASC_PITCH", "AFTER_START_CL", "Pitch trim: SET"),
            // no rudder trim the First Officer can read on these aircraft
            Reminder("ASC_RUDDER", "AFTER_START_CL", "Rudder trim: NEUTRAL"))
    };

    private static Group BuildTaxiCL(IniAirbusKeys k) => new()
    {
        Id = "TAXI_CL", Name = "Taxi Checklist",
        Items = Items(
            Reminder("TXC_FCTEST", "TAXI_CL", "Flight controls: CHECKED"),
            Reminder("TXC_FLAPS", "TAXI_CL", "Flaps setting: SET",
                s => AirbusReadbackFormat.FlapsLever(s.GetValue(IniAirbusKeys.Flaps))),
            Auto("TXC_WXR", "TAXI_CL", "Radar and predictive windshear: ON and AUTO", S.WxrOnAuto, v => v > 0.5, null),
            Reminder("TXC_ENGMODE", "TAXI_CL", "Engine mode selector: SET"),
            Auto("TXC_MEMO_AUTOBRK", "TAXI_CL", "Autobrake: MAX", S.AutobrakeMax, v => v > 0.5, null),
            Auto("TXC_MEMO_SIGNS", "TAXI_CL", "Seat belts: ON", S.SignsOn, v => v > 0.5, null),
            Auto("TXC_MEMO_SPLRS", "TAXI_CL", "Spoilers: ARMED", IniAirbusKeys.SpoilersArmed, v => v > 0.5, null),
            Auto("TXC_MEMO_FLAPS", "TAXI_CL", "Flaps: T.O", IniAirbusKeys.Flaps, S.IsTakeoffFlaps, null),
            Reminder("TXC_MEMO_TOCFG", "TAXI_CL", "T.O config: NORMAL"))
    };

    private static Group BuildLineupCL(IniAirbusKeys k) => new()
    {
        Id = "LINEUP_CL", Name = "Line-up Checklist",
        Items = Items(
            Reminder("LUC_RUNWAY", "LINEUP_CL", "Takeoff runway: CONFIRMED"),
            Reminder("LUC_CABIN", "LINEUP_CL", "Cabin: READY"),
            Auto("LUC_TCAS", "LINEUP_CL", "TCAS: TA/RA", k.TcasMode, v => Math.Abs(v - 2) < 0.5, null,
                live: s => AirbusReadbackFormat.Tcas(s.GetValue(k.TcasMode))),
            Reminder("LUC_PACKS", "LINEUP_CL", "Packs 1 and 2: SET",
                s => AirbusReadbackFormat.Packs(s.GetValue(k.Packs[0]), s.GetValue(k.Packs[1]))))
    };

    private static Group BuildApproachCL(IniAirbusKeys k) => new()
    {
        Id = "APPROACH_CL", Name = "Approach Checklist",
        Items = Items(
            Reminder("APC_BARO", "APPROACH_CL", "Baro reference: SET", s => s.BaroText()),
            Auto("APC_SEATBELTS", "APPROACH_CL", "Seat belts: ON", S.SeatBeltsOn, v => v > 0.5, null),
            Reminder("APC_MINIMUM", "APPROACH_CL", "Minimum: SET"),
            Reminder("APC_AUTOBRAKE", "APPROACH_CL", "Autobrake: SET",
                s => s.AutobrakeText()),
            Reminder("APC_ENGMODE", "APPROACH_CL", "Engine mode selector: SET"))
    };

    private static Group BuildLandingCL(IniAirbusKeys k) => new()
    {
        Id = "LANDING_CL", Name = "Landing Checklist",
        Items = Items(
            Auto("LDC_MEMO_GEAR", "LANDING_CL", "Landing gear: DOWN", S.GearDown, v => v > 0.5, null),
            Auto("LDC_MEMO_SIGNS", "LANDING_CL", "Seat belts: ON", S.SeatBeltsOn, v => v > 0.5, null),
            Reminder("LDC_MEMO_CABIN", "LANDING_CL", "Cabin: READY"),
            Auto("LDC_MEMO_SPLRS", "LANDING_CL", "Spoilers: ARMED", IniAirbusKeys.SpoilersArmed, v => v > 0.5, null),
            Auto("LDC_MEMO_FLAPS", "LANDING_CL", "Flaps: SET", S.LdgFlapsSet, v => v > 0.5, null,
                live: s => AirbusReadbackFormat.FlapsLever(s.GetValue(IniAirbusKeys.Flaps))))
    };

    private static Group BuildAfterLandingCL(IniAirbusKeys k) => new()
    {
        Id = "AFTER_LANDING_CL", Name = "After Landing Checklist",
        Items = Items(
            Auto("ALC_WXR", "AFTER_LANDING_CL", "Radar and predictive windshear: OFF", S.WxrPwsOff, v => v > 0.5, null))
    };

    private static Group BuildParkingCL(IniAirbusKeys k) => new()
    {
        Id = "PARKING_CL", Name = "Parking Checklist",
        Items = Items(
            Reminder("PKC_PARKBRK", "PARKING_CL", "Parking brake or chocks: SET",
                s => AirbusReadbackFormat.OnOff(s.GetValue(IniAirbusKeys.ParkingBrake))),
            Auto("PKC_ENGINES", "PARKING_CL", "Engines: OFF", S.EnginesOff, v => v > 0.5, null),
            Auto("PKC_WINGLT", "PARKING_CL", "Wing lights: OFF", k.WingLights, v => v < 0.5, null),
            Auto("PKC_FUELPUMPS", "PARKING_CL", "Fuel pumps: OFF", S.FuelPumpsOff, v => v > 0.5, null))
    };

    private static Group BuildSecuringCL(IniAirbusKeys k) => new()
    {
        Id = "SECURING_CL", Name = "Securing Checklist",
        Items = Items(
            Auto("SCC_OXY", "SECURING_CL", "Oxygen: OFF", k.CrewOxygen, v => v < 0.5, null),
            Auto("SCC_EMEREXIT", "SECURING_CL", "Emergency exit lights: OFF", k.EmerExit, v => Math.Abs(v - 2) < 0.5, null),
            Reminder("SCC_EFBS", "SECURING_CL", "EFBs: OFF"),
            Auto("SCC_BAT", "SECURING_CL", "Batteries: OFF", S.BatteriesOff, v => v > 0.5, null))
    };

    // -----------------------------------------------------------------------
    // Item builders
    // -----------------------------------------------------------------------

    private static Item Auto(string id, string groupId, string label, string field, Func<double, bool> condition,
        CheckFn? action, Func<S, string?>? live = null) => new()
    {
        Id = id, GroupId = groupId, Label = label,
        Type = ChecklistItemType.AutoDetectable,
        AutoCompleteAllowed = true,
        ManualCompletionAllowed = true,
        StateFieldName = field,
        StateCondition = condition,
        RevertBehavior = RevertBehavior.RevertToState,
        CheckAction = action,
        LiveValue = live,
    };

    private static Item ActionManual(string id, string groupId, string label, CheckFn action) => new()
    {
        Id = id, GroupId = groupId, Label = label,
        Type = ChecklistItemType.Actionable,
        ManualCompletionAllowed = true,
        CheckAction = action,
    };

    private static Item Reminder(string id, string groupId, string text, Func<S, string?>? live = null) => new()
    {
        Id = id, GroupId = groupId, Label = text,
        Type = ChecklistItemType.CaptainReminder,
        ManualCompletionAllowed = true,
        ReminderText = text,
        LiveValue = live,
    };

    private static Item LeaveAlone(Item item, Func<S, bool> when, string text)
    {
        item.LeaveAloneWhen = when;
        item.LeaveAloneText = text;
        return item;
    }

    /// <summary>True unless every engine reads stopped (unread counts as running).</summary>
    internal static bool EnginesNotStopped(S s) => !(s.GetValue(S.EnginesOff) > 0.5);

    internal const string EnginesRunningText = "Left as it is: the engines do not read stopped.";

    /// <summary>True unless the aircraft reads on the ground (unread counts as airborne).</summary>
    internal static bool NotOnGround(S s) => !s.IsPosition("SIM_ON_GROUND", 1);

    internal const string NotOnGroundText = "Left as it is: the aircraft is not on the ground.";

    /// <summary>The Securing flow's APU-flap wait, in the tick.</summary>
    internal const int ApuFlapWaitMs = 150_000;
}
