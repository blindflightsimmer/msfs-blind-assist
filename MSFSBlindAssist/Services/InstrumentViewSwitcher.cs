using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The camera SimVars as the switcher sees them. <c>SimConnectManager</c> implements it
/// (SimConnectManager.Camera.cs); tests fake it.
/// </summary>
public interface ICameraViewIo
{
    /// <summary>One-shot read of the camera; null when nothing arrives within <paramref name="timeoutMs"/> or the sim is not connected.</summary>
    Task<CameraViewReading?> ReadAsync(int timeoutMs);

    /// <summary>Writes <c>CAMERA VIEW TYPE AND INDEX:0</c> (the type) then <c>:1</c> (the index).</summary>
    void Set(int viewType, int viewIndex);
}

/// <summary>
/// What an aircraft definition hands <c>BaseAircraftDefinition.ReadDisplay</c> to have the sim
/// moved to a particular instrument view before the capture: the camera to move (the
/// SimConnectManager) and the 0-based instrument view index from the aircraft's cameras.cfg.
/// </summary>
public sealed record InstrumentViewRequest(ICameraViewIo Camera, int ViewIndex);

/// <summary>
/// The result of <see cref="InstrumentViewSwitcher.EnterAsync"/>, and the way back. Created by
/// the switcher only.
/// </summary>
public sealed class InstrumentViewSession
{
    private readonly ICameraViewIo _io;
    private bool _restored;

    internal InstrumentViewSession(ICameraViewIo io, InstrumentViewOutcome outcome, bool verified, (int Type, int Index)? restoreTo)
    {
        _io = io;
        Outcome = outcome;
        Verified = verified;
        RestoreTo = restoreTo;
    }

    public InstrumentViewOutcome Outcome { get; }

    /// <summary>True when the camera was seen on the wanted view — including when it was there already.</summary>
    public bool Verified { get; }

    /// <summary>The view to put back, or null when nothing was known to restore (already there, refused, or unreadable).</summary>
    public (int Type, int Index)? RestoreTo { get; }

    /// <summary>
    /// Puts the previous view back. Idempotent and never throws: a failed restore is logged, the
    /// display read that owns this session must not die of it.
    /// </summary>
    public void Restore()
    {
        if (_restored || RestoreTo is not { } back) return;
        _restored = true;
        try
        {
            _io.Set(back.Type, back.Index);
        }
        catch (Exception ex)
        {
            Log.Debug("Camera", $"Restoring camera view type {back.Type} index {back.Index} failed: {ex.Message}");
        }
    }
}

/// <summary>
/// Moves the simulator camera to an instrument view for an AI display read: read, plan
/// (<see cref="InstrumentViewPlan"/>), write, verify by read-back, settle for a rendered frame.
/// Live-measured on MSFS 2024 (2026-09-08): the cut is instantaneous, so the read-back normally
/// matches on its first poll and the whole entry costs one settle.
/// </summary>
public sealed class InstrumentViewSwitcher
{
    public const int DefaultReadTimeoutMs = 500;
    public const int DefaultPollStepMs = 100;
    public const int DefaultVerifyCapMs = 1000;
    public const int DefaultSettleMs = 250;

    private readonly ICameraViewIo _io;
    private readonly Func<int, Task> _delay;
    private readonly int _readTimeoutMs;
    private readonly int _pollStepMs;
    private readonly int _verifyCapMs;
    private readonly int _settleMs;

    /// <param name="delay">Task.Delay in production; tests pass a recorder that completes at once.</param>
    public InstrumentViewSwitcher(
        ICameraViewIo io,
        Func<int, Task>? delay = null,
        int readTimeoutMs = DefaultReadTimeoutMs,
        int pollStepMs = DefaultPollStepMs,
        int verifyCapMs = DefaultVerifyCapMs,
        int settleMs = DefaultSettleMs)
    {
        _io = io;
        _delay = delay ?? (ms => Task.Delay(ms));
        _readTimeoutMs = readTimeoutMs;
        _pollStepMs = pollStepMs;
        _verifyCapMs = verifyCapMs;
        _settleMs = settleMs;
    }

    /// <summary>
    /// Reads the camera, writes the wanted instrument view when a write is called for, polls the
    /// read-back until it matches (giving up after the cap), then waits one settle so the sim has
    /// rendered a frame of the new view. Never throws; the session says what happened.
    /// </summary>
    public async Task<InstrumentViewSession> EnterAsync(int wantedIndex)
    {
        var plan = InstrumentViewPlan.For(await TryReadAsync(), wantedIndex);
        if (plan.Writes is not { } writes)
            return new InstrumentViewSession(_io, plan.Outcome, plan.Outcome == InstrumentViewOutcome.AlreadyThere, null);

        try
        {
            _io.Set(writes.Type, writes.Index);
        }
        catch (Exception ex)
        {
            Log.Debug("Camera", $"Setting camera view type {writes.Type} index {writes.Index} failed: {ex.Message}");
        }

        bool verified = false;
        for (int waitedMs = 0; ; waitedMs += _pollStepMs)
        {
            var now = await TryReadAsync();
            if (now is { } reading && InstrumentViewPlan.IsOn(reading, wantedIndex))
            {
                verified = true;
                break;
            }
            if (waitedMs >= _verifyCapMs) break;
            await _delay(_pollStepMs);
        }

        if (verified) await _delay(_settleMs);
        else Log.Debug("Camera", $"Instrument view {wantedIndex} did not verify within {_verifyCapMs} ms (outcome {plan.Outcome})");

        return new InstrumentViewSession(_io, plan.Outcome, verified, plan.Restore);
    }

    /// <summary>A read that throws is a read that returned nothing: the caller must always get its session, or the camera is never restored.</summary>
    private async Task<CameraViewReading?> TryReadAsync()
    {
        try
        {
            return await _io.ReadAsync(_readTimeoutMs);
        }
        catch (Exception ex)
        {
            Log.Debug("Camera", $"Reading the camera view failed: {ex.Message}");
            return null;
        }
    }
}
