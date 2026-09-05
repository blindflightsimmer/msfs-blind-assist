using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Composed control state (spec §3.3–3.7): the hook MainForm labels buttons and status rows
/// from, the DC-power gate, lamp-change announcements and press feedback.
/// </summary>
public partial class TFDiMD11Definition
{
    /// <summary>Reads a state L:var (by NAME, as the state block spells it) from the shared cache.</summary>
    private double? ReadStateVar(string stateVar) => _sim?.GetCachedVariableValue(KeyFor(stateVar));

    /// <summary>True while the stock DC bus voltage says the annunciators have power (spec §3.4).</summary>
    public bool IsDcPowered() => Md11ControlState.IsPowered(_sim?.GetCachedVariableValue(DcPowerKey));

    /// <summary>
    /// MainForm's label seam. No opinion before <see cref="Attach"/> (no cache to read) and for
    /// controls without a state block (momentary buttons, knobs, switches, read-outs).
    /// </summary>
    public override bool TryDescribeControlState(string varKey, out string stateText)
    {
        stateText = "";
        if (_sim == null) return false;
        if (!_byNodeId.TryGetValue(varKey, out var c) || c.State == null) return false;

        var text = Md11ControlState.Compose(c.State, ReadStateVar, IsDcPowered());
        if (text == null) return false;
        stateText = text;
        return true;
    }
}
