using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The switch-verify-settle-restore sequence against a fake camera. The delays are recorded, not
/// slept, so "gives up after the cap" is a count of poll steps rather than a wall clock.
/// </summary>
public class InstrumentViewSwitcherTests
{
    private sealed class FakeCamera : ICameraViewIo
    {
        public CameraViewReading? Current;
        public bool HonoursWrites = true;
        public bool ThrowOnSet;
        public readonly List<(int Type, int Index)> Writes = new();

        public Task<CameraViewReading?> ReadAsync(int timeoutMs) => Task.FromResult(Current);

        public void Set(int viewType, int viewIndex)
        {
            if (ThrowOnSet) throw new InvalidOperationException("SimConnect down");
            Writes.Add((viewType, viewIndex));
            if (HonoursWrites && Current is { } c)
                Current = c with { ViewType = viewType, ViewIndex = viewIndex };
        }
    }

    private static (InstrumentViewSwitcher Switcher, List<int> Delays) Make(FakeCamera camera)
    {
        var delays = new List<int>();
        var switcher = new InstrumentViewSwitcher(camera, ms => { delays.Add(ms); return Task.CompletedTask; });
        return (switcher, delays);
    }

    [Fact]
    public async Task AlreadyOnTheView_WritesNothing_AndRestoreIsANoOp()
    {
        var camera = new FakeCamera { Current = new CameraViewReading(2, 2, 2) };
        var (switcher, delays) = Make(camera);

        var session = await switcher.EnterAsync(2);
        session.Restore();

        Assert.Equal(InstrumentViewOutcome.AlreadyThere, session.Outcome);
        Assert.True(session.Verified);
        Assert.Null(session.RestoreTo);
        Assert.Empty(camera.Writes);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task FromThePilotView_Switches_VerifiesOnTheFirstRead_SettlesOnce_AndRestoresOnce()
    {
        var camera = new FakeCamera { Current = new CameraViewReading(2, 1, 0) };
        var (switcher, delays) = Make(camera);

        var session = await switcher.EnterAsync(2);

        Assert.Equal(InstrumentViewOutcome.Switch, session.Outcome);
        Assert.True(session.Verified);
        Assert.Equal((1, 0), session.RestoreTo);
        Assert.Equal(new[] { (2, 2) }, camera.Writes);
        Assert.Equal(new[] { 250 }, delays);

        session.Restore();
        session.Restore();

        Assert.Equal(new[] { (2, 2), (1, 0) }, camera.Writes);
    }

    [Fact]
    public async Task AWriteTheSimIgnores_GivesUpAfterTheCap_ReportsUnverified_AndStillRestores()
    {
        var camera = new FakeCamera { Current = new CameraViewReading(2, 1, 0), HonoursWrites = false };
        var (switcher, delays) = Make(camera);

        var session = await switcher.EnterAsync(2);

        Assert.Equal(InstrumentViewOutcome.Switch, session.Outcome);
        Assert.False(session.Verified);
        Assert.Equal(Enumerable.Repeat(100, 10), delays);   // 1000 ms cap / 100 ms steps, no settle
        Assert.Equal((1, 0), session.RestoreTo);

        session.Restore();

        Assert.Equal((1, 0), camera.Writes.Last());
    }

    [Fact]
    public async Task AnExternalCamera_IsNotInCockpit_AndWritesNothing()
    {
        var camera = new FakeCamera { Current = new CameraViewReading(3, 0, 0) };
        var (switcher, delays) = Make(camera);

        var session = await switcher.EnterAsync(2);
        session.Restore();

        Assert.Equal(InstrumentViewOutcome.NotInCockpit, session.Outcome);
        Assert.False(session.Verified);
        Assert.Empty(camera.Writes);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task AnUnreadableCamera_WritesTheView_ReportsUnverified_AndHasNothingToRestore()
    {
        var camera = new FakeCamera { Current = null };
        var (switcher, _) = Make(camera);

        var session = await switcher.EnterAsync(3);
        session.Restore();

        Assert.Equal(InstrumentViewOutcome.Unknown, session.Outcome);
        Assert.False(session.Verified);
        Assert.Null(session.RestoreTo);
        Assert.Equal(new[] { (2, 3) }, camera.Writes);
    }

    [Fact]
    public async Task AThrowingWrite_DoesNotEscape_OnEntryOrOnRestore()
    {
        var camera = new FakeCamera { Current = new CameraViewReading(2, 1, 0), ThrowOnSet = true };
        var (switcher, _) = Make(camera);

        var session = await switcher.EnterAsync(2);
        var restore = Record.Exception(session.Restore);

        Assert.Equal(InstrumentViewOutcome.Switch, session.Outcome);
        Assert.False(session.Verified);
        Assert.Null(restore);
    }

    [Fact]
    public void TheDefaults_AreTheSpecsNumbers()
    {
        Assert.Equal(500, InstrumentViewSwitcher.DefaultReadTimeoutMs);
        Assert.Equal(100, InstrumentViewSwitcher.DefaultPollStepMs);
        Assert.Equal(1000, InstrumentViewSwitcher.DefaultVerifyCapMs);
        Assert.Equal(250, InstrumentViewSwitcher.DefaultSettleMs);
    }
}
