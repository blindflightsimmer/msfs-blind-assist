using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// iniBuilds A330 (MSFS 2024 — the A330-200 shipped with the sim, a streamed and ENCRYPTED package).
///
/// MECHANISM MAP (everything measured live 2026-10-16; docs/ini-a330.md):
///  * Cockpit switches are the aircraft's FS2024 INPUT EVENTS (<c>B:AIRLINER_*</c>, 649 of them). The
///    panels come from the generated map (Resources/a330_control_map.json, tools/a330-gen) whose every
///    read-back and position was found by a probe that set each event and recorded which of the
///    1,936 <c>INI_*</c> L:vars moved. Selectors and latching switches are written with SetInputEvent
///    (no MobiFlight needed); momentary keys need the event's own <c>_Push</c>/<c>_Release</c> actions
///    over the calculator path, because a raw value does nothing on them. Switches that move no L:var
///    are read from the event itself (<see cref="SimVarType.InputEvent"/>, SimConnectManager.InputEventReads).
///  * Autoflight is the A300's iniBuilds code: the FCU windows are the same <c>INI_*_DIAL</c> L:vars and
///    a direct write sets them (heading 270 written, the stock AP heading followed); the knob pushes
///    and pulls are the same <c>INI_FCU_*_BUTTON</c> press flags; the flight modes are the same
///    <c>INI_PITCH_MODE</c>/<c>INI_ROLL_MODE</c> numbers, spoken with A330 words (<see cref="A330Fma"/>).
///  * MCDU: no text export (the FS2024 A300's <c>iniAirbusMCDU_1</c> area does not exist here), so it is
///    read from the pixels of a forced pop-out exactly as the A300 is, with its own grid and glyphs.
///  * EFB: a Coherent HTML page (<c>iniEfbA330</c>), scraped into the shared EFB shell.
/// </summary>
public partial class IniA330Definition : BaseAircraftDefinition
{
    public override string AircraftName => "iniBuilds A330";
    public override string AircraftCode => "INI_A330";

    internal const string AutopilotSection = "Glareshield";
    internal const string AutopilotPanel = "Autopilot";
    internal const string FlightModesPanel = "Flight Modes";
    /// <summary>The airframe's name in window titles and messages.</summary>
    internal virtual string ShortName => "A330";

    private readonly A330ControlMap _map;
    private int _seq;

    public IniA330Definition() : this(A330ControlMap.Embedded) { }

    internal IniA330Definition(A330ControlMap map) => _map = map;

    internal virtual HashSet<string> DisabledMonitorSet => Settings.SettingsManager.Current.A330DisabledMonitorVariablesSet;
    internal virtual List<string> DisabledMonitorList => Settings.SettingsManager.Current.A330DisabledMonitorVariables;

    /// <summary>The airframe's name for one of the A330's input events (the A321 names its FCU, autobrake and
    /// parking brake events differently). Null = this airframe has no such control.</summary>
    internal virtual string? Ev(string a330Event) => a330Event;

    public override FCUControlType GetAltitudeControlType() => FCUControlType.SetValue;
    public override FCUControlType GetHeadingControlType() => FCUControlType.SetValue;
    public override FCUControlType GetSpeedControlType() => FCUControlType.SetValue;
    public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.SetValue;

    public override Dictionary<string, string> GetButtonStateMapping() => new();

    /// <summary>RPN through the calculator path, made unique so MobiFlight never drops a repeat.</summary>
    // Interlocked: writes come from the UI thread and from read-back tasks; two writes sharing a
    // sequence number would be coalesced by MobiFlight and the second dropped.
    internal void Write(SimConnectManager sim, string rpn) => sim.ExecuteCalculatorCode($"{Interlocked.Increment(ref _seq)} 0 * {rpn}");

    /// <summary>Pulse an iniBuilds press flag (write 1; the module consumes it).</summary>
    internal void Pulse(SimConnectManager sim, string lvar) => Write(sim, $"1 (>L:{lvar})");

    // ---------------- panels ----------------

    public override Dictionary<string, List<string>> GetPanelStructure()
    {
        var s = new Dictionary<string, List<string>>();
        void AddPanel(string section, string panel)
        {
            if (!s.TryGetValue(section, out var list)) s[section] = list = new List<string>();
            if (!list.Contains(panel)) list.Add(panel);
        }
        AddPanel(AutopilotSection, AutopilotPanel);
        AddPanel(AutopilotSection, FlightModesPanel);
        foreach (var sec in _map.Sections)
            foreach (var p in sec.Panels)
                AddPanel(sec.Name, p);
        foreach (var (section, panel, _) in OwnPlacedControls) AddPanel(section, panel);
        return s;
    }

    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        var vars = GetVariables();
        var p = new Dictionary<string, List<string>>();
        void Add(string panel, string key)
        {
            if (!p.TryGetValue(panel, out var list)) p[panel] = list = new List<string>();
            if (!list.Contains(key)) list.Add(key);
        }
        foreach (var k in Buttons.Select(b => b.key).Concat(SelectKeys)) Add(AutopilotPanel, k);
        foreach (var (_, panel, k) in OwnPlacedControls) Add(panel, k);
        p.TryAdd(FlightModesPanel, new List<string>());
        foreach (var c in _map.Controls)
            if (c.Kind != "status" && vars.ContainsKey(c.Key)) Add(c.Panel, c.Key);
        return p;
    }

    public override Dictionary<string, List<string>> GetPanelDisplayVariables()
    {
        GetVariables();
        var d = new Dictionary<string, List<string>>();
        void Add(string panel, string key)
        {
            if (!d.TryGetValue(panel, out var list)) d[panel] = list = new List<string>();
            if (!list.Contains(key)) list.Add(key);
        }
        foreach (var k in FmaDisplayKeys) Add(FlightModesPanel, k);
        foreach (var k in AutopilotDisplayKeys) Add(AutopilotPanel, k);
        foreach (var (panel, key) in OwnPlacedDisplays) Add(panel, key);
        foreach (var (panel, key) in _mapDisplayRows) Add(panel, key);
        return d;
    }
}
