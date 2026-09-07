using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// A hold-to-test button must be DOWN for its hold time from the moment the DOWN is WRITTEN,
/// not from the moment it is queued: the CEVENT bus paces one write per MinGapMs, so a DOWN
/// queued behind N pending events lands N gaps later, and a wait measured from enqueue would
/// release it that much early — behind a guard lift or a burst of walker clicks, a 3 s hold
/// could shrink below what the 1 Hz lamp batch needs to show the lights at all.
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
}
