using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer.IniAirbus;

/// <summary>
/// iniBuilds A330-200 (INI_A330) / A321LR (INI_A321) / A340-300 (INI_A340) First Officer profile. One
/// profile class for all three airframes, as the definitions share one code base: <see cref="ForA330"/>,
/// <see cref="ForA321"/> and <see cref="ForA340"/> bind it to that airframe's <see cref="IniAirbusKeys"/>; the live definition (the executor writes
/// through its own <c>HandleUIVariableSet</c>) is passed in by <c>Forms.FirstOfficer.FirstOfficerWindows</c>.
/// </summary>
public sealed class IniAirbusFoProfile : IFoProfile<IniAirbusActionExecutor, IniAirbusStateEvaluator>
{
    private readonly IniA330Definition _def;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly IniAirbusKeys _keys;

    public IniAirbusFoProfile(IniA330Definition def, ScreenReaderAnnouncer announcer, IniAirbusKeys keys)
    {
        _def = def;
        _announcer = announcer;
        _keys = keys;
    }

    public static IniAirbusFoProfile ForA330(IniA330Definition def, ScreenReaderAnnouncer a) => new(def, a, IniAirbusKeys.A330);
    public static IniAirbusFoProfile ForA321(IniA321Definition def, ScreenReaderAnnouncer a) => new(def, a, IniAirbusKeys.A321);

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
