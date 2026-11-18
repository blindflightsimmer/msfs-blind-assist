using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist;

/// <summary>Aircraft-menu handler for the iniBuilds A330 (MSFS 2024).</summary>
public partial class MainForm
{
    private void IniA330MenuItem_Click(object? sender, EventArgs e)
    {
        SwitchAircraft(new IniA330Definition());
    }
}
