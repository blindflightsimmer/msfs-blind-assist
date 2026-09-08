using System.Runtime.InteropServices;
using Microsoft.FlightSimulator.SimConnect;
using static Microsoft.FlightSimulator.SimConnect.SimConnect;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// The simulator camera over SimConnect: one fixed data definition for <c>CAMERA STATE</c> and
/// the two halves of <c>CAMERA VIEW TYPE AND INDEX</c>, a one-shot read, and the write that moves
/// the camera. Backs <see cref="InstrumentViewSwitcher"/>, which AI display reads use to put a
/// particular instrument view in front of the capture (the MD-11's PFD/ND/EAD/SD/standby).
///
/// Live-verified on MSFS 2024 (2026-09-08): both SimVars are settable, the write applies within
/// a frame and the read reports it, from pilot, instrument and quickview cameras alike.
/// </summary>
public partial class SimConnectManager : ICameraViewIo
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct CameraViewData
    {
        public double State;
        public double ViewType;
        public double ViewIndex;
    }

    // One waiter shared by concurrent readers, completed on the dispatch of REQUEST_CAMERA_VIEW
    // and released with null on disconnect / aircraft switch (FailCameraViewRead). A waiter
    // abandoned by a timeout or a failed request is released at once (ReleaseCameraViewWaiter),
    // so its late delivery lands on nobody.
    private readonly object _cameraReadLock = new();
    private TaskCompletionSource<CameraViewReading?>? _cameraRead;

    /// <summary>
    /// Registers the camera definition with the other fixed definitions. Its own try/catch, like
    /// the GSX one beside it: a failure here degrades display reads to "capture the current
    /// view" and must not take the bulk registration down with it.
    /// </summary>
    private void RegisterCameraViewDefinition()
    {
        try
        {
            var sc = simConnect!;
            sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_CAMERA_VIEW, "CAMERA STATE", "number",
                SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)0);
            sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_CAMERA_VIEW, "CAMERA VIEW TYPE AND INDEX:0", "number",
                SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)1);
            sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_CAMERA_VIEW, "CAMERA VIEW TYPE AND INDEX:1", "number",
                SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)2);
            sc.RegisterDataDefineStruct<CameraViewData>(DATA_DEFINITIONS.DEF_CAMERA_VIEW);
            Log.Debug("SimConnect", "Registered camera view definition");
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"Camera view registration failed (display reads will capture the current view): {ex.Message}");
        }
    }

    /// <summary>
    /// One-shot read of the camera. Null when not connected, when the request cannot be issued,
    /// or when nothing arrives within <paramref name="timeoutMs"/> — never a stale value.
    /// </summary>
    public Task<CameraViewReading?> ReadCameraViewAsync(int timeoutMs)
    {
        if (!IsConnected || simConnect == null) return Task.FromResult<CameraViewReading?>(null);

        TaskCompletionSource<CameraViewReading?> tcs;
        lock (_cameraReadLock)
        {
            _cameraRead ??= new TaskCompletionSource<CameraViewReading?>(TaskCreationOptions.RunContinuationsAsynchronously);
            tcs = _cameraRead;
        }

        try
        {
            simConnect.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_CAMERA_VIEW,
                DATA_DEFINITIONS.DEF_CAMERA_VIEW, SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"Camera view request failed: {ex.Message}");
            ReleaseCameraViewWaiter(tcs);
            return Task.FromResult<CameraViewReading?>(null);
        }

        return AwaitCameraViewAsync(tcs, timeoutMs);
    }

    // No ConfigureAwait(false): the caller (an AI display read on the UI thread) writes SimVars
    // right after this returns, and SimConnect calls stay on the UI thread in this app.
    private async Task<CameraViewReading?> AwaitCameraViewAsync(TaskCompletionSource<CameraViewReading?> tcs, int timeoutMs)
    {
        try
        {
            return await tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs));
        }
        catch (TimeoutException)
        {
            // A timed-out request is abandoned: its late delivery must not answer the next read.
            ReleaseCameraViewWaiter(tcs);
            return null;
        }
    }

    /// <summary>
    /// Forgets <paramref name="tcs"/> if it is still the registered waiter, so a late delivery
    /// for an abandoned request lands on nobody instead of answering a newer read with the
    /// camera as it was before an earlier restore.
    /// </summary>
    private void ReleaseCameraViewWaiter(TaskCompletionSource<CameraViewReading?> tcs)
    {
        lock (_cameraReadLock)
        {
            if (ReferenceEquals(_cameraRead, tcs)) _cameraRead = null;
        }
    }

    /// <summary>Called from the dispatch for <c>REQUEST_CAMERA_VIEW</c>.</summary>
    private void CompleteCameraViewRead(CameraViewData data)
    {
        TaskCompletionSource<CameraViewReading?>? tcs;
        lock (_cameraReadLock)
        {
            tcs = _cameraRead;
            _cameraRead = null;
        }
        tcs?.TrySetResult(new CameraViewReading(
            (int)Math.Round(data.State),
            (int)Math.Round(data.ViewType),
            (int)Math.Round(data.ViewIndex)));
    }

    /// <summary>A disconnect or aircraft switch means no delivery is coming: release the waiter with null.</summary>
    private void FailCameraViewRead()
    {
        TaskCompletionSource<CameraViewReading?>? tcs;
        lock (_cameraReadLock)
        {
            tcs = _cameraRead;
            _cameraRead = null;
        }
        tcs?.TrySetResult(null);
    }

    /// <summary>
    /// Moves the camera: the view type first, then the index within it (the order verified live —
    /// index alone only works within the current type).
    /// </summary>
    public void SetCameraView(int viewType, int viewIndex)
    {
        SetSimVar("CAMERA VIEW TYPE AND INDEX:0", viewType);
        SetSimVar("CAMERA VIEW TYPE AND INDEX:1", viewIndex);
    }

    Task<CameraViewReading?> ICameraViewIo.ReadAsync(int timeoutMs) => ReadCameraViewAsync(timeoutMs);

    void ICameraViewIo.Set(int viewType, int viewIndex) => SetCameraView(viewType, viewIndex);
}
