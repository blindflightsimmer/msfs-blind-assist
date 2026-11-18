using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer.IniAirbus;

/// <summary>
/// iniBuilds A330-200 (INI_A330) First Officer profile, bound to <see cref="IniAirbusKeys.A330"/>; the
/// live definition (the executor writes through its own <c>HandleUIVariableSet</c>) is passed in by
/// <c>Forms.FirstOfficer.FirstOfficerWindows</c>.
/// </summary>
public sealed class IniAirbusFoProfile : IFoProfile<IniAirbusActionExecutor, IniAirbusStateEvaluator>
{
    private readonly IniA330Definition _def;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly IniAirbusKeys _keys = IniAirbusKeys.A330;

    public IniAirbusFoProfile(IniA330Definition def, ScreenReaderAnnouncer announcer)
    {
        _def = def;
        _announcer = announcer;
    }

    public IniAirbusKeys Keys => _keys;
    public string Title => _keys.Title;

    public IniAirbusActionExecutor CreateExecutor() => new(_keys);
    public IniAirbusStateEvaluator CreateEvaluator() => new(_keys);

    public void BindDataManager(IniAirbusStateEvaluator state, SimConnectManager sc) => state.SetSimConnect(sc);

    public void SetExecutorSimConnect(IniAirbusActionExecutor exec, SimConnectManager? sc)
    {
        exec.SetDefinition(_def);
        exec.SetAnnouncer(_announcer);
        exec.SetSimConnect(sc);
    }

    public List<FlowDefinition<IniAirbusStateEvaluator>> BuildFlows() => IniAirbusFlowDefinitions.Build(_keys);

    public List<ChecklistGroup<IniAirbusActionExecutor, IniAirbusStateEvaluator>> BuildChecklists()
        => IniAirbusChecklistDefinitions.Build(_keys);

    public IFoAutoManager CreateAutoManager(IniAirbusActionExecutor exec, IniAirbusStateEvaluator state,
        ScreenReaderAnnouncer a, UserSettings s)
        => new IniAirbusFOAutoManager { AutoFlapsEnabled = s.FOAutoFlapsEnabled };

    public IFoPhaseMonitor CreatePhaseMonitor(IniAirbusActionExecutor exec, IniAirbusStateEvaluator state, ScreenReaderAnnouncer a)
        => new IniAirbusFlightPhaseMonitor(exec, a);
}
