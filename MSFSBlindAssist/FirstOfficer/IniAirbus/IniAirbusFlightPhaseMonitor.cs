using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.FirstOfficer.IniAirbus;

/// <summary>
/// The iniBuilds A330 / A321LR phase monitor: the 10,000 ft landing lights (climbing through 10,300 ft
/// the landing lights stow — A330 OFF, A321 RETRACT — and the nose light goes OFF; descending through
/// 9,700 ft both come back on), the seat-belt automation (SeatbeltAutomation, through the executor's
/// SetSeatbeltSign), and the transition altimeters as REMINDERS ONLY: the STD presses of this code base
/// are untested (docs/ini-a330.md), so the First Officer never presses them and every crossing is
/// spoken (TransitionAltimeters.ReminderInstead with the write path treated as off), whatever the "set
/// altimeters automatically" setting says. Crossing calls are queued (Announce), as on the other
/// monitors.
/// </summary>
public sealed class IniAirbusFlightPhaseMonitor : IFoPhaseMonitor
{
    private readonly IniAirbusActionExecutor _executor;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly SeatbeltAutomation _seatbelt;
    private readonly TransitionCrossingDetector _trans = new();

    private const int LandingLightThresholdFt = 10_000;
    private const int HysteresisFt = 300;

    private bool? _prevAbove10k;
    private bool _noTransReminderFired;

    public IniAirbusFlightPhaseMonitor(IniAirbusActionExecutor executor, ScreenReaderAnnouncer announcer)
    {
        _executor = executor;
        _announcer = announcer;
        _seatbelt = new SeatbeltAutomation(on => { _ = _executor.SetSeatbeltSign(on); }, announcer.Announce);
    }

    public bool AutoLights10kEnabled { get; set; } = true;

    /// <summary>Stored for the interface; it never lets the First Officer press STD (see the summary).</summary>
    public bool AutoAltimetersEnabled { get; set; } = true;

    /// <summary>False: this aircraft's altimeter STD presses are not verified, so every crossing is a
    /// reminder.</summary>
    internal const bool AltimeterWritesVerified = false;

    public FoSeatbeltMode AutoSeatbeltMode
    {
        get => _seatbelt.Mode;
        set => _seatbelt.Mode = value;
    }

    public void SetPlannedCruiseAltitude(int? cruiseFt) => _seatbelt.PlannedCruiseFt = cruiseFt;

    public void SetThresholds(int transAltFt, int transLevelFt) => _trans.SetThresholds(transAltFt, transLevelFt);

    public void Reset()
    {
        _prevAbove10k = null;
        _trans.Reset();
        _noTransReminderFired = false;
        _seatbelt.Reset();
    }

    public void Update(double altitudeFt, double verticalSpeedFpm, bool onGround)
    {
        if (!_executor.IsAvailable) return;
        bool climbing = verticalSpeedFpm > 150;
        bool descending = verticalSpeedFpm < -150;

        Check10kCrossing(altitudeFt, climbing, descending);
        _seatbelt.Update(altitudeFt, verticalSpeedFpm, onGround);

        if (_trans.HasThresholds)
        {
            var crossing = _trans.Update(altitudeFt, climbing, descending);
            if (TransitionAltimeters.ReminderInstead(crossing, AltimeterWritesVerified) is { } reminder)
                _announcer.Announce(reminder);
        }
        else if (!_noTransReminderFired && climbing && altitudeFt > 18_000 + HysteresisFt)
        {
            _noTransReminderFired = true;
            _announcer.Announce(TransitionAltimeters.NoTransitionLoadedReminder(AltimeterWritesVerified));
        }
        else if (_noTransReminderFired && altitudeFt < 17_000)
        {
            _noTransReminderFired = false;
        }
    }

    private void Check10kCrossing(double alt, bool climbing, bool descending)
    {
        bool nowAbove = alt > LandingLightThresholdFt + HysteresisFt;
        bool nowBelow = alt < LandingLightThresholdFt - HysteresisFt;

        if (AutoLights10kEnabled)
        {
            if (!descending && nowAbove && _prevAbove10k == false)
            {
                _ = _executor.SetLandingLights(IniAirbusActionExecutor.LandingStowed);
                _ = _executor.SetNoseLight(2);
                _announcer.Announce("Above ten thousand. Landing lights off.");
            }
            else if (!climbing && nowBelow && _prevAbove10k == true)
            {
                _ = _executor.SetLandingLights(IniAirbusActionExecutor.LandingOn);
                _ = _executor.SetNoseLight(0);
                _announcer.Announce("Below ten thousand. Landing lights on.");
            }
        }

        // the latch follows the aircraft whatever the setting (FO-19)
        if (nowAbove) _prevAbove10k = true;
        else if (nowBelow) _prevAbove10k = false;
    }
}
