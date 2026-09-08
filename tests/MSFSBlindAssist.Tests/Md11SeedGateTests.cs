using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// WHEN the MD-11 seeds its still-empty trackers from the cache after a context reset: on the
/// batch deliveries' evidence — a full cycle, a change seen, then every batch quiet — with a
/// ceiling; never a wall clock.
/// </summary>
public class Md11SeedGateTests
{
    private static readonly int[] TwoBatches = { 1, 2 };

    /// <summary>Deliveries 1, 2, 1, 2, … half a second apart; nothing seedable moving.</summary>
    private static Md11SeedTrigger DeliverQuiet(Md11SeedGate gate, int ordinal) =>
        gate.OnBatchDelivered(ordinal % 2 == 1 ? 1 : 2, TwoBatches, ordinal * 500L);

    /// <summary>A gate that has seen a change (the reconnect's re-fire, a load's new values): delivery 1 carried it.</summary>
    private static Md11SeedGate ArmedAfterAChange()
    {
        var gate = new Md11SeedGate();
        gate.Arm();
        gate.NoteValue("MD11_SOME_LT", 1);
        Assert.Equal(Md11SeedTrigger.None, DeliverQuiet(gate, 1));
        Assert.True(gate.SawChange);
        return gate;
    }

    [Fact]
    public void NothingSeeds_UntilEveryBatchHasBeenDeliveredSinceTheArm()
    {
        var gate = ArmedAfterAChange();
        for (int i = 0; i < 40; i++)
            Assert.Equal(Md11SeedTrigger.None, gate.OnBatchDelivered(1, TwoBatches, 1000 + i * 1000L));   // batch 2 never arrives
        Assert.True(gate.Armed);
        Assert.Equal(40, gate.QuietDeliveries);
    }

    [Fact]
    public void AStillCockpit_ThatHasNotChangedSinceTheReset_WaitsForTheCeiling_NotTheQuietWindow()
    {
        // The ambiguous case: a loaded aircraft that has not published yet looks exactly like this.
        var gate = new Md11SeedGate();
        gate.Arm();
        int released = 0;
        for (int d = 1; d <= 80; d++)
        {
            var trigger = DeliverQuiet(gate, d);                         // the cycle completes on delivery 2, at 1.0 s
            if (trigger == Md11SeedTrigger.None) continue;
            Assert.Equal(Md11SeedTrigger.Ceiling, trigger);
            released = d;
            break;
        }
        Assert.Equal(1000 + Md11SeedGate.CeilingMs, released * 500L);      // 30 s after the first full cycle, not 5 s
        Assert.False(gate.SawChange);
        Assert.Equal(released, gate.QuietDeliveries);
        Assert.False(gate.Armed);
    }

    [Fact]
    public void AfterAChange_EveryBatchMustDeliverTheQuietCyclesItself_ThenItSeedsOnce()
    {
        var gate = ArmedAfterAChange();                                       // delivery 1 (batch 1) carried the change
        int last = 1 + 2 * Md11SeedGate.QuietCycles;                          // batch 1's fifth quiet delivery is the 11th overall
        for (int d = 2; d <= last; d++)
            Assert.Equal(d == last ? Md11SeedTrigger.Quiet : Md11SeedTrigger.None, DeliverQuiet(gate, d));
        Assert.False(gate.Armed);
        Assert.Equal(last, gate.Deliveries);
        Assert.Equal(last - 1, gate.QuietDeliveries);
        Assert.Equal(Md11SeedTrigger.None, DeliverQuiet(gate, last + 1));    // once per arm
    }

    [Fact]
    public void ALateBatch_CannotPassOnOneUnchangedSample()
    {
        var gate = ArmedAfterAChange();
        for (int i = 0; i < 20; i++)
            Assert.Equal(Md11SeedTrigger.None, gate.OnBatchDelivered(1, TwoBatches, 1000 + i * 1000L));   // batch 2 stalls
        long t = 21_000;
        for (int n = 1; n <= Md11SeedGate.QuietCycles; n++)
        {
            var expected = n == Md11SeedGate.QuietCycles ? Md11SeedTrigger.Quiet : Md11SeedTrigger.None;
            Assert.Equal(expected, gate.OnBatchDelivered(2, TwoBatches, t));   // batch 2's own nth quiet sample
            t += 500;
            if (n < Md11SeedGate.QuietCycles) { Assert.Equal(Md11SeedTrigger.None, gate.OnBatchDelivered(1, TwoBatches, t)); t += 500; }
        }
    }

    [Fact]
    public void AChange_RestartsTheQuietCount_AnUnchangedRedeliveryDoesNot()
    {
        var gate = new Md11SeedGate();
        gate.Arm();
        gate.NoteValue("MD11_SOME_LT", 0);                    // the first sight since the arm is a change
        DeliverQuiet(gate, 1);
        DeliverQuiet(gate, 2);
        Assert.Equal(1, gate.QuietDeliveries);                // delivery 1 carried the change; delivery 2 was quiet

        gate.NoteValue("MD11_SOME_LT", 0);                    // a forced redelivery of the same value
        DeliverQuiet(gate, 3);
        Assert.Equal(2, gate.QuietDeliveries);

        gate.NoteValue("MD11_SOME_LT", 1);                    // it lit
        DeliverQuiet(gate, 4);
        Assert.Equal(0, gate.QuietDeliveries);

        int last = 4 + 2 * Md11SeedGate.QuietCycles;
        for (int d = 5; d <= last; d++)
            Assert.Equal(d == last ? Md11SeedTrigger.Quiet : Md11SeedTrigger.None, DeliverQuiet(gate, d));
    }

    [Fact]
    public void ARestlessCockpit_SeedsAtTheCeiling_MeasuredFromTheFirstFullCycle_NotTheReset()
    {
        var gate = new Md11SeedGate();
        gate.Arm();
        // A loading screen that delivers nothing for 40 s: the ceiling cannot start.
        long t = 40_000;
        int deliveries = 0;
        var trigger = Md11SeedTrigger.None;
        for (; trigger == Md11SeedTrigger.None; t += 500)
        {
            gate.NoteValue("MD11_FLASHING_LT", deliveries % 2);                  // never the same value twice running
            trigger = gate.OnBatchDelivered(deliveries % 2 == 0 ? 1 : 2, TwoBatches, t);
            deliveries++;
            Assert.True(t < 200_000, "never seeded");
        }
        Assert.Equal(Md11SeedTrigger.Ceiling, trigger);
        Assert.Equal(40_500 + Md11SeedGate.CeilingMs, t - 500);                  // the cycle completed on delivery 2, at 40.5 s
        Assert.True(gate.SawChange);
        Assert.Equal(0, gate.QuietDeliveries);
        Assert.False(gate.Armed);
    }

    [Fact]
    public void AnEmptyBatchSet_ReleasesNothing_EvenAfterTheCycleCompleted()
    {
        var gate = ArmedAfterAChange();
        for (int d = 2; d <= 2 * Md11SeedGate.QuietCycles; d++) Assert.Equal(Md11SeedTrigger.None, DeliverQuiet(gate, d));
        Assert.Equal(Md11SeedTrigger.None, gate.OnBatchDelivered(1, Array.Empty<int>(), 20_000));   // mid re-registration
        Assert.True(gate.Armed);
        Assert.Equal(Md11SeedTrigger.None, gate.OnBatchDelivered(1, Array.Empty<int>(), 60_000));   // no ceiling on nothing either
        Assert.True(gate.Armed);
    }

    [Fact]
    public void ANewArm_StartsOver_AndDisarmDropsThePass()
    {
        var gate = ArmedAfterAChange();
        for (int d = 2; d <= 2 * Md11SeedGate.QuietCycles; d++) DeliverQuiet(gate, d);   // one short of the release
        Assert.True(gate.Armed);

        gate.Arm();                                            // a second reset before the first pass ran
        Assert.Equal(0, gate.Deliveries);
        Assert.False(gate.SawChange);
        Assert.Equal(Md11SeedTrigger.None, DeliverQuiet(gate, 1));

        gate.Disarm();
        Assert.False(gate.Armed);
        Assert.Equal(Md11SeedTrigger.None, DeliverQuiet(gate, 2));
        gate.NoteValue("MD11_SOME_LT", 1);                     // ignored while disarmed
        Assert.False(gate.SawChange);
        Assert.Equal(Md11SeedTrigger.None, DeliverQuiet(gate, 3));
    }

    [Fact]
    public void TheDefinition_ArmsThePassOnAContextReset_AndDropsItOnDispose()
    {
        var def = new TFDiMD11Definition();
        Assert.False(def.SeedPassPending);
        def.OnSimContextReset();
        Assert.True(def.SeedPassPending);
        def.OnContinuousBatchDelivered(1);                     // no sim to read: still pending
        Assert.True(def.SeedPassPending);
        def.Dispose();
        Assert.False(def.SeedPassPending);
    }

    [Fact]
    public void TheSeedListIsTheEvidenceList_AndCoversEveryScalarTrackerAndEveryLamp()
    {
        var def = new TFDiMD11Definition();
        var expected = new[] { Md11Squawk.CodeKey, Md11Fcp.ReadCaptainBaro, Md11SpeedbrakeSystem.ArmKey, Md11SpeedbrakeSystem.LeverKey }
            .Concat(Md11VSpeeds.Keys).Concat(Md11Radios.Keys).OrderBy(k => k);
        Assert.Equal(expected, TFDiMD11Definition.SeededScalarKeys.OrderBy(k => k));
        foreach (var key in TFDiMD11Definition.SeededScalarKeys) Assert.True(def.IsSeededFromCache(key), key);

        var lamp = Md11ControlMap.Load().Controls.First(c => c.Kind == Md11Kinds.Annunciator).NodeId;
        Assert.True(def.IsSeededFromCache(lamp), lamp);

        Assert.False(def.IsSeededFromCache(Md11TakeoffCallouts.IasKey));
        Assert.False(def.IsSeededFromCache("SIM_ON_GROUND"));
        Assert.False(def.IsSeededFromCache(Md11FlapSystem.LeverKey));       // the flap pair dedups on its spoken text instead
    }
}
