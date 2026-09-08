using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// WHEN the MD-11 seeds its still-empty trackers from the cache after a context reset: on the
/// batch deliveries' evidence — a full cycle, then quiet, with a ceiling — never a wall clock.
/// </summary>
public class Md11SeedGateTests
{
    private static readonly int[] TwoBatches = { 1, 2 };

    /// <summary>Deliveries 1, 2, 1, 2, … half a second apart; nothing seedable moving.</summary>
    private static Md11SeedTrigger DeliverQuiet(Md11SeedGate gate, int ordinal) =>
        gate.OnBatchDelivered(ordinal % 2 == 1 ? 1 : 2, TwoBatches, ordinal * 500L);

    [Fact]
    public void NothingSeeds_UntilEveryBatchHasBeenDeliveredSinceTheArm()
    {
        var gate = new Md11SeedGate();
        gate.Arm();
        for (int i = 0; i < 40; i++)
            Assert.Equal(Md11SeedTrigger.None, gate.OnBatchDelivered(1, TwoBatches, i * 1000L));   // batch 2 never arrives
        Assert.True(gate.Armed);
        Assert.Equal(Md11SeedTrigger.None, gate.OnBatchDelivered(1, Array.Empty<int>(), 41_000));   // nothing registered: no cycle either
    }

    [Fact]
    public void AQuietCockpit_SeedsOnTheLastDeliveryOfTheQuietCycles_AndOnlyOnce()
    {
        var gate = new Md11SeedGate();
        gate.Arm();
        int last = 2 * Md11SeedGate.QuietCycles;
        for (int d = 1; d <= last; d++)
            Assert.Equal(d == last ? Md11SeedTrigger.Quiet : Md11SeedTrigger.None, DeliverQuiet(gate, d));
        Assert.False(gate.Armed);
        Assert.Equal(last, gate.Deliveries);
        Assert.Equal(last, gate.QuietDeliveries);
        Assert.Equal(Md11SeedTrigger.None, DeliverQuiet(gate, last + 1));                        // once per arm
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
        Assert.Equal(0, gate.QuietDeliveries);
        Assert.False(gate.Armed);
    }

    [Fact]
    public void ANewArm_StartsOver_AndDisarmDropsThePass()
    {
        var gate = new Md11SeedGate();
        gate.Arm();
        for (int d = 1; d <= 2 * Md11SeedGate.QuietCycles - 1; d++) DeliverQuiet(gate, d);
        Assert.True(gate.Armed);

        gate.Arm();                                            // a second reset before the first pass ran
        Assert.Equal(0, gate.Deliveries);
        Assert.Equal(Md11SeedTrigger.None, DeliverQuiet(gate, 1));

        gate.Disarm();
        Assert.False(gate.Armed);
        Assert.Equal(Md11SeedTrigger.None, DeliverQuiet(gate, 2));
        gate.NoteValue("MD11_SOME_LT", 1);                     // ignored while disarmed
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
    public void EveryVarTheSeedPassReads_CountsAsEvidence_AndNothingElseDoes()
    {
        var def = new TFDiMD11Definition();
        foreach (var key in Md11VSpeeds.Keys) Assert.True(def.IsSeededFromCache(key), key);
        foreach (var key in Md11Radios.Keys) Assert.True(def.IsSeededFromCache(key), key);
        Assert.True(def.IsSeededFromCache(Md11Squawk.CodeKey));
        Assert.True(def.IsSeededFromCache(Md11Fcp.ReadCaptainBaro));
        Assert.True(def.IsSeededFromCache(Md11SpeedbrakeSystem.ArmKey));
        Assert.True(def.IsSeededFromCache(Md11SpeedbrakeSystem.LeverKey));
        var lamp = Md11ControlMap.Load().Controls.First(c => c.Kind == Md11Kinds.Annunciator).NodeId;
        Assert.True(def.IsSeededFromCache(lamp), lamp);

        Assert.False(def.IsSeededFromCache(Md11TakeoffCallouts.IasKey));
        Assert.False(def.IsSeededFromCache("SIM_ON_GROUND"));
        Assert.False(def.IsSeededFromCache(Md11FlapSystem.LeverKey));       // the flap pair dedups on its spoken text instead
    }
}
