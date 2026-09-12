using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The calc-path bridge probe's verdict is per CONNECTION, not per aircraft — and an aircraft
/// switch never disconnects. These pin the re-arm primitive `MainForm.ArmBridgeProbe` calls, which
/// is what stops one profile's verdict deciding the next one's writes.
///
/// The regression this closes: a session begun on any profile that registers no MSFSBA_BRIDGE_PROBE
/// target (PMDG 737/777, HS787, iFly, Fenix) concludes UNVERIFIED and silently. Before the re-arm,
/// picking the MD-11 from the Aircraft menu afterwards left that verdict standing, so
/// `CalcWriteCanLand` was false and EVERY MD-11 write refused "unavailable" for the rest of the
/// connection — with the module installed, the calc path healthy, and no warning ever spoken,
/// because the conclusion had been reached on the previous profile.
///
/// SimConnectManager is constructed here with a null window handle: the constructor only stores it
/// and creates timers, and nothing below touches the socket.
/// </summary>
public class CalcPathProbeRearmTests
{
    private static SimConnectManager NewManager() => new(IntPtr.Zero);

    /// <summary>A fresh manager has reached no verdict, so nothing is refused on its evidence.</summary>
    [Fact]
    public void AFreshManager_HasNoVerdict()
    {
        var sim = NewManager();

        Assert.False(sim.CalcPathVerified);
        Assert.False(sim.CalcPathProbeConcluded);
    }

    /// <summary>
    /// The case that caused the regression: a profile with no probe target concludes UNVERIFIED,
    /// and the re-arm must clear it so the next aircraft is judged on its own evidence.
    /// </summary>
    [Fact]
    public void AConcludedUnverifiedVerdict_IsClearedByTheReArm()
    {
        var sim = NewManager();
        sim.MarkCalcPathProbeConcluded();

        Assert.True(sim.CalcPathProbeConcluded);
        Assert.False(sim.CalcPathVerified);

        sim.ResetCalcPathProbe();

        Assert.False(sim.CalcPathProbeConcluded);
        Assert.False(sim.CalcPathVerified);
    }

    /// <summary>
    /// The other direction matters too: a VERIFIED verdict earned on one aircraft must not be
    /// inherited by the next, or a profile whose calc path is genuinely dead would look healthy.
    /// </summary>
    [Fact]
    public void AVerifiedVerdict_IsAlsoClearedByTheReArm()
    {
        var sim = NewManager();
        sim.MarkCalcPathVerified();

        Assert.True(sim.CalcPathVerified);
        Assert.True(sim.CalcPathProbeConcluded);   // verifying concludes the probe too

        sim.ResetCalcPathProbe();

        Assert.False(sim.CalcPathVerified);
        Assert.False(sim.CalcPathProbeConcluded);
    }

    /// <summary>
    /// Re-arming twice, or on a manager that never concluded, is harmless — the switch path calls
    /// it unconditionally on every aircraft change.
    /// </summary>
    [Fact]
    public void TheReArm_IsIdempotentAndSafeBeforeAnyVerdict()
    {
        var sim = NewManager();

        sim.ResetCalcPathProbe();
        sim.ResetCalcPathProbe();

        Assert.False(sim.CalcPathVerified);
        Assert.False(sim.CalcPathProbeConcluded);
    }
}
