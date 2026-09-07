using System.Collections.Concurrent;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// Drives a detented MD-11 control to an absolute target position.
///
/// WHY THIS EXISTS. Every multi-position control on the MD-11 is actuated by RELATIVE step
/// events — a knob exposes WHEEL_UP/WHEEL_DOWN, a rotary switch exposes LEFT_BUTTON_DOWN /
/// RIGHT_BUTTON_DOWN (click left side / right side). None of them accept "go to position 3".
/// But MSFSBA's panels are combo boxes: the user picks a target and expects the aircraft to get
/// there. Something has to close that loop, and this is it — read state, step once, re-read,
/// repeat. It is the same shape as <c>PMDGNG3DataManager.WalkSelectorClosedLoop</c>, which
/// exists for the same reason on the PMDG NG3's detented rotaries.
///
/// WHY IT SELF-CALIBRATES. Which direction WHEEL_UP turns a given knob is not stated anywhere
/// in TFDi's exported behaviours or docs — the ModelBehaviorDefs give us the event ids and
/// nothing about their sign. Guessing costs a control that walks confidently the wrong way to
/// its end stop, silently, on an aircraft nobody can see. So the walker treats polarity as
/// UNKNOWN, assumes the conventional mapping (WHEEL_UP / left-click = increase), and watches
/// which way the state var actually moved after the first step. A step the wrong way flips the
/// sign, and the flip is persisted through <see cref="LoadPolarity"/> / <see cref="SavePolarity"/>
/// (the definition wires them to settings), so each control pays for its calibration once, ever.
///
/// HOW A STEP IS READ BACK (2026-09-06). A var with its own data definition is read through
/// <see cref="SimConnectManager.ReadFreshAsync"/>, which completes on the delivery itself, so a
/// step is confirmed the moment the aircraft reports it: fire, then poll every <see cref="PollMs"/>
/// until the position index has CHANGED and the value is settled — exactly on a point detent, or
/// two consecutive reads agree (an ANIM_LAG var travels for up to a second) — or
/// <see cref="StepCapMs"/> elapses, which is the only way to conclude "no movement". The previous
/// protocol slept a fixed interval and read the cache; it cost 300 ms per read, read stale values,
/// called real movement "no movement", and mis-learned polarity (the autobrake flipped its learned
/// sign twice in one session). That protocol survives, unchanged, for a var that answers only with
/// the next 1 Hz delivery — batch-covered, or on its own PERIOD.SECOND subscription
/// (<see cref="SimConnectManager.SupportsFreshReads"/> decides) — and for the analog walk. The flap
/// lever and speedbrake stream on their own SIM_FRAME subscription, so their fresh read is the
/// cache, at most a frame old. A walk also waits out <see cref="ClickSettleMs"/> after the last
/// click recorded on its node before its first read, because a walk cancelled by fast arrowing can
/// have a click still landing.
///
/// A FIRST "no movement" AT AN END STOP (fresh protocol) is ambiguous — an inhibited control, or a
/// wrong polarity guess whose asked direction is the stop itself — so the walker clicks the OTHER
/// event once: toward the target means the polarity was wrong (flip, continue); nothing either way
/// means inhibited (give up). A stall at mid-range can only be a dropped or refused click, because a
/// wrong guess would have MOVED the control, and probing there would actuate the control the wrong
/// way (on the engine fire handles the walked axis is the agent discharge) — so it gives up at once,
/// and so does a second no-movement in the same walk.
///
/// The walk is bounded and always terminates: <see cref="MaxSteps"/> caps the step count, and a
/// control that will not move breaks out rather than hammering CEVENT — which TFDi explicitly asks
/// us not to overuse.
/// </summary>
public static class Md11SelectorWalker
{
    /// <summary>
    /// Step budget. The widest control in the map is well under a dozen detents, so this is
    /// generous headroom; it exists to guarantee termination when a control refuses to move
    /// (unpowered bus, guarded switch, hydraulics off), never as a normal operating limit.
    /// </summary>
    private const int MaxSteps = 24;

    /// <summary>Legacy protocol and analog walk: how long to wait after a step before the first cache poll.</summary>
    private const int SettleMs = 90;

    /// <summary>Fresh protocol: the interval between reads after a step.</summary>
    internal const int PollMs = 80;

    /// <summary>Fresh protocol: no position change by then means no movement (ANIM_LAG is at most 1000 ms).</summary>
    internal const int StepCapMs = 1200;

    /// <summary>Fresh protocol: a click this recent on the same node may still be landing.</summary>
    internal const int ClickSettleMs = 1000;

    /// <summary>One awaited delivery; a batch-covered var answers within one 1 Hz period.</summary>
    internal const int FreshReadTimeoutMs = 1200;

    /// <summary>Legacy protocol: poll interval and cap for the cache-poll settle wait (exceeds the largest ANIM_LAG).</summary>
    private const int SettlePollMs = 150;
    private const int SettleCapMs = 1650;

    /// <summary>Two reads this close are the same value — the animation has stopped.</summary>
    private const double SettledTolerance = 0.05;

    /// <summary>A value this close to a point detent is not travelling.</summary>
    private const double OnDetentTolerance = 0.01;

    /// <summary>Values are floats from the sim; compare with a tolerance, never with ==.</summary>
    private const double Epsilon = 0.5;

    /// <summary>
    /// Learned step polarity per node id: true = the conventional mapping (WHEEL_UP / left-click
    /// increases the state value), false = inverted. Seeded from <see cref="LoadPolarity"/> on
    /// first use; concurrent because a walk runs off the UI thread.
    /// </summary>
    private static readonly ConcurrentDictionary<string, bool> Polarity = new();

    /// <summary>When each node last had a step fired at it (monotonic ms) — see <see cref="ClickSettleMs"/>.</summary>
    private static readonly ConcurrentDictionary<string, long> LastClick = new();

    /// <summary>Persisted polarity lookup: true = conventional, false = inverted, null = never learned. Wired by the definition.</summary>
    internal static Func<string, bool?>? LoadPolarity { get; set; }

    /// <summary>Persists a learned polarity (true = conventional). Wired by the definition.</summary>
    internal static Action<string, bool>? SavePolarity { get; set; }

    private static bool ResolvePolarity(string nodeId)
        => Polarity.GetOrAdd(nodeId, id => LoadPolarity?.Invoke(id) ?? true);

    private static void SetPolarity(string nodeId, bool conventional)
    {
        Polarity[nodeId] = conventional;
        try { SavePolarity?.Invoke(nodeId, conventional); }
        catch (Exception ex) { Log.Debug("MD11", $"{nodeId}: could not persist step polarity: {ex.Message}"); }
    }

    /// <summary>
    /// Walks <paramref name="control"/> to <paramref name="targetValue"/>.
    /// </summary>
    /// <param name="varKey">The MSFSBA variable key the control's state is cached under (the node id).</param>
    /// <returns>True if the control landed on the target; false on budget exhaustion or a stuck control.</returns>
    public static Task<bool> WalkAsync(
        Md11Control control,
        double targetValue,
        string varKey,
        SimConnectManager sim,
        Md11EventBus bus,
        CancellationToken ct = default)
        => WalkCoreAsync(control, targetValue, Md11WalkIo.ForSim(sim, bus, varKey), ct);

    /// <summary>The step protocol proper, over an IO seam — see the class remarks.</summary>
    internal static async Task<bool> WalkCoreAsync(
        Md11Control control,
        double targetValue,
        Md11WalkIo io,
        CancellationToken ct = default)
    {
        var (incEvent, decEvent) = StepEvents(control);
        if (incEvent == null || decEvent == null)
        {
            Log.Debug("MD11", $"{control.NodeId}: no step events — cannot walk.");
            return false;
        }

        var ordered = OrderedValues(control);
        if (ordered.Count == 0) return false;

        var node = control.NodeId;
        var targetIdx = PositionIndex(control, ordered, targetValue);
        var noMovement = 0;

        await SettleAfterRecentClickAsync(node, io, ct).ConfigureAwait(false);

        for (var step = 0; step < MaxSteps; step++)
        {
            // Superseded by a newer selection (the user arrowed on in the combo) — stop now rather
            // than keep force-requesting the state var. Throws so SafeWalk swallows it silently: a
            // cancelled walk is not a failure to announce.
            ct.ThrowIfCancellationRequested();

            var current = await ReadSettledAsync(control, ordered, io, ct).ConfigureAwait(false);
            if (current == null)
            {
                Log.Debug("MD11", $"{node}: state var {control.StateVar} unreadable — aborting walk.");
                return false;
            }

            var currentIdx = PositionIndex(control, ordered, current.Value);
            if (currentIdx == targetIdx) return true;

            var wantUp = targetIdx > currentIdx;
            var conventional = ResolvePolarity(node);
            // conventional: inc event raises the value. Inverted: inc event lowers it.
            var eventId = (wantUp == conventional) ? incEvent.Value : decEvent.Value;

            var after = await StepAndReadAsync(control, ordered, io, eventId, currentIdx, ct).ConfigureAwait(false);
            if (after == null) return false;
            var afterIdx = PositionIndex(control, ordered, after.Value);

            if (afterIdx == currentIdx)
            {
                noMovement++;
                // A stall at MID-RANGE can only be a dropped or refused click: a wrong polarity guess
                // would have MOVED the control, which the direction test below catches. Probing the
                // other event there would actuate the control the wrong way — on the engine fire
                // handles the walked axis IS the agent discharge — so give up at once, as the legacy
                // protocol always did. Only at an END STOP is a stall ambiguous (inhibited, or the
                // guess is wrong and the asked direction is the stop itself), and there the other
                // event either moves toward the target — polarity learned — or cannot move at all.
                bool atEndStop = currentIdx == 0 || currentIdx == ordered.Count - 1;
                if (!io.FreshReads || !atEndStop || noMovement > 1)
                {
                    Log.Debug("MD11",
                        $"{node}: step produced no movement at value {current.Value} " +
                        $"(target {targetValue}) — control may be inhibited, or a click was dropped.");
                    return false;
                }
                var otherId = eventId == incEvent.Value ? decEvent.Value : incEvent.Value;
                var probe = await StepAndReadAsync(control, ordered, io, otherId, currentIdx, ct).ConfigureAwait(false);
                if (probe == null) return false;
                var probeIdx = PositionIndex(control, ordered, probe.Value);
                if (probeIdx == currentIdx)
                {
                    Log.Debug("MD11",
                        $"{node}: no movement in either direction at value {current.Value} " +
                        $"(target {targetValue}) — control inhibited or unpowered.");
                    return false;
                }

                var probeUp = probeIdx > currentIdx;
                if (probeUp == wantUp)
                {
                    SetPolarity(node, !conventional);
                    Log.Info("MD11",
                        $"{node}: step polarity calibrated to {(!conventional ? "INVERTED" : "conventional")} " +
                        $"(the other event moved {current.Value} -> {probe.Value} toward {targetValue} from an end stop).");
                }
                else
                {
                    Log.Debug("MD11", $"{node}: the other event moved {current.Value} -> {probe.Value} AWAY from {targetValue} from an end stop — leaving polarity alone.");
                }
                continue;
            }

            // Polarity is a DIRECTION: judge the step by which way it went, never by distance — a
            // click that lands twice can overshoot to a spot no nearer the target than where it
            // started, and a distance test would flip the sign on a step that went the right way.
            var movedUp = afterIdx > currentIdx;
            if (movedUp != wantUp)
            {
                var flipped = !conventional;
                SetPolarity(node, flipped);
                Log.Info("MD11",
                    $"{node}: step polarity calibrated to {(flipped ? "INVERTED" : "conventional")} " +
                    $"(value went {current.Value} -> {after.Value} while walking toward {targetValue}).");
            }
        }

        Log.Debug("MD11", $"{node}: walk to {targetValue} exhausted {MaxSteps} steps.");
        return false;
    }

    /// <summary>
    /// A walk cancelled mid-flight by a newer selection may have a click still landing (ANIM_LAG
    /// up to a second). Reading through that is how a polarity gets mis-learned, so wait out the
    /// remainder of the settle window before the first read. Fresh protocol only.
    /// </summary>
    private static async Task SettleAfterRecentClickAsync(string nodeId, Md11WalkIo io, CancellationToken ct)
    {
        if (!io.FreshReads || !LastClick.TryGetValue(nodeId, out var last)) return;
        var remaining = ClickSettleMs - (io.Now() - last);
        if (remaining > 0) await io.Delay((int)remaining, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The current position: one fresh delivery, plus a settle poll only when the value is not
    /// resting on a point detent (a var caught mid-travel). Legacy protocol for batch-covered vars.
    /// </summary>
    private static async Task<double?> ReadSettledAsync(Md11Control control, List<double> ordered, Md11WalkIo io, CancellationToken ct)
    {
        if (!io.FreshReads) return await LegacyReadAsync(io, ct).ConfigureAwait(false);

        var v = await io.ReadFresh(ct).ConfigureAwait(false) ?? io.ReadCached();
        if (v == null) return null;
        if (OnDetent(control, ordered, v.Value)) return v;

        var prev = v.Value;
        for (var waited = 0; waited < SettleCapMs; waited += PollMs)
        {
            await io.Delay(PollMs, ct).ConfigureAwait(false);
            var next = await io.ReadFresh(ct).ConfigureAwait(false) ?? prev;
            if (Math.Abs(next - prev) < SettledTolerance) return next;
            prev = next;
        }
        return prev;
    }

    /// <summary>
    /// Fires one step and reads the control back. Fresh protocol: poll until the position index
    /// has changed from <paramref name="beforeIdx"/> and the value is settled, or the cap elapses
    /// (then the last read, which the caller reads as "no movement"). Legacy protocol: the old
    /// settle wait and cache poll.
    /// </summary>
    private static async Task<double?> StepAndReadAsync(Md11Control control, List<double> ordered, Md11WalkIo io,
        int eventId, int beforeIdx, CancellationToken ct)
    {
        io.Fire(eventId);
        LastClick[control.NodeId] = io.Now();

        if (!io.FreshReads)
        {
            await io.Delay(SettleMs, ct).ConfigureAwait(false);
            return await LegacyReadAsync(io, ct).ConfigureAwait(false);
        }

        var deadline = io.Now() + StepCapMs;
        double? prev = null, last = null;
        while (true)
        {
            await io.Delay(PollMs, ct).ConfigureAwait(false);
            var v = await io.ReadFresh(ct).ConfigureAwait(false);
            if (v != null)
            {
                last = v;
                if (PositionIndex(control, ordered, v.Value) != beforeIdx
                    && (OnDetent(control, ordered, v.Value)
                        || (prev != null && Math.Abs(v.Value - prev.Value) < SettledTolerance)))
                    return v;
                prev = v;
            }
            if (io.Now() >= deadline) return last ?? io.ReadCached();
        }
    }

    /// <summary>
    /// True when <paramref name="value"/> rests exactly on a POINT detent. A range detent (the flap
    /// lever's Dial-A-Flap band) has no single resting value and never vouches for "settled".
    /// </summary>
    internal static bool OnDetent(Md11Control control, List<double> ordered, double value)
    {
        if (control.Detents is { Count: > 0 })
            return control.Detents.Any(d => d.Range is not { Count: 2 } && Math.Abs(d.Value - value) < OnDetentTolerance);
        return ordered.Any(v => Math.Abs(v - value) < OnDetentTolerance);
    }

    /// <summary>
    /// The legacy read: request, sleep, read the cache, until two consecutive reads agree or the
    /// cap elapses. Still the right protocol for a batch-covered var, whose delivery comes at most
    /// once a second whatever we do; the analog walk uses it too.
    /// </summary>
    private static async Task<double?> LegacyReadAsync(Md11WalkIo io, CancellationToken ct)
    {
        double? prev = null;
        for (var waited = 0; waited < SettleCapMs; waited += SettlePollMs)
        {
            ct.ThrowIfCancellationRequested();
            io.RequestRead();
            await io.Delay(SettlePollMs, ct).ConfigureAwait(false);
            var v = io.ReadCached();
            if (v != null && prev != null && Math.Abs(v.Value - prev.Value) < SettledTolerance)
                return v;   // two reads agree → the value has settled
            prev = v;
        }
        return prev;
    }

    private static Task<double?> ReadAsync(string varKey, SimConnectManager sim, CancellationToken ct = default)
        => LegacyReadAsync(Md11WalkIo.ForSim(sim, null, varKey), ct);

    /// <summary>
    /// The (increase, decrease) CEVENT pair, under the CONVENTIONAL assumption that WHEEL_UP and
    /// a left-click step "up". <see cref="WalkCoreAsync"/> verifies that against the aircraft and
    /// inverts if wrong — nothing here is trusted as fact.
    /// </summary>
    private static (int? inc, int? dec) StepEvents(Md11Control c)
    {
        var wheelUp = c.Event("WHEEL_UP");
        var wheelDown = c.Event("WHEEL_DOWN");
        if (wheelUp != null && wheelDown != null) return (wheelUp, wheelDown);

        // Detented rotaries and multi-position switches: clicking the left vs right half of the
        // knob steps in opposite directions.
        var left = c.Event("LEFT_BUTTON_DOWN");
        var right = c.Event("RIGHT_BUTTON_DOWN");
        if (left != null && right != null) return (left, right);

        return (null, null);
    }

    /// <summary>
    /// The control's positions, ascending. Prefers curated detents (which carry the real ordering
    /// including range-valued positions the tooltip parser cannot see) over the tooltip value map.
    /// </summary>
    public static List<double> OrderedValues(Md11Control c)
    {
        if (c.Detents is { Count: > 0 })
            return c.Detents.Select(d => d.Value).OrderBy(v => v).ToList();

        var vals = new List<double>();
        foreach (var k in c.ValueMap.Keys)
            if (double.TryParse(k, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v))
                vals.Add(v);
        vals.Sort();
        return vals;
    }

    /// <summary>
    /// Index of the position <paramref name="value"/> currently sits in.
    ///
    /// Prefers the curated detent test over nearest-value, because nearest-value is WRONG for a
    /// range-valued detent. The flap lever is the case in point: its Dial-A-Flap detent spans
    /// FLAP_RNG 38–65 but is represented by the single value 50, so nearest-value puts everything
    /// from 60 upward (the midpoint of 50 and the next detent, 70) into "Flap 28" — i.e. a handle
    /// sitting in Dial-A-Flap with the thumbwheel toward 25° would read out, and be walked from,
    /// as though it were at 28. The range test resolves it correctly; nearest-value remains the
    /// fallback for controls with no curated detents and for a lever caught mid-travel.
    /// </summary>
    public static int PositionIndex(Md11Control control, List<double> ordered, double value)
    {
        if (control.Detents is { Count: > 0 })
        {
            var hit = control.Detents.FirstOrDefault(d => d.Matches(value));
            if (hit != null)
            {
                var idx = ordered.FindIndex(v => Near(v, hit.Value));
                if (idx >= 0) return idx;
            }
        }
        return NearestIndex(ordered, value);
    }

    /// <summary>
    /// Index of the position nearest <paramref name="value"/>. Nearest rather than exact because
    /// a lever caught mid-travel sits between detents. Prefer <see cref="PositionIndex"/>, which
    /// honours range-valued detents first.
    /// </summary>
    public static int NearestIndex(List<double> ordered, double value)
    {
        var best = 0;
        var bestDist = double.MaxValue;
        for (var i = 0; i < ordered.Count; i++)
        {
            var d = Math.Abs(ordered[i] - value);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    /// <summary>
    /// Walks a CONTINUOUS (non-detented) control to a raw target value — the Dial-A-Flap
    /// thumbwheel being the motivating case.
    ///
    /// Detent-walking one step at a time does not work here. The thumbwheel's raw range is
    /// 0–100 spanning 10°–25°, and nothing tells us how far one wheel click moves it: if a click
    /// is one raw unit, crossing the full range is 100 clicks — four times
    /// <see cref="MaxSteps"/>, and 100 CEVENT writes at a channel TFDi asks us not to overuse.
    ///
    /// So measure instead of assume. One probe click yields a SIGNED delta, which gives both the
    /// step size and the polarity in a single observation; from there the remaining distance is
    /// arithmetic. Fire that many clicks, verify, and allow a couple of correction rounds for
    /// rounding and for clicks the aircraft dropped. Typically ~3 rounds and well under 20 writes
    /// even for a full-range move.
    /// </summary>
    /// <param name="tolerance">Raw units within which the target counts as reached.</param>
    public static async Task<bool> WalkAnalogAsync(
        Md11Control control,
        double targetRaw,
        string varKey,
        SimConnectManager sim,
        Md11EventBus bus,
        double tolerance,
        int maxRounds = 8,
        CancellationToken ct = default)
    {
        var (incEvent, decEvent) = StepEvents(control);
        if (incEvent == null || decEvent == null) return false;

        Log.Info("MD11", $"{control.NodeId}: analog walk START target={targetRaw:0.##} tol={tolerance:0.##}.");

        for (var round = 0; round < maxRounds; round++)
        {
            if (ct.IsCancellationRequested) return false;   // superseded by a newer target
            var current = await ReadAsync(varKey, sim).ConfigureAwait(false);
            if (current == null) return false;
            if (Math.Abs(current.Value - targetRaw) <= tolerance)
            {
                Log.Info("MD11", $"{control.NodeId}: reached {current.Value:0.##} (target {targetRaw:0.##}) in {round} rounds.");
                return true;
            }

            // Probe: one click in the direction we believe is "toward target", then measure what
            // actually happened. This single observation carries BOTH unknowns — how big a click
            // is, and which way it goes.
            var conventional = ResolvePolarity(control.NodeId);
            var wantUp = targetRaw > current.Value;
            bus.Fire((wantUp == conventional) ? incEvent.Value : decEvent.Value);
            await Task.Delay(SettleMs).ConfigureAwait(false);

            var probed = await ReadAsync(varKey, sim).ConfigureAwait(false);
            if (probed == null) return false;

            var delta = probed.Value - current.Value;
            if (Math.Abs(delta) < 1e-6)
            {
                Log.Debug("MD11", $"{control.NodeId}: analog probe produced no movement at {current.Value} — end stop or inhibited.");
                return Math.Abs(probed.Value - targetRaw) <= tolerance;
            }

            // Movement away from the target means our polarity assumption was wrong. Record it;
            // the next round probes the other way.
            var movingUp = delta > 0;
            if (movingUp != wantUp)
            {
                SetPolarity(control.NodeId, !conventional);
                Log.Info("MD11", $"{control.NodeId}: analog step polarity calibrated to {(!conventional ? "INVERTED" : "conventional")}.");
                continue;
            }

            if (Math.Abs(probed.Value - targetRaw) <= tolerance) return true;

            // Remaining distance / measured step size = clicks to go.
            var clicks = (int)Math.Round((targetRaw - probed.Value) / delta);
            Log.Info("MD11", $"{control.NodeId}: round {round} current={current.Value:0.##} probed={probed.Value:0.##} " +
                $"delta/click={delta:0.###} target={targetRaw:0.##} → {clicks} clicks.");
            if (clicks <= 0) continue;

            clicks = Math.Min(clicks, MaxSteps * 4);   // hard bound; the loop re-verifies anyway
            var eventId = (targetRaw > probed.Value) == movingUp ? incEvent.Value : decEvent.Value;
            for (var i = 0; i < clicks && !ct.IsCancellationRequested; i++) bus.Fire(eventId);
            if (ct.IsCancellationRequested) return false;

            // Wait for the whole burst to LAND before re-reading, or the re-read counts a
            // half-finished walk and the next round's click math is wrong. The bus paces each
            // CEVENT write at its MinGapMs (60 ms); this per-click budget must exceed that with
            // margin. (It was 35 ms — fine when the pump paced at 30 ms, too short once the
            // press-release gap was raised to 60 ms, which is what made the walk undershoot.)
            await Task.Delay(SettleMs + clicks * 80).ConfigureAwait(false);
        }

        var final = await ReadAsync(varKey, sim).ConfigureAwait(false);
        var ok = final != null && Math.Abs(final.Value - targetRaw) <= tolerance;
        Log.Info("MD11", $"{control.NodeId}: analog walk END final={final?.ToString("0.##") ?? "null"} " +
            $"target={targetRaw:0.##} converged={ok} after {maxRounds} rounds.");
        return ok;
    }

    /// <summary>Test seam: clears learned polarity so a test can assert calibration from scratch.</summary>
    internal static void ResetPolarity() => Polarity.Clear();

    /// <summary>Test seam: the learned polarity for a node, null until a walk has touched it.</summary>
    internal static bool? PolarityFor(string nodeId) => Polarity.TryGetValue(nodeId, out var v) ? v : null;

    /// <summary>Approximate equality against a detent value.</summary>
    public static bool Near(double a, double b) => Math.Abs(a - b) < Epsilon;
}
