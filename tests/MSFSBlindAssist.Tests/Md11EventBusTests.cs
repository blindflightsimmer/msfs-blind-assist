using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// A hold-to-test button must be DOWN for its hold time from the moment the DOWN is WRITTEN,
/// not from the moment it is queued: the CEVENT bus paces one write per MinGapMs, so a DOWN
/// queued behind N pending events lands N gaps later, and a wait measured from enqueue would
/// release it that much early — behind a guard lift or a burst of walker clicks, a 3 s hold
/// could shrink below what the 1 Hz lamp batch needs to show the lights at all.
///
/// And the bus must END clean: Dispose writes what is queued and releases any button still
/// held, because cancelling the pump first discarded both and left a test button pressed in the
/// aircraft whenever the MD-11 was re-selected inside the 3 s hold.
/// </summary>
public class Md11EventBusTests
{
    [Fact]
    public void HoldDelay_IsTheHoldItself_WhenTheQueueIsIdle()
    {
        Assert.Equal(3000, Md11EventBus.HoldDelayMs(3000, backlogMs: 0));
    }

    [Fact]
    public void HoldDelay_AddsTheBacklogTheDownWaitsBehind()
    {
        var backlog = 5 * Md11EventBus.MinGapMs;
        Assert.Equal(3000 + backlog, Md11EventBus.HoldDelayMs(3000, backlog));
    }

    [Fact]
    public void HoldDelay_NeverReleasesInsideOnePacingGap()
    {
        // A hold shorter than one gap would let the UP be written in the same pump tick as the DOWN.
        Assert.Equal(Md11EventBus.MinGapMs, Md11EventBus.HoldDelayMs(1, backlogMs: 0));
        Assert.Equal(Md11EventBus.MinGapMs + 120, Md11EventBus.HoldDelayMs(0, backlogMs: 120));
    }

    /// <summary>
    /// Pins the SHAPE of the read-back rule the walker, <c>PressAndHoldAsync</c> and the three
    /// spoken read-backs now share: a read-back after a QUEUED click waits the aircraft's settle
    /// from the moment the click is WRITTEN, which is the settle plus whatever backlog the click
    /// waits behind. An idle queue therefore costs the pilot nothing extra. (What actually removed
    /// the false "Ground spoilers did not arm." is reading on DELIVERY rather than after a fixed
    /// sleep — see the ArmReadBack / TuneReadBack rows; today's backlogs are at most a couple of
    /// events, the MCDU scratchpad send being the one real burst.)
    /// </summary>
    [Theory]
    [InlineData(700, 0, 700)]
    [InlineData(700, 5 * Md11EventBus.MinGapMs, 700 + 5 * Md11EventBus.MinGapMs)]
    [InlineData(0, 120, 120)]
    public void ReadBackDelay_IsTheSettleMeasuredFromTheClicksWrite(int settleMs, int backlogMs, int expected)
    {
        Assert.Equal(expected, Md11EventBus.ReadBackDelayMs(settleMs, backlogMs));
    }

    // ---- Dispose: drain and release -------------------------------------------------------

    /// <summary>Records every calc string the pump writes; ids parsed off the "{seq} 0 * {id} (>L:CEVENT)" shape.</summary>
    private sealed class Recorder
    {
        private readonly List<string> _lines = new();

        public void Write(string rpn) { lock (_lines) _lines.Add(rpn); }

        public IReadOnlyList<int> Ids
        {
            get
            {
                lock (_lines)
                    return _lines.Where(l => l.EndsWith(" (>L:CEVENT)", StringComparison.Ordinal))
                                 .Select(l => int.Parse(l.Split(' ')[3]))
                                 .ToList();
            }
        }

        public int CountOf(int id) => Ids.Count(i => i == id);

        public async Task<bool> WaitFor(int id, int timeoutMs = 2000)
        {
            var deadline = Environment.TickCount64 + timeoutMs;
            while (Environment.TickCount64 < deadline)
            {
                if (CountOf(id) > 0) return true;
                await Task.Delay(10);
            }
            return false;
        }
    }

    private static Md11Control TestButton(int? down, int up)
    {
        var events = new Dictionary<string, int> { ["LEFT_BUTTON_UP"] = up };
        if (down is > 0) events["LEFT_BUTTON_DOWN"] = down.Value;
        return new Md11Control { NodeId = "TEST_BT", Kind = Md11Kinds.Button, Events = events };
    }

    [Fact]
    public void Dispose_WritesEveryQueuedId_BeforeStoppingThePump()
    {
        var rec = new Recorder();
        var bus = new Md11EventBus(rec.Write);
        bus.Fire(11);
        bus.Fire(12);
        bus.Fire(13);

        bus.Dispose();

        // Drained in order, not discarded: a walk's last click and a held button's UP ride this queue.
        Assert.Equal(new[] { 11, 12, 13 }, rec.Ids);
    }

    [Fact]
    public async Task Dispose_ReleasesAHeldButton_AndTheLateHoldDoesNotReleaseItAgain()
    {
        var rec = new Recorder();
        var bus = new Md11EventBus(rec.Write);
        var hold = bus.PressAndHoldAsync(TestButton(down: 100, up: 101), holdMs: 400);
        Assert.True(await rec.WaitFor(100));      // the DOWN is on the aircraft
        Assert.Equal(0, rec.CountOf(101));        // and the hold is still running

        bus.Dispose();

        Assert.Equal(1, rec.CountOf(101));        // released by Dispose, not lost with the pump
        await hold;                               // the hold outlives the bus without throwing
        Assert.Equal(1, rec.CountOf(101));        // and does not write a second UP
    }

    [Fact]
    public async Task Dispose_DoesNotReleaseAButtonThatWasNeverPressed()
    {
        var rec = new Recorder();
        var bus = new Md11EventBus(rec.Write);
        var hold = bus.PressAndHoldAsync(TestButton(down: null, up: 201), holdMs: 100);

        bus.Dispose();

        await hold;                               // no DOWN was queued, so no UP is owed — and no throw
        Assert.Equal(0, rec.CountOf(201));
    }

    /// <summary>
    /// The guarded hold-to-test button (MD11_OVHD_HYD_HYD_TEST_BT) lifts its cover on a pool
    /// thread first, so its DOWN can be queued AFTER Dispose has already swept the held table.
    /// Once the bus has closed, a hold must write neither half — a DOWN accepted after the sweep
    /// would leave the button held with no UP owed to anyone.
    /// </summary>
    [Fact]
    public async Task AHoldThatStartsAfterDispose_WritesNeitherHalf()
    {
        var rec = new Recorder();
        var bus = new Md11EventBus(rec.Write);

        bus.Dispose();
        await bus.PressAndHoldAsync(TestButton(down: 300, up: 301), holdMs: 50);

        Assert.Equal(0, rec.CountOf(300));
        Assert.Equal(0, rec.CountOf(301));
    }
}
