using System.Collections.Concurrent;

namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// One-shot "wake me on the NEXT delivery of this key" waiters behind
/// <see cref="SimConnectManager.ReadFreshAsync"/>.
///
/// A forced read of an individual-def var answers on the next SimConnect dispatch (a frame or
/// two); a batch-covered var answers with its next continuous batch (up to one period). Either
/// way the delivery is the only trustworthy "fresh" signal. Sleeping a fixed interval and reading
/// the cache — the MD-11 walker's previous protocol — read stale values, called real movement "no
/// movement", and mis-learned the control's step polarity. Same idea as
/// <c>PMDGNG3DataManager.RequestFreshSnapshotAsync</c>, generalised to a key.
///
/// One waiter per key: concurrent callers share it and both wake on the one delivery.
/// Continuations run asynchronously so the SimConnect dispatch that completes a waiter never runs
/// walker code inline. Completion happens on the dispatch (UI) thread; waits come from pool
/// threads.
/// </summary>
internal sealed class FreshReadWaiters
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<double?>> _pending =
        new(StringComparer.Ordinal);

    /// <summary>Registered waiters, including any left over after a timeout until the next delivery clears it.</summary>
    public int Count => _pending.Count;

    /// <summary>
    /// Registers a waiter for <paramref name="key"/>, runs <paramref name="issueRequest"/> (the
    /// force-read) and waits for the next delivery. Returns the delivered value, or null when
    /// nothing arrives within <paramref name="timeoutMs"/> — never a stale cached value. Throws
    /// <see cref="OperationCanceledException"/> when <paramref name="ct"/> is cancelled: a
    /// cancelled walk must stop, not read.
    /// </summary>
    public async Task<double?> WaitAsync(string key, Action issueRequest, int timeoutMs, CancellationToken ct = default)
    {
        var tcs = _pending.GetOrAdd(key,
            _ => new TaskCompletionSource<double?>(TaskCreationOptions.RunContinuationsAsynchronously));
        try
        {
            issueRequest();
            return await tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Leave the waiter registered: another caller may share it, and the next delivery (or
            // FailAll) clears it. A later caller shares it too and correctly receives the first
            // delivery after its own request.
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }

    /// <summary>Completes the waiter for <paramref name="key"/>, if any. Call on EVERY delivery of the key, changed or not.</summary>
    public bool Complete(string key, double value)
    {
        return _pending.TryRemove(key, out var tcs) && tcs.TrySetResult(value);
    }

    /// <summary>Releases every waiter with null — a disconnect or aircraft switch means no delivery is coming.</summary>
    public void FailAll()
    {
        foreach (var key in _pending.Keys)
        {
            if (_pending.TryRemove(key, out var tcs)) tcs.TrySetResult(null);
        }
    }
}
