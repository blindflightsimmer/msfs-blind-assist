using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.IniA330;

/// <summary>
/// Per-variable background-announcement manager for the iniBuilds A330 (Ctrl+M). Rows
/// come from <see cref="MonitorRowBuilder"/>: every Continuous + IsAnnounced variable not flagged
/// ExcludeFromMonitorManager. Unticked keys go to
/// UserSettings.A330DisabledMonitorVariables, which MainForm honours both through the
/// Suppressed wrap (DefAnnounceMuteSets — flap detents are spoken from inside ProcessSimVarUpdate)
/// and the generic gate; the flight-mode changes, spoken from a settle timer, check the list
/// themselves (row "Flight mode changes").
/// </summary>
public sealed class A330MonitorManagerForm : MonitorManagerFormBase
{
    private readonly ICollection<string> _disabled;

    /// <param name="disabled">The persisted mute list (UserSettings.A330DisabledMonitorVariables).</param>
    public A330MonitorManagerForm(Dictionary<string, SimVarDefinition> variables, string name, ICollection<string> disabled)
        : base($"{name} Monitor Manager", MonitorRowBuilder.Build(variables))
    {
        _disabled = disabled;
    }

    protected override ICollection<string> DisabledVariables => _disabled;
}
