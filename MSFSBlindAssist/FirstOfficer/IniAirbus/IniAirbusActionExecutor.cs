using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.FirstOfficer.IniAirbus;

/// <summary>
/// Drives the iniBuilds A330 / A321LR / A340-300 for the First Officer through the definition's own panel write
/// path, <see cref="IniA330Definition.HandleUIVariableSet"/> — the very call a panel combo makes — so
/// the First Officer and the panels can never disagree: the A330's input-event positions go through
/// <c>A330ControlMap.InputFor</c>, its momentary keys through their <c>_Push</c>/<c>_Release</c>
/// actions, the A321's controls through their direct L:var writes, and every calculator string through
/// the definition's <c>{seq} 0 *</c> uniquifier. A suppressed announcer keeps the First Officer's own
/// step narration the single voice.
///
/// Before each write the executor asks the definition how the write travels
/// (<see cref="IniA330Definition.FoWriteRoute"/>) and REFUSES one that cannot land, so the flow says
/// the step failed instead of reporting success: a key that is not a writable control (never an L:var
/// fallback — a key the definition does not claim is a mapping bug), a calculator write while the
/// MobiFlight path is down, an input event this aircraft does not have, and a toggling key whose state
/// is unread (a press there could flip it the wrong way).
///
/// Pseudo-keys: <see cref="LandingLightsKey"/> (both landing lights, the airframe's own values) and
/// <c>ECAM_PAGE_&lt;page&gt;</c> (a system-display page; left alone when that page already shows,
/// because a second press of a selected A330 ECP key returns the display to automatic).
/// </summary>
public sealed class IniAirbusActionExecutor : IFoActionExecutor
{
    private const int WriteSpacingMs = 200;

    public const string LandingLightsKey = "LANDING_LIGHTS";
    public const string EcamPagePrefix = "ECAM_PAGE_";

    /// <summary>Landing light commands for <see cref="LandingLightsKey"/>.</summary>
    public const int LandingOn = 1, LandingOff = 0, LandingStowed = 2;

    private readonly IniAirbusKeys _keys;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SimConnectManager? _sc;
    private IniA330Definition? _def;
    private ScreenReaderAnnouncer? _announcer;
    private DateTime _lastWriteUtc = DateTime.MinValue;

    public IniAirbusActionExecutor(IniAirbusKeys keys) => _keys = keys;

    public IniAirbusKeys Keys => _keys;

    public void SetSimConnect(SimConnectManager? sc) => _sc = sc;
    public void SetDefinition(IniA330Definition def) => _def = def;
    public void SetAnnouncer(ScreenReaderAnnouncer a) => _announcer = a;

    public bool IsAvailable => _sc is { IsConnected: true } && _def != null && _announcer != null;

    public async Task WaitForDispatchDrainAsync()
    {
        await _gate.WaitAsync();
        _gate.Release();
    }

    public async Task<bool> ExecuteStepAsync(IFlowStepDispatch step)
    {
        switch (step.ActionType)
        {
            case FlowStepActionType.SetSwitch:
                return step.EventName != null && await DispatchAsync(step.EventName, step.TargetValue ?? 1);
            case FlowStepActionType.SetSwitchMultiple:
                await _gate.WaitAsync();
                try
                {
                    bool ok = true;
                    foreach (var (ev, tv) in step.MultiActions)
                        ok &= await DispatchCoreAsync(ev, tv ?? 1);
                    return ok;
                }
                finally { _gate.Release(); }
            default:
                return false;
        }
    }

    /// <summary>One write by key (the checklist CheckAction path).</summary>
    public Task<bool> Set(string key, int value) => DispatchAsync(key, value);

    public Task<bool> SetSeatbeltSign(bool on) => DispatchAsync(_keys.SeatBelts, on ? _keys.SeatBeltsOn : _keys.SeatBeltsOff);
    public Task<bool> SetLandingLights(int command) => DispatchAsync(LandingLightsKey, command);
    public Task<bool> SetNoseLight(int position) => DispatchAsync(_keys.NoseLight, position);

    private async Task<bool> DispatchAsync(string key, int value)
    {
        await _gate.WaitAsync();
        try { return await DispatchCoreAsync(key, value); }
        finally { _gate.Release(); }
    }

    // Must be called inside _gate.
    private async Task<bool> DispatchCoreAsync(string key, int value)
    {
        if (key == LandingLightsKey)
        {
            bool ok = true;
            foreach (var (k, v) in LandingLightsPlan(_keys, value))
                ok &= await WriteCoreAsync(k, v);
            return ok;
        }
        if (key.StartsWith(EcamPagePrefix, StringComparison.Ordinal))
        {
            string page = key[EcamPagePrefix.Length..];
            if (!IniAirbusKeys.EcamPageNumber.TryGetValue(page, out int number)) return false;
            // already showing: a second ECP press would return the display to automatic
            if (_sc?.GetCachedVariableValue(_keys.EcamPageRead) is double shown && Math.Abs(shown - number) < 0.5) return true;
            return await WriteCoreAsync(_keys.EcamKey(page), 1);
        }
        return await WriteCoreAsync(key, value);
    }

    /// <summary>The (key, value) writes a landing light command makes on this airframe.</summary>
    public static IReadOnlyList<(string Key, int Value)> LandingLightsPlan(IniAirbusKeys keys, int command) =>
        keys.LandingLights.Select(l => (l.Key, command switch
        {
            LandingOn => l.On,
            LandingStowed => l.Stowed,
            _ => l.Off,
        })).ToList();

    private async Task<bool> WriteCoreAsync(string key, int value)
    {
        var sc = _sc;
        if (sc is not { IsConnected: true } || _def == null || _announcer == null) return false;
        await PaceAsync();
        // again after the pacing wait: a closed window (an aircraft switch) detaches the executor meanwhile
        sc = _sc;
        if (sc is not { IsConnected: true } || _def == null || _announcer == null) return false;

        var vars = _def.GetVariables();
        var route = _def.FoWriteRoute(key);
        string? refusal = Refusal(key, vars.ContainsKey(key), route, sc.CalcWriteCanLand,
            ev => sc.HasInputEvent(ev), sc.GetCachedVariableValue(key));
        if (refusal != null)
        {
            Log.Warn("fo_ini_airbus", $"{_keys.Airframe}: refused write {key}={value}: {refusal}");
            _lastWriteUtc = DateTime.UtcNow;
            return false;
        }

        bool prior = _announcer.Suppressed;
        _announcer.Suppressed = true;
        bool ok;
        try { ok = _def.HandleUIVariableSet(key, value, vars[key], sc, _announcer); }
        finally { _announcer.Suppressed = prior; }
        _lastWriteUtc = DateTime.UtcNow;
        Log.Debug("fo_ini_airbus", $"{_keys.Airframe}: {key}={value} -> {(ok ? "sent" : "not claimed")}");
        // A held momentary key (the fire and T.O CONFIG tests): the definition releases it itself after
        // its hold; the step lasts as long, so the pilot hears the test while the step is on.
        if (ok && route!.Value.HoldMs > 0) await Task.Delay(route.Value.HoldMs);
        return ok;
    }

    /// <summary>
    /// Why a write must not be sent, or null when it may. Pure: the definition's route, whether the
    /// key is registered, the calculator path, the input events this aircraft has and the key's cached
    /// state are passed in (IniAirbusFoExecutorTests).
    /// </summary>
    internal static string? Refusal(string key, bool registered,
        (bool Calc, string? InputEvent, bool Toggle, int HoldMs)? route,
        bool calcCanLand, Func<string, bool> hasInputEvent, double? cached)
    {
        if (!registered) return "not a registered control";
        if (route is not { } r) return "not a control the First Officer may write";
        if (r.Calc && !calcCanLand) return "the MobiFlight calculator path is not available";
        if (r.InputEvent != null && !hasInputEvent(r.InputEvent)) return $"this aircraft has no input event {r.InputEvent}";
        if (r.Toggle && cached is null) return "a toggling key whose state is not read yet";
        return null;
    }

    private async Task PaceAsync()
    {
        var since = DateTime.UtcNow - _lastWriteUtc;
        var gap = TimeSpan.FromMilliseconds(WriteSpacingMs);
        if (since < gap) await Task.Delay(gap - since);
    }

    // ---- APU start (BS_APU / AL_APU checklist lines, FO-11) ----

    public const int ApuAvailTimeoutMs = 180_000;
    private const int ApuPollMs = 1000;
    private const int ApuMasterToStartMs = 3000;

    public static bool IsApuAvailable(double lamp) => lamp > 0.5;

    /// <summary>Master ON, dwell, START, then hold the tick open until AVAIL lights (FO-11), as the flow does.</summary>
    public async Task<bool> StartApuAsync(IniAirbusStateEvaluator s)
    {
        bool ok = true;
        if (!s.IsOn(_keys.ApuMaster)) ok &= await Set(_keys.ApuMaster, 1);
        if (!IsApuAvailable(s.GetValue(_keys.ApuAvailLamp)))
        {
            await Task.Delay(ApuMasterToStartMs);
            // the A321's START is a press flag pressed only from 0 (it reads 1 while the APU runs): over a 1,
            // or unread, the Captain starts the APU and the tick waits for AVAIL as before
            if (s.GetValue(IniAirbusStateEvaluator.ApuStartPressable) > 0.5) ok &= await Set(_keys.ApuStart, 1);
            else _announcer?.Announce("Start the APU.");
        }
        if (!ok) return false;
        for (int waited = 0; waited < ApuAvailTimeoutMs; waited += ApuPollMs)
        {
            if (IsApuAvailable(s.GetValue(_keys.ApuAvailLamp))) return true;
            await Task.Delay(ApuPollMs);
        }
        return IsApuAvailable(s.GetValue(_keys.ApuAvailLamp));
    }
}
