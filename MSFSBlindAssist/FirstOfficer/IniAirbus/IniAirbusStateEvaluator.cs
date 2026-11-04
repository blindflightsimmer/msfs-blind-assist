using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Airbus;
using MSFSBlindAssist.FirstOfficer.Generic;

namespace MSFSBlindAssist.FirstOfficer.IniAirbus;

/// <summary>
/// Reads iniBuilds A330 / A321LR / A340-300 state from the SimConnect cache for the First Officer. Every
/// field is a key the definition registers (IniAirbusFoTests), or one of the synthetic <c>FO_*</c> fields
/// below, composed from registered keys. An unread field is NaN (indeterminate: no tick, no revert, no
/// guard pass), the contract every First Officer evaluator keeps.
///
/// FOUR ENGINES (the A340): engines 1 and 2 N2 arrive through <see cref="IFoStateEvaluator.SetEngineN2"/>,
/// engine 3 through <see cref="IFoEngine3N2Sink"/> and engine 4 through SetEngine4N2
/// (the window's TURB ENG N2:1-4 pushes), each NaN until its first answer. On a two-engine airframe the
/// engine 3/4 values are simply never read.
/// </summary>
public sealed class IniAirbusStateEvaluator : LVarStateEvaluator, IFoEngine3N2Sink
{
    public IniAirbusKeys Keys { get; }

    /// <summary>The A330's evaluator. For reflection-based sweeps that build every LVarStateEvaluator
    /// (FoPollSilenceTests); both airframes poll the same fields. The profile always passes its keys.</summary>
    public IniAirbusStateEvaluator() : this(IniAirbusKeys.A330) { }

    public IniAirbusStateEvaluator(IniAirbusKeys keys)
    {
        Keys = keys;
        _poll = new[]
            {
                // OnRequest keys (never on the continuous batch): the FO's own reads and the altimeter.
                IniAirbusKeys.Eng1Avail, IniAirbusKeys.Eng2Avail, IniAirbusKeys.ApuFlap,
                IniAirbusKeys.GearLeft, IniAirbusKeys.GearRight,
                IniAirbusKeys.BaroMb, IniAirbusKeys.BaroMode, IniA330Definition.FoApuStartPbKey,
            }
            .Concat(keys.ExtraPollReads).Distinct().ToArray();
    }

    private readonly string[] _poll;
    public override IReadOnlyList<string> OnRequestPollFields => _poll;

    // ---- synthetic field names ----
    public const string Eng1Running = "FO_ENG1_RUNNING", Eng2Running = "FO_ENG2_RUNNING";
    public const string Eng3Running = "FO_ENG3_RUNNING", Eng4Running = "FO_ENG4_RUNNING";
    /// <summary>Engine n STARTED: running (its AVAIL) and settled — the engine-start waits read these.</summary>
    public const string Eng1Started = "FO_ENG1_STARTED", Eng2Started = "FO_ENG2_STARTED";
    public const string Eng3Started = "FO_ENG3_STARTED", Eng4Started = "FO_ENG4_STARTED";
    public const string EnginesOff = "FO_ENGINES_OFF";
    public const string MastersOff = "FO_MASTERS_OFF";
    public const string ApuOrExtPower = "FO_APU_OR_EXT_POWER";
    public const string ExtPowerAvailable = "FO_EXT_POWER_AVAILABLE";
    public const string ExtPowerOn = "FO_EXT_POWER_ON";
    public const string ExtPowerOff = "FO_EXT_POWER_OFF";
    public const string BatteriesOn = "FO_BATTERIES_ON", BatteriesOff = "FO_BATTERIES_OFF";
    public const string FuelPumpsOn = "FO_FUEL_PUMPS_ON", FuelPumpsOff = "FO_FUEL_PUMPS_OFF";
    public const string PacksOn = "FO_PACKS_ON";
    public const string HotAirOn = "FO_HOT_AIR_ON";
    public const string LandingLightsOn = "FO_LANDING_LIGHTS_ON", LandingLightsOff = "FO_LANDING_LIGHTS_OFF";
    public const string IrsNav = "FO_IRS_NAV", IrsOff = "FO_IRS_OFF";
    public const string WxrOnAuto = "FO_WXR_ON_AUTO", WxrPwsOff = "FO_WXR_PWS_OFF";
    public const string SignsOn = "FO_SIGNS_ON";
    public const string SeatBeltsOn = "FO_SEAT_BELTS_ON", SeatBeltsOff = "FO_SEAT_BELTS_OFF";
    public const string TaxiMemoOk = "FO_TAXI_MEMO_OK";
    public const string LdgFlapsSet = "FO_LDG_FLAPS_SET";
    public const string LdgMemoOk = "FO_LDG_MEMO_OK";
    public const string GearUp = "FO_GEAR_UP", GearDown = "FO_GEAR_DOWN";
    public const string AntiIceOff = "FO_ANTI_ICE_OFF";
    public const string LsOn = "FO_LS_ON", LsOff = "FO_LS_OFF";
    public const string FdsOn = "FO_FDS_ON";
    public const string WipersOff = "FO_WIPERS_OFF";
    public const string AutobrakeMax = "FO_AUTOBRAKE_MAX";
    /// <summary>1 when the First Officer may press APU START: the A330 always (its input event's own
    /// press), the A321 and A340 only while INI_APU_START_BUTTON reads 0. 0 when it reads 1, NaN while unread.</summary>
    public const string ApuStartPressable = "FO_APU_START_PRESSABLE";

    /// <summary>Engine <paramref name="n"/>'s running field (1-4).</summary>
    public static string RunningField(int n) => n switch
    {
        1 => Eng1Running, 2 => Eng2Running, 3 => Eng3Running, 4 => Eng4Running,
        _ => throw new ArgumentOutOfRangeException(nameof(n)),
    };

    /// <summary>Engine <paramref name="n"/>'s started (running and settled) field (1-4).</summary>
    public static string StartedField(int n) => n switch
    {
        1 => Eng1Started, 2 => Eng2Started, 3 => Eng3Started, 4 => Eng4Started,
        _ => throw new ArgumentOutOfRangeException(nameof(n)),
    };

    /// <summary>True when the autobrake MAX is the Captain's because this airframe's MAX key is measured only
    /// from OFF (the A321) and the level reads LO or MED.</summary>
    public bool AutobrakeMaxLeftToCaptain()
    {
        double level = GetValue(IniAirbusKeys.Autobrake);
        return Keys.AutobrakeMaxOnlyFromOff && level is >= 0.5 and < 2.5;
    }

    // ---- engines 3 and 4 (the A340) ----

    private double _eng3N2 = double.NaN;
    private double _eng4N2 = double.NaN;

    public void SetEngine3N2(double eng3N2) => Volatile.Write(ref _eng3N2, eng3N2);
    public void SetEngine4N2(double eng4N2) => Volatile.Write(ref _eng4N2, eng4N2);

    /// <summary>Engine <paramref name="n"/> (1-4) N2, NaN while unread.</summary>
    public double EngineN2(int n) => n switch
    {
        1 => Eng1N2, 2 => Eng2N2, 3 => Volatile.Read(ref _eng3N2), 4 => Volatile.Read(ref _eng4N2), _ => double.NaN,
    };

    private double EngineAvail(int n) => n <= Keys.EngineAvail.Count ? GetValue(Keys.EngineAvail[n - 1]) : double.NaN;

    /// <summary>Engine idle N2 floor used ONLY when the aircraft's own AVAIL L:var is unread. The A330's
    /// ready_for_takeoff panel state shows idle N2 48.5 % (the A340's ground idle read 58.5 % live,
    /// 2026-10-28), so 45 % (below both). The stock N2 is used, never ini_engineN_n2 (it reads 0 in the
    /// A321's panel state).</summary>
    internal const double RunningN2Fallback = 45;

    /// <summary>N2 below which an engine counts as stopped (the guards).</summary>
    internal const double StoppedN2Below = 10;

    /// <summary>Running: the aircraft's ENGINE_n_AVAIL reads 1 (the E/WD's green AVAIL). With it
    /// unread, N2 at or above <see cref="RunningN2Fallback"/>. NaN when neither is known.</summary>
    internal static double RunningFrom(double avail, double n2)
    {
        if (!double.IsNaN(avail) && avail > 0.5) return 1;
        if (double.IsNaN(avail))
            return double.IsNaN(n2) ? double.NaN : n2 >= RunningN2Fallback ? 1 : 0;
        return 0;
    }

    /// <summary>Both engines stopped: both N2 READ and below <see cref="StoppedN2Below"/>, and neither
    /// AVAIL reading 1. An unread N2 is NaN: the stock N2 is the robust reading, the AVAIL only adds.</summary>
    internal static double EnginesOffFrom(double n2a, double n2b, double avail1, double avail2)
        => EnginesOffFrom(new[] { n2a, n2b }, new[] { avail1, avail2 });

    /// <summary>Every engine stopped: every N2 READ and below <see cref="StoppedN2Below"/>, and no AVAIL
    /// reading 1 (a known-running engine is never hidden by an unread one: an AVAIL lit is 0 at once).</summary>
    internal static double EnginesOffFrom(IReadOnlyList<double> n2, IReadOnlyList<double> avail)
    {
        foreach (double a in avail) if (a > 0.5) return 0;
        bool unknown = false;
        foreach (double v in n2)
        {
            if (double.IsNaN(v)) { unknown = true; continue; }
            if (v >= StoppedN2Below) return 0;
        }
        return unknown ? double.NaN : 1;
    }

    // ---- STARTED: running and settled (the engine-start waits) ----

    /// <summary>How long an engine must read running, with N2 steady, before the next one is started.</summary>
    public static readonly TimeSpan StartedHold = TimeSpan.FromSeconds(3);

    /// <summary>N2 change (% per second) over the hold below which the engine is no longer accelerating.</summary>
    public const double StartedMaxRatePctPerSec = 0.3;

    /// <summary>The clock the started test reads (tests replace it).</summary>
    internal Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    private readonly List<(DateTime At, double Running, double N2)>[] _history = { new(), new(), new(), new() };
    private readonly object _historyLock = new();

    /// <summary>
    /// Started from an engine's observed samples (oldest first, ending now): the LatinVFR A340's
    /// stable-running rule over the iniBuilds aircraft's own AVAIL. NaN when there are no samples or the
    /// newest is unread; 1 when every sample in the last <see cref="StartedHold"/> — and one at or before
    /// its start, so the hold is really covered — reads running, and, where N2 is read at both ends, N2
    /// moved less than <see cref="StartedMaxRatePctPerSec"/> a second across it (the next engine never
    /// starts while this one is still accelerating); else 0.
    /// </summary>
    internal static double StartedFrom(IReadOnlyList<(DateTime At, double Running, double N2)> samples, DateTime now)
    {
        if (samples.Count == 0 || double.IsNaN(samples[^1].Running)) return double.NaN;
        var last = samples[^1];
        if (last.Running < 0.5) return 0;
        DateTime from = now - StartedHold;
        int anchor = -1;
        for (int i = samples.Count - 1; i >= 0; i--)
        {
            if (!(samples[i].Running > 0.5)) return 0;
            if (samples[i].At <= from) { anchor = i; break; }
        }
        if (anchor < 0) return 0;   // not yet held for the whole window
        double a = samples[anchor].N2, b = last.N2;
        if (double.IsNaN(a) || double.IsNaN(b)) return 1;   // AVAIL held; no N2 to judge the settle by
        double dt = (last.At - samples[anchor].At).TotalSeconds;
        if (dt <= 0) return 0;
        return Math.Abs(b - a) / dt < StartedMaxRatePctPerSec ? 1 : 0;
    }

    private double Started(int n)
    {
        double running = RunningFrom(EngineAvail(n), EngineN2(n));
        DateTime now = Clock();
        lock (_historyLock)
        {
            var h = _history[n - 1];
            if (double.IsNaN(running)) { h.Clear(); return double.NaN; }
            if (h.Count > 0 && h[^1].At == now) h[^1] = (now, running, EngineN2(n));
            else h.Add((now, running, EngineN2(n)));
            // keep a little more than the hold; never an anchor older than twice the hold
            DateTime cut = now - StartedHold - StartedHold;
            while (h.Count > 0 && h[0].At < cut) h.RemoveAt(0);
            return StartedFrom(h.ToArray(), now);
        }
    }

    /// <summary>1 when every key reads <paramref name="value"/>, 0 when any reads otherwise, NaN when any
    /// is unread and none reads otherwise.</summary>
    private double All(IEnumerable<string> keys, Func<double, bool> test)
    {
        bool anyNaN = false, any = false;
        foreach (var k in keys)
        {
            any = true;
            double v = GetValue(k);
            if (double.IsNaN(v)) { anyNaN = true; continue; }
            if (!test(v)) return 0;
        }
        return !any || anyNaN ? double.NaN : 1;
    }

    /// <summary>Flap handle (FLAPS HANDLE INDEX, 0 Up .. 4 Full) at a takeoff position, 1-3.</summary>
    internal static bool IsTakeoffFlaps(double lever) => lever is >= 0.5 and <= 3.5;

    /// <summary>The flaps line of the landing memo: FULL (index 4). CONF 3 landings: the iniBuilds
    /// aircraft publish no CONF 3 selection the First Officer can read, so a CONF 3 approach leaves this
    /// line for the pilot's tick.</summary>
    internal static bool IsLandingFlaps(double lever) => Math.Abs(lever - 4) < 0.5;

    private const double GearUpBelow = 5, GearDownAbove = 95;

    /// <summary>Gear UP: lever up and every leg below 5 %; DOWN: lever down and every leg above 95 %
    /// (the FlyByWire rule, FO-13). NaN when the lever or any leg is unread.</summary>
    internal static double GearFrom(double lever, double nose, double left, double right, bool up)
    {
        if (double.IsNaN(lever) || double.IsNaN(nose) || double.IsNaN(left) || double.IsNaN(right)) return double.NaN;
        return up
            ? lever < 0.5 && nose < GearUpBelow && left < GearUpBelow && right < GearUpBelow ? 1 : 0
            : lever > 0.5 && nose > GearDownAbove && left > GearDownAbove && right > GearDownAbove ? 1 : 0;
    }

    private bool IsSeatBelts(double v, bool on) => !double.IsNaN(v) && Math.Abs(v - (on ? Keys.SeatBeltsOn : Keys.SeatBeltsOff)) < 0.5;

    protected override bool TryGetSyntheticValue(string field, out double value)
    {
        var k = Keys;
        switch (field)
        {
            case Eng1Running: value = RunningFrom(EngineAvail(1), EngineN2(1)); return true;
            case Eng2Running: value = RunningFrom(EngineAvail(2), EngineN2(2)); return true;
            case Eng3Running: value = k.EngineCount >= 3 ? RunningFrom(EngineAvail(3), EngineN2(3)) : double.NaN; return true;
            case Eng4Running: value = k.EngineCount >= 4 ? RunningFrom(EngineAvail(4), EngineN2(4)) : double.NaN; return true;
            case Eng1Started: value = Started(1); return true;
            case Eng2Started: value = Started(2); return true;
            case Eng3Started: value = k.EngineCount >= 3 ? Started(3) : double.NaN; return true;
            case Eng4Started: value = k.EngineCount >= 4 ? Started(4) : double.NaN; return true;
            case EnginesOff:
            {
                var n2 = Enumerable.Range(1, k.EngineCount).Select(EngineN2).ToArray();
                var avail = Enumerable.Range(1, k.EngineCount).Select(EngineAvail).ToArray();
                value = EnginesOffFrom(n2, avail);
                return true;
            }
            case MastersOff: value = All(k.EngineMasters, v => v < 0.5); return true;
            case ExtPowerAvailable:
            {
                double lamp = k.ExtPowerAvailLamp == null ? double.NaN : GetValue(k.ExtPowerAvailLamp);
                value = double.IsNaN(lamp) ? double.NaN : lamp > 0.5 ? 1 : 0;
            }
                return true;
            case ExtPowerOn: value = AnyOn(k.ExtPower); return true;
            case ExtPowerOff: value = All(k.ExtPower, v => v < 0.5); return true;
            case ApuOrExtPower: value = AnyLit(GetValue(k.ApuAvailLamp), AnyOn(k.ExtPower)); return true;
            case BatteriesOn: value = All(k.Batteries, v => v > 0.5); return true;
            case BatteriesOff: value = All(k.Batteries, v => v < 0.5); return true;
            case FuelPumpsOn: value = AllPumps(on: true); return true;
            case FuelPumpsOff: value = AllPumps(on: false); return true;
            case PacksOn: value = All(k.Packs, v => v > 0.5); return true;
            case HotAirOn: value = All(k.HotAir, v => v > 0.5); return true;
            case LandingLightsOn: value = AllLanding(on: true); return true;
            case LandingLightsOff: value = AllLanding(on: false); return true;
            case IrsNav: value = k.IrsModeReads.Count == 0 ? double.NaN : All(k.IrsModeReads, v => Math.Abs(v - 1) < 0.5); return true;
            case IrsOff: value = k.IrsModeReads.Count == 0 ? double.NaN : All(k.IrsModeReads, v => v < 0.5); return true;
            case WxrOnAuto:
                value = Both(GetValue(k.WxSystem), GetValue(k.Pws), (sys, pws) => Math.Abs(sys - 1) > 0.5 && Math.Abs(pws - 1) < 0.5);
                return true;
            case WxrPwsOff:
                value = Both(GetValue(k.WxSystem), GetValue(k.Pws), (sys, pws) => Math.Abs(sys - 1) < 0.5 && pws < 0.5);
                return true;
            case SeatBeltsOn:
            {
                double b = GetValue(k.SeatBelts);
                value = double.IsNaN(b) ? double.NaN : IsSeatBelts(b, on: true) ? 1 : 0;
                return true;
            }
            case SeatBeltsOff:
            {
                double b = GetValue(k.SeatBelts);
                value = double.IsNaN(b) ? double.NaN : IsSeatBelts(b, on: false) ? 1 : 0;
                return true;
            }
            case SignsOn:
                // the memo's SIGNS line: seat belts ON and no smoking ON or AUTO (0 On / 1 Auto / 2 Off)
                value = Both(GetValue(SeatBeltsOn), GetValue(k.NoSmoking), (belts, smoking) => belts > 0.5 && smoking < 1.5);
                return true;
            case TaxiMemoOk:
                // read-only check of the takeoff memo the Captain set: spoilers armed, takeoff flaps, signs on
                value = TaxiMemoFrom(GetValue(IniAirbusKeys.SpoilersArmed), GetValue(IniAirbusKeys.Flaps), GetValue(SignsOn));
                return true;
            case LdgFlapsSet:
            {
                double f = GetValue(IniAirbusKeys.Flaps);
                value = double.IsNaN(f) ? double.NaN : IsLandingFlaps(f) ? 1 : 0;
                return true;
            }
            case LdgMemoOk:
                value = Both(GetValue(SeatBeltsOn), GetValue(LdgFlapsSet), (belts, flaps) => belts > 0.5 && flaps > 0.5);
                return true;
            case GearUp: value = Gear(up: true); return true;
            case GearDown: value = Gear(up: false); return true;
            case AntiIceOff: value = All(k.EngAntiIce.Append(k.WingAntiIce), v => v < 0.5); return true;
            case LsOn: value = All(new[] { k.CaptLs, k.FoLs }, v => v > 0.5); return true;
            case LsOff: value = All(new[] { k.CaptLs, k.FoLs }, v => v < 0.5); return true;
            case FdsOn: value = All(new[] { k.CaptFd, k.FoFd }, v => v > 0.5); return true;
            case WipersOff: value = All(k.Wipers, v => v < 0.5); return true;
            case AutobrakeMax:
            {
                double a = GetValue(IniAirbusKeys.Autobrake);
                value = double.IsNaN(a) ? double.NaN : Math.Abs(a - k.AutobrakeMax) < 0.5 ? 1 : 0;
                return true;
            }
            case ApuStartPressable:
            {
                if (k.ApuStartPressRead == null) { value = 1; return true; }
                double pb = GetValue(k.ApuStartPressRead);
                value = double.IsNaN(pb) ? double.NaN : pb < 0.5 ? 1 : 0;
                return true;
            }
        }
        value = double.NaN;
        return false;
    }

    /// <summary>The Taxi memo check: NaN when any input is unread, else 1 only when all three hold.</summary>
    internal static double TaxiMemoFrom(double spoilersArmed, double flaps, double signsOn) =>
        double.IsNaN(spoilersArmed) || double.IsNaN(flaps) || double.IsNaN(signsOn)
            ? double.NaN
            : spoilersArmed > 0.5 && IsTakeoffFlaps(flaps) && signsOn > 0.5 ? 1 : 0;

    private double AnyOn(IEnumerable<string> keys)
    {
        bool anyNaN = false;
        foreach (var key in keys)
        {
            double v = GetValue(key);
            if (v > 0.5) return 1;
            if (double.IsNaN(v)) anyNaN = true;
        }
        return anyNaN ? double.NaN : 0;
    }

    private double AllPumps(bool on)
    {
        bool anyNaN = false;
        foreach (var (key, onV, offV) in Keys.FuelPumps)
        {
            double v = GetValue(key);
            if (double.IsNaN(v)) { anyNaN = true; continue; }
            if (Math.Abs(v - (on ? onV : offV)) >= 0.5) return 0;
        }
        return anyNaN ? double.NaN : 1;
    }

    private double AllLanding(bool on)
    {
        bool anyNaN = false;
        foreach (var (key, onV, offV, stowedV) in Keys.LandingLights)
        {
            double v = GetValue(key);
            if (double.IsNaN(v)) { anyNaN = true; continue; }
            bool isOn = Math.Abs(v - onV) < 0.5;
            if (on ? !isOn : isOn) return 0;   // "off" = any position but On (Off or Retract)
        }
        return anyNaN ? double.NaN : 1;
    }

    private double Gear(bool up) => GearFrom(GetValue(IniAirbusKeys.GearLever), GetValue(IniAirbusKeys.GearNose),
        GetValue(IniAirbusKeys.GearLeft), GetValue(IniAirbusKeys.GearRight), up);

    // ---- read-back live values ----

    /// <summary>Total fuel in the aircraft's unit (INI_IS_METRIC 1 = kilograms); null when unknown.</summary>
    public string? FuelText()
    {
        double lb = GetValue(IniAirbusKeys.FuelTotalLb), metric = GetValue(IniAirbusKeys.WeightMetric);
        if (double.IsNaN(lb) || double.IsNaN(metric)) return null;
        return AirbusReadbackFormat.FuelQuantity(lb / 2.20462262, pounds: metric < 0.5);
    }

    /// <summary>The After Start "Anti-ice: SET" read-back: the A320-family words on two engines, the
    /// four-engine words (each engine named only when they differ) on the A340; null while any is unread.</summary>
    public string? AntiIceText()
    {
        var e = Keys.EngAntiIce.Select(GetValue).ToArray();
        double wing = GetValue(Keys.WingAntiIce);
        return AirbusReadbackFormat.AntiIce(e[0], e[1], wing);
    }

    /// <summary>The autobrake level in words ("off", "low", "medium", "max"), the A340's raw values mapped
    /// to the read-back scale; null when unread or unknown.</summary>
    public string? AutobrakeText()
    {
        double raw = GetValue(IniAirbusKeys.Autobrake);
        if (double.IsNaN(raw)) return null;
        if (Keys.AutobrakeRawToLevel is { } map)
            return map.TryGetValue((int)Math.Round(raw), out int level) ? AirbusReadbackFormat.Autobrake(level) : null;
        return AirbusReadbackFormat.Autobrake(raw);
    }

    /// <summary>Captain's altimeter: "standard" from the cockpit's own baro mode (2/3 = STD, the A300
    /// code base's convention the B key reads), else the QNH in the unit the captain's EFIS shows
    /// (hectopascals when the unit is not readable). Null when unknown.</summary>
    public string? BaroText()
    {
        double mode = GetValue(IniAirbusKeys.BaroMode), mb = GetValue(IniAirbusKeys.BaroMb);
        if (double.IsNaN(mode)) return null;
        if (IniA330Definition.IsStdMode(mode)) return "standard";
        if (double.IsNaN(mb) || mb <= 0) return null;
        bool inHg = Keys.CaptBaroUnit != null && GetValue(Keys.CaptBaroUnit) is var u && !double.IsNaN(u) && u < 0.5;
        return AirbusReadbackFormat.Baro(0, inHg ? mb / 33.8639 : mb, inHg);
    }
}
