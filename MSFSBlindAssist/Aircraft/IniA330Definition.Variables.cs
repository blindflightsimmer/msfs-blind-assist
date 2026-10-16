using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

public partial class IniA330Definition
{
    private readonly List<(string panel, string key)> _mapDisplayRows = new();
    private readonly Dictionary<string, A330ControlMap.Control> _controlsByKey = new();
    /// <summary>Continuous keys never spoken by the generic monitor (knob positions, caches).</summary>
    private readonly HashSet<string> _silentKeys = new();

    // ---------------- the definition's own controls ----------------

    /// <summary>FCU buttons: key -> what a press does (an L:var press flag, or an input event).</summary>
    internal static readonly (string key, string label, string? pulse, string? inputEvent)[] AutopilotButtons =
    {
        ("A330_AP1", "AP 1", "INI_AP1_BUTTON", null),
        ("A330_AP2", "AP 2", "INI_AP2_BUTTON", null),
        ("A330_ATHR", "A/THR", null, "AIRLINER_ATHR_PUSH"),
        ("A330_SPD_PULL", "Speed knob pull (selected speed)", "INI_FCU_SELECTED_SPEED_BUTTON", null),
        ("A330_SPD_PUSH", "Speed knob push (managed speed)", "INI_FCU_MANAGED_SPEED_BUTTON", null),
        ("A330_HDG_PULL", "Heading knob pull (selected heading)", "INI_FCU_SELECTED_HEADING_BUTTON", null),
        ("A330_HDG_PUSH", "Heading knob push (managed, NAV)", "INI_FCU_MANAGED_HEADING_BUTTON", null),
        ("A330_ALT_PULL", "Altitude knob pull (open climb or descent)", "INI_FCU_ALTITUDE_PULL_COMMAND", null),
        ("A330_ALT_PUSH", "Altitude knob push (managed climb or descent)", "INI_FCU_ALTITUDE_PUSH_COMMAND", null),
        ("A330_VS_PULL", "V/S knob pull (selected vertical speed)", null, "AIRLINER_MCU_VS_PULL"),
        ("A330_VS_PUSH", "V/S knob push (level off)", null, "AIRLINER_MCU_VS_PUSH"),
        ("A330_LOC", "LOC", "INI_LOCALIZER_BUTTON", null),
        ("A330_APPR", "APPR", "INI_APPROACH_BUTTON", null),
        ("A330_ALT_BTN", "ALT (hold the current altitude)", null, "AIRLINER_ALT_PUSH"),
    };

    /// <summary>The FCU buttons this airframe has (the A321 has EXPED and no ALT button).</summary>
    internal virtual IEnumerable<(string key, string label, string? pulse, string? inputEvent)> Buttons => AutopilotButtons;

    internal static readonly string[] AutopilotSelectKeys = { "A330_SPD_MACH", "A330_TRK_FPA", "A330_ALT_STEP", "A330_METRIC" };

    /// <summary>The FCU selectors this airframe can work from outside (the A321's 100/1000 selector cannot).</summary>
    internal virtual IReadOnlyCollection<string> SelectKeys => AutopilotSelectKeys;

    internal static IEnumerable<string> AutopilotControlKeys =>
        AutopilotButtons.Select(b => b.key).Concat(AutopilotSelectKeys);

    internal static readonly string[] AutopilotDisplayKeys =
        { "A330_AP1_ON", "A330_AP2_ON", "A330_ATHR_ON", "A330_FCU_SPD", "A330_FCU_HDG", "A330_FCU_ALT", "A330_FCU_VS" };

    internal static readonly string[] FmaDisplayKeys =
        { "A330_FMA_THRUST", "A330_FMA_VERT", "A330_FMA_LAT", "A330_FMA_ARMED", "A330_FMA_CAP", "A330_FMA_AP" };

    internal static readonly (string section, string panel, string key)[] OwnPlacedControls =
    {
        ("Pedestal", "Flaps and Speedbrake", "A330_FLAPS"), ("Pedestal", "Flaps and Speedbrake", "A330_SPDBRK"),
        ("Pedestal", "Flaps and Speedbrake", "A330_SPLR_ARM"), ("Main panel", "Landing Gear", "A330_GEAR"),
        ("Main panel", "Brakes", "A330_PARKBRK"), ("Main panel", "Brakes", "A330_AUTOBRAKE"),
    };

    internal static readonly (string panel, string key)[] OwnPlacedDisplays =
    {
        ("Flaps and Speedbrake", "A330_SLAT_ANGLE"), ("Flaps and Speedbrake", "A330_FLAP_ANGLE"),
        ("Flaps and Speedbrake", "A330_TRIM"), ("Landing Gear", "A330_GEAR_POS"),
    };

    internal const string FmaMonitorKey = "A330_M_PITCH";

    // FMA inputs: silent caches composed in ProcessSimVarUpdate
    internal static readonly (string key, string lvar)[] FmaInputVars =
    {
        ("A330_M_AT", "INI_AT_MODE"), ("A330_M_PITCH", "INI_PITCH_MODE"), ("A330_M_PITCH_ARM", "INI_PITCH_MODE_ARM"),
        ("A330_M_PITCH_ARM2", "INI_PITCH_MODE_ARM2"), ("A330_M_ROLL", "INI_ROLL_MODE"), ("A330_M_ROLL_ARM", "INI_ROLL_MODE_ARM"),
        ("A330_M_APPR", "INI_PITCH_ROLL_MODE"), ("A330_M_MACH", "INI_AIRSPEED_IS_MACH"), ("A330_M_TRKFPA", "INI_TRACK_FPA_STATE"),
        ("A330_M_TOGA_LOCK", "INI_TOGA_LOCK_ACTIVE"), ("A330_M_THR_LATCH", "INI_FORCE_THRUST_LATCH"),
        ("A330_M_CAP", "INI_AUTOPILOT_CAPABILITY"), ("A330_M_SPD_MANAGED", "INI_SPEED_MANAGED"),
    };

    /// <summary>The fuel tanks the module publishes in kilograms (centre comes from the sim, see FuelSummary).</summary>
    internal static readonly (string key, string lvar, string name)[] FuelVars =
    {
        ("A330_FUEL_LO", "INI_FUEL_WEIGHT_LEFT_OUTER", "left outer"), ("A330_FUEL_LI", "INI_FUEL_WEIGHT_LEFT_INNER", "left inner"),
        ("A330_FUEL_RI", "INI_FUEL_WEIGHT_RIGHT_INNER", "right inner"), ("A330_FUEL_RO", "INI_FUEL_WEIGHT_RIGHT_OUTER", "right outer"),
        ("A330_FUEL_TRIM", "INI_FUEL_WEIGHT_TRIM", "trim tank"),
    };

    /// <summary>The A330's flap lever (FS flight model: 4 handle positions after UP; 1+F is automatic).</summary>
    internal static readonly string[] FlapDetents = { "Flaps up", "Flaps 1", "Flaps 2", "Flaps 3", "Flaps full" };

    internal virtual string[] Detents => FlapDetents;

    /// <summary>The fuel tanks this airframe publishes in kilograms.</summary>
    internal virtual (string key, string lvar, string name)[] Tanks => FuelVars;

    /// <summary>True when the centre tank is read from the sim in gallons (the A330); false when it is one of
    /// <see cref="Tanks"/> and anything left over in the sim's total is additional centre tanks (the A321LR).</summary>
    internal virtual bool CentreTankFromSim => true;

    /// <summary>Green dot, S and F speed L:vars.</summary>
    internal virtual (string greenDot, string s, string f) SpeedLvars => ("INI_GREENDOT_SPEED", "INI_S_SPEED", "INI_F_SPEED");

    internal const string TakeoffIasKey = "A330_TO_IAS", TakeoffV1Key = "A330_TO_V1", TakeoffVrKey = "A330_TO_VR", TakeoffV2Key = "A330_TO_V2";

    protected override Dictionary<string, SimVarDefinition> BuildVariables()
    {
        var vars = new Dictionary<string, SimVarDefinition>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string key, SimVarDefinition d)
        {
            vars[key] = d;
            if (d.UpdateFrequency == UpdateFrequency.Continuous) usedNames.Add(d.Type + ":" + d.Name);
        }

        void Button(string key, string display) => Add(key, new SimVarDefinition
        {
            Name = "MSFSBA_" + key, DisplayName = display, Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest,
            RenderAsButton = true, SuppressRestingButtonState = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Idle", [1] = "Press" },
        });

        void Watched(string key, string name, string display, Dictionary<double, string>? values = null, bool silent = false,
                     string units = "number", SimVarType type = SimVarType.LVar) => Add(key, new SimVarDefinition
        {
            Name = name, DisplayName = display, Type = type, Units = units,
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = values ?? new Dictionary<double, string>(),
            ExcludeFromMonitorManager = silent,
        });

        void Silent(string key, string name, string display, string units = "number", SimVarType type = SimVarType.LVar)
        {
            Watched(key, name, display, null, true, units, type);
            _silentKeys.Add(key);
        }

        void Synthetic(string key, string display) => Add(key, new SimVarDefinition
        {
            Name = $"MSFSBA_{key}", DisplayName = display, Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest,
        });

        void OnRequest(string key, string name, string display, string units = "number", SimVarType type = SimVarType.LVar)
            => Add(key, new SimVarDefinition { Name = name, DisplayName = display, Type = type, Units = units, UpdateFrequency = UpdateFrequency.OnRequest });

        var engaged = new Dictionary<double, string> { [0] = "Off", [1] = "Engaged" };

        // ---------------- Autopilot (FCU) ----------------
        foreach (var (key, label, _, _) in Buttons) Button(key, label);
        Watched("A330_SPD_MACH", "INI_AIRSPEED_IS_MACH", "Speed window units", new() { [0] = "Knots", [1] = "Mach" });
        Watched("A330_TRK_FPA", "INI_TRACK_FPA_STATE", "HDG-V/S or TRK-FPA", new() { [0] = "HDG V/S", [1] = "TRK FPA" });
        // ALT knob step (measured: input 0 reads 1 and the knob steps 100 feet)
        Watched("A330_ALT_STEP", "INI_ALTITUDE_STATE", "Altitude knob steps", new() { [1] = "100 feet", [0] = "1000 feet" });
        Watched("A330_METRIC", "INI_FCU_METRIC_STATE", "Metric altitude", new() { [0] = "Off", [1] = "On" });
        Watched("A330_AP1_ON", "INI_AP1_ON", "Autopilot 1", engaged);
        Watched("A330_AP2_ON", "INI_AP2_ON", "Autopilot 2", engaged);
        Watched("A330_ATHR_ON", "INI_AT_ON", "Autothrust", engaged);
        Silent("A330_FCU_SPD", "INI_AIRSPEED_DIAL", "FCU speed");
        Silent("A330_FCU_HDG", "INI_HEADING_DIAL", "FCU heading");
        Silent("A330_FCU_TRK", "INI_TRK_DIAL", "FCU track");
        Silent("A330_FCU_ALT", "INI_ALTITUDE_DIAL", "FCU altitude");
        Silent("A330_FCU_VS", "INI_VVI_DIAL", "FCU vertical speed");
        Silent("A330_FCU_FPA", "INI_FPA_DIAL", "FCU flight path angle");
        Silent("A330_ALTITUDE", "INDICATED ALTITUDE", "Altitude", "feet", SimVarType.SimVar);

        // ---------------- Flight modes ----------------
        foreach (var (key, lvar) in FmaInputVars) Silent(key, lvar, key);
        vars[FmaMonitorKey].ExcludeFromMonitorManager = false;   // one Ctrl+M row mutes every flight-mode change
        vars[FmaMonitorKey].DisplayName = "Flight mode changes";
        Synthetic("A330_FMA_THRUST", "Thrust mode");
        Synthetic("A330_FMA_VERT", "Vertical mode");
        Synthetic("A330_FMA_LAT", "Lateral mode");
        Synthetic("A330_FMA_ARMED", "Armed modes");
        Synthetic("A330_FMA_CAP", "Landing capability");
        Synthetic("A330_FMA_AP", "Autopilot and autothrust");

        // ---------------- Flight controls (the definition's own) ----------------
        Add("A330_FLAPS", new SimVarDefinition
        {
            Name = "FLAPS HANDLE INDEX", DisplayName = "Flaps", Type = SimVarType.SimVar, Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = Enumerable.Range(0, Detents.Length).ToDictionary(i => (double)i, i => Detents[i]),
        });
        _silentKeys.Add("A330_FLAPS");   // detent call-outs come from ProcessSimVarUpdate
        Add("A330_SPDBRK", new SimVarDefinition
        {
            Name = "SPOILERS HANDLE POSITION", DisplayName = "Speedbrake lever", Type = SimVarType.SimVar, Units = "percent over 100",
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Retracted", [0.5] = "Half", [1] = "Full" },
            ValueToDescriptionKey = v => v < 0.25 ? 0 : v < 0.75 ? 0.5 : 1,
        });
        _silentKeys.Add("A330_SPDBRK");
        Watched("A330_SPLR_ARM", "SPOILERS ARMED", "Ground spoilers", new() { [0] = "Disarmed", [1] = "Armed" }, units: "Bool", type: SimVarType.SimVar);
        Watched("A330_GEAR", "INI_GEAR_HANDLE_STATUS", "Landing gear lever", new() { [0] = "Up", [1] = "Down" });
        Watched("A330_PARKBRK", "INI_PARKING_BRAKE_STATUS", "Parking brake", new() { [0] = "Off", [1] = "Set" });
        Watched("A330_AUTOBRAKE", "INI_AUTOBRAKE_LEVEL", "Autobrake", new() { [0] = "Off", [1] = "Low", [2] = "Medium", [3] = "Max" });
        Silent("A330_FLAP_ANGLE", "TRAILING EDGE FLAPS LEFT ANGLE", "Flap angle", "degrees", SimVarType.SimVar);
        Silent("A330_SLAT_ANGLE", "LEADING EDGE FLAPS LEFT ANGLE", "Slat angle", "degrees", SimVarType.SimVar);
        Silent("A330_GEAR_POS", "GEAR CENTER POSITION", "Nose gear", "percent", SimVarType.SimVar);
        Silent("A330_TRIM", "ELEVATOR TRIM POSITION", "Stabilizer trim", "degrees", SimVarType.SimVar);

        // ---------------- Fuel caches ----------------
        foreach (var (key, lvar, name) in Tanks) Silent(key, lvar, name);
        Silent("A330_FUEL_CENTER", "FUEL TANK CENTER QUANTITY", "Centre tank", "gallons", SimVarType.SimVar);
        Silent("A330_FUEL_TOTAL_LB", "FUEL TOTAL QUANTITY WEIGHT", "Total fuel", "pounds", SimVarType.SimVar);
        Silent("A330_FUEL_WPG", "FUEL WEIGHT PER GALLON", "Fuel weight per gallon", "pounds", SimVarType.SimVar);
        Silent("A330_IS_METRIC", "INI_IS_METRIC", "Weight units");

        // ---------------- Generated cockpit controls ----------------
        foreach (var c in _map.Controls)
        {
            if (string.IsNullOrEmpty(c.Key) || vars.ContainsKey(c.Key)) continue;
            _controlsByKey[c.Key] = c;
            var read = c.Read is { Length: > 0 } ? A330ControlMap.ParseReadKind(c.Read) : ((A330ControlMap.ReadKind, string, string)?)null;
            var positions = c.Positions?.GroupBy(p => p.Value).ToDictionary(g => g.Key, g => g.First().Label) ?? new Dictionary<double, string>();
            SimVarType TypeOf(A330ControlMap.ReadKind k) => k switch
            {
                A330ControlMap.ReadKind.SimVar => SimVarType.SimVar,
                A330ControlMap.ReadKind.InputEvent => SimVarType.InputEvent,
                _ => SimVarType.LVar,
            };
            switch (c.Kind)
            {
                case "select" when read is { } r && positions.Count >= 2:
                    Add(c.Key, new SimVarDefinition
                    {
                        Name = r.Item2, DisplayName = c.Label, Type = TypeOf(r.Item1), Units = r.Item3,
                        UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true, ValueDescriptions = positions,
                        // a guard is operated on the way to its switch: never narrate it
                        ExcludeFromMonitorManager = c.Label.EndsWith(" guard", StringComparison.Ordinal),
                    });
                    if (c.Label.EndsWith(" guard", StringComparison.Ordinal)) _silentKeys.Add(c.Key);
                    break;
                case "button":
                    Button(c.Key, c.Label);
                    break;
                case "continuous" when read is { } rc:
                    Add(c.Key, new SimVarDefinition
                    {
                        Name = rc.Item2, DisplayName = c.Label, Type = TypeOf(rc.Item1), Units = rc.Item3,
                        UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true, ExcludeFromMonitorManager = true,
                        RenderAsSlider = true, SliderMin = c.Min ?? 0, SliderMax = c.Max ?? 100,
                    });
                    _silentKeys.Add(c.Key);
                    break;
                case "status" when read is { } rs:
                    if (usedNames.Contains(TypeOf(rs.Item1) + ":" + rs.Item2)) break;
                    Add(c.Key, new SimVarDefinition
                    {
                        Name = rs.Item2, DisplayName = c.Label, Type = TypeOf(rs.Item1), Units = rs.Item3,
                        UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = positions.Count > 0, ValueDescriptions = positions,
                        ExcludeFromMonitorManager = positions.Count == 0,
                    });
                    if (positions.Count == 0) _silentKeys.Add(c.Key);
                    _mapDisplayRows.Add((c.Panel, c.Key));
                    break;
            }
            if (c.Lamps == null) continue;
            int n = 0;
            foreach (var lamp in c.Lamps)
            {
                n++;
                if (string.IsNullOrWhiteSpace(lamp.Read)) continue;
                var lr = A330ControlMap.ParseReadKind(lamp.Read);
                if (usedNames.Contains(TypeOf(lr.kind) + ":" + lr.name)) continue;
                string lk = $"{c.Key}_LAMP{n}";
                string legend = string.IsNullOrWhiteSpace(lamp.Legend) ? "light" : $"{lamp.Legend} light";
                Add(lk, new SimVarDefinition
                {
                    Name = lr.name, DisplayName = $"{c.Label} {legend}", Type = TypeOf(lr.kind), Units = lr.units,
                    UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
                    ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Lit" },
                    ValueToDescriptionKey = v => v > 0.5 ? 1 : 0,
                });
                _mapDisplayRows.Add((c.Panel, lk));
            }
        }

        // ---------------- Read-outs (fresh on the key press) ----------------
        foreach (var (key, lvar, display) in new[]
                 {
                     ("A330_DIST_DEST", "INI_FMC_DIST_TO_DEST", "Distance to destination"),
                     ("A330_APPR_DIST", "INI_TOTAL_APPR_DIST", "Distance along the route to the destination"),
                     ("A330_TD_DIST_TOTAL", "INI_TD_DIST_TOTAL", "Distance to top of descent"),
                     ("A330_GREEN_DOT", SpeedLvars.greenDot, "Green dot speed"), ("A330_S_SPEED", SpeedLvars.s, "S speed"),
                     ("A330_F_SPEED", SpeedLvars.f, "F speed"), ("A330_VLS", "INI_VLS_SPEED", "VLS"),
                     ("A330_VMAX", "INI_MAX_SPEED", "Maximum speed"), ("A330_VAPP", "INI_VAPP_SPEED", "Approach speed"),
                     ("A330_MANAGED_SPEED", "INI_MANAGED_SPEED", "Managed speed target"),
                     ("A330_MINIMUMS", "INI_MINIMUMS_PILOT", "Captain minimums"), ("A330_MDA", "INI_FMS_MDA", "FMS MDA"),
                     ("A330_ILS_FREQ", "INI_ILS_FREQUENCY", "ILS frequency"),
                 })
            OnRequest(key, lvar, display);
        OnRequest("A330_GS_FRESH", "GROUND VELOCITY", "Ground speed", "knots", SimVarType.SimVar);
        OnRequest("A330_BARO_FRESH", "KOHLSMAN SETTING MB:1", "Captain altimeter setting", "millibars", SimVarType.SimVar);
        OnRequest("A330_BARO_STD_FRESH", "KOHLSMAN SETTING STD:1", "Captain altimeter standard", "Bool", SimVarType.SimVar);
        foreach (var (key, simvar, units) in new[]
                 {
                     ("A330_NAV1_FREQ", "NAV ACTIVE FREQUENCY:1", "MHz"), ("A330_NAV2_FREQ", "NAV ACTIVE FREQUENCY:2", "MHz"),
                     ("A330_NAV3_FREQ", "NAV ACTIVE FREQUENCY:3", "MHz"), ("A330_NAV3_LOC", "NAV HAS LOCALIZER:3", "Bool"),
                     ("A330_NAV3_GS", "NAV HAS GLIDE SLOPE:3", "Bool"), ("A330_NAV3_DME", "NAV DME:3", "nautical miles"),
                     ("A330_COM1_ACT", "COM ACTIVE FREQUENCY:1", "MHz"), ("A330_COM1_STBY", "COM STANDBY FREQUENCY:1", "MHz"),
                 })
            OnRequest(key, simvar, key, units, SimVarType.SimVar);

        // ---------------- Take-off call-outs ----------------
        Watched(TakeoffV1Key, "INI_V1", "V1 call-out");
        Watched(TakeoffVrKey, "INI_VR", "Rotate call-out");
        Watched(TakeoffV2Key, "INI_V2", "V2 call-out");
        Add(TakeoffIasKey, new SimVarDefinition
        {
            Name = "AIRSPEED INDICATED", DisplayName = "Take-off call-out airspeed", Type = SimVarType.SimVar, Units = "knots",
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
            ExcludeFromBatch = true, HighFrequency = true, ExcludeFromMonitorManager = true,
        });

        // End-to-end MobiFlight probe target: the momentary keys, the FCU press flags and the MCDU keys
        // are calculator writes, so they gate on the probe's verdict; registering this opts in.
        vars["MSFSBA_BRIDGE_PROBE"] = new SimVarDefinition
        {
            Name = "MSFSBA_BRIDGE_PROBE", DisplayName = "Bridge Probe", Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest,
        };

        foreach (var kv in GetBaseVariables())
            if (!vars.ContainsKey(kv.Key)) vars[kv.Key] = kv.Value;
        return vars;
    }
}
