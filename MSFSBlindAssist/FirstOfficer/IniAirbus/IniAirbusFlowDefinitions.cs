using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.FirstOfficer.IniAirbus;

using Flow = Models.FlowDefinition<IniAirbusStateEvaluator>;
using Step = Models.FlowStep<IniAirbusStateEvaluator>;
using S = IniAirbusStateEvaluator;

/// <summary>
/// iniBuilds A330-200 / A321LR / A340-300 (MSFS 2024) First Officer flows — the Airbus phases of the
/// other Airbus profiles (FBWA320/HWA330: Electrical Power Up through Securing), built from one airframe's
/// <see cref="IniAirbusKeys"/>.
///
/// SOURCES. Order: the Airbus normal procedures as the A320-family / Headwind A330 flows already carry
/// them (FOA-8/FOA-10/FOA-11, docs/invariants/first-officer-airbus.md), with the owner's iniBuilds-era
/// A330 checklist (Checklists/FBW_A330_Checklist.txt: "Eng2 master on, Eng1 master on") for the start
/// order: engine 2 first, then engine 1, on both airframes — on the A330 from the owner's checklist, on
/// the A321 from the Airbus A320-family FCOM normal procedure (engine 2 first: on the A320 family it
/// pressurises the yellow hydraulic system, which carries the parking-brake accumulator). The A340:
/// engines 1, 2, 3, then 4, one at a time — iniBuilds' own A340 procedure (its checklist XML, ENGINES
/// START), resolved to text; the A340's other lines follow that same procedure's sections.
///
/// ENGINE START AND SHUTDOWN are the First Officer's on every airframe (2026-10-28, owner): start selector
/// IGN START (read back, Stop), then each master ON (read back, Stop) and a wait for that engine to read
/// STARTED — its own ENGINE_n_AVAIL held, with N2 steady, for 3 s (Stop on a 180 s timeout, the master
/// left ON for the Captain to judge) — before the next; then the selector NORM. Shutdown cuts the masters
/// only on the ground, with the parking brake read SET and the APU or ground power on the buses. On the
/// A330 and A321 the master and selector writes are NOT VERIFIED IN THE SIM (IniAirbusKeys).
///
/// WHAT THE FIRST OFFICER DOES NOT DO HERE, and why (each is a Captain reminder, never a write):
/// <list type="bullet">
/// <item>ADIRS mode selectors on the A330 (not probed) and the A321 (absent from its map); the A340's are
/// the First Officer's (its cockpit code is readable).</item>
/// <item>Flaps and ground spoilers: the definition drives them with stock FLAPS_SET / SPOILERS_ARM_ON
/// events nothing measured on this aircraft. Read-only checks follow each reminder.</item>
/// <item>Parking brake: its write direction is listed unverified in docs/ini-a330.md and docs/ini-a321.md.</item>
/// <item>Altimeter STD/QNH: the STD presses are untested (docs/ini-a330.md), so the phase monitor speaks
/// the transition reminders instead.</item>
/// <item>Fire tests on every airframe (the A330's are in the never-probed fire family, the A321 has none,
/// and on the A340 a held test is the Captain's to hear). A321 only:
/// the T.O CONFIG test (its map has none), the cabin call (a bare press-flag write nothing shows is released
/// again), APU START over a press flag already reading 1, and autobrake MAX over LO or MED.</item>
/// </list>
/// Engine running is read from the aircraft's own ENGINE_n_AVAIL (N2 when that is unread), and every
/// engine, fuel, IRS and battery cut is behind a read-only guard (FO-25). The A340 has no dome light the
/// First Officer sets (a two-position switch) and no readable pressurisation mode: those lines are absent.
/// </summary>
public static class IniAirbusFlowDefinitions
{
    public static List<Flow> Build(IniAirbusKeys k) => new()
    {
        BuildElectricalPowerUp(k),
        BuildPreflight(k),
        BuildBeforeStart(k),
        BuildEngineStart(k),
        BuildAfterStart(k),
        BuildTaxi(k),
        BuildLineup(k),
        BuildAfterTakeoff(k),
        BuildDescent(k),
        BuildApproach(k),
        BuildLanding(k),
        BuildAfterLanding(k),
        BuildShutdown(k),
        BuildSecure(k),
    };

    // -----------------------------------------------------------------------
    private static Flow BuildElectricalPowerUp(IniAirbusKeys k) => new()
    {
        Id = "ELECTRICAL_POWER_UP", Name = "Electrical Power Up",
        Description = "Safety checks, batteries on, external power if available, nav lights, cockpit lights.",
        RelatedChecklistGroupIds = new[] { "ELEC_POWER_UP" },
        Steps = Steps(
            EnginesStoppedGuard("EPU_ENGINES_STOPPED"),
            // cold and dark only (behind the guard above): every master OFF and the selector NORM
            Masters(k, n => Done(Skip(SW($"EPU_CHK_MASTER{n}", $"Engine {n} master: OFF", k.Master(n), 0),
                s => s.IsPosition(k.Master(n), 0)), $"EPU_CHK_MASTER{n}")),
            Done(Skip(SW("EPU_CHK_MODE", "Engine mode selector: NORM", k.EngineModeSelector, IniAirbusKeys.ModeNorm),
                s => s.IsPosition(k.EngineModeSelector, IniAirbusKeys.ModeNorm)), "EPU_CHK_MODE"),
            // read-only safety checks: the lever and the brake are the Captain's to move
            Done(Skip(WaitForField("EPU_CHK_GEAR", "Gear lever: DOWN", IniAirbusKeys.GearLever, v => v > 0.5, 5),
                s => s.GetValue(IniAirbusKeys.GearLever) > 0.5), "EPU_CHK_GEAR"),
            Done(Skip(WaitForField("EPU_CHK_PARK", "Parking brake: SET", IniAirbusKeys.ParkingBrake, v => v > 0.5, 5),
                s => s.GetValue(IniAirbusKeys.ParkingBrake) > 0.5), "EPU_CHK_PARK"),
            Done(Skip(Multi("EPU_CHK_WIPERS", "Wipers: OFF", k.Wipers.Select(w => (w, (int?)0)).ToArray()),
                s => s.GetValue(S.WipersOff) > 0.5), "EPU_CHK_WIPERS"),
            Done(Skip(SW("EPU_CHK_WXR", "Weather radar: OFF", k.WxSystem, 1),
                s => s.IsPosition(k.WxSystem, 1)), "EPU_CHK_WXR"),
            Done(Skip(Multi("EPU_BATS", "Batteries: ON", k.Batteries.Select(b => (b, (int?)1)).ToArray()),
                s => s.GetValue(S.BatteriesOn) > 0.5), "EPU_BATS"),
            // external power only when its AVAIL lamp reads lit; left alone (said so) when it reads dark
            Done(LeaveAlone(Skip(SW("EPU_EXTPWR", "External power: ON", k.ExtPower[0], 1),
                    s => s.GetValue(S.ExtPowerOn) > 0.5),
                s => s.GetValue(S.ExtPowerAvailable) is var a && !double.IsNaN(a) && a < 0.5,
                "No external power available. External power left off."), "EPU_EXTPWR"),
            Done(Skip(SW("EPU_NAVLOGO", "Nav and logo lights: ON", k.NavLogo, 1),
                s => s.GetValue(k.NavLogo) < 1.5), "EPU_NAVLOGO"),
            Done(Skip(SW("EPU_ANN", "Annunciator lights: BRIGHT", k.AnnLights, 1),
                s => s.IsPosition(k.AnnLights, 1)), "EPU_ANN"),
            k.Dome is { } dome
                ? Done(Skip(SW("EPU_DOME", "Dome light: BRIGHT", dome, 0), s => s.IsPosition(dome, 0)), "EPU_DOME")
                : null,
            Done(Skip(SW("EPU_STBYCOMPASS", "Standby compass light: ON", k.StbyCompassLight, 1),
                s => s.IsOn(k.StbyCompassLight)), "EPU_STBYCOMPASS"))
    };

    // -----------------------------------------------------------------------
    private static Flow BuildPreflight(IniAirbusKeys k) => new()
    {
        Id = "PREFLIGHT", Name = "Preflight",
        Description = "Cockpit preparation: IRS, oxygen, fire tests, air conditioning, signs, transponder, flight directors.",
        RelatedChecklistGroupIds = new[] { "PREFLIGHT" },
        Steps = Steps(
            // ADIRS: the A340's are the First Officer's; on the A330/A321 the Captain's (not probed / not in
            // the map), and the A330 reads its knobs back
            k.IrsWritable
                ? Done(Skip(Multi("PF_IRS", "IRS 1, 2 and 3: NAV", k.IrsModeReads.Select(i => (i, (int?)1)).ToArray()),
                    s => s.GetValue(S.IrsNav) > 0.5), "PF_IRS")
                : Skip(Captain("PF_IRS_CAPT", "Set IRS 1, 2 and 3 to NAV"), s => s.GetValue(S.IrsNav) > 0.5),
            !k.IrsWritable && k.IrsModeReads.Count > 0
                ? Done(Skip(WaitForField("PF_IRS_CHECK", "IRS 1, 2 and 3: NAV", S.IrsNav, v => v > 0.5, 20),
                    s => s.GetValue(S.IrsNav) > 0.5), "PF_IRS")
                : null,
            Done(Skip(SW("PF_OXY", "Crew oxygen supply: ON", k.CrewOxygen, 1),
                s => s.IsOn(k.CrewOxygen)), "PF_OXY"),
            k.ApuFireTest != null ? Done(SW("PF_FIRE_APU", "APU fire test: listen for the fire warning", k.ApuFireTest, 1), "PF_FIRE_APU")
                : Captain("PF_FIRE_APU", "APU fire test"),
            k.EngFireTest != null ? Done(SW("PF_FIRE_ENG", "Engine fire test: listen for the fire warning", k.EngFireTest, 1), "PF_FIRE_ENG")
                : Captain("PF_FIRE_ENG", k.EngineCount == 2 ? "Engine 1 and 2 fire tests" : "Engine fire tests"),
            Done(SW("PF_ECAMDOOR", "ECAM page: door", IniAirbusActionExecutor.EcamPagePrefix + "DOOR", 1), "PF_ECAMDOOR"),
            Done(Skip(Multi("PF_PACKS", "Packs 1 and 2: ON", k.Packs.Select(p => (p, (int?)1)).ToArray()),
                s => s.GetValue(S.PacksOn) > 0.5), "PF_PACKS"),
            Done(Skip(SW("PF_XBLEED", "Crossbleed: AUTO", k.CrossBleed, 1),
                s => s.IsPosition(k.CrossBleed, 1)), "PF_XBLEED"),
            Done(Skip(SW("PF_PACKFLOW", "Pack flow: NORMAL", k.PackFlow, 1),
                s => s.IsPosition(k.PackFlow, 1)), "PF_PACKFLOW"),
            Done(Skip(Multi("PF_HOTAIR", "Hot air: ON", k.HotAir.Select(h => (h, (int?)1)).ToArray()),
                s => s.GetValue(S.HotAirOn) > 0.5), "PF_HOTAIR"),
            k.PressMode is { } press
                ? Done(Skip(SW("PF_PRESSMODE", "Cabin pressure mode: AUTO", press, k.PressModeAuto),
                    s => s.IsPosition(press, k.PressModeAuto)), "PF_PRESSMODE")
                : null,
            Done(Skip(SW("PF_STROBE", "Strobes: AUTO", k.Strobe, 1),
                s => s.IsPosition(k.Strobe, 1)), "PF_STROBE"),
            Done(Skip(SW("PF_WING_LT", "Wing lights: OFF", k.WingLights, 0),
                s => s.IsPosition(k.WingLights, 0)), "PF_WING_LT"),
            Done(Skip(SW("PF_NOSMOKE", "No smoking: AUTO", k.NoSmoking, 1),
                s => s.IsPosition(k.NoSmoking, 1)), "PF_NOSMOKE"),
            Done(Skip(SW("PF_EMEREXIT", "Emergency exit lights: ARM", k.EmerExit, 1),
                s => s.IsPosition(k.EmerExit, 1)), "PF_EMEREXIT"),
            k.AltReporting != null
                ? Done(Skip(SW("PF_ALTRPTG", "Altitude reporting: ON", k.AltReporting, 1),
                    s => s.IsOn(k.AltReporting)), "PF_ALTRPTG")
                : null,
            Done(Skip(SW("PF_TCASTRAFFIC", "TCAS traffic: ALL", k.TcasTraffic, 1),
                s => s.IsPosition(k.TcasTraffic, 1)), "PF_TCASTRAFFIC"),
            Done(Skip(Multi("PF_FDS", "Flight directors: ON", (k.CaptFd, 1), (k.FoFd, 1)),
                s => s.GetValue(S.FdsOn) > 0.5), "PF_FDS"),
            Captain("PF_BARO", "Set QNH on both altimeters and the standby altimeter"),
            Captain("PF_FCUALT", "Set the initial cleared altitude on the FCU"),
            Captain("PF_SQUAWK", "Set the squawk code"),
            Captain("PF_EFB", "EFB setup — load fuel and payload"),
            Captain("PF_MCDU", "MCDU setup — INIT, flight plan and PERF pages"))
    };

    // -----------------------------------------------------------------------
    private static Flow BuildBeforeStart(IniAirbusKeys k) => new()
    {
        Id = "BEFORE_START", Name = "Before Start",
        Description = "ECAM APU page, APU start, APU bleed, fuel pumps, external power off, seat belts, beacon.",
        RelatedChecklistGroupIds = new[] { "BEFORE_START" },
        Steps = Steps(
            Done(SW("BS_ECAMAPU", "ECAM page: APU", IniAirbusActionExecutor.EcamPagePrefix + "APU", 1), "BS_ECAMAPU"),
            // APU: master, dwell, START, wait for AVAIL. A timeout STOPS the flow here, before external
            // power is switched off below — never a silent transfer to the batteries. The AVAIL wait, not
            // the master write, completes "APU: ON and available" (FO-6/FO-11).
            Skip(SW("BS_APU_MASTER", "APU master: ON", k.ApuMaster, 1),
                s => s.IsOn(k.ApuMaster) || IniAirbusActionExecutor.IsApuAvailable(s.GetValue(k.ApuAvailLamp))),
            Skip(Wait("BS_APU_DWELL", "Waiting before APU start", 3),
                s => IniAirbusActionExecutor.IsApuAvailable(s.GetValue(k.ApuAvailLamp))),
            // The A321's START is a press flag that reads 1 while the APU runs: pressed only from 0. Over a 1,
            // or unread, the First Officer does not press and the Captain is asked (the A330 always presses).
            Skip(Captain("BS_APU_START_CAPT", "Start the APU"),
                s => IniAirbusActionExecutor.IsApuAvailable(s.GetValue(k.ApuAvailLamp)) || s.GetValue(S.ApuStartPressable) > 0.5),
            LeaveAlone(Skip(SW("BS_APU_START", "APU start", k.ApuStart, 1),
                    s => IniAirbusActionExecutor.IsApuAvailable(s.GetValue(k.ApuAvailLamp))),
                s => !(s.GetValue(S.ApuStartPressable) > 0.5), "APU start left to the Captain."),
            Done(WaitForField("BS_APU_AVAIL", "Waiting for APU available", k.ApuAvailLamp, v => v > 0.5, 180,
                onTimeout: FlowStepFailurePolicy.Stop), "BS_APU"),
            Done(Skip(SW("BS_APUBLEED", "APU bleed: ON", k.ApuBleed, 1),
                s => s.IsOn(k.ApuBleed)), "BS_APUBLEED"),
            Done(Skip(Multi("BS_FUELPUMPS", "Fuel pumps: ALL ON", k.FuelPumps.Select(p => (p.Key, (int?)p.On)).ToArray()),
                s => s.GetValue(S.FuelPumpsOn) > 0.5), "BS_FUELPUMPS"),
            Done(Skip(Multi("BS_EXTPWR_OFF", "External power: OFF", k.ExtPower.Select(e => (e, (int?)0)).ToArray()),
                s => s.GetValue(S.ExtPowerOff) > 0.5), "BS_EXTPWR_OFF"),
            Done(Skip(SW("BS_SEATBELTS", "Seat belts: ON", k.SeatBelts, k.SeatBeltsOn),
                s => s.GetValue(S.SeatBeltsOn) > 0.5), "BS_SEATBELTS"),
            Also(Done(Skip(SW("BS_BEACON", "Beacon: ON", k.Beacon, 1),
                s => s.IsOn(k.Beacon)), "BS_BEACON"), "BSC_BEACON"),
            Captain("BS_FCU", "Set the FCU: cleared altitude, speed and heading managed"),
            Captain("BS_DOORS", "Close doors and remove ground services on the EFB"),
            Captain("BS_THRLEVERS", "Confirm thrust levers idle"),
            Captain("BS_CLEARANCE", "Obtain pushback and start clearance"))
    };

    // -----------------------------------------------------------------------
    private static Flow BuildEngineStart(IniAirbusKeys k)
    {
        string order = string.Join(", ", k.StartOrder.Take(k.StartOrder.Count - 1)) + " then " + k.StartOrder[^1];
        var steps = new List<object?>
        {
            // the First Officer starts engines on the ground only (an airborne relight is the Captain's)
            OnGroundGuard("ES_ON_GROUND"),
            Done(SW("ES_ECAMENG", "ECAM page: engine", IniAirbusActionExecutor.EcamPagePrefix + "ENG", 1), "ES_ECAMENG"),
            // STOPS the flow when the selector does not read IGN START: a master opened with the selector
            // at NORM gets fuel with no start sequence, and a selector that never moved would leave every
            // master refused and a long wait on nothing (the LatinVFR A330's 2026-10-20 lesson).
            Stop(Done(Skip(Verify(SW("ES_MODE", "Engine mode selector: IGN START", k.EngineModeSelector, IniAirbusKeys.ModeIgnStart),
                    k.EngineModeSelector, v => Math.Abs(v - IniAirbusKeys.ModeIgnStart) < 0.5),
                s => s.IsPosition(k.EngineModeSelector, IniAirbusKeys.ModeIgnStart)), "ES_MODE")),
        };
        foreach (int n in k.StartOrder)
        {
            string master = k.Master(n);
            // a master that does not read ON stops the flow, never a wait on a dead start; skipped (never
            // pressed) over an engine already running, so a toggling master is never pressed OFF
            steps.Add(Requires(Stop(Done(Skip(Verify(SW($"ES_ENG{n}", $"Engine {n} master: ON", master, 1), master, v => v > 0.5),
                    s => s.IsOn(master) || s.GetValue(S.RunningField(n)) > 0.5), $"ES_ENG{n}")),
                "ES_MODE", $"Engine mode selector not at IGN START, so engine {n} master stays off."));
            // 180 s, Stop on a timeout (nothing latched): the engine must read started — its AVAIL held
            // with N2 steady — before the next one is started
            steps.Add(Done(WaitForField($"ES_ENG{n}_WAIT", StartWaitLabel(n), S.StartedField(n), v => v > 0.5, 180,
                onTimeout: FlowStepFailurePolicy.Stop), $"ES_ENG{n}_RUN"));
        }
        // every engine started: the selector back to NORM (After Start checks it again)
        steps.Add(Skip(Verify(SW("ES_MODE_NORM", "Engine mode selector: NORM", k.EngineModeSelector, IniAirbusKeys.ModeNorm),
                k.EngineModeSelector, v => Math.Abs(v - IniAirbusKeys.ModeNorm) < 0.5),
            s => s.IsPosition(k.EngineModeSelector, IniAirbusKeys.ModeNorm)));
        return new Flow
        {
            Id = "ENGINE_START", Name = "Engine Start",
            Description = $"ECAM engine page, engine mode IGN START, then engines {order}, one at a time, each waited "
                + "until it runs steadily, then the engine mode NORM.",
            RelatedChecklistGroupIds = new[] { "ENGINE_START" },
            Steps = Steps(steps.ToArray()),
        };
    }

    /// <summary>
    /// The engine-start wait's words. FlowManager speaks a wait's label as it starts, on a timeout and in
    /// "flow stopped. Unable to complete: …", so the label itself says what a failed start leaves behind:
    /// the master stays ON (the First Officer never cuts fuel on a start it cannot judge) and the Captain
    /// decides (the LatinVFR A340's wording).
    /// </summary>
    internal static string StartWaitLabel(int engine) =>
        $"Engine {engine} running. If the start fails, engine {engine} master stays ON; " +
        $"Captain, set it OFF if the start has failed";

    // -----------------------------------------------------------------------
    private static Flow BuildAfterStart(IniAirbusKeys k) => new()
    {
        Id = "AFTER_START", Name = "After Start",
        Description = "Engine mode NORM, APU bleed and APU off, spoilers and flaps for the Captain, nose light taxi, lights dim, ECAM status.",
        RelatedChecklistGroupIds = new[] { "AFTER_START" },
        Steps = Steps(
            Done(Skip(SW("AS_MODE_NORM", "Engine mode selector: NORM", k.EngineModeSelector, IniAirbusKeys.ModeNorm),
                s => s.IsPosition(k.EngineModeSelector, IniAirbusKeys.ModeNorm)), "AS_MODE_NORM"),
            Done(Skip(SW("AS_APUBLEED_OFF", "APU bleed: OFF", k.ApuBleed, 0),
                s => s.IsPosition(k.ApuBleed, 0)), "AS_APUBLEED_OFF"),
            Done(Skip(SW("AS_APUMASTER_OFF", "APU master: OFF", k.ApuMaster, 0),
                s => s.IsPosition(k.ApuMaster, 0)), "AS_APUMASTER_OFF"),
            // ground spoilers and flaps: the Captain's (stock events nothing measured here), then read back
            Skip(Captain("AS_SPOILERS_CAPT", "Ground spoilers: ARM"), s => s.IsOn(IniAirbusKeys.SpoilersArmed)),
            Done(Skip(WaitForField("AS_SPOILERS_CHECK", "Ground spoilers: ARMED", IniAirbusKeys.SpoilersArmed, v => v > 0.5, 15),
                s => s.IsOn(IniAirbusKeys.SpoilersArmed)), "AS_SPOILERS_ARM"),
            Skip(Captain("AS_FLAPS_CAPT", "Flaps: set for takeoff"), s => S.IsTakeoffFlaps(s.GetValue(IniAirbusKeys.Flaps))),
            Done(Skip(WaitForField("AS_FLAPS_CHECK", "Flaps: takeoff setting", IniAirbusKeys.Flaps, S.IsTakeoffFlaps, 20),
                s => S.IsTakeoffFlaps(s.GetValue(IniAirbusKeys.Flaps))), "AS_FLAPS"),
            Done(Skip(SW("AS_NOSE_TAXI", "Nose light: TAXI", k.NoseLight, 1),
                s => s.IsPosition(k.NoseLight, 1)), "AS_NOSE_TAXI"),
            Captain("AS_ANTIICE", "Set engine and wing anti-ice as required"),
            Captain("AS_PITCHTRIM", "Set pitch trim per the loadsheet"),
            Done(Skip(SW("AS_ANN", "Annunciator lights: DIM", k.AnnLights, 2),
                s => s.IsPosition(k.AnnLights, 2)), "AS_ANN"),
            k.Dome is { } dome
                ? Done(Skip(SW("AS_DOME", "Dome light: OFF", dome, 2), s => s.IsPosition(dome, 2)), "AS_DOME")
                : null,
            Done(SW("AS_ECAMSTS", "ECAM page: status", IniAirbusActionExecutor.EcamPagePrefix + "STS", 1), "AS_ECAMSTS"))
    };

    // -----------------------------------------------------------------------
    private static Flow BuildTaxi(IniAirbusKeys k) => new()
    {
        Id = "TAXI", Name = "Taxi",
        Description = "Autobrake MAX, weather radar and predictive windshear, transponder AUTO, a check of the takeoff memo items, then the takeoff config test.",
        RelatedChecklistGroupIds = new[] { "TAXI" },
        Steps = Steps(
            // MAX is a toggling key on both airframes (A330: the HI press; A321: the MAX key): never pressed
            // over MAX or over an unread level.
            Also(Done(LeaveAlone(Skip(SW("TX_AUTOBRAKE", "Autobrake: MAX", IniAirbusKeys.Autobrake, k.AutobrakeMax),
                    s => s.GetValue(S.AutobrakeMax) > 0.5),
                s => double.IsNaN(s.GetValue(IniAirbusKeys.Autobrake)) || s.AutobrakeMaxLeftToCaptain(),
                "Autobrake left as it is."),
                "TX_AUTOBRAKE"), "TXC_MEMO_AUTOBRK"),
            // the A321's MAX key is measured only from OFF: over LO or MED the Captain selects MAX
            Skip(Captain("TX_AUTOBRAKE_CAPT", "Autobrake: MAX"), s => !s.AutobrakeMaxLeftToCaptain()),
            Done(Skip(SW("TX_WXR", "Weather radar: SYSTEM 1", k.WxSystem, 0),
                s => s.IsPosition(k.WxSystem, 0)), "TX_WXR"),
            Done(Skip(SW("TX_PWS", "Predictive windshear: AUTO", k.Pws, 1),
                s => s.IsPosition(k.Pws, 1)), "TX_PWS"),
            Done(Skip(WaitForField("TX_WXR_PWS_CHECK", "Radar and predictive windshear: ON and AUTO", S.WxrOnAuto, v => v > 0.5, 5),
                s => s.GetValue(S.WxrOnAuto) > 0.5), "TXC_WXR"),
            Done(Skip(SW("TX_XPDRAUTO", "Transponder: AUTO", k.XpdrMode, 1),
                s => s.IsPosition(k.XpdrMode, 1)), "TX_XPDRAUTO"),
            Also(Done(Skip(WaitForField("TX_MEMO_CHECK", "Spoilers, flaps and seat belts: set for takeoff", S.TaxiMemoOk, v => v > 0.5, 5),
                s => s.GetValue(S.TaxiMemoOk) > 0.5), "TXC_MEMO_SPLRS"),
                "TXC_MEMO_FLAPS", "TXC_FLAPS", "TXC_MEMO_SIGNS"),
            k.TakeoffConfigTest != null
                ? Also(Done(SW("TX_CONFIG", "Takeoff config test", k.TakeoffConfigTest, 1), "TX_CONFIG"), "TXC_MEMO_TOCFG")
                : Captain("TX_CONFIG", "Takeoff config test"))
    };

    // -----------------------------------------------------------------------
    private static Flow BuildLineup(IniAirbusKeys k) => new()
    {
        Id = "LINEUP", Name = "Line-up",
        Description = "Cabin call, TCAS TA/RA, strobes, runway turn-off, nose and landing lights, then the takeoff clearance reminder.",
        RelatedChecklistGroupIds = new[] { "LINEUP" },
        Steps = Steps(
            k.CabinCallAll != null
                ? Also(Done(SW("LU_CABIN", "Advise the cabin crew for takeoff (call all)", k.CabinCallAll, 1), "LU_CABIN"), "LUC_CABIN")
                : Captain("LU_CABIN", "Advise the cabin crew for takeoff"),
            Also(Done(Skip(SW("LU_TCAS", "TCAS: TA/RA", k.TcasMode, 2),
                s => s.IsPosition(k.TcasMode, 2)), "LU_TCAS"), "LUC_TCAS"),
            Done(Skip(SW("LU_STROBE", "Strobes: ON", k.Strobe, 0),
                s => s.IsPosition(k.Strobe, 0)), "LU_STROBE"),
            Done(Skip(SW("LU_TURNOFF", "Runway turn-off lights: ON", k.RunwayTurnoff, 1),
                s => s.IsOn(k.RunwayTurnoff)), "LU_TURNOFF"),
            Done(Skip(SW("LU_NOSE_TO", "Nose light: TAKEOFF", k.NoseLight, 0),
                s => s.IsPosition(k.NoseLight, 0)), "LU_NOSE_TO"),
            Done(Skip(SW("LU_LANDING_LT", "Landing lights: ON", IniAirbusActionExecutor.LandingLightsKey, IniAirbusActionExecutor.LandingOn),
                s => s.GetValue(S.LandingLightsOn) > 0.5), "LU_LANDING_LT"),
            Captain("LU_CLEARANCE", "Obtain takeoff clearance"))
    };

    // -----------------------------------------------------------------------
    private static Flow BuildAfterTakeoff(IniAirbusKeys k) => new()
    {
        Id = "AFTER_TAKEOFF", Name = "After Takeoff",
        Description = "Ground spoilers for the Captain, packs, runway turn-off lights off; ends by confirming the gear is up.",
        RelatedChecklistGroupIds = new[] { "AFTER_TAKEOFF" },
        Steps = Steps(
            Skip(Captain("AT_SPOILERS_CAPT", "Ground spoilers: DISARM"), s => s.IsPosition(IniAirbusKeys.SpoilersArmed, 0)),
            Done(Skip(Multi("AT_PACKS", "Packs 1 and 2: ON", k.Packs.Select(p => (p, (int?)1)).ToArray()),
                s => s.GetValue(S.PacksOn) > 0.5), "AT_PACKS"),
            Done(Skip(SW("AT_TURNOFF_OFF", "Runway turn-off lights: OFF", k.RunwayTurnoff, 0),
                s => s.IsPosition(k.RunwayTurnoff, 0)), "AT_TURNOFF_OFF"),
            // read-only (FO-13): lever up and every leg retracted
            Skip(WaitForField("AT_GEAR_UP_CHECK", "Landing gear: UP", S.GearUp, v => v > 0.5, 20),
                s => s.GetValue(S.GearUp) > 0.5))
    };

    // -----------------------------------------------------------------------
    private static Flow BuildDescent(IniAirbusKeys k) => new()
    {
        Id = "DESCENT", Name = "Descent",
        Description = "Seat belts on; landing autobrake and the descent preparation are Captain items.",
        RelatedChecklistGroupIds = new[] { "DESCENT" },
        Steps = Steps(
            // FO-14: the landing autobrake is always the Captain's
            Captain("DC_AUTOBRAKE", "Set the landing autobrake — Main panel section, Brakes panel"),
            Done(Skip(SW("DC_SEATBELTS", "Seat belts: ON", k.SeatBelts, k.SeatBeltsOn),
                s => s.GetValue(S.SeatBeltsOn) > 0.5), "DC_SEATBELTS"),
            Captain("DC_MCDU",
                "Descent preparation: MCDU PERF APPR set — QNH, temperature, wind and minimums; landing configuration reviewed"))
    };

    // -----------------------------------------------------------------------
    private static Flow BuildApproach(IniAirbusKeys k) => new()
    {
        Id = "APPROACH", Name = "Approach",
        Description = "LS on both sides, seat belts on, notify the cabin, approach reminders.",
        RelatedChecklistGroupIds = new[] { "APPROACH" },
        Steps = Steps(
            Done(Skip(Multi("AP_LS", "LS captain and first officer: ON", (k.CaptLs, 1), (k.FoLs, 1)),
                s => s.GetValue(S.LsOn) > 0.5), "AP_LS"),
            Also(Done(Skip(SW("AP_SEATBELTS", "Seat belts: ON", k.SeatBelts, k.SeatBeltsOn),
                s => s.GetValue(S.SeatBeltsOn) > 0.5), "AP_SEATBELTS"), "APC_SEATBELTS"),
            k.CabinCallAll != null
                ? Done(SW("AP_CABIN", "Notify the cabin crew for landing (call all)", k.CabinCallAll, 1), "AP_CABIN")
                : Captain("AP_CABIN", "Notify the cabin crew for landing"),
            Captain("AP_BARO", "Set the altimeters to the local QNH when cleared"),
            Captain("AP_MINIMUMS", "Check minimums set on the MCDU approach page"),
            Captain("AP_ENGMODE", "Set engine mode selector as required"))
    };

    // -----------------------------------------------------------------------
    private static Flow BuildLanding(IniAirbusKeys k) => new()
    {
        Id = "LANDING", Name = "Landing",
        Description = "Ground spoilers for the Captain, then a check of the landing memo and the gear.",
        RelatedChecklistGroupIds = new[] { "LANDING" },
        Steps = Steps(
            Skip(Captain("LD_SPOILERS_CAPT", "Ground spoilers: ARM"), s => s.IsOn(IniAirbusKeys.SpoilersArmed)),
            Also(Done(Skip(WaitForField("LD_SPOILERS_CHECK", "Ground spoilers: ARMED", IniAirbusKeys.SpoilersArmed, v => v > 0.5, 15),
                s => s.IsOn(IniAirbusKeys.SpoilersArmed)), "LD_SPOILERS_ARM"), "LDC_MEMO_SPLRS"),
            Also(Done(Skip(WaitForField("LD_MEMO_CHECK", "Seat belts and flaps: set for landing", S.LdgMemoOk, v => v > 0.5, 5),
                s => s.GetValue(S.LdgMemoOk) > 0.5), "LDC_MEMO_SIGNS"), "LDC_MEMO_FLAPS"),
            // LAST (FO-13): the gear confirmed down, read only
            Done(Skip(WaitForField("LD_GEAR_DOWN_CHECK", "Landing gear: DOWN", S.GearDown, v => v > 0.5, 20),
                s => s.GetValue(S.GearDown) > 0.5), "LDC_MEMO_GEAR"))
    };

    // -----------------------------------------------------------------------
    private static Flow BuildAfterLanding(IniAirbusKeys k) => new()
    {
        Id = "AFTER_LANDING", Name = "After Landing",
        Description = "Clean up after vacating: spoilers and flaps for the Captain, radar, strobes, lights, APU start, anti-ice off.",
        RelatedChecklistGroupIds = new[] { "AFTER_LANDING" },
        Steps = Steps(
            Skip(Captain("AL_SPOILERS_CAPT", "Ground spoilers: DISARM"), s => s.IsPosition(IniAirbusKeys.SpoilersArmed, 0)),
            Skip(Captain("AL_FLAPS_CAPT", "Flaps: UP"), s => s.IsPosition(IniAirbusKeys.Flaps, 0)),
            Done(Skip(SW("AL_WXR_OFF", "Weather radar: OFF", k.WxSystem, 1),
                s => s.IsPosition(k.WxSystem, 1)), "AL_WXR_OFF"),
            Done(Skip(SW("AL_PWS_OFF", "Predictive windshear: OFF", k.Pws, 0),
                s => s.IsPosition(k.Pws, 0)), "AL_PWS_OFF"),
            Done(Skip(WaitForField("AL_WXR_PWS_CHECK", "Radar and predictive windshear: OFF", S.WxrPwsOff, v => v > 0.5, 5),
                s => s.GetValue(S.WxrPwsOff) > 0.5), "ALC_WXR"),
            Done(Skip(SW("AL_STROBE_AUTO", "Strobes: AUTO", k.Strobe, 1),
                s => s.IsPosition(k.Strobe, 1)), "AL_STROBE_AUTO"),
            Done(Skip(SW("AL_LANDING_OFF", "Landing lights: OFF", IniAirbusActionExecutor.LandingLightsKey, IniAirbusActionExecutor.LandingStowed),
                s => s.GetValue(S.LandingLightsOff) > 0.5), "AL_LANDING_OFF"),
            Done(Skip(SW("AL_NOSE_TAXI", "Nose light: TAXI", k.NoseLight, 1),
                s => s.IsPosition(k.NoseLight, 1)), "AL_NOSE_TAXI"),
            Skip(SW("AL_APU_MASTER", "APU master: ON", k.ApuMaster, 1),
                s => s.IsOn(k.ApuMaster) || IniAirbusActionExecutor.IsApuAvailable(s.GetValue(k.ApuAvailLamp))),
            Skip(Wait("AL_APU_DWELL", "Waiting before APU start", 3),
                s => IniAirbusActionExecutor.IsApuAvailable(s.GetValue(k.ApuAvailLamp))),
            // The A321's START is a press flag that reads 1 while the APU runs: pressed only from 0. Over a 1,
            // or unread, the First Officer does not press and the Captain is asked (the A330 always presses).
            Skip(Captain("AL_APU_START_CAPT", "Start the APU"),
                s => IniAirbusActionExecutor.IsApuAvailable(s.GetValue(k.ApuAvailLamp)) || s.GetValue(S.ApuStartPressable) > 0.5),
            LeaveAlone(Skip(SW("AL_APU_START", "APU start", k.ApuStart, 1),
                    s => IniAirbusActionExecutor.IsApuAvailable(s.GetValue(k.ApuAvailLamp))),
                s => !(s.GetValue(S.ApuStartPressable) > 0.5), "APU start left to the Captain."),
            Done(WaitForField("AL_APU_AVAIL", "Waiting for APU available", k.ApuAvailLamp, v => v > 0.5, 180), "AL_APU"),
            Done(Skip(Multi("AL_ANTIICE_OFF", "Engine and wing anti-ice: OFF",
                    k.EngAntiIce.Append(k.WingAntiIce).Select(a => (a, (int?)0)).ToArray()),
                s => s.GetValue(S.AntiIceOff) > 0.5), "AL_ANTIICE_OFF"))
    };

    // -----------------------------------------------------------------------
    private static Flow BuildShutdown(IniAirbusKeys k) => new()
    {
        Id = "SHUTDOWN", Name = "Shutdown",
        Description = "Parking brake and APU bleed, the engine masters off once the APU or ground power is on; then once every engine reads stopped: transponder, lights, signs, beacon, fuel pumps.",
        RelatedChecklistGroupIds = new[] { "SHUTDOWN" },
        Steps = Steps(
            OnGroundGuard("SD_ON_GROUND"),
            Skip(Captain("SD_PARKBRAKE_CAPT", "Parking brake: SET"), s => s.IsOn(IniAirbusKeys.ParkingBrake)),
            Done(Skip(WaitForField("SD_PARKBRAKE", "Parking brake: SET", IniAirbusKeys.ParkingBrake, v => v > 0.5, 20),
                s => s.IsOn(IniAirbusKeys.ParkingBrake)), "SD_PARKBRAKE"),
            Done(Skip(SW("SD_APUBLEED_ON", "APU bleed: ON", k.ApuBleed, 1),
                s => s.IsOn(k.ApuBleed)), "SD_APUBLEED_ON"),
            // the buses need the APU or external power before the engine generators go (stop otherwise)
            Skip(WaitForField("SD_POWER_SOURCE", "APU or external power available", S.ApuOrExtPower, v => v > 0.5, 120,
                    onTimeout: FlowStepFailurePolicy.Stop),
                s => s.GetValue(S.EnginesOff) > 0.5),
            // the masters: the First Officer's, on the ground (the guard above) with the parking brake read SET
            // (SD_PARKBRAKE) and the buses powered (SD_POWER_SOURCE stops the flow otherwise)
            Masters(k, n => Requires(Done(Skip(SW($"SD_ENG{n}_OFF", $"Engine {n} master: OFF", k.Master(n), 0),
                    s => s.IsPosition(k.Master(n), 0)), $"SD_ENG{n}_OFF"),
                "SD_PARKBRAKE", $"Parking brake not read set, so engine {n} master stays on.")),
            // nothing below runs until every engine reads stopped: the fuel pumps go off only then
            Done(WaitForField("SD_ENGINES_OFF", "Engines stopped", S.EnginesOff, v => v > 0.5, 120,
                onTimeout: FlowStepFailurePolicy.Stop), "SD_ENGINES_OFF"),
            Done(Skip(SW("SD_XPDR_STBY", "Transponder: STANDBY", k.XpdrMode, 0),
                s => s.IsPosition(k.XpdrMode, 0)), "SD_XPDR_STBY"),
            Done(Skip(SW("SD_TCAS_STBY", "TCAS: STANDBY", k.TcasMode, 0),
                s => s.IsPosition(k.TcasMode, 0)), "SD_TCAS_STBY"),
            Done(Skip(Multi("SD_LS_OFF", "LS captain and first officer: OFF", (k.CaptLs, 0), (k.FoLs, 0)),
                s => s.GetValue(S.LsOff) > 0.5), "SD_LS_OFF"),
            Done(Skip(SW("SD_SEATBELTS_OFF", "Seat belts: OFF", k.SeatBelts, k.SeatBeltsOff),
                s => s.GetValue(S.SeatBeltsOff) > 0.5), "SD_SEATBELTS_OFF"),
            Done(Skip(SW("SD_BEACON_OFF", "Beacon: OFF", k.Beacon, 0),
                s => s.IsPosition(k.Beacon, 0)), "SD_BEACON_OFF"),
            Done(Skip(SW("SD_WING_LT_OFF", "Wing lights: OFF", k.WingLights, 0),
                s => s.IsPosition(k.WingLights, 0)), "SD_WING_LT_OFF"),
            Done(Skip(Multi("SD_FUELPUMPS_OFF", "Fuel pumps: ALL OFF", k.FuelPumps.Select(p => (p.Key, (int?)p.Off)).ToArray()),
                s => s.GetValue(S.FuelPumpsOff) > 0.5), "SD_FUELPUMPS_OFF"),
            Done(Skip(SW("SD_NOSE_OFF", "Nose light: OFF", k.NoseLight, 2),
                s => s.IsPosition(k.NoseLight, 2)), "SD_NOSE_OFF"),
            Done(Skip(SW("SD_TURNOFF_OFF", "Runway turn-off lights: OFF", k.RunwayTurnoff, 0),
                s => s.IsPosition(k.RunwayTurnoff, 0)), "SD_TURNOFF_OFF"),
            Done(Skip(SW("SD_ANN", "Annunciator lights: BRIGHT", k.AnnLights, 1),
                s => s.IsPosition(k.AnnLights, 1)), "SD_ANN"),
            k.Dome is { } dome
                ? Done(Skip(SW("SD_DOME", "Dome light: BRIGHT", dome, 0), s => s.IsPosition(dome, 0)), "SD_DOME")
                : null,
            Done(SW("SD_ECAMDOOR", "ECAM page: door", IniAirbusActionExecutor.EcamPagePrefix + "DOOR", 1), "SD_ECAMDOOR"))
    };

    // -----------------------------------------------------------------------
    private static Flow BuildSecure(IniAirbusKeys k) => new()
    {
        Id = "SECURE", Name = "Securing",
        Description = "ADIRS for the Captain, oxygen, emergency lights, signs, APU off, wait for the APU flap, external power and batteries off.",
        RelatedChecklistGroupIds = new[] { "SECURE", "SECURING_CL" },
        Steps = Steps(
            OnGroundGuard("SC_ON_GROUND"),
            EnginesStoppedGuard("SC_ENGINES_STOPPED"),
            k.IrsWritable
                ? Done(Skip(Multi("SC_ADIRS", "IRS 1, 2 and 3: OFF", k.IrsModeReads.Select(i => (i, (int?)0)).ToArray()),
                    s => s.GetValue(S.IrsOff) > 0.5), "SC_ADIRS")
                : Skip(Captain("SC_ADIRS_CAPT", "Set IRS 1, 2 and 3 to OFF"), s => s.GetValue(S.IrsOff) > 0.5),
            !k.IrsWritable && k.IrsModeReads.Count > 0
                ? Done(Skip(WaitForField("SC_ADIRS_CHECK", "IRS 1, 2 and 3: OFF", S.IrsOff, v => v > 0.5, 20),
                    s => s.GetValue(S.IrsOff) > 0.5), "SC_ADIRS")
                : null,
            Also(Done(Skip(SW("SC_OXY", "Crew oxygen supply: OFF", k.CrewOxygen, 0),
                s => s.IsPosition(k.CrewOxygen, 0)), "SC_OXY"), "SCC_OXY"),
            Also(Done(Skip(SW("SC_EMEREXIT", "Emergency exit lights: OFF", k.EmerExit, 2),
                s => s.IsPosition(k.EmerExit, 2)), "SC_EMEREXIT"), "SCC_EMEREXIT"),
            Done(Skip(SW("SC_NOSMOKE", "No smoking: OFF", k.NoSmoking, 2),
                s => s.IsPosition(k.NoSmoking, 2)), "SC_NOSMOKE"),
            Done(Skip(SW("SC_APUBLEED", "APU bleed: OFF", k.ApuBleed, 0),
                s => s.IsPosition(k.ApuBleed, 0)), "SC_APUBLEED"),
            Done(Skip(SW("SC_APUMASTER", "APU master: OFF", k.ApuMaster, 0),
                s => s.IsPosition(k.ApuMaster, 0)), "SC_APUMASTER"),
            // FOA-10: the APU air intake flap closes on APU or external power, so it is waited for before
            // external power goes. Instant when the APU never ran (reads 0); unread, it times out and the
            // flow carries on.
            WaitForField("SC_APU_WAIT", "APU flap to close", IniAirbusKeys.ApuFlap, v => v < 0.05, 150),
            Done(Skip(Multi("SC_EXTPWR_OFF", "External power: OFF", k.ExtPower.Select(e => (e, (int?)0)).ToArray()),
                s => s.GetValue(S.ExtPowerOff) > 0.5), "SC_EXTPWR_OFF"),
            Also(Done(Skip(Multi("SC_BATS", "Batteries: OFF", k.Batteries.Select(b => (b, (int?)0)).ToArray()),
                s => s.GetValue(S.BatteriesOff) > 0.5), "SC_BATS"), "SCC_BAT"),
            k.Dome is { } dome
                ? Done(Skip(SW("SC_DOME", "Dome light: OFF", dome, 2), s => s.IsPosition(dome, 2)), "SC_DOME")
                : null,
            Done(Skip(SW("SC_STBYCOMPASS", "Standby compass light: OFF", k.StbyCompassLight, 0),
                s => s.IsPosition(k.StbyCompassLight, 0)), "SC_STBYCOMPASS"))
    };

    // -----------------------------------------------------------------------
    // Step builders
    // -----------------------------------------------------------------------

    // A step, a run of steps (one per engine) or null (a line this airframe does not have).
    private static List<Step> Steps(params object?[] steps) =>
        steps.SelectMany(o => o switch { Step st => new[] { st }, Step[] many => many, _ => Array.Empty<Step>() }).ToList();

    private static Step SW(string id, string label, string eventName, int target) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.SetSwitch,
        EventName = eventName,
        TargetValue = target,
        FailurePolicy = FlowStepFailurePolicy.Skip,
    };

    private static Step Multi(string id, string label, params (string EventName, int? TargetValue)[] actions) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.SetSwitchMultiple,
        MultiActions = actions.ToList(),
        FailurePolicy = FlowStepFailurePolicy.Skip,
    };

    private static Step Wait(string id, string label, int seconds) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.WaitSeconds,
        WaitSeconds = seconds,
    };

    private static Step WaitForField(string id, string label, string field, Func<double, bool> condition, int timeoutSec,
        FlowStepFailurePolicy onTimeout = FlowStepFailurePolicy.Skip) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.WaitForCondition,
        ConditionFieldName = field,
        Condition = condition,
        TimeoutSeconds = timeoutSec,
        FailurePolicy = onTimeout,
    };

    /// <summary>One step per engine, engine 1 first.</summary>
    private static Step[] Masters(IniAirbusKeys k, Func<int, Step> step) =>
        Enumerable.Range(1, k.EngineCount).Select(step).ToArray();

    private static Step Verify(Step step, string field, Func<double, bool> condition)
    {
        step.VerifyFieldName = field;
        step.VerifyCondition = condition;
        return step;
    }

    private static Step Stop(Step step)
    {
        step.FailurePolicy = FlowStepFailurePolicy.Stop;
        return step;
    }

    private static Step Requires(Step step, string stepId, string skipText)
    {
        step.RequiresStepId = stepId;
        step.RequiresStepSkipText = skipText;
        return step;
    }

    private static Step Captain(string id, string label) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.CaptainReminder,
        ReminderText = label,
    };

    private static Step Skip(Step step, Func<S, bool> cond)
    {
        step.SkipCondition = cond;
        return step;
    }

    private static Step Done(Step step, string checklistItemId)
    {
        step.CompletesChecklistItemId = checklistItemId;
        return step;
    }

    private static Step Also(Step step, params string[] alsoCompletes)
    {
        step.AlsoCompletesChecklistItemIds = alsoCompletes;
        return step;
    }

    private static Step LeaveAlone(Step step, Func<S, bool> when, string text)
    {
        step.LeaveAloneWhen = when;
        step.LeaveAloneText = text;
        return step;
    }

    /// <summary>Stops a flow unless every engine reads stopped (FO_ENGINES_OFF; unread fails the guard).</summary>
    private static Step EnginesStoppedGuard(string id) =>
        WaitForField(id, "Engines stopped", S.EnginesOff, v => v > 0.5, 5, onTimeout: FlowStepFailurePolicy.Stop);

    /// <summary>Stops a flow unless the aircraft reads on the ground (stock SIM ON GROUND; unread fails).</summary>
    private static Step OnGroundGuard(string id) =>
        WaitForField(id, "On the ground", "SIM_ON_GROUND", v => v > 0.5, 5, onTimeout: FlowStepFailurePolicy.Stop);
}
