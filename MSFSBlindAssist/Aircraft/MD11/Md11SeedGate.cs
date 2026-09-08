using System;
using System.Collections.Generic;

namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>What released a context-reset seed pass.</summary>
public enum Md11SeedTrigger
{
    /// <summary>Not yet: keep counting deliveries.</summary>
    None,

    /// <summary>A seedable value changed since the arm, and since the last change every active batch has been delivered <see cref="Md11SeedGate.QuietCycles"/> times with nothing seedable moving.</summary>
    Quiet,

    /// <summary><see cref="Md11SeedGate.CeilingMs"/> after the first full cycle: either something seedable never stopped moving, or nothing seedable ever moved and stillness alone was not trusted.</summary>
    Ceiling,
}

/// <summary>
/// Decides WHEN, after a context reset, the trackers still without a baseline are seeded from
/// the cache — on evidence from the batch deliveries, never a wall clock. A flight load leaves
/// the cache holding the PREVIOUS situation until the new one has been delivered, and a pass run
/// too early freezes those stale values as the baselines: every lamp that then comes up speaks,
/// and the load narrates the cockpit. A pass run too late merely swallows one edge per
/// still-empty tracker (the change that would have seeded it), which is the pre-seed status quo.
/// The failure is asymmetric, so the gate errs late. (A 3 s timer stood here first; review,
/// 2026-09-08.)
///
/// Three conditions, all read off SimConnectManager.ContinuousBatchDelivered:
/// (1) every active batch has been delivered at least once since the arm — a batch writes the
/// cache on EVERY delivery, changed or not, so one full cycle makes it current for every
/// batch-covered var (the speedbrake lever streams per frame on its own subscription and
/// re-seeds on its own change);
/// (2) at least one seedable value has CHANGED since the arm — stillness alone is the ambiguous
/// signal, meaning either "the load changed nothing" or "the aircraft has not published yet",
/// and the gate cannot tell them apart: a loaded MD-11's client-data channel first delivered
/// 8.47 s after AircraftLoaded (debug.log, review), and a stream that merely repeats the
/// pre-load values is quiet for all of it. A cockpit in which nothing seedable changes waits for
/// the ceiling instead, where seeding from a cache nothing has moved is trivially right and
/// nothing was swallowed on the way;
/// (3) since the last change, every active batch has been delivered <see cref="QuietCycles"/>
/// times with nothing seedable moving — per batch, so one unchanged sample of a batch that came
/// late cannot pass on the strength of the others. The first sight of a var since the arm counts
/// as a change; a forced redelivery of the same value (the panel auto-refresh) does not.
///
/// A lamp that never stops changing would hold every other tracker empty forever, so
/// <see cref="CeilingMs"/> after the first full cycle the pass runs regardless. Fires once per
/// arm.
///
/// Known residual: a load that settles in two bursts more than the quiet window apart seeds
/// between them, and the second burst's vars speak. The in-sim check is a flight load with the
/// app connected, after which nothing may be narrated.
/// </summary>
public sealed class Md11SeedGate
{
    /// <summary>Deliveries of EACH active batch, after the last change, with nothing seedable moving — five seconds at the 1 Hz batch rate.</summary>
    public const int QuietCycles = 5;

    /// <summary>After the first full cycle, seed regardless at this age.</summary>
    public const int CeilingMs = 30_000;

    private readonly Dictionary<string, double> _lastSeen = new(StringComparer.Ordinal);
    private readonly HashSet<int> _delivered = new();
    private readonly Dictionary<int, int> _quietByBatch = new();
    private bool _armed;
    private bool _cycleComplete;
    private long _cycleCompleteAtMs;
    private bool _changedSinceDelivery;

    /// <summary>A seed pass is pending.</summary>
    public bool Armed => _armed;

    /// <summary>A seedable value has changed since the arm.</summary>
    public bool SawChange { get; private set; }

    /// <summary>Batch deliveries counted since the arm.</summary>
    public int Deliveries { get; private set; }

    /// <summary>Consecutive deliveries, up to the latest, with nothing seedable moving.</summary>
    public int QuietDeliveries { get; private set; }

    /// <summary>A context reset: start counting from nothing.</summary>
    public void Arm()
    {
        _lastSeen.Clear();
        _delivered.Clear();
        _quietByBatch.Clear();
        _cycleComplete = false;
        _cycleCompleteAtMs = 0;
        _changedSinceDelivery = false;
        SawChange = false;
        Deliveries = 0;
        QuietDeliveries = 0;
        _armed = true;
    }

    /// <summary>The pass is no longer wanted (the definition is going away).</summary>
    public void Disarm() => _armed = false;

    /// <summary>
    /// A seedable var was delivered. A change — the first sight of the key since the arm, or a
    /// value that differs from the last one delivered — restarts every batch's quiet count at
    /// the delivery that carried it; an unchanged redelivery is not evidence of anything.
    /// </summary>
    public void NoteValue(string key, double value)
    {
        if (!_armed) return;
        if (_lastSeen.TryGetValue(key, out var previous) && previous.Equals(value)) return;
        _lastSeen[key] = value;
        _changedSinceDelivery = true;
        SawChange = true;
    }

    /// <summary>
    /// A batch has finished dispatching. <paramref name="activeBatches"/> is the set of batch
    /// numbers currently registered; an empty set releases nothing. Returns what released the
    /// pass, or None. A release disarms the gate.
    /// </summary>
    public Md11SeedTrigger OnBatchDelivered(int batchNum, IReadOnlyCollection<int> activeBatches, long nowMs)
    {
        if (!_armed) return Md11SeedTrigger.None;
        Deliveries++;
        _delivered.Add(batchNum);
        if (_changedSinceDelivery)
        {
            _changedSinceDelivery = false;      // this delivery carried the change: nothing is quiet yet
            _quietByBatch.Clear();
            QuietDeliveries = 0;
        }
        else
        {
            _quietByBatch[batchNum] = _quietByBatch.GetValueOrDefault(batchNum) + 1;
            QuietDeliveries++;
        }

        if (activeBatches.Count == 0) return Md11SeedTrigger.None;   // nothing registered: neither a cycle nor a threshold

        if (!_cycleComplete)
        {
            foreach (var batch in activeBatches)
                if (!_delivered.Contains(batch)) return Md11SeedTrigger.None;
            _cycleComplete = true;
            _cycleCompleteAtMs = nowMs;
        }

        var trigger = SawChange && EveryBatchQuiet(activeBatches) ? Md11SeedTrigger.Quiet
            : nowMs - _cycleCompleteAtMs >= CeilingMs ? Md11SeedTrigger.Ceiling
            : Md11SeedTrigger.None;
        if (trigger != Md11SeedTrigger.None) _armed = false;
        return trigger;
    }

    private bool EveryBatchQuiet(IReadOnlyCollection<int> activeBatches)
    {
        foreach (var batch in activeBatches)
            if (_quietByBatch.GetValueOrDefault(batch) < QuietCycles) return false;
        return true;
    }
}
