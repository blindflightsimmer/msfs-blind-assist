using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.IniA300;

/// <summary>
/// Per-variable background-announcement manager for the iniBuilds A330 and A321LR (Ctrl+M). Rows
/// come from <see cref="MonitorRowBuilder"/>: every Continuous + IsAnnounced variable not flagged
/// ExcludeFromMonitorManager. Unticked keys go to the airframe's own list
/// (UserSettings.A330/A321DisabledMonitorVariables), which MainForm honours both through the
/// Suppressed wrap (DefAnnounceMuteSets — flap detents are spoken from inside ProcessSimVarUpdate)
/// and the generic gate; the flight-mode changes, spoken from a settle timer, check the list
/// themselves (row "Flight mode changes").
/// </summary>
public sealed class A300MonitorManagerForm : MonitorManagerFormBase
{
    private readonly ICollection<string> _disabled;

    /// <param name="disabled">The airframe's persisted mute list (each iniBuilds airframe keeps its own).</param>
    public A300MonitorManagerForm(Dictionary<string, SimVarDefinition> variables, string name, ICollection<string> disabled)
        : base($"{name} Monitor Manager", MonitorRowBuilder.Build(variables))
    {
        _disabled = disabled;
    }

    protected override ICollection<string> DisabledVariables => _disabled;
}
