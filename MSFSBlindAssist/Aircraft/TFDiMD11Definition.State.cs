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

    /// <summary>True while the stock DC bus voltage says the annunciators have power (spec §3.4).</summary>
    public bool IsDcPowered() => Md11ControlState.IsPowered(_sim?.GetCachedVariableValue(DcPowerKey));

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

    /// <summary>Lamps ride the 1 Hz batch; a press's effect is visible within this. Guarded presses add the guard settle.</summary>
    private const int PressSettleMs = 1200;
    private const int GuardedPressExtraMs = 500;

    /// <summary>
    /// A lamp value arrived. Baseline-first (the first sight of every lamp is silent — connecting
    /// mid-flight must not narrate the cockpit), blink-guarded, then spoken as its OWNER's composed
    /// state or, for a standalone light, as its own lit/dark word. Always consumed: the generic
    /// announce path never sees an MD-11 lamp, so Ctrl+M works through MainForm's Suppressed wrap.
    /// </summary>
    private void HandleLampUpdate(Md11Control lamp, string varName, double value, ScreenReaderAnnouncer announcer)
    {
        bool firstSight = !_lampLastVal.TryGetValue(varName, out var last);
        bool unchanged = !firstSight && Math.Abs(last - value) < 0.0001;
        if (SuppressAnnunciatorFlap(varName, value)) return;   // blinking: quiet until it settles
        if (firstSight || unchanged) return;

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

        // Standalone light. A lamp going dark while the aircraft is unpowered is not news —
        // it is the whole panel losing power, and every bus light would otherwise say "Powered".
        bool lit = value > Md11ControlState.LitThreshold;
        if (!lit && !IsDcPowered()) return;
        var self = lamp.State?.Lamps.FirstOrDefault();
        string state = lit ? (self?.Lit ?? "on") : (lamp.State?.Dark ?? "off");
        if (string.IsNullOrEmpty(state)) return;
        if (_gate.ShouldSpeakBackground(lamp.NodeId, state, now))
            announcer.Announce($"{lamp.DisplayLabel}: {state}");
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
            var text = Md11ControlState.Compose(c.State, ReadStateVar, IsDcPowered());
            if (text == null) return;
            announcer.Announce($"{c.DisplayLabel}: {_gate.Feedback(c.NodeId, text)}");
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
    }
}
