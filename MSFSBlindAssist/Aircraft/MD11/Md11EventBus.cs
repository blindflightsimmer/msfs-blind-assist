using System.Collections.Concurrent;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// The MD-11's one and only actuation channel.
///
/// TFDi's Integration Guide is explicit about the architecture:
///
///   "The TFDi Design MD-11 is primarily event-driven. This means that variables and systems
///    are driven by an event, not by reading the state of an L:VAR or similar. Writing directly
///    to any of the variables will bypass our integrity checks and allow potentially incorrect
///    or conflicting states. To trigger a custom event, you can write the value of the event ID
///    to the L:VAR named CEVENT and our code will translate it. Please note that the aircraft
///    itself also uses this event, so do not overuse it."
///
/// So: every switch, knob and button on this aircraft is actuated by writing ONE integer — the
/// event id — to <c>L:CEVENT</c>. There is no per-control L:var to set, and setting one anyway
/// is explicitly unsupported. (The one sanctioned exception is the <c>MD11_EXTCTL_*</c> family,
/// which TFDi documents as "designed for external control" — those are direct writes by design.)
///
/// Three constraints fall out of that paragraph, and all three are load-bearing:
///
/// 1. ANTI-COALESCE. MobiFlight's command channel silently drops a calc string identical to the
///    one before it — this repo has been bitten twice already (the A380 RMP repeated-digit drop
///    and the DCDU WILCO→SEND two-step). Here it would be worse than cosmetic: pressing the same
///    button twice, or stepping a knob N times, emits the SAME string every time, so every repeat
///    after the first would vanish. Every write therefore carries a <c>{seq} 0 *</c> prefix,
///    which computes a discarded zero and exists purely to make the string textually unique.
///
/// 2. PACING. "Do not overuse it" is a real warning, not boilerplate: CEVENT is a single shared
///    slot that the aircraft's own code also writes. Blasting a burst of writes at frame rate
///    risks ours landing between the aircraft's own and being lost — or clobbering theirs. Writes
///    are serialized through one queue with a minimum gap, so a 5-step knob walk paces out over
///    ~150 ms instead of racing.
///
/// 3. PRESS *AND* RELEASE. Buttons carry a DOWN id and an UP id. Sending only DOWN leaves the
///    button held for the rest of the session — the exact Fenix stuck-button bug that re-fired
///    the takeoff-config test after touchdown (see the A32NX invariants). Always send both.
/// </summary>
public sealed class Md11EventBus : IDisposable
{
    /// <summary>The L:var TFDi's Integration Guide names as the event channel.</summary>
    public const string CEventVar = "CEVENT";

    /// <summary>
    /// Minimum gap between consecutive CEVENT writes. The channel is shared with the aircraft's
    /// own code ("do not overuse it"), so this is deliberately conservative rather than tuned for
    /// speed: a knob walk that takes an extra 100 ms is invisible to a pilot, whereas a dropped
    /// or clobbered event is a control that silently doesn't work.
    ///
    /// This is ALSO the gap the pump leaves between a button's DOWN and its UP, so it must be long
    /// enough that the aircraft samples the pressed state on its own tick before the release lands
    /// (the FBW Rust sampler misses same-tick pulses; assume the MD-11's WASM tick is no faster).
    /// It was 30 ms, which is under one frame at 30 fps — short enough that a CDU key's press and
    /// release could fall in the same tick and the key silently do nothing (the FMC-paging bug).
    /// </summary>
    internal const int MinGapMs = 60;

    /// <summary>Legacy alias — the down/up gap is now the single <see cref="MinGapMs"/> pacing gap.</summary>
    private const int PressReleaseGapMs = MinGapMs;

    /// <summary>
    /// Bound on the queue. A runaway producer (a stuck key repeat, a walk that never converges)
    /// must not grow this without limit; dropping the overflow is strictly better than pumping a
    /// thousand stale events at the aircraft seconds later.
    /// </summary>
    private const int MaxQueued = 256;

    private readonly Action<string> _write;
    private readonly BlockingCollection<int> _queue = new(new ConcurrentQueue<int>(), MaxQueued);
    private readonly Task _pump;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// Bound on how long <see cref="Dispose"/> lets the pump write what is already queued before
    /// cancelling it. Sixteen ids — every held test button's UP plus a guard lift and a walker
    /// burst — drain inside it at <see cref="MinGapMs"/> pacing. It sits on the UI thread (an
    /// aircraft switch, or window close when the definition is disposed at exit), so it is a
    /// bound, not a target: an idle bus returns within one pacing gap.
    /// </summary>
    internal const int DrainTimeoutMs = 1000;

    /// <summary>
    /// Buttons whose DOWN has been queued and whose UP has not: UP id → how many holds are in
    /// flight on it. <see cref="PressAndHoldAsync"/> registers as it queues the DOWN and takes back
    /// as it queues the UP; <see cref="Dispose"/> takes everything still owed and fires those UPs,
    /// so a hold that outlives the bus never leaves the button held in the aircraft (constraint 3).
    /// Guarded by its own lock — a hold ends on a pool thread while Dispose runs on the UI thread —
    /// and BOTH halves of each pair happen under it (<see cref="TrackHeldAndFireDown"/>,
    /// <see cref="ReleaseHeldAndFireUp"/>), which is what makes the register and the write atomic
    /// with respect to the sweep.
    /// </summary>
    private readonly Dictionary<int, int> _heldUps = new();

    /// <summary>
    /// Set by <see cref="TakeAllHeld"/> under the <see cref="_heldUps"/> lock: the release sweep
    /// has run and no further hold may register. The guarded test button (the hydraulic test) lifts
    /// its cover on a pool thread first, so its DOWN can be queued AFTER the sweep — a DOWN accepted
    /// there would leave the button held with no UP owed to anyone.
    /// </summary>
    private bool _closed;

    /// <summary>
    /// Makes each calc string unique — see constraint 1. Only ever incremented, never read for
    /// meaning; the value is multiplied by zero and discarded by the RPN.
    /// </summary>
    private int _seq;

    private int _dropped;

    /// <summary>CEVENTs queued and not yet written. The walker adds <see cref="Pending"/> × <see cref="MinGapMs"/> to a click's timestamp so a click behind a burst is judged when it lands, not when it was queued.</summary>
    public int Pending
    {
        get
        {
            // Same disposal tolerance Fire gets, for the same reason: a walk or a hold can outlive
            // the bus by a tick, and reading Pending on the way out must not surface as an Error-level
            // "set threw" line from SafeWalk's generic catch for a benign shutdown race.
            try { return _queue.Count; }
            catch (ObjectDisposedException) { return 0; }
        }
    }

    /// <summary>
    /// How long the next write has to wait for the queue ahead of it: <see cref="Pending"/> ×
    /// <see cref="MinGapMs"/>. The one spelling of the write-time stamp — it was written out at
    /// half a dozen call sites, one of which is the guarded press's echo-window budget, where the
    /// term has to be recognisable as the same quantity in both places it is added.
    /// </summary>
    public int BacklogMs => Pending * MinGapMs;

    public Md11EventBus(SimConnectManager sim)
        : this(rpn => sim.ExecuteCalculatorCode(rpn, quiet: true)) { }

    /// <summary>
    /// The write seam. Production wires it to <c>ExecuteCalculatorCode(…, quiet: true)</c>; the
    /// tests hand in a recorder so the real pump, its pacing and <see cref="Dispose"/> can be
    /// driven without a sim. Every calc string still carries the <c>{seq} 0 *</c> prefix.
    /// </summary>
    internal Md11EventBus(Action<string> write)
    {
        _write = write;
        _pump = Task.Run(PumpAsync);
    }

    /// <summary>
    /// Queues one CEVENT id. Non-blocking: returns immediately, the pump does the pacing.
    /// Ids are what <c>md11_control_map.json</c> carries in each control's <c>events</c> map.
    /// </summary>
    public void Fire(int eventId)
    {
        if (eventId <= 0) return;
        bool queued;
        try
        {
            queued = _queue.TryAdd(eventId);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            // The bus has been disposed (or is closing): a hold or walk that outlived it. Dropped,
            // not thrown — Dispose has already released whatever it was holding.
            Log.Debug("MD11", $"CEVENT id {eventId} fired after the bus closed — dropped.");
            return;
        }
        if (!queued)
        {
            // Log the first drop only; a flooding producer would otherwise flood the log too.
            if (Interlocked.Increment(ref _dropped) == 1)
                Log.Warn("MD11", $"CEVENT queue full ({MaxQueued}) — dropping events. First dropped id {eventId}.");
        }
    }

    /// <summary>
    /// Fires a full press→release pair for a momentary control. Both ids go through the same
    /// queue in order, so the release can never overtake the press. See constraint 3 — a press
    /// without its release leaves the button held down for the session.
    /// </summary>
    public void FirePressRelease(int? downId, int? upId)
    {
        if (downId is > 0) Fire(downId.Value);
        if (upId is > 0) Fire(upId.Value);
    }

    /// <summary>Convenience overload for a control from the map.</summary>
    public void Press(Md11Control control)
        => FirePressRelease(control.Event("LEFT_BUTTON_DOWN"), control.Event("LEFT_BUTTON_UP"));

    private async Task PumpAsync()
    {
        try
        {
            foreach (var id in _queue.GetConsumingEnumerable(_cts.Token))
            {
                try
                {
                    Write(id);
                }
                catch (Exception ex)
                {
                    Log.Debug("MD11", $"CEVENT write failed for id {id}: {ex.Message}");
                }

                // Pace even the last event of a burst: a follow-up burst may arrive immediately.
                await Task.Delay(MinGapMs, _cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (Exception ex)
        {
            Log.Error("MD11", $"CEVENT pump died: {ex.Message}");
        }
    }

    /// <summary>
    /// The actual write. Goes through ExecuteCalculatorCode rather than SetLVar because SetLVar
    /// would emit the bare string <c>"86018 (>L:CEVENT)"</c> — byte-identical on every repeat of
    /// the same event, which is precisely what MobiFlight coalesces away (constraint 1). The
    /// <c>{seq} 0 *</c> prefix pushes seq, pushes 0, multiplies to a discarded zero, and leaves
    /// the stack clean for the real write; MSFS's RPN ignores the residual value.
    /// </summary>
    private void Write(int eventId)
    {
        var seq = Interlocked.Increment(ref _seq);
        _write($"{seq} 0 * {eventId} (>L:{CEventVar})");
    }

    /// <summary>Fires a press/release pair and waits for the queue to drain past it.</summary>
    public async Task PressAndSettleAsync(Md11Control control, int settleMs = 120)
    {
        Press(control);
        await Task.Delay(PressReleaseGapMs + settleMs).ConfigureAwait(false);
    }

    /// <summary>
    /// Holds a momentary control: DOWN now, UP after <paramref name="holdMs"/>. Both ids ride the
    /// same paced queue, so the release can never overtake the press — and the hold is measured
    /// from when the DOWN will be WRITTEN, not from when it is queued: the queue's backlog at
    /// that moment (<see cref="Pending"/> × <see cref="MinGapMs"/>, the same stamp the walker
    /// gives a click) is added to the wait, so a press queued behind a guard lift or a burst of
    /// walker clicks is still held for the full time once it lands. With the queue idle the
    /// backlog is zero and the hold is within one pacing gap of exact. For the hold-to-test
    /// buttons (see Md11TestButtons), whose lights are only on while the button is down.
    /// </summary>
    public async Task PressAndHoldAsync(Md11Control control, int holdMs)
    {
        var down = control.Event("LEFT_BUTTON_DOWN");
        var up = control.Event("LEFT_BUTTON_UP");
        // A button with no DOWN was never pressed, so it cannot be released: writing its UP alone
        // sends the aircraft a release for a press that never happened. (Nothing is tracked either
        // — Dispose must not release, or log, a button it never held.)
        if (down is not > 0) return;
        var backlogMs = BacklogMs;   // sampled before the DOWN joins the queue
        var tracked = up is > 0;
        if (tracked)
        {
            // Registered AND queued under the held table's lock, so the release sweep can neither
            // miss this DOWN nor be beaten by it. Refused once the sweep has run: a DOWN accepted
            // then owes an UP nobody is left to fire, so neither half is written at all.
            if (!TrackHeldAndFireDown(up!.Value, down.Value)) return;
        }
        else Fire(down.Value);   // no UP id at all: pressed, with nothing owed to release
        await Task.Delay(HoldDelayMs(holdMs, backlogMs)).ConfigureAwait(false);
        // Dispose may have released this button already: whoever takes the entry back fires the
        // UP, so it is written exactly once either way.
        if (tracked) ReleaseHeldAndFireUp(up!.Value);
    }

    /// <summary>
    /// How long to wait between queuing the DOWN and queuing the UP so the button is down for
    /// <paramref name="holdMs"/> from the time the DOWN is written: the hold itself plus the
    /// backlog the DOWN has to wait behind. The one-gap floor is belt-and-braces — the pump paces
    /// every write by <see cref="MinGapMs"/> regardless of what a caller waits, so the UP can never
    /// share a tick with the DOWN — kept so a zero hold still reads as "one paced press".
    /// </summary>
    internal static int HoldDelayMs(int holdMs, int backlogMs) => Math.Max(holdMs, MinGapMs) + backlogMs;

    /// <summary>
    /// How long a read-back waits after QUEUING a click so that the aircraft's settle
    /// (<paramref name="settleMs"/>) is measured from when the click is WRITTEN: the backlog it
    /// waits behind (<see cref="Pending"/> × <see cref="MinGapMs"/>, sampled before the Fire) plus
    /// the settle. The same stamp the walker gives every click and <see cref="PressAndHoldAsync"/>
    /// gives its DOWN, generalised to the ground spoiler, guard-lift and press-feedback read-backs.
    ///
    /// It is the general RULE, not the fix for the false "Ground spoilers did not arm." — on
    /// today's paths the backlog is at most a couple of events (the walker queues one click per
    /// step; the MCDU scratchpad send is the one real burst), so with the queue idle this is the
    /// bare settle and costs the pilot nothing. What removed the false verdict is reading on
    /// DELIVERY instead of sleeping and reading the cache (see <c>SimConnectManager.ReadFreshAsync</c>).
    /// </summary>
    internal static int ReadBackDelayMs(int settleMs, int backlogMs) => settleMs + backlogMs;

    /// <summary>
    /// Registers one hold on <paramref name="upId"/> and queues its <paramref name="downId"/> under
    /// the same lock; false once <see cref="Dispose"/>'s release sweep has run.
    ///
    /// The two used to be separate steps, and the gap between them was a real window rather than a
    /// theoretical one: the guarded hold-to-test button lifts its cover on a POOL thread, so a
    /// <see cref="Dispose"/> on the UI thread (an aircraft switch) could sweep a table that did not
    /// yet owe this UP and then let the DOWN through — the button held in the aircraft with no
    /// release owed to anyone, which is exactly what <see cref="_closed"/> exists to prevent. Under
    /// one lock the pair is atomic against the sweep: either the sweep is first and this refuses, or
    /// the DOWN is queued and its UP is in the table for the sweep to fire BEHIND it (the sweep
    /// still fires before <c>CompleteAdding</c>, so the queue takes it and the FIFO keeps the order).
    ///
    /// <see cref="Fire"/> under the lock is safe: it only does the queue's non-blocking TryAdd, and
    /// the sole other holder of this lock is Dispose, which takes nothing else.
    /// </summary>
    private bool TrackHeldAndFireDown(int upId, int downId)
    {
        lock (_heldUps)
        {
            if (_closed) return false;
            _heldUps[upId] = _heldUps.TryGetValue(upId, out var n) ? n + 1 : 1;
            Fire(downId);
            return true;
        }
    }

    /// <summary>
    /// Takes one hold back off <paramref name="upId"/> and queues that UP, under the same lock and
    /// for the mirror-image reason: taking the entry and then firing outside the lock let Dispose
    /// reach <c>CompleteAdding</c> in between — its sweep found nothing owed, and the UP the hold
    /// then fired was dropped, leaving the button held. False when Dispose already released it.
    /// </summary>
    private bool ReleaseHeldAndFireUp(int upId)
    {
        lock (_heldUps)
        {
            if (!_heldUps.TryGetValue(upId, out var n)) return false;
            if (n <= 1) _heldUps.Remove(upId); else _heldUps[upId] = n - 1;
            Fire(upId);
            return true;
        }
    }

    /// <summary>Every UP still owed, taken back so the holds in flight fire nothing more — and the table closed.</summary>
    private List<int> TakeAllHeld()
    {
        lock (_heldUps)
        {
            _closed = true;
            var ids = _heldUps.Keys.ToList();
            _heldUps.Clear();
            return ids;
        }
    }

    /// <summary>
    /// Writes one L:var directly through the calc path — the shared channel for the three writes on
    /// this aircraft that are NOT a CEVENT: the sanctioned <c>MD11_EXTCTL_*</c> inboxes (the
    /// contract below), the Dial-A-Flap wheel's own backing var (<c>Md11FlapSystem.SetDialRawAsync</c>)
    /// and the walk's gated direct-write fallback (<c>Md11DirectSet</c>). Nothing else may call it.
    ///
    /// VERIFIED AGAINST THE LIVE AIRCRAFT (2026-07-17), because none of this is documented:
    /// writing 123 to <c>MD11_EXTCTL_FCP_HDG</c> put 123 into <c>MD11_AFS_HDG</c> (the FCP window
    /// read-back) and left <c>MD11_EXTCTL_FCP_HDG</c> back at <c>-1</c>. So each of these is a
    /// one-shot COMMAND INBOX, not a mirror: the FCC consumes the value, applies it, and resets the
    /// var to the -1 idle sentinel. That is also why writing here does not "bypass integrity
    /// checks" — the value goes THROUGH TFDi's own FCC exactly as a knob turn would.
    ///
    /// Deliberately NOT queued behind the CEVENT pump: that queue is paced because CEVENT is one
    /// shared slot the aircraft also writes. These are per-quantity vars with no such contention,
    /// and a type-in box should land now, not after a knob walk drains.
    ///
    /// Still seq-prefixed, though — the self-clear to -1 is what makes this necessary rather than
    /// optional: setting the SAME value twice (250 kt → 250 kt) emits a byte-identical calc string,
    /// which MobiFlight coalesces away. The first write would land, the var would reset to -1, and
    /// the second would silently vanish.
    /// </summary>
    public void WriteExternal(string varName, double value)
    {
        var seq = Interlocked.Increment(ref _seq);
        // Invariant fixed-point: default interpolation can emit scientific notation or a
        // comma-decimal, both of which the MSFS RPN parser rejects. Six decimals carries Mach
        // (0.820) and FPA (-3.00) without ever reaching an exponent.
        var literal = value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        _write($"{seq} 0 * {literal} (>L:{varName})");
    }

    public void Dispose()
    {
        try
        {
            // Release FIRST, while the queue still accepts: a DOWN whose UP has not been queued yet
            // would otherwise leave the button held in the aircraft (constraint 3). Overlapping
            // holds on one button owe one UP between them — one release is enough.
            var held = TakeAllHeld();
            foreach (var up in held) Fire(up);
            if (held.Count > 0)
                Log.Info("MD11", $"CEVENT bus disposing with {held.Count} held button(s) — releasing UP {string.Join(", ", held)}.");

            // Stop accepting, then let the pump write what is queued — bounded, this sits on the UI
            // thread — and only THEN cancel. Cancelling straight after CompleteAdding discarded
            // every queued id, a held button's UP and a walk's last click among them.
            _queue.CompleteAdding();
            if (!_pump.Wait(TimeSpan.FromMilliseconds(DrainTimeoutMs)))
                Log.Warn("MD11", $"CEVENT bus did not drain within {DrainTimeoutMs} ms — {_queue.Count} id(s) discarded.");
            _cts.Cancel();
            // Bounded wait: a hung pump must not hold up an aircraft switch.
            _pump.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch { /* best effort */ }
        finally
        {
            _cts.Dispose();
            _queue.Dispose();
        }
    }
}
