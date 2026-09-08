using System;
using System.Collections.Generic;

namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>What released a context-reset seed pass.</summary>
public enum Md11SeedTrigger
{
    /// <summary>Not yet: keep counting deliveries.</summary>
    None,

    /// <summary>Every batch has been delivered since the arm and nothing seedable has moved for <see cref="Md11SeedGate.QuietCycles"/> cycles.</summary>
    Quiet,

    /// <summary>Something seedable never stopped moving; <see cref="Md11SeedGate.CeilingMs"/> after the first full cycle the rest are seeded anyway.</summary>
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
/// Two conditions, both read off SimConnectManager.ContinuousBatchDelivered: (1) every active
/// batch has been delivered at least once since the arm — a batch writes the cache on EVERY
/// delivery, changed or not, so after one full cycle the cache is current for every var; and
/// (2) the deliveries have gone QUIET: no seedable var's delivered value has differed from its
/// previous one for <see cref="QuietCycles"/> full cycles. The first sight of a var since the
/// arm counts as a change; a forced redelivery of the same value (the panel auto-refresh) does
/// not. A lamp that never stops changing would hold every other tracker empty forever, so
/// <see cref="CeilingMs"/> after the first full cycle the pass runs regardless. Fires once per
/// arm.
///
/// Known residual: a simulator that keeps delivering a FROZEN stream through a loading screen
/// delivers a quiet one, and the pass could run on pre-load values before the flight is live.
/// Only the sim can say whether it does; the in-sim check is a flight load with the app
/// connected, after which the cockpit must not be narrated.
/// </summary>
public sealed class Md11SeedGate
{
    /// <summary>Full cycles of every active batch with nothing seedable moving before the cache is trusted as settled — five seconds at the 1 Hz batch rate.</summary>
    public const int QuietCycles = 5;

    /// <summary>After the first full cycle, seed regardless at this age.</summary>
    public const int CeilingMs = 30_000;

    private readonly Dictionary<string, double> _lastSeen = new(StringComparer.Ordinal);
    private readonly HashSet<int> _delivered = new();
    private bool _armed;
    private bool _cycleComplete;
    private long _cycleCompleteAtMs;
    private bool _changedSinceDelivery;

    /// <summary>A seed pass is pending.</summary>
    public bool Armed => _armed;

    /// <summary>Batch deliveries counted since the arm.</summary>
    public int Deliveries { get; private set; }

    /// <summary>Consecutive deliveries, up to the latest, with nothing seedable moving.</summary>
    public int QuietDeliveries { get; private set; }

    /// <summary>A context reset: start counting from nothing.</summary>
    public void Arm()
    {
        _lastSeen.Clear();
        _delivered.Clear();
        _cycleComplete = false;
        _cycleCompleteAtMs = 0;
        _changedSinceDelivery = false;
        Deliveries = 0;
        QuietDeliveries = 0;
        _armed = true;
    }

    /// <summary>The pass is no longer wanted (the definition is going away).</summary>
    public void Disarm() => _armed = false;

    /// <summary>
    /// A seedable var was delivered. A change — the first sight of the key since the arm, or a
    /// value that differs from the last one delivered — restarts the quiet count at the next
    /// delivery; an unchanged redelivery is not evidence of anything.
    /// </summary>
    public void NoteValue(string key, double value)
    {
        if (!_armed) return;
        if (_lastSeen.TryGetValue(key, out var previous) && previous.Equals(value)) return;
        _lastSeen[key] = value;
        _changedSinceDelivery = true;
    }

    /// <summary>
    /// A batch has finished dispatching. <paramref name="activeBatches"/> is the set of batch
    /// numbers currently registered; the cycle is complete once each has been delivered since
    /// the arm. Returns what released the pass, or None. A release disarms the gate.
    /// </summary>
    public Md11SeedTrigger OnBatchDelivered(int batchNum, IReadOnlyCollection<int> activeBatches, long nowMs)
    {
        if (!_armed) return Md11SeedTrigger.None;
        Deliveries++;
        _delivered.Add(batchNum);
        if (_changedSinceDelivery)
        {
            QuietDeliveries = 0;
            _changedSinceDelivery = false;
        }
        else
        {
            QuietDeliveries++;
        }

        if (!_cycleComplete)
        {
            if (activeBatches.Count == 0) return Md11SeedTrigger.None;
            foreach (var batch in activeBatches)
                if (!_delivered.Contains(batch)) return Md11SeedTrigger.None;
            _cycleComplete = true;
            _cycleCompleteAtMs = nowMs;
        }

        var trigger = QuietDeliveries >= QuietCycles * activeBatches.Count ? Md11SeedTrigger.Quiet
            : nowMs - _cycleCompleteAtMs >= CeilingMs ? Md11SeedTrigger.Ceiling
            : Md11SeedTrigger.None;
        if (trigger != Md11SeedTrigger.None) _armed = false;
        return trigger;
    }
}
