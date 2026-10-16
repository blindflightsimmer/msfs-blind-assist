using System.Globalization;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

public partial class IniA330Definition
{
    private static string Num(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>A momentary key's press: its own _Push and _Release actions. The release waits so the
    /// aircraft sees the press on a frame of its own.</summary>
    internal const int DefaultPushMs = 150;

    protected bool CanCalc(SimConnectManager sim, ScreenReaderAnnouncer announcer, string what)
    {
        if (sim.CalcWriteCanLand) return true;
        announcer.AnnounceImmediate($"{what} unavailable: the MobiFlight module is not responding.");
        return false;
    }

    private void PushEvent(SimConnectManager sim, string inputEvent, int holdMs = DefaultPushMs)
    {
        Write(sim, $"(>B:{inputEvent}_Push)");
        _ = ReleaseLater();
        // awaited on the caller's context (the UI thread), so the release is sent from there too
        async Task ReleaseLater() { await Task.Delay(holdMs); Write(sim, $"(>B:{inputEvent}_Release)"); }
    }

    /// <summary>A press done by value (set 1, then back to 0): the FCU events that answer a value
    /// and not their _Push action (measured: V/S pull, ALT, the autobrake levels).</summary>
    private static void PressByValue(SimConnectManager sim, string inputEvent)
    {
        if (!sim.TrySetInputEvent(inputEvent, 1)) return;
        _ = ReleaseLater();
        // the input-event hashes and the SimConnect handle belong to the UI thread: release from there
        async Task ReleaseLater() { await Task.Delay(DefaultPushMs); sim.TrySetInputEvent(inputEvent, 0); }
    }

    protected bool SetInputEvent(SimConnectManager sim, ScreenReaderAnnouncer announcer, string what, string inputEvent, double value)
    {
        if (sim.TrySetInputEvent(inputEvent, value)) return true;
        announcer.AnnounceImmediate($"{what} unavailable: this aircraft does not have that control.");
        return false;
    }

    /// <summary><see cref="SetInputEvent"/> for one of the A330's own event names, translated by <see cref="Ev"/>.</summary>
    protected virtual bool SetOwnEvent(SimConnectManager sim, ScreenReaderAnnouncer announcer, string what, string a330Event, double value)
    {
        if (Ev(a330Event) is { } ev && IsMomentary(a330Event))
        {
            // a momentary key toggles: one press flips it (callers only ask when the state should change)
            if (CanCalc(sim, announcer, what)) PushEvent(sim, ev);
            return true;
        }
        if (Ev(a330Event) is { } ev2) return SetInputEvent(sim, announcer, what, ev2, value);
        announcer.AnnounceImmediate($"{what} unavailable: this aircraft does not have that control.");
        return false;
    }

    // =====================================================================
    // Panel writes
    // =====================================================================

    public override bool HandleUIVariableSet(string varKey, double value, SimVarDefinition varDef,
        SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        double? cur = simConnect.GetCachedVariableValue(varKey);
        var button = Buttons.FirstOrDefault(b => b.key == varKey);
        if (button.key != null)
        {
            PressAutopilot(simConnect, announcer, varKey);
            return true;
        }
        switch (varKey)
        {
            case "A330_SPD_MACH" or "A330_METRIC" or "A330_ALT_STEP" when cur is double same && Math.Abs(same - value) < 0.5:
                return true;
            case "A330_SPD_MACH":
                SetOwnEvent(simConnect, announcer, "Speed/Mach", "AIRLINER_SPDMACH", value > 0.5 ? 1 : 0);
                return true;
            case "A330_METRIC":
                SetOwnEvent(simConnect, announcer, "Metric altitude", "AIRLINER_METRIC", value > 0.5 ? 1 : 0);
                return true;
            case "A330_ALT_STEP":
                SetOwnEvent(simConnect, announcer, "Altitude knob steps", "AIRLINER_MCU_ALTSELECT", AltStepInput(value > 0.5));
                return true;
            case "A330_TRK_FPA":
                // the HDG-V/S/TRK-FPA key toggles: press only when the pick differs (unknown counts as differing)
                if ((cur is null || Math.Abs(cur.Value - value) > 0.5) && CanCalc(simConnect, announcer, "TRK-FPA"))
                    Pulse(simConnect, "INI_FCU_HDG_VS_COMMAND");
                return true;
            case "A330_FLAPS":
                if (CanCalc(simConnect, announcer, "Flaps"))
                    Write(simConnect, $"{Num(Math.Round(Math.Clamp(Math.Round(value), 0, Detents.Length - 1) * 16383.0 / (Detents.Length - 1)))} (>K:FLAPS_SET)");
                return true;
            case "A330_SPDBRK":
                if (CanCalc(simConnect, announcer, "Speedbrake"))
                    Write(simConnect, $"{Num(Math.Round(Math.Clamp(value, 0, 1) * 16383))} (>K:SPOILERS_SET)");
                return true;
            case "A330_SPLR_ARM":
                if (CanCalc(simConnect, announcer, "Ground spoilers"))
                    Write(simConnect, value > 0.5 ? "(>K:SPOILERS_ARM_ON)" : "(>K:SPOILERS_ARM_OFF)");
                return true;
            case "A330_GEAR":
                if (CanCalc(simConnect, announcer, "Gear"))
                    Write(simConnect, value > 0.5 ? "(>K:GEAR_DOWN)" : "(>K:GEAR_UP)");
                return true;
            case "A330_PARKBRK":
                SetOwnEvent(simConnect, announcer, "Parking brake", "AIRLINER_PARKINGBRAKE", ParkingBrakeInput(value > 0.5));
                return true;
            case "A330_AUTOBRAKE":
                if (!AutobrakeWritesCalc || CanCalc(simConnect, announcer, "Autobrake"))
                    SetAutobrake(simConnect, (int)Math.Round(value), cur);
                return true;
        }

        if (!_controlsByKey.TryGetValue(varKey, out var c)) return false;
        // an "rpn" control writes L:vars (the A321's whole map); every other mode drives an input event
        if (c.Mode != "rpn" && string.IsNullOrEmpty(c.Event) && string.IsNullOrEmpty(c.Rpn)) return false;
        switch (c.Mode)
        {
            case "toggle":
                // a momentary push-button that reports its state: press once, only when the pick differs
                // (an unknown state counts as differing, so a cold cache still actuates). The press is the
                // control's own rpn (an L:var key, the A321) or its input event's _Push (the A330).
                if ((cur is null || Math.Abs(cur.Value - value) > 0.5) && CanCalc(simConnect, announcer, c.Label))
                {
                    if (!string.IsNullOrWhiteSpace(c.Rpn)) Write(simConnect, c.Rpn);
                    else PushEvent(simConnect, c.Event!, c.HoldMs ?? DefaultPushMs);
                }
                return true;
            case "push" when string.IsNullOrEmpty(c.Event):
                return false;
            case "push":
                if (CanCalc(simConnect, announcer, c.Label)) PushEvent(simConnect, c.Event, c.HoldMs ?? DefaultPushMs);
                return true;
            case "rpn":
                if (!string.IsNullOrWhiteSpace(c.Rpn) && CanCalc(simConnect, announcer, c.Label))
                    Write(simConnect, c.Rpn.Replace("{value}", Num(value)));
                return true;
            default:
                if (c.Event is not { Length: > 0 } ev) return false;
                double input = c.Kind == "continuous" ? value : A330ControlMap.InputFor(c, value) ?? value;
                SetInputEvent(simConnect, announcer, c.Label, ev, input);
                return true;
        }
    }

    internal static string? AutobrakeEvent(int level) => level switch
    {
        1 => "AIRLINER_AUTOBRK_LO", 2 => "AIRLINER_AUTOBRK_MED", 3 => "AIRLINER_AUTOBRK_HI", _ => null,
    };

    /// <summary>Whether SetAutobrake writes through the calculator path (then a dead path is refused
    /// out loud first). The A330 presses its own input events by value, which need no MobiFlight.</summary>
    protected virtual bool AutobrakeWritesCalc => false;

    protected virtual void SetAutobrake(SimConnectManager sim, int level, double? current)
    {
        // measured: pressing a level button arms it; Off = press the ACTIVE level again (the A300's rule)
        if (AutobrakeEvent(level) is { } a) { PressOwn(sim, a); return; }
        if (current is double c && AutobrakeEvent((int)Math.Round(c)) is { } b) PressOwn(sim, b);
    }

    /// <summary>Press one of the A330's own events the way this airframe takes it: its _Push action
    /// (momentary keys) or a value press.</summary>
    private void PressOwn(SimConnectManager sim, string a330Event)
    {
        if (Ev(a330Event) is not { } ev) return;
        if (IsMomentary(a330Event) || PressesWithPushAction(a330Event)) PushEvent(sim, ev);
        else PressByValue(sim, ev);
    }

    /// <summary>True for an event that is a momentary KEY on this airframe (its value does not hold, so a
    /// press is its _Push action). The A330's FCU selectors are latching; the A321's are keys.</summary>
    internal virtual bool IsMomentary(string a330Event) => false;

    /// <summary>The parking brake lever's input for set / released. A330: 0 = set (measured: input 0 with
    /// the brake set, INI status 1).</summary>
    internal virtual double ParkingBrakeInput(bool set) => set ? 0 : 1;

    /// <summary>The ALT knob step selector's input for 100-foot / 1000-foot steps. A330: input 0 = 100 ft
    /// (measured: input 0 reads 1 and the knob steps 100 feet).</summary>
    internal virtual double AltStepInput(bool hundredFeet) => hundredFeet ? 0 : 1;

    /// <summary>True for an FCU event pressed with its own _Push/_Release actions rather than by value
    /// (A330 measured: the V/S knob push).</summary>
    internal virtual bool PressesWithPushAction(string a330Event) => a330Event == "AIRLINER_MCU_VS_PUSH";

    private void PressAutopilot(SimConnectManager sim, ScreenReaderAnnouncer announcer, string key)
    {
        var b = Buttons.FirstOrDefault(x => x.key == key);
        if (b.key == null) return;   // this airframe has no such button
        if (b.pulse != null)
        {
            if (CanCalc(sim, announcer, b.label)) Pulse(sim, b.pulse);
        }
        else if (b.inputEvent != null && Ev(b.inputEvent) is { } ev)
        {
            if (PressesWithPushAction(b.inputEvent) || IsMomentary(b.inputEvent)) { if (CanCalc(sim, announcer, b.label)) PushEvent(sim, ev); }
            else PressByValue(sim, ev);
        }
    }

    // =====================================================================
    // Announcements
    // =====================================================================

    private int _lastFlapIndex = -1;
    private A330FmaText? _fmaAnnounced;
    private System.Windows.Forms.Timer? _fmaTimer;
    private ScreenReaderAnnouncer? _fmaAnnouncer;
    private readonly Dictionary<string, double> _fmaValues = new();
    private readonly TakeoffVSpeedCallouts _takeoffCallouts = new();
    private bool _calloutOnGround = true;

    public override void ResetAnnouncementBaselines()
    {
        _lastFlapIndex = -1;
        _fmaAnnounced = null;
        _takeoffCallouts.Reset();
    }

    public override void OnSimContextReset()
    {
        _lastFlapIndex = -1;
        _fmaAnnounced = null;
        _fmaValues.Clear();
        _takeoffCallouts.Reset();
    }

    internal static string TakeoffMuteKey(string callout) => callout switch
    {
        "V1" => TakeoffV1Key, "Rotate" => TakeoffVrKey, "V2" => TakeoffV2Key, _ => "",
    };

    public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        switch (varName)
        {
            case TakeoffIasKey:
                var crossed = _takeoffCallouts.ProcessSample(value, _calloutOnGround);
                if (crossed.Count > 0 && !announcer.Suppressed
                    && TakeoffVSpeedCallouts.Compose(crossed, c => TakeoffMuteKey(c) is { Length: > 0 } k && DisabledMonitorSet.Contains(k)) is string sentence)
                    announcer.AnnounceImmediate(sentence);
                return true;
            case "SIM_ON_GROUND":
                _calloutOnGround = value >= 0.5;
                break;
            case TakeoffV1Key: _takeoffCallouts.SetV1(value); return true;
            case TakeoffVrKey: _takeoffCallouts.SetVR(value); return true;
            case TakeoffV2Key: _takeoffCallouts.SetV2(value); return true;
            case "A330_FLAPS":
            {
                int idx = (int)Math.Round(value);
                if (_lastFlapIndex < 0 || idx == _lastFlapIndex) { _lastFlapIndex = idx; return true; }
                _lastFlapIndex = idx;
                if (idx >= 0 && idx < Detents.Length) announcer.Announce(Detents[idx]);
                return true;
            }
        }
        if (FmaInputVars.Any(f => f.key == varName))
        {
            _fmaValues[varName] = value;
            ScheduleFma(announcer);
            return true;
        }
        if (_silentKeys.Contains(varName)) return true;
        return base.ProcessSimVarUpdate(varName, value, announcer);
    }

    // After a context reset (_fmaValues cleared) a flight load re-delivers only CHANGED vars, so an
    // unchanged one falls back to the cache rather than reading 0 until it next moves.
    private int Fv(string key) => _fmaValues.TryGetValue(key, out var v) ? (int)Math.Round(v)
        : (int)Math.Round(_sim?.GetCachedVariableValue(key) ?? 0);

    private SimConnectManager? _sim;

    internal A330FmaText CurrentFma()
    {
        double fcuAlt = _sim?.GetCachedVariableValue("A330_FCU_ALT") ?? 0;
        double alt = _sim?.GetCachedVariableValue("A330_ALTITUDE") ?? 0;
        return A330Fma.Compose(new A330FmaInputs(
            Fv("A330_M_AT"), Fv("A330_M_PITCH"), Fv("A330_M_PITCH_ARM"), Fv("A330_M_PITCH_ARM2"),
            Fv("A330_M_ROLL"), Fv("A330_M_ROLL_ARM"), Fv("A330_M_APPR"),
            Fv("A330_M_MACH") != 0, Fv("A330_M_TRKFPA") != 0, Fv("A330_M_TOGA_LOCK") != 0 || Fv("A330_M_THR_LATCH") != 0,
            Fv("A330_M_CAP"), fcuAlt > alt));
    }

    /// <summary>Capture modes flip within a frame or two; settle 300 ms so one change is spoken once.</summary>
    private void ScheduleFma(ScreenReaderAnnouncer announcer)
    {
        _fmaAnnouncer = announcer;
        if (_fmaTimer == null)
        {
            _fmaTimer = new System.Windows.Forms.Timer { Interval = 300 };
            _fmaTimer.Tick += (_, _) => { _fmaTimer!.Stop(); AnnounceFmaChange(); };
        }
        _fmaTimer.Stop();
        _fmaTimer.Start();
    }

    private void AnnounceFmaChange()
    {
        var now = CurrentFma();
        var prev = _fmaAnnounced;
        _fmaAnnounced = now;
        Log.Channel("fma").Info($"{ShortName} FMA: {now.Summary()} [at={Fv("A330_M_AT")} pitch={Fv("A330_M_PITCH")}/{Fv("A330_M_PITCH_ARM")}/{Fv("A330_M_PITCH_ARM2")} roll={Fv("A330_M_ROLL")}/{Fv("A330_M_ROLL_ARM")} appr={Fv("A330_M_APPR")}]");
        if (prev == null || _fmaAnnouncer == null) return;   // baseline-first
        var parts = new List<string>();
        if (now.Thrust != prev.Thrust && now.Thrust.Length > 0) parts.Add(now.Thrust);
        if (now.Vertical != prev.Vertical && now.Vertical.Length > 0) parts.Add(now.Vertical);
        if (now.Lateral != prev.Lateral && now.Lateral.Length > 0) parts.Add(now.Lateral);
        if (now.Armed != prev.Armed && now.Armed.Length > 0) parts.Add("armed " + now.Armed);
        if (now.Capability != prev.Capability && now.Capability.Length > 0) parts.Add(now.Capability);
        if (parts.Count > 0 && !DisabledMonitorSet.Contains(FmaMonitorKey)) _fmaAnnouncer.Announce(string.Join(", ", parts));
    }

    private bool SpeedManaged() => (_sim?.GetCachedVariableValue("A330_M_SPD_MANAGED") ?? 0) > 0.5;

    public override bool TryGetDisplayOverride(string varKey, double value, out string displayText)
    {
        var f = CurrentFma();
        bool fpa = Fv("A330_M_TRKFPA") == 1;
        switch (varKey)
        {
            case "A330_FMA_THRUST": displayText = Or(f.Thrust); return true;
            case "A330_FMA_VERT": displayText = Or(f.Vertical); return true;
            case "A330_FMA_LAT": displayText = Or(f.Lateral); return true;
            case "A330_FMA_ARMED": displayText = Or(f.Armed); return true;
            case "A330_FMA_CAP": displayText = Or(f.Capability); return true;
            case "A330_FMA_AP": displayText = ApStatus(); return true;
            case "A330_FCU_SPD":
                displayText = SpeedManaged() ? "Managed" : Fv("A330_M_MACH") == 1 ? $"Mach {value:0.00}" : $"{value:0} knots";
                return true;
            case "A330_FCU_HDG":
            {
                double v = fpa ? _sim?.GetCachedVariableValue("A330_FCU_TRK") ?? value : value;
                displayText = $"{(fpa ? "track" : "heading")} {((int)Math.Round(v) % 360 + 360) % 360:000}";
                return true;
            }
            case "A330_FCU_ALT": displayText = $"{value:0} feet"; return true;
            case "A330_FCU_VS":
                displayText = fpa ? $"flight path angle {(_sim?.GetCachedVariableValue("A330_FCU_FPA") ?? 0):0.0} degrees" : $"{value:0} feet per minute";
                return true;
            case "A330_FLAP_ANGLE": case "A330_SLAT_ANGLE": displayText = $"{value:0} degrees"; return true;
            case "A330_GEAR_POS": displayText = $"{value:0} percent down"; return true;
            case "A330_TRIM": displayText = FormatTrim(value); return true;
            case "A330_LDG_ELEV_FT": displayText = $"{value:0} feet"; return true;
        }
        displayText = "";
        return false;
        static string Or(string s) => s.Length > 0 ? s : "none";
    }

    internal static string FormatTrim(double deg) =>
        Math.Abs(deg) < 0.05 ? "0.0" : $"{Math.Abs(deg).ToString("0.0", CultureInfo.InvariantCulture)} {(deg > 0 ? "up" : "down")}";

    private string ApStatus()
    {
        var sim = _sim;
        if (sim == null) return "unknown";
        bool ap1 = (sim.GetCachedVariableValue("A330_AP1_ON") ?? 0) > 0.5;
        bool ap2 = (sim.GetCachedVariableValue("A330_AP2_ON") ?? 0) > 0.5;
        bool at = (sim.GetCachedVariableValue("A330_ATHR_ON") ?? 0) > 0.5;
        return (ap1 && ap2 ? "AP 1 and 2" : ap1 ? "AP 1" : ap2 ? "AP 2" : "AP off") + (at ? ", A/THR on" : ", A/THR off");
    }

    public override void OnDisplayPanelShown(string panelKey, SimConnectManager simConnect)
    {
        _sim = simConnect;
        base.OnDisplayPanelShown(panelKey, simConnect);
    }

    // =====================================================================
    // Hotkeys
    // =====================================================================

    /// <summary>Instrument camera views (0-based), found by sweeping all 38 on 2026-10-16: view 3 frames
    /// both ECAM screens, view 6 the captain's PFD and ND under the FCU.</summary>
    internal const int EcamView = 3, PfdNdView = 6;

    internal virtual int EcamViewIndex => EcamView;
    internal virtual int PfdNdViewIndex => PfdNdView;

    public override bool HandleHotkeyAction(HotkeyAction action, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeyManager)
    {
        _sim = simConnect;
        double Cur(string key) => simConnect.GetCachedVariableValue(key) ?? 0;
        var fma = CurrentFma();
        string Suffix(string mode) => mode.Length > 0 ? $", {mode}" : "";

        switch (action)
        {
            case HotkeyAction.ReadHeading:
            {
                bool trk = Fv("A330_M_TRKFPA") == 1;
                int v = ((int)Math.Round(Cur(trk ? "A330_FCU_TRK" : "A330_FCU_HDG")) % 360 + 360) % 360;
                announcer.AnnounceImmediate($"FCU {(trk ? "track" : "heading")} {v:000}{Suffix(fma.Lateral)}");
                return true;
            }
            case HotkeyAction.ReadSpeed:
                if (SpeedManaged())
                {
                    double m = Cur("A330_MANAGED_SPEED");
                    announcer.AnnounceImmediate($"Speed managed{(m > 0 ? $", target {m:0} knots" : "")}{Suffix(fma.Thrust)}");
                }
                else if (Fv("A330_M_MACH") == 1)
                    announcer.AnnounceImmediate($"FCU Mach {Cur("A330_FCU_SPD"):0.00}{Suffix(fma.Thrust)}");
                else
                    announcer.AnnounceImmediate($"FCU speed {Cur("A330_FCU_SPD"):0} knots{Suffix(fma.Thrust)}");
                return true;
            case HotkeyAction.ReadAltitude:
                announcer.AnnounceImmediate($"FCU altitude {Cur("A330_FCU_ALT"):0} feet{Suffix(fma.Vertical)}");
                return true;
            case HotkeyAction.ReadFCUVerticalSpeedFPA:
            {
                bool fpa = Fv("A330_M_TRKFPA") == 1;
                string window = fpa ? $"flight path angle {Cur("A330_FCU_FPA"):0.0} degrees" : $"vertical speed {Cur("A330_FCU_VS"):0} feet per minute";
                announcer.AnnounceImmediate($"FCU {window}{Suffix(fma.Vertical)}");
                return true;
            }
            case HotkeyAction.ReadApproachCapability:
            {
                string cap = A330Fma.Capability(Fv("A330_M_CAP"));
                announcer.AnnounceImmediate(cap.Length > 0 ? cap : "No landing capability shown");
                return true;
            }
            case HotkeyAction.ReadFlaps:
            {
                int idx = (int)Math.Round(Cur("A330_FLAPS"));
                string lever = idx >= 0 && idx < Detents.Length ? Detents[idx] : $"Flap lever {idx}";
                announcer.AnnounceImmediate($"{lever}. Slats {Cur("A330_SLAT_ANGLE"):0}, flaps {Cur("A330_FLAP_ANGLE"):0} degrees");
                return true;
            }
            case HotkeyAction.ReadGear:
                announcer.AnnounceImmediate(Cur("A330_GEAR") > 0.5 ? "Gear lever down" : "Gear lever up");
                return true;
            case HotkeyAction.ReadFuelQuantity:
                announcer.AnnounceImmediate(FuelSummary(simConnect));
                return true;
            case HotkeyAction.ReadAltimeter: ReadAltimeter(simConnect, announcer); return true;
            case HotkeyAction.ReadNavRadioInfo: ReadRadios(simConnect, announcer); return true;
            case HotkeyAction.ReadDistanceToDest: ReadFmsDistance(simConnect, announcer, toTod: false); return true;
            case HotkeyAction.ReadDistanceToTOD: ReadFmsDistance(simConnect, announcer, toTod: true); return true;
            case HotkeyAction.ReadSpeedGD: ReadSpeed(simConnect, announcer, "A330_GREEN_DOT", "Green dot"); return true;
            case HotkeyAction.ReadSpeedS: ReadSpeed(simConnect, announcer, "A330_S_SPEED", "S speed"); return true;
            case HotkeyAction.ReadSpeedF: ReadSpeed(simConnect, announcer, "A330_F_SPEED", "F speed"); return true;
            case HotkeyAction.ReadSpeedVLS: ReadSpeed(simConnect, announcer, "A330_VLS", "VLS"); return true;
            case HotkeyAction.ReadSpeedVFE: ReadSpeed(simConnect, announcer, "A330_VMAX", "Maximum speed"); return true;

            // ---- dialogs ----
            case HotkeyAction.FCUSetHeading: ShowHeadingDialog(simConnect, announcer, parentForm); return true;
            case HotkeyAction.FCUSetSpeed: ShowSpeedDialog(simConnect, announcer, parentForm); return true;
            case HotkeyAction.FCUSetAltitude: ShowAltitudeDialog(simConnect, announcer, parentForm); return true;
            case HotkeyAction.FCUSetVS: ShowVsDialog(simConnect, announcer, parentForm); return true;
            case HotkeyAction.FCUSetAutopilot:
                hotkeyManager.ExitInputHotkeyMode();
                ShowTrackedWindow(() => new Forms.IniA300.IniA300AutopilotWindow(AutopilotRows(simConnect, announcer), ShortName), w => w.ShowForm());
                return true;

            // ---- engage / mode keys (the result is announced by the monitor) ----
            case HotkeyAction.ToggleAutopilot1: PressAutopilot(simConnect, announcer, "A330_AP1"); return true;
            case HotkeyAction.ToggleAutopilot2: PressAutopilot(simConnect, announcer, "A330_AP2"); return true;
            case HotkeyAction.ToggleAutothrust: PressAutopilot(simConnect, announcer, "A330_ATHR"); return true;
            case HotkeyAction.ToggleLocalizer: PressAutopilot(simConnect, announcer, "A330_LOC"); return true;
            case HotkeyAction.ToggleApproachMode: PressAutopilot(simConnect, announcer, "A330_APPR"); return true;
            case HotkeyAction.FCUHeadingPull: PressAutopilot(simConnect, announcer, "A330_HDG_PULL"); return true;
            case HotkeyAction.FCUHeadingPush: PressAutopilot(simConnect, announcer, "A330_HDG_PUSH"); return true;
            case HotkeyAction.FCUSpeedPull: PressAutopilot(simConnect, announcer, "A330_SPD_PULL"); return true;
            case HotkeyAction.FCUSpeedPush: PressAutopilot(simConnect, announcer, "A330_SPD_PUSH"); return true;
            case HotkeyAction.FCUAltitudePull: PressAutopilot(simConnect, announcer, "A330_ALT_PULL"); return true;
            case HotkeyAction.FCUAltitudePush: PressAutopilot(simConnect, announcer, "A330_ALT_PUSH"); return true;
            case HotkeyAction.FCUVSPull: PressAutopilot(simConnect, announcer, "A330_VS_PULL"); return true;
            case HotkeyAction.FCUVSPush: PressAutopilot(simConnect, announcer, "A330_VS_PUSH"); return true;

            // ---- display reads (AI vision on the aircraft's own instrument camera) ----
            case HotkeyAction.ReadDisplayPFD:
                ReadDisplay(Services.GeminiService.DisplayType.PFD, "PFD", announcer, parentForm,
                    new Services.InstrumentViewRequest(simConnect, PfdNdViewIndex));
                return true;
            case HotkeyAction.ReadDisplayND:
                ReadDisplay(Services.GeminiService.DisplayType.ND, "Navigation Display", announcer, parentForm,
                    new Services.InstrumentViewRequest(simConnect, PfdNdViewIndex));
                return true;
            case HotkeyAction.ReadDisplayUpperECAM:
                ReadDisplay(Services.GeminiService.DisplayType.UpperECAM, "Upper ECAM", announcer, parentForm,
                    new Services.InstrumentViewRequest(simConnect, EcamViewIndex));
                return true;
            case HotkeyAction.ReadDisplayLowerECAM:
                ReadDisplay(Services.GeminiService.DisplayType.LowerECAM, "System display", announcer, parentForm,
                    new Services.InstrumentViewRequest(simConnect, EcamViewIndex));
                return true;

            // ---- windows ----
            case HotkeyAction.ShowFenixMCDU:
                hotkeyManager.ExitInputHotkeyMode();
                ShowTrackedWindow(() => new Forms.IniA300.IniA300CduForm(simConnect, announcer, CduProfile()), f => f.ShowForm());
                return true;
            case HotkeyAction.ShowPMDGEFB:
                hotkeyManager.ExitInputHotkeyMode();
                ShowEfb(announcer);
                return true;
            case HotkeyAction.MonitorManager:
                hotkeyManager.ExitOutputHotkeyMode();
                ShowTrackedWindow(() => new Forms.IniA300.A300MonitorManagerForm(GetVariables(), ShortName,
                    DisabledMonitorList), m => m.ShowForm());
                return true;
        }
        return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
    }

    private IEnumerable<Forms.IniA300.IniA300AutopilotWindow.Row> AutopilotRows(SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        bool On(string key) => (sim.GetCachedVariableValue(key) ?? 0) > 0.5;
        Forms.IniA300.IniA300AutopilotWindow.Row R(string label, Func<string> state, string key)
            => new(label, state, () => PressAutopilot(sim, announcer, key));
        yield return R("Autopilot &1", () => On("A330_AP1_ON") ? "engaged" : "off", "A330_AP1");
        yield return R("Autopilot &2", () => On("A330_AP2_ON") ? "engaged" : "off", "A330_AP2");
        yield return R("A/&THR", () => On("A330_ATHR_ON") ? "on" : "off", "A330_ATHR");
        yield return R("L&OC", () => CurrentFma().Lateral.StartsWith("LOC") ? "active" : "", "A330_LOC");
        yield return R("A&PPR", () => "", "A330_APPR");
        yield return R("&Speed pull (selected)", () => SpeedManaged() ? "managed now" : "selected now", "A330_SPD_PULL");
        yield return R("Speed push (&managed)", () => "", "A330_SPD_PUSH");
        yield return R("&Heading pull (selected)", () => CurrentFma().Lateral, "A330_HDG_PULL");
        yield return R("Heading push (&NAV)", () => "", "A330_HDG_PUSH");
        yield return R("Altitude p&ull (open climb or descent)", () => CurrentFma().Vertical, "A330_ALT_PULL");
        yield return R("Altitude pu&sh (managed)", () => "", "A330_ALT_PUSH");
        yield return R("&V/S pull", () => "", "A330_VS_PULL");
        yield return R("V/S push (level off)", () => "", "A330_VS_PUSH");
        if (HasButton(AltButtonKey)) yield return R("A&LT (hold this altitude)", () => "", AltButtonKey);
        if (HasButton(ExpedButtonKey)) yield return R("E&XPED", () => "", ExpedButtonKey);
        yield return new("&Read flight modes", () => "", () => announcer.AnnounceImmediate(CurrentFma().Summary()));
    }

    private SimConnect.CoherentPmdgEfbClient? _efbClient;

    internal virtual SimConnect.CoherentPmdgEfbClient CreateEfbClient() => SimConnect.CoherentPmdgEfbClient.ForIniA330();

    internal virtual Forms.IniA300.CduFormProfile CduProfile() => Forms.IniA300.CduFormProfile.A330(A300CduGlyphLibrary.A330);

    private void ShowEfb(ScreenReaderAnnouncer announcer)
    {
        ShowTrackedWindow(() =>
        {
            _efbClient?.Dispose();
            _efbClient = CreateEfbClient();
            _efbClient.Start();
            var client = _efbClient;
            var f = new Forms.FBWA380.FbwEfbForm(client, announcer, $"{ShortName} EFB", "EFB", "EFB");
            f.VisibleChanged += (_, _) => client.SetActive(!f.IsDisposed && f.Visible);
            f.Disposed += (_, _) => { client.Dispose(); if (ReferenceEquals(_efbClient, client)) _efbClient = null; };
            return f;
        }, f => { _efbClient?.SetActive(true); f.ShowForm(); });
    }

    internal string FuelSummary(SimConnectManager sim)
    {
        double V(string key) => sim.GetCachedVariableValue(key) ?? 0;
        bool metric = (sim.GetCachedVariableValue("A330_IS_METRIC") ?? 1) > 0.5;
        string W(double kg) => metric ? $"{kg:0} kilograms" : $"{kg * 2.20462:0} pounds";
        double centreKg = CentreTankFromSim ? V("A330_FUEL_CENTER") * V("A330_FUEL_WPG") / 2.20462 : 0;
        double tanks = Tanks.Sum(f => V(f.key)) + centreKg;
        double total = V("A330_FUEL_TOTAL_LB") > 0 ? V("A330_FUEL_TOTAL_LB") / 2.20462 : tanks;
        var parts = Tanks.Where(f => V(f.key) > 1).Select(f => $"{f.name} {W(V(f.key))}").ToList();
        if (centreKg > 1) parts.Insert(Math.Min(2, parts.Count), $"centre {W(centreKg)}");
        // the A321LR's additional centre tanks are not published separately: what the sim's total holds
        // beyond the published tanks is theirs
        if (!CentreTankFromSim && total - tanks > 50) parts.Add($"additional centre tanks {W(total - tanks)}");
        return $"Fuel {W(total)}. " + string.Join(", ", parts);
    }

    private static void ReadSpeed(SimConnectManager sim, ScreenReaderAnnouncer announcer, string key, string name)
    {
        var ui = SynchronizationContext.Current;
        _ = Task.Run(async () =>
        {
            double? v = await sim.ReadFreshAsync(key, 1000);
            string msg = v is double k && k > 20 ? $"{name} {k:0} knots" : $"{name} not available";
            if (ui != null) ui.Post(_ => announcer.AnnounceImmediate(msg), null);
        });
    }

    private static void ReadFmsDistance(SimConnectManager sim, ScreenReaderAnnouncer announcer, bool toTod)
    {
        var ui = SynchronizationContext.Current;
        _ = Task.Run(async () =>
        {
            double? dist = await sim.ReadFreshAsync(toTod ? "A330_TD_DIST_TOTAL" : "A330_DIST_DEST", 1000);
            if (!toTod && (dist is not double d0 || d0 <= 0.5)) dist = await sim.ReadFreshAsync("A330_APPR_DIST", 1000);
            double gs = await sim.ReadFreshAsync("A330_GS_FRESH", 1000) ?? 0;
            Log.Info("a330", $"{(toTod ? "T/D" : "dest")} distance raw={dist?.ToString(CultureInfo.InvariantCulture) ?? "none"} gs={gs:0}");
            string msg = DescribeFmsDistance(dist, gs, toTod);
            if (ui != null) ui.Post(_ => announcer.AnnounceImmediate(msg), null);
        });
    }

    internal static string DescribeFmsDistance(double? nm, double gsKnots, bool toTod)
    {
        if (nm is not double d || d <= 0.5 || double.IsNaN(d))
            return toTod ? "Top of descent not available" : "Distance to destination not available. Enter a flight plan in the MCDU.";
        string time = "";
        if (gsKnots >= 30)
        {
            int min = (int)Math.Round(d / gsKnots * 60);
            time = min >= 60 ? $", {min / 60} hour{(min / 60 == 1 ? "" : "s")} {min % 60} minutes" : $", {min} minute{(min == 1 ? "" : "s")}";
        }
        return toTod ? $"{d:0} miles to top of descent{time}" : $"Destination {d:0} miles{time}";
    }

    private static void ReadAltimeter(SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        var ui = SynchronizationContext.Current;
        _ = Task.Run(async () =>
        {
            double? mb = await sim.ReadFreshAsync("A330_BARO_FRESH", 1000);
            double? std = await sim.ReadFreshAsync("A330_BARO_STD_FRESH", 1000);
            string msg = std is double s && s > 0.5 ? "Altimeter standard"
                : mb is double m && m > 800 ? $"Altimeter {m:0} hectopascals, {(m * 0.02953).ToString("0.00", CultureInfo.InvariantCulture)} inches"
                : "Altimeter not available";
            if (ui != null) ui.Post(_ => announcer.AnnounceImmediate(msg), null);
        });
    }

    private static void ReadRadios(SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        var ui = SynchronizationContext.Current;
        _ = Task.Run(async () =>
        {
            async Task<double?> R(string k) => await sim.ReadFreshAsync(k, 1000);
            string Mhz(double? v) => v is double f && f > 100 ? f.ToString("0.00#", CultureInfo.InvariantCulture) : "not tuned";
            var parts = new List<string>();
            double? ils = await R("A330_NAV3_FREQ");
            bool loc = (await R("A330_NAV3_LOC") ?? 0) > 0.5, gs = (await R("A330_NAV3_GS") ?? 0) > 0.5;
            parts.Add($"ILS {Mhz(ils)}{(loc ? (gs ? ", localizer and glide slope received" : ", localizer received") : ", not received")}");
            parts.Add($"VOR 1 {Mhz(await R("A330_NAV1_FREQ"))}");
            parts.Add($"VOR 2 {Mhz(await R("A330_NAV2_FREQ"))}");
            parts.Add($"VHF 1 {Mhz(await R("A330_COM1_ACT"))}, standby {Mhz(await R("A330_COM1_STBY"))}");
            string msg = string.Join(". ", parts);
            if (ui != null) ui.Post(_ => announcer.AnnounceImmediate(msg), null);
        });
    }

    // =====================================================================
    // FCU set dialogs — the windows are written directly (measured: heading 270 written, the stock
    // autopilot heading followed) and confirmed by a fresh read-back.
    // =====================================================================

    private void SetAndConfirm(SimConnectManager sim, ScreenReaderAnnouncer announcer, string lvar, string cacheKey,
        double target, Func<double, string> describe, double tolerance = 0.6)
    {
        if (!CanCalc(sim, announcer, "FCU")) return;
        var ui = SynchronizationContext.Current;
        _ = Task.Run(async () =>
        {
            Write(sim, $"{Num(target)} (>L:{lvar})");
            await Task.Delay(400);
            double? got = await sim.ReadFreshAsync(cacheKey, 1500);
            string msg = got is null ? $"{describe(target)} sent, the FCU did not report back"
                : Math.Abs(got.Value - target) < tolerance ? describe(got.Value)
                : $"FCU shows {describe(got.Value)}, not {describe(target)}";
            if (ui != null) ui.Post(_ => announcer.AnnounceImmediate(msg), null);
        });
    }

    private static bool On(SimConnectManager sim, string key) => (sim.GetCachedVariableValue(key) ?? 0) > 0.5;

    internal const string AltButtonKey = "A330_ALT_BTN", ExpedButtonKey = "A330_EXPED";

    private bool HasButton(string key) => Buttons.Any(b => b.key == key);

    /// <summary>LOC / APPR show what the FMA says they did — active, armed, or off.</summary>
    private string LocState()
    {
        var f = CurrentFma();
        return f.Lateral == "LOC" || f.Lateral.StartsWith("LOC ") ? "Active" : f.Armed.Contains("LOC") ? "Armed" : "Off";
    }

    private string ApprState()
    {
        var f = CurrentFma();
        if (f.Vertical is "LAND" or "G S" or "G S star") return "Active";
        return f.Armed.Contains("G S") ? "Armed" : "Off";
    }

    /// <summary>The HDG-V/S / TRK-FPA key: a toggle on the aircraft, so one press flips it. The dialog
    /// it sits in was opened in one mode; the button shows the live one.</summary>
    private ToggleButtonDef TrkFpaToggle(SimConnectManager sim, ScreenReaderAnnouncer announcer) =>
        new("&TRK/FPA", () => Fv("A330_M_TRKFPA") == 1 ? "TRK FPA" : "HDG V/S", () =>
        {
            if (CanCalc(sim, announcer, "TRK-FPA")) Pulse(sim, "INI_FCU_HDG_VS_COMMAND");
        });

    private void ShowHeadingDialog(SimConnectManager sim, ScreenReaderAnnouncer announcer, Form parent)
    {
        bool trk = Fv("A330_M_TRKFPA") == 1;
        var toggles = new List<ToggleButtonDef>
        {
            new("Heading &pull (selected)", () => "Pull", () => PressAutopilot(sim, announcer, "A330_HDG_PULL")),
            new("Heading pu&sh (NAV)", () => CurrentFma().Lateral == "NAV" ? "Active" : "Push", () => PressAutopilot(sim, announcer, "A330_HDG_PUSH")),
            new("&LOC", LocState, () => PressAutopilot(sim, announcer, "A330_LOC")),
            TrkFpaToggle(sim, announcer),
        };
        var dlg = new ValueInputForm(trk ? "FCU Track" : "FCU Heading", trk ? "track" : "heading", "0-359", announcer,
            input => int.TryParse(input, out int v) && v is >= 0 and <= 360 ? (true, "") : (false, "Enter a heading between 0 and 359"),
            toggles,
            input =>
            {
                if (int.TryParse(input, out int h))
                    SetAndConfirm(sim, announcer, trk ? "INI_TRK_DIAL" : "INI_HEADING_DIAL", trk ? "A330_FCU_TRK" : "A330_FCU_HDG",
                        h % 360, v => $"{(trk ? "track" : "heading")} {((int)Math.Round(v) % 360 + 360) % 360:000}");
            });
        dlg.ShowCancelButton = false;
        dlg.Show(parent);
    }

    private void ShowSpeedDialog(SimConnectManager sim, ScreenReaderAnnouncer announcer, Form parent)
    {
        var toggles = new List<ToggleButtonDef>
        {
            new("&Selected (pull)", () => SpeedManaged() ? "Managed" : "Selected", () => PressAutopilot(sim, announcer, "A330_SPD_PULL")),
            new("&Managed (push)", () => SpeedManaged() ? "Managed" : "Selected", () => PressAutopilot(sim, announcer, "A330_SPD_PUSH")),
            new("SPD/MA&CH", () => Fv("A330_M_MACH") == 1 ? "Mach" : "Knots",
                () => SetOwnEvent(sim, announcer, "Speed/Mach", "AIRLINER_SPDMACH", Fv("A330_M_MACH") == 1 ? 0 : 1)),
            new("A/&THR", () => On(sim, "A330_ATHR_ON") ? "On" : "Off", () => PressAutopilot(sim, announcer, "A330_ATHR")),
        };
        var dlg = new ValueInputForm("FCU Speed", "speed", "Knots: 100-399 / Mach: 0.10-0.89", announcer,
            input => double.TryParse(input, NumberStyles.Any, CultureInfo.InvariantCulture, out double v) && ((v >= 100 && v <= 399) || (v >= 0.1 && v < 0.9))
                ? (true, "") : (false, "Enter knots (100-399) or Mach (0.10-0.89)"),
            toggles,
            input =>
            {
                if (!double.TryParse(input, NumberStyles.Any, CultureInfo.InvariantCulture, out double v)) return;
                if (SpeedManaged()) announcer.AnnounceImmediate("Speed is managed; pull the speed knob to fly this speed.");
                bool wantMach = v < 1, isMach = Fv("A330_M_MACH") == 1;
                if (wantMach != isMach)
                {
                    // one window holds both (measured: SPD/MACH turned 100 into 0.151) — swap units first
                    SetOwnEvent(sim, announcer, "Speed/Mach", "AIRLINER_SPDMACH", wantMach ? 1 : 0);
                    // awaited on the dialog's (UI) context: SetAndConfirm captures that context to
                    // speak its verdict, and from a pool thread it captured none and said nothing
                    _ = SetAfterSwap();
                    async Task SetAfterSwap()
                    {
                        await Task.Delay(600);
                        SetAndConfirm(sim, announcer, "INI_AIRSPEED_DIAL", "A330_FCU_SPD", wantMach ? Math.Round(v, 2) : Math.Round(v),
                            s => wantMach ? $"Mach {s:0.00}" : $"speed {s:0} knots", wantMach ? 0.006 : 0.6);
                    }
                }
                else
                    SetAndConfirm(sim, announcer, "INI_AIRSPEED_DIAL", "A330_FCU_SPD", wantMach ? Math.Round(v, 2) : Math.Round(v),
                        s => wantMach ? $"Mach {s:0.00}" : $"speed {s:0} knots", wantMach ? 0.006 : 0.6);
            });
        dlg.ShowCancelButton = false;
        dlg.Show(parent);
    }

    private void ShowAltitudeDialog(SimConnectManager sim, ScreenReaderAnnouncer announcer, Form parent)
    {
        var toggles = new List<ToggleButtonDef>
        {
            new("Altitude p&ull (open climb or descent)", () => "Pull", () => PressAutopilot(sim, announcer, "A330_ALT_PULL")),
            new("Altitude pu&sh (managed)", () => "Push", () => PressAutopilot(sim, announcer, "A330_ALT_PUSH")),
            new("M&etric altitude", () => On(sim, "A330_METRIC") ? "On" : "Off",
                () => SetOwnEvent(sim, announcer, "Metric altitude", "AIRLINER_METRIC", On(sim, "A330_METRIC") ? 0 : 1)),
            new("AP &1", () => On(sim, "A330_AP1_ON") ? "On" : "Off", () => PressAutopilot(sim, announcer, "A330_AP1")),
            new("AP &2", () => On(sim, "A330_AP2_ON") ? "On" : "Off", () => PressAutopilot(sim, announcer, "A330_AP2")),
        };
        if (SelectKeys.Contains("A330_ALT_STEP"))
            toggles.Insert(2, new("&Knob steps (100 or 1000 feet)", () => (sim.GetCachedVariableValue("A330_ALT_STEP") ?? 0) > 0.5 ? "100 feet" : "1000 feet",
                () => SetOwnEvent(sim, announcer, "Altitude knob steps", "AIRLINER_MCU_ALTSELECT", AltStepInput(!((sim.GetCachedVariableValue("A330_ALT_STEP") ?? 0) > 0.5)))));
        if (HasButton(AltButtonKey))
            toggles.Insert(2, new("&ALT (hold the current altitude)", () => CurrentFma().Vertical == "ALT" ? "Active" : "Press", () => PressAutopilot(sim, announcer, AltButtonKey)));
        if (HasButton(ExpedButtonKey))
            toggles.Insert(2, new("E&XPED", () => "Press", () => PressAutopilot(sim, announcer, ExpedButtonKey)));
        var dlg = new ValueInputForm("FCU Altitude", "altitude", "100-49000 feet", announcer,
            input => int.TryParse(input, out int v) && v is >= 100 and <= 49000 ? (true, "") : (false, "Enter an altitude between 100 and 49000 feet"),
            toggles,
            input =>
            {
                if (int.TryParse(input, out int a))
                    SetAndConfirm(sim, announcer, "INI_ALTITUDE_DIAL", "A330_FCU_ALT", Math.Round(a / 100.0) * 100, v => $"altitude {v:0} feet");
            });
        dlg.ShowCancelButton = false;
        dlg.Show(parent);
    }

    private void ShowVsDialog(SimConnectManager sim, ScreenReaderAnnouncer announcer, Form parent)
    {
        bool fpa = Fv("A330_M_TRKFPA") == 1;
        var toggles = new List<ToggleButtonDef>
        {
            new("V/S p&ull (engage)", () => "Pull", () => PressAutopilot(sim, announcer, "A330_VS_PULL")),
            new("V/S pu&sh (level off)", () => "Push", () => PressAutopilot(sim, announcer, "A330_VS_PUSH")),
            TrkFpaToggle(sim, announcer),
            new("A&PPR", ApprState, () => PressAutopilot(sim, announcer, "A330_APPR")),
        };
        var dlg = new ValueInputForm(fpa ? "FCU Flight Path Angle" : "FCU Vertical Speed", fpa ? "flight path angle" : "vertical speed",
            fpa ? "-9.9 to 9.9 degrees" : "-6000 to 6000 feet per minute", announcer,
            input => double.TryParse(input, NumberStyles.Any, CultureInfo.InvariantCulture, out double v)
                     && (fpa ? Math.Abs(v) <= 9.9 : Math.Abs(v) <= 6000) ? (true, "") : (false, fpa ? "Enter -9.9 to 9.9" : "Enter -6000 to 6000"),
            toggles,
            input =>
            {
                if (!double.TryParse(input, NumberStyles.Any, CultureInfo.InvariantCulture, out double v)) return;
                if (fpa) SetAndConfirm(sim, announcer, "INI_FPA_DIAL", "A330_FCU_FPA", Math.Round(v, 1), x => $"flight path angle {x:0.0} degrees", 0.06);
                else SetAndConfirm(sim, announcer, "INI_VVI_DIAL", "A330_FCU_VS", Math.Round(v / 100.0) * 100, x => $"vertical speed {x:0} feet per minute", 60);
            });
        dlg.ShowCancelButton = false;
        dlg.Show(parent);
    }
}
