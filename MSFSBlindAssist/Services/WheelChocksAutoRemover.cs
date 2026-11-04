namespace MSFSBlindAssist.Services;

/// <summary>
/// Decides when to take MSFS 2024's own wheel chocks off (the sim's "COVER ON:0", set with
/// K:COVER_SET — the same pair the sim tablet's Ground Services → Tools → Chocks button uses).
/// One instance per cover: MainForm also runs one for each of the sim's other covers
/// (<see cref="SimConnect.SimCovers"/> — engine, pitot, …), which hold an engine
/// at 0 % N2 with its starter engaged just as silently as the chocks hold the aircraft.
///
/// The chocks hold the aircraft at ANY thrust and are toggled only from a tablet a screen reader
/// cannot use, so a blind pilot meets them as "the aircraft will not move" with nothing saying
/// why (KDTW 2026-10-16: the iniBuilds A330 sat still at 93 % N1 with every brake released).
/// The app therefore removes them whenever it sees them on, and says so.
///
/// Pure: the caller feeds samples and acts on the returned <see cref="Action"/>.
/// </summary>
public sealed class WheelChocksAutoRemover
{
    public enum Action
    {
        None,
        /// <summary>Send COVER_SET 0.</summary>
        Remove,
        /// <summary>A removal we asked for has taken effect — say so.</summary>
        AnnounceRemoved,
        /// <summary>The chocks keep coming back; stop fighting whoever is placing them, and say so.</summary>
        AnnounceGivingUp,
    }

    /// <summary>Removals allowed inside <see cref="RetryWindow"/> before giving up.</summary>
    public const int MaxRemovalsInWindow = 3;

    /// <summary>Window over which repeated removals are counted.</summary>
    public static readonly TimeSpan RetryWindow = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The least time between two removals while the first is still pending. The covers are read
    /// every second whether or not they changed (so a removal that never takes still produces
    /// samples to retry and give up on), and the sim takes about a second to apply one — without
    /// this, the three removals would all go out inside three seconds and the give-up would be
    /// spoken over a removal that was about to land.
    /// </summary>
    public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);

    private readonly List<DateTime> _removals = new();
    private bool _pending;
    private bool _gaveUp;

    /// <summary>Feeds one reading of the chocks state.</summary>
    public Action OnSample(bool chocksOn, DateTime nowUtc)
    {
        if (!chocksOn)
        {
            if (!_pending) return Action.None;
            _pending = false;
            return Action.AnnounceRemoved;
        }

        if (_gaveUp) return Action.None;
        if (_pending && _removals.Count > 0 && nowUtc - _removals[^1] < RetryInterval) return Action.None;

        _removals.RemoveAll(t => nowUtc - t > RetryWindow);
        if (_removals.Count >= MaxRemovalsInWindow)
        {
            // Something (a GSX/ground-service script, the sim itself) keeps putting them back.
            // Leave them be rather than toggle forever; a context reset re-arms.
            _gaveUp = true;
            _pending = false;
            return Action.AnnounceGivingUp;
        }

        _removals.Add(nowUtc);
        _pending = true;
        return Action.Remove;
    }

    /// <summary>Forgets everything — call on a new connection or a newly loaded flight.</summary>
    public void Reset()
    {
        _removals.Clear();
        _pending = false;
        _gaveUp = false;
    }
}
