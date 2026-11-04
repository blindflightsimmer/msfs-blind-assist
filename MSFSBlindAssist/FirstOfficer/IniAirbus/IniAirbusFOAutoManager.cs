namespace MSFSBlindAssist.FirstOfficer.IniAirbus;

/// <summary>
/// iniBuilds A330 / A321LR flap-management placeholder, the Fenix's shape: gear and AP engage are the
/// app-wide UniversalAutomationService's, and there is deliberately NO auto-flap schedule here. The
/// definition moves the flap lever with a stock FLAPS_SET calculator write nothing has measured on
/// these aircraft, so the First Officer never moves the flaps itself (FO-15 lists the aircraft the
/// "Auto-manage flaps" setting acts on; these are not among them). AutoFlapsEnabled is stored, never
/// acted on.
/// </summary>
public sealed class IniAirbusFOAutoManager : IFoAutoManager
{
    public bool AutoFlapsEnabled { get; set; }

    public void Reset() { }

    public void Update(double altitudeMsl, double verticalSpeedFpm, double altitudeAgl, double airspeedKts, bool onGround) { }
}
