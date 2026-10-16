using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist;

/// <summary>Aircraft-menu handlers for the iniBuilds A330 and A321LR (MSFS 2024).</summary>
public partial class MainForm
{
    private void IniA330MenuItem_Click(object? sender, EventArgs e)
    {
        SwitchAircraft(new IniA330Definition());
    }

    private void IniA321MenuItem_Click(object? sender, EventArgs e)
    {
        SwitchAircraft(new IniA321Definition());
    }
}
