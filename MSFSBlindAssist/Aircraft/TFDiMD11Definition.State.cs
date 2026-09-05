using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Composed control state (spec §3.3–3.7): the hook MainForm labels buttons and status rows
/// from, the DC-power gate, lamp-change announcements and press feedback.
/// </summary>
public partial class TFDiMD11Definition
{
    /// <summary>Reads a state L:var (by NAME, as the state block spells it) from the shared cache.</summary>
    private double? ReadStateVar(string stateVar) => _sim?.GetCachedVariableValue(KeyFor(stateVar));

    /// <summary>
    /// True while the annunciators have power: a live main bus AND DC bus 1 not annunciated off.
    /// The second half is what makes battery-only read as unpowered — see
    /// <see cref="Md11ControlState.IsPowered"/> for the measurements behind it.
    /// </summary>
    public bool IsDcPowered() => Md11ControlState.IsPowered(
        _sim?.GetCachedVariableValue(DcPowerKey),
        _sim?.GetCachedVariableValue(Dc1BusOffKey));

    /// <summary>
    /// MainForm's label seam. No opinion before <see cref="Attach"/> (no cache to read) and for
    /// controls without a state block (momentary buttons, knobs, switches, read-outs).
    /// </summary>
    public override bool TryDescribeControlState(string varKey, out string stateText)
    {
        stateText = "";
        if (_sim == null) return false;
        if (!_byNodeId.TryGetValue(varKey, out var c) || c.State == null) return false;

        var text = Md11ControlState.Compose(c.State, ReadStateVar, IsDcPowered());
        if (text == null) return false;
        stateText = text;
        return true;
    }

    private readonly Md11AnnouncementGate _gate = new();

    /// <summary>
    /// The UI thread's context, captured by <see cref="SetControl"/> the first time it runs (every
    /// caller is a WinForms event handler). The gate's plain dictionaries are also mutated by
    /// <see cref="HandleLampUpdate"/> on the event-batch consumer thread, so
    /// <see cref="PressFeedbackAsync"/>'s tail — which runs on a thread-pool thread after its
    /// ConfigureAwait(false) delays — must hop back via <see cref="OnUiThread"/> rather than touch
    /// the gate or the announcer directly.
    /// </summary>
    private SynchronizationContext? _uiContext;

    /// <summary>Lamps ride the 1 Hz batch; a press's effect is visible within this. Guarded presses add the guard settle.</summary>
    private const int PressSettleMs = 1200;
    private const int GuardedPressExtraMs = 500;

    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread when one was captured (the announcer and the
    /// gate's dictionaries are UI-thread objects — HandleLampUpdate mutates the same gate on the
    /// event-batch consumer thread), else inline as a last resort.
    /// </summary>
    private void OnUiThread(Action action)
    {
        var ctx = _uiContext;
        if (ctx != null) ctx.Post(_ => action(), null);
        else action();
    }

    /// <summary>How long a lamp going DARK waits before it is allowed to speak — see <see cref="DeferDarkTransitionAsync"/>.</summary>
    private const int DarkSettleMs = 1500;

    /// <summary>
    /// Bumped by <see cref="ResetAnnouncementBaselines"/> so a deferred dark transition scheduled
    /// before an aircraft switch or a reconnect cannot speak the old session's state afterwards.
    /// </summary>
    private int _announceGeneration;

    /// <summary>
    /// A lamp value arrived. Baseline-first (the first sight of every lamp is silent — connecting
    /// mid-flight must not narrate the cockpit), blink-guarded, then spoken as its OWNER's composed
    /// state or, for a standalone light, as its own lit/dark word. Always consumed: the generic
    /// announce path never sees an MD-11 lamp, so Ctrl+M works through MainForm's Suppressed wrap.
    ///
    /// A lamp going DARK is the one case that never speaks on the spot — see
    /// <see cref="DeferDarkTransitionAsync"/>.
    /// </summary>
    private void HandleLampUpdate(Md11Control lamp, string varName, double value, ScreenReaderAnnouncer announcer)
    {
        bool firstSight = !_lampLastVal.TryGetValue(varName, out var last);
        bool unchanged = !firstSight && Math.Abs(last - value) < LampEpsilon;
        if (SuppressAnnunciatorFlap(varName, value)) return;   // blinking: quiet until it settles
        if (firstSight || unchanged) return;

        bool lit = value > Md11ControlState.LitThreshold;
        if (!lit)
        {
            // Going dark while the aircraft is already unpowered is not news — it is the whole
            // panel losing power, and one sentence per control is exactly the narration §3.7
            // forbids. Otherwise let it settle first: the DC-bus-1 lamp the gate reads rides a
            // different SimConnect batch from these, so "still powered" can be a beat stale.
            if (!IsDcPowered()) return;
            _ = DeferDarkTransitionAsync(lamp, announcer);
            return;
        }

        long now = Environment.TickCount64;
        if (_lampOwners.TryGetValue(lamp.NodeId, out var owners))
        {
            foreach (var owner in owners)
            {
                var text = Md11ControlState.Compose(owner.State, ReadStateVar, IsDcPowered());
                if (text != null && _gate.ShouldSpeakBackground(owner.NodeId, text, now))
                    announcer.Announce($"{owner.DisplayLabel}: {text}");
            }
            return;
        }

        string state = StandaloneWord(lamp, lit: true);
        if (string.IsNullOrEmpty(state)) return;
        if (_gate.ShouldSpeakBackground(lamp.NodeId, state, now))
            announcer.Announce($"{lamp.DisplayLabel}: {state}");
    }

    /// <summary>A standalone light's own word for its current value: its lit legend, or the state block's dark meaning.</summary>
    private static string StandaloneWord(Md11Control lamp, bool lit)
        => lit ? (lamp.State?.Lamps.FirstOrDefault()?.Lit ?? "on") : (lamp.State?.Dark ?? "off");

    /// <summary>
    /// A lamp went out. Wait <see cref="DarkSettleMs"/>, then decide from what is true THEN.
    ///
    /// Two reasons, both measured. POWER: the 528 continuous lamps ride two SimConnect batches
    /// sorted by name, and <c>MD11_OVHD_ELEC_DC1_BUS_OFF_LT</c> (which the gate reads) sits in
    /// batch 1 while the <c>MD11_OVHD_HYD_*</c> and <c>MD11_OVHD_PNEU_*</c> lamps sit in batch 2 —
    /// so in the normal shutdown order (external power off, battery still on) a batch-2 lamp can
    /// go dark while the gate has not yet learned the DC busses died, and every OFF-legend button
    /// on them would announce its dark meaning: "Tank 1 Fuel Pumps: On", "Pack 1: On", … one per
    /// control. STATE: a control with several legends may still have one lit, and what is worth
    /// speaking is what it reads now, not which lamp moved.
    ///
    /// So the state is re-composed here, on the UI thread (the gate and the announcer are
    /// UI-thread objects), and <see cref="Md11AnnouncementGate.SpeakDarkTransition"/> decides.
    /// LIT transitions are unaffected and still speak at once — a light coming on is news.
    /// </summary>
    private async Task DeferDarkTransitionAsync(Md11Control lamp, ScreenReaderAnnouncer announcer)
    {
        int generation = _announceGeneration;
        try
        {
            await Task.Delay(DarkSettleMs).ConfigureAwait(false);
            OnUiThread(() =>
            {
                try
                {
                    if (generation != _announceGeneration) return;   // aircraft switch / reconnect

                    long now = Environment.TickCount64;
                    bool powered = IsDcPowered();

                    if (_lampOwners.TryGetValue(lamp.NodeId, out var owners))
                    {
                        foreach (var owner in owners)
                        {
                            var text = _gate.SpeakDarkTransition(owner.NodeId,
                                Md11ControlState.Compose(owner.State, ReadStateVar, powered), powered, now);
                            if (text != null) announcer.Announce($"{owner.DisplayLabel}: {text}");
                        }
                        return;
                    }

                    // Standalone: whatever the lamp reads now, which may be lit again.
                    bool litNow = _lampLastVal.TryGetValue(lamp.NodeId, out var v) && v > Md11ControlState.LitThreshold;
                    var word = _gate.SpeakDarkTransition(lamp.NodeId, StandaloneWord(lamp, litNow), powered, now);
                    if (word != null) announcer.Announce($"{lamp.DisplayLabel}: {word}");
                }
                catch (Exception ex)
                {
                    Log.Debug("MD11", $"Deferred dark transition (UI-thread tail) for {lamp.NodeId} threw: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Log.Debug("MD11", $"Deferred dark transition for {lamp.NodeId} threw: {ex.Message}");
        }
    }

    /// <summary>
    /// Press feedback (spec §3.6): after the lamps and latch settle, speak the resulting state
    /// once, queued — always, so an inert press (engines off, AUTO mode) tells the pilot the
    /// unchanged state rather than nothing. Seeds the dedup so the press's own lamp echo is quiet.
    /// </summary>
    private async Task PressFeedbackAsync(Md11Control c, SimConnectManager sim, ScreenReaderAnnouncer announcer, bool guarded)
    {
        try
        {
            await Task.Delay(PressSettleMs + (guarded ? GuardedPressExtraMs : 0)).ConfigureAwait(false);
            if (c.State?.Latch != null)
            {
                sim.RequestVariable(c.NodeId, forceUpdate: true);
                await Task.Delay(300).ConfigureAwait(false);
            }

            // The delays above leave this on a thread-pool thread. Compose + feedback + announce
            // must run on the UI thread: the gate's dictionaries are also mutated by
            // HandleLampUpdate on the event-batch consumer thread (a same-thread invariant, not a
            // locked one), and ScreenReaderAnnouncer.Announce is unreliable off the UI thread.
            OnUiThread(() =>
            {
                try
                {
                    var text = Md11ControlState.Compose(c.State, ReadStateVar, IsDcPowered());
                    if (text == null) return;
                    announcer.Announce($"{c.DisplayLabel}: {_gate.Feedback(c.NodeId, text)}");
                }
                catch (Exception ex)
                {
                    Log.Debug("MD11", $"Press feedback (UI-thread tail) for {c.NodeId} threw: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Log.Debug("MD11", $"Press feedback for {c.NodeId} threw: {ex.Message}");
        }
    }

    /// <summary>A guard has no feedback sentence (the reader spoke the button); its label refreshes from a forced read.</summary>
    private async Task GuardRefreshAsync(Md11Control guard, SimConnectManager sim)
    {
        try
        {
            await Task.Delay(400).ConfigureAwait(false);
            sim.RequestVariable(guard.NodeId, forceUpdate: true);
        }
        catch (Exception ex)
        {
            Log.Debug("MD11", $"Guard refresh for {guard.NodeId} threw: {ex.Message}");
        }
    }

    public override void ResetAnnouncementBaselines()
    {
        base.ResetAnnouncementBaselines();
        _lampLastVal.Clear();
        _lampChangeTicks.Clear();
        _gate.Reset();
        _announceGeneration++;   // drops any dark transition still waiting out its settle
    }
}
