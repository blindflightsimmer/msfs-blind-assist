using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins the one-shot waiter behind SimConnectManager.ReadFreshAsync: a fresh read completes on the
/// delivery, times out to null (never a stale value), shares one waiter per key, and honours
/// cancellation — the properties the MD-11 walker's step protocol relies on.
/// </summary>
public class FreshReadWaitersTests
{
    [Fact]
    public async Task CompletesWithTheDeliveredValue_AndUnregisters()
    {
        var waiters = new FreshReadWaiters();
        var issued = 0;

        var read = waiters.WaitAsync("K", () => issued++, timeoutMs: 5000);

        Assert.Equal(1, issued);                 // the force-read is issued before waiting
        Assert.Equal(1, waiters.Count);
        Assert.True(waiters.Complete("K", 2.0));
        Assert.Equal(2.0, await read);
        Assert.Equal(0, waiters.Count);
    }

    [Fact]
    public async Task TimesOutToNull_AndTheNextDeliveryClearsTheLeftoverWaiter()
    {
        var waiters = new FreshReadWaiters();

        var value = await waiters.WaitAsync("K", () => { }, timeoutMs: 20);

        Assert.Null(value);                        // never a stale cached value
        Assert.Equal(1, waiters.Count);            // the waiter stays until a delivery clears it
        Assert.True(waiters.Complete("K", 1.0));
        Assert.Equal(0, waiters.Count);
    }

    [Fact]
    public async Task ConcurrentCallersShareOneDelivery()
    {
        var waiters = new FreshReadWaiters();

        var first = waiters.WaitAsync("K", () => { }, timeoutMs: 5000);
        var second = waiters.WaitAsync("K", () => { }, timeoutMs: 5000);

        Assert.Equal(1, waiters.Count);
        Assert.True(waiters.Complete("K", 7.0));
        Assert.Equal(7.0, await first);
        Assert.Equal(7.0, await second);
    }

    [Fact]
    public async Task CancellationThrows_AndLeavesTheWaiterForTheNextDelivery()
    {
        var waiters = new FreshReadWaiters();
        using var cts = new CancellationTokenSource();

        var read = waiters.WaitAsync("K", () => { }, timeoutMs: 5000, cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        Assert.Equal(1, waiters.Count);
        Assert.True(waiters.Complete("K", 1.0));
        Assert.Equal(0, waiters.Count);
    }

    [Fact]
    public void CompleteWithoutAWaiterIsFalse()
    {
        var waiters = new FreshReadWaiters();

        Assert.False(waiters.Complete("K", 1.0));
    }

    [Fact]
    public async Task FailAllReleasesEveryWaiterWithNull()
    {
        var waiters = new FreshReadWaiters();
        var a = waiters.WaitAsync("A", () => { }, timeoutMs: 5000);
        var b = waiters.WaitAsync("B", () => { }, timeoutMs: 5000);

        waiters.FailAll();

        Assert.Null(await a);
        Assert.Null(await b);
        Assert.Equal(0, waiters.Count);
    }

    /// <summary>Two callers share one waiter; the first timing out must not orphan the second.</summary>
    [Fact]
    public async Task ACallerThatOverlapsATimedOutOne_StillGetsTheDelivery()
    {
        var waiters = new FreshReadWaiters();

        var first = waiters.WaitAsync("K", () => { }, timeoutMs: 20);
        var second = waiters.WaitAsync("K", () => { }, timeoutMs: 5000);
        Assert.Null(await first);

        Assert.True(waiters.Complete("K", 3.0));
        Assert.Equal(3.0, await second);
        Assert.Equal(0, waiters.Count);
    }
}
