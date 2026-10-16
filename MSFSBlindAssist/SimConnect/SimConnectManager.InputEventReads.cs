using Microsoft.FlightSimulator.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// READS of MSFS 2024 input events (<c>B:</c> events). A variable whose <see cref="SimVarDefinition.Type"/>
/// is <see cref="SimVarType.InputEvent"/> names an input event of the loaded aircraft; it gets no data
/// definition and no batch slot — instead, once the aircraft's input events are enumerated (their hashes
/// are only known then), each is SUBSCRIBED (SubscribeInputEvent, pushes every change) and read once
/// (GetInputEvent) for its current value. Values land in the same cache and raise the same
/// <see cref="SimVarUpdated"/> as any other variable, so panels, read-outs and announcements treat them
/// alike. Built for the iniBuilds A330, whose cockpit switches ARE input events and whose
/// position lives in the event's own value (many switches move no L:var at all).
///
/// Opt-in by type: no aircraft that registers no InputEvent variable sends a single request here.
/// GetInputEvent answers carry only their request id, so each read takes the next id from a rotating
/// range (REQUEST_INPUT_EVENT_READ, 701-890) mapped back to the variable keys it serves.
/// </summary>
public partial class SimConnectManager
{
    public const uint InputEventReadIdCount = 190;

    private readonly object _inputEventReadLock = new();
    /// <summary>Subscribed input event hash -> the variable keys that read it.</summary>
    private readonly Dictionary<ulong, List<string>> _inputEventReadKeys = new();
    /// <summary>Outstanding GetInputEvent request id -> hash.</summary>
    private readonly Dictionary<uint, ulong> _inputEventReadRequests = new();
    private uint _inputEventReadSeq;

    /// <summary>Subscribes every InputEvent variable of the current aircraft whose event exists and
    /// reads its value once. Called when the input-event enumeration completes.</summary>
    private void SubscribeInputEventReads()
    {
        if (!IsConnected || simConnect == null) return;
        var variables = CurrentAircraft?.GetVariables();
        if (variables == null) return;
        int subscribed = 0, missing = 0;
        lock (_inputEventReadLock)
        {
            foreach (var kv in variables)
            {
                if (kv.Value.Type != SimVarType.InputEvent || kv.Value.UpdateFrequency == UpdateFrequency.Never) continue;
                if (!inputEventHashes.TryGetValue(kv.Value.Name, out ulong hash)) { missing++; continue; }
                if (!_inputEventReadKeys.TryGetValue(hash, out var keys))
                {
                    _inputEventReadKeys[hash] = keys = new List<string>();
                    try { simConnect.SubscribeInputEvent(hash); subscribed++; }
                    catch (Exception ex) { Log.Debug("SimConnect", $"SubscribeInputEvent {kv.Value.Name} failed: {ex.Message}"); }
                }
                if (!keys.Contains(kv.Key)) keys.Add(kv.Key);
            }
        }
        List<ulong> hashes;
        lock (_inputEventReadLock) hashes = _inputEventReadKeys.Keys.ToList();
        foreach (var hash in hashes) RequestInputEventRead(hash);
        if (subscribed + missing > 0)
            Log.Info("SimConnect", $"InputEvent reads: {subscribed} subscribed, {missing} not present in this aircraft");
    }

    /// <summary>
    /// Re-subscribes the input-event reads for the aircraft now selected. Called on an Aircraft-menu
    /// switch (no reconnect, so no enumeration ever runs) and on an in-sim aircraft load (the hashes
    /// can change with the model). Without it the reads were subscribed only at the first full connect,
    /// so a pilot who picked the A330 from the menu while connected got no state for any control read
    /// back from its input event. Re-enumerates only when the hashes are not known yet or the aircraft
    /// model changed; otherwise re-subscribes from the known hashes. Safe from any thread.
    /// </summary>
    public void RefreshInputEventReads(bool reenumerate)
    {
        if (_uiGate.PostIfOffThread(() => RefreshInputEventReads(reenumerate))) return;
        if (!IsConnected || simConnect == null) return;
        bool wantsReads = CurrentAircraft?.GetVariables().Values.Any(d => d.Type == SimVarType.InputEvent) == true;
        if (reenumerate || inputEventHashes.Count == 0)
        {
            // Only an aircraft that reads input events needs the catalogue rebuilt on a load; others
            // keep today's single enumeration at connect.
            if (wantsReads || inputEventHashes.Count == 0) RequestEnumerateInputEvents();
            return;
        }
        UnsubscribeInputEventReads();
        if (wantsReads) SubscribeInputEventReads();
    }

    /// <summary>Forgets every subscription without talking to SimConnect (the connection is gone).</summary>
    private void ForgetInputEventReads()
    {
        lock (_inputEventReadLock)
        {
            _inputEventReadKeys.Clear();
            _inputEventReadRequests.Clear();
        }
    }

    /// <summary>Drops every input-event subscription (the next aircraft has other events and hashes).</summary>
    private void UnsubscribeInputEventReads()
    {
        lock (_inputEventReadLock)
        {
            if (simConnect != null)
                foreach (var hash in _inputEventReadKeys.Keys)
                    try { simConnect.UnsubscribeInputEvent(hash); } catch { }
            _inputEventReadKeys.Clear();
            _inputEventReadRequests.Clear();
        }
    }

    private void RequestInputEventRead(ulong hash)
    {
        if (!IsConnected || simConnect == null) return;
        uint id;
        lock (_inputEventReadLock)
        {
            id = (uint)DATA_REQUESTS.REQUEST_INPUT_EVENT_READ + (_inputEventReadSeq++ % InputEventReadIdCount);
            _inputEventReadRequests[id] = hash;
        }
        try { simConnect.GetInputEvent((DATA_REQUESTS)id, hash); }
        catch (Exception ex) { Log.Debug("SimConnect", $"GetInputEvent failed: {ex.Message}"); }
    }

    /// <summary>A forced read of an InputEvent variable (RequestVariable with this type lands here).</summary>
    internal bool TryRequestInputEventVariable(SimVarDefinition def)
    {
        if (def.Type != SimVarType.InputEvent) return false;
        // inputEventHashes and the SimConnect handle belong to the UI thread (the RequestVariable rule).
        if (_uiGate.PostIfOffThread(() => TryRequestInputEventVariable(def))) return true;
        if (inputEventHashes.TryGetValue(def.Name, out ulong hash)) RequestInputEventRead(hash);
        return true;
    }

    private void SimConnect_OnRecvGetInputEvent(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_GET_INPUT_EVENT data)
    {
        ulong hash;
        lock (_inputEventReadLock)
        {
            if (!_inputEventReadRequests.Remove(data.dwRequestID, out hash)) return;
        }
        if (TryDouble(data.Value, out double v)) DeliverInputEventValue(hash, v, forced: true);
    }

    private void SimConnect_OnRecvSubscribeInputEvent(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_SUBSCRIBE_INPUT_EVENT data)
    {
        if (TryDouble(data.Value, out double v)) DeliverInputEventValue(data.Hash, v, forced: false);
    }

    private static bool TryDouble(object[]? value, out double v)
    {
        v = 0;
        if (value == null || value.Length == 0 || value[0] == null) return false;
        try { v = Convert.ToDouble(value[0], System.Globalization.CultureInfo.InvariantCulture); return !double.IsNaN(v); }
        catch { return false; }
    }

    private void DeliverInputEventValue(ulong hash, double value, bool forced)
    {
        List<string>? keys;
        lock (_inputEventReadLock)
        {
            if (!_inputEventReadKeys.TryGetValue(hash, out keys)) return;
            keys = keys.ToList();
        }
        var variables = CurrentAircraft?.GetVariables();
        if (variables == null) return;
        foreach (var key in keys)
        {
            // A stale subscription must never write into another aircraft's same-named L:var key.
            if (!variables.TryGetValue(key, out var def) || def.Type != SimVarType.InputEvent) continue;
            double? last = lastVariableValues.TryGetValue(key, out double cached) ? cached : null;
            bool changed = IsValueChange(last, value, def);
            lastVariableValues[key] = value;
            _freshReads.Complete(key, value);
            if (!changed && !forced) continue;
            if (!changed && last.HasValue) continue;   // a read-back of an unchanged value says nothing
            SimVarUpdated?.Invoke(this, new SimVarUpdateEventArgs
            {
                VarName = key,
                Value = value,
                Description = FormatVariableValue(key, def, value),
            });
        }
    }
}
