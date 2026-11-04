using Microsoft.FlightSimulator.SimConnect;
using static Microsoft.FlightSimulator.SimConnect.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// MSFS 2024's own wheel chocks and covers — the sim tablet's Ground Services → Tools buttons,
/// which read <c>COVER ON:n</c> and set it with <c>K:COVER_SET</c>. The app watches the state so
/// MainForm can take them off (Services/WheelChocksAutoRemover).
///
/// Registered ONLY when MSFS 2024 is detected, each on its own definition, never inside the shared
/// continuous batches: the variable is not known to have existed in MSFS 2020, and an unknown
/// name or an index an aircraft does not have must not be able to disturb any other reading.
/// </summary>
public partial class SimConnectManager
{
    /// <summary>VarName carried by <see cref="SimVarUpdated"/> for the chocks state (1 = on).</summary>
    public const string WheelChocksVarName = "WHEEL_CHOCKS";

    // The connection the chocks and cover definitions were added to (a definition is per connection).
    private Microsoft.FlightSimulator.SimConnect.SimConnect? _wheelChocksRegisteredOn;

    /// <summary>Subscribes to the chocks and covers, once per connection. MSFS 2024 only.</summary>
    private void StartWheelChocksMonitor()
    {
        var sc = simConnect;
        if (!IsConnected || sc == null || DetectedSimulatorVersion != "FS2024") return;
        if (ReferenceEquals(_wheelChocksRegisteredOn, sc)) return;

        try
        {
            sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_WHEEL_CHOCKS, "COVER ON:0", "Bool",
                SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
            sc.RegisterDataDefineStruct<SingleValue>(DATA_DEFINITIONS.DEF_WHEEL_CHOCKS);
            sc.RequestDataOnSimObject(DATA_REQUESTS.REQUEST_WHEEL_CHOCKS, DATA_DEFINITIONS.DEF_WHEEL_CHOCKS,
                SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD.SECOND, SIMCONNECT_DATA_REQUEST_FLAG.CHANGED, 0, 0, 0);
            _wheelChocksRegisteredOn = sc;
            Log.Debug("SimConnect", "Wheel chocks monitor started (COVER ON:0)");
        }
        catch (Exception ex)
        {
            Log.Warn("SimConnect", $"Wheel chocks monitor could not start: {ex.Message}");
        }

        // The covers are read every second WITHOUT the CHANGED flag: a removal that does not take
        // leaves the value unchanged, and only further samples let the remover retry and then say
        // it could not remove them (the chocks have never failed to come off, the covers are new).
        foreach (var (index, _) in SimCovers.All)
        {
            try
            {
                var def = (DATA_DEFINITIONS)((int)DATA_DEFINITIONS.DEF_COVER_FIRST + index - 1);
                var req = (DATA_REQUESTS)((int)DATA_REQUESTS.REQUEST_COVER_FIRST + index - 1);
                sc.AddToDataDefinition(def, $"COVER ON:{index}", "Bool",
                    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SIMCONNECT_UNUSED);
                sc.RegisterDataDefineStruct<SingleValue>(def);
                sc.RequestDataOnSimObject(req, def, SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD.SECOND,
                    SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                Log.Warn("SimConnect", $"Cover {index} monitor could not start: {ex.Message}");
            }
        }
        Log.Debug("SimConnect", $"Covers monitor started (COVER ON:1..{SimCovers.All.Count})");
    }

    /// <summary>Takes MSFS 2024's wheel chocks off — the value the sim tablet's own button sends.</summary>
    public void RemoveWheelChocks() => SendEvent("COVER_SET", 0);

    /// <summary>
    /// Takes cover <paramref name="index"/> off: <c>COVER_SET</c> with the index first and the state
    /// (0 = off) second — the order of the only working example published (the sim's own JavaScript,
    /// <c>TRIGGER_KEY_EVENT("COVER_SET", true, 1, 0, 0)</c> = engine covers off), and the order under
    /// which the chocks' one-number <c>COVER_SET 0</c> already means "index 0, off". NOT verified in
    /// the sim when written (2026-10-28): the remover's read-back says out loud if it does not work.
    /// </summary>
    public void RemoveCover(int index) => SendEventEx1("COVER_SET", (uint)index, 0);
}
