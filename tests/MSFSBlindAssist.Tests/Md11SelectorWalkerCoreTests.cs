using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The walker's step protocol against a fake switch the walker cannot see into: only a state var,
/// only after the aircraft's own lag, only through the same IO seam production uses. These pin
/// the behaviour a blind pilot experiences at the combo: how many clicks a selection costs, that
/// a wrong polarity guess is learned once and persisted, that an end stop is not mistaken for an
/// inhibited control, and that a lagging read-back is not mistaken for "no movement".
/// </summary>
public class Md11SelectorWalkerCoreTests
{
    private const int Left = 90248, Right = 90249;

    /// <summary>A three-position switch (0 Off / 1 Auto / 2 On) with a virtual clock.</summary>
    private sealed class FakeSwitch
    {
        public int Position;
        public int Max = 2;
        public bool LeftIncreases = true;       // the walker's conventional guess is right by default
        public bool Inhibited;
        public int LagReads;                    // reads after a click that still show the old value
        public int DetentsPerClick = 1;         // 2 = the click lands twice
        public bool Fresh = true;               // false = the legacy cache-poll protocol
        public bool BlockUp;                    // clicks in the increasing direction are ignored (a one-way inhibit)
        public int ReadFreshCalls;
        public int RequestReadCalls;
        public readonly List<int> Clicks = new();
        public readonly List<long> ClickTimes = new();
        public long Clock;                      // virtual milliseconds
        public long FirstReadAt = -1;
        private int _visible;
        private int _pendingReads;

        public FakeSwitch(int start) { Position = start; _visible = start; }

        private void Fire(int id)
        {
            Clicks.Add(id);
            ClickTimes.Add(Clock);
            if (Inhibited) return;
            var up = (id == Left) == LeftIncreases;
            if (up && BlockUp) return;
            Position = Math.Clamp(Position + (up ? 1 : -1) * DetentsPerClick, 0, Max);
            _pendingReads = LagReads;
        }

        private double? Read()
        {
            if (FirstReadAt < 0) FirstReadAt = Clock;
            if (_pendingReads > 0) { _pendingReads--; return _visible; }
            _visible = Position;
            return _visible;
        }

        public Md11WalkIo Io() => new()
        {
            ReadFresh = _ => { ReadFreshCalls++; return Task.FromResult(Read()); },
            ReadCached = () => _visible,
            RequestRead = () => { RequestReadCalls++; Read(); },
            Fire = Fire,
            Delay = (ms, _) => { Clock += ms; return Task.CompletedTask; },
            Now = () => Clock,
            FreshReads = Fresh,
        };
    }

    private static string NewId() => "TEST_" + Guid.NewGuid().ToString("N");

    private static Md11Control Switch(string id) => new()
    {
        NodeId = id,
        Kind = Md11Kinds.Switch,
        StateVar = id,
        ValueMap = new Dictionary<string, string> { ["0"] = "Off", ["1"] = "Auto", ["2"] = "On" },
        Events = new Dictionary<string, int> { ["LEFT_BUTTON_DOWN"] = Left, ["RIGHT_BUTTON_DOWN"] = Right },
    };

    private static Task<bool> Walk(Md11Control c, double target, FakeSwitch sw)
        => Md11SelectorWalker.WalkCoreAsync(c, target, sw.Io());

    [Fact]
    public async Task ConventionalControl_TwoDetentsUp_CostsExactlyTwoClicks()
    {
        var id = NewId();
        var sw = new FakeSwitch(0);

        Assert.True(await Walk(Switch(id), 2, sw));

        Assert.Equal(new[] { Left, Left }, sw.Clicks);
        Assert.Equal(2, sw.Position);
        Assert.True(Md11SelectorWalker.PolarityFor(id) ?? true);   // never flipped
        Assert.Equal(0, sw.RequestReadCalls);        // the fresh protocol never polls the cache
        Assert.True(sw.ReadFreshCalls > 0);
    }

    [Fact]
    public async Task ConventionalControl_OneDetent_SettlesInWellUnderASecond()
    {
        var sw = new FakeSwitch(0);

        Assert.True(await Walk(Switch(NewId()), 1, sw));

        Assert.Single(sw.Clicks);
        Assert.InRange(sw.Clock, 1, 400);   // one step: a poll or two after the click, no 300 ms settle reads
    }

    [Fact]
    public async Task InvertedControl_MidRange_LearnsOnce_PersistsIt_AndLands()
    {
        var id = NewId();
        var saved = new List<(string Id, bool Conventional)>();
        Md11SelectorWalker.SavePolarity = (n, c) => { if (n == id) saved.Add((n, c)); };
        try
        {
            var sw = new FakeSwitch(1) { LeftIncreases = false };

            Assert.True(await Walk(Switch(id), 2, sw));

            // One wrong-way click (1 -> 0), then the learned direction twice (0 -> 1 -> 2).
            Assert.Equal(new[] { Left, Right, Right }, sw.Clicks);
            Assert.Equal(2, sw.Position);
            Assert.False(Md11SelectorWalker.PolarityFor(id));
            Assert.Equal(new[] { (id, false) }, saved);
        }
        finally { Md11SelectorWalker.SavePolarity = null; }
    }

    [Fact]
    public async Task InvertedControl_AtAnEndStop_ProbesTheOtherWay_ThenLands()
    {
        var id = NewId();
        var sw = new FakeSwitch(0) { LeftIncreases = false };

        Assert.True(await Walk(Switch(id), 2, sw));

        // Left at the bottom stop does nothing (the cap elapses), the probe Right moves toward the
        // target and teaches the polarity, then one more Right lands.
        Assert.Equal(new[] { Left, Right, Right }, sw.Clicks);
        Assert.Equal(2, sw.Position);
        Assert.False(Md11SelectorWalker.PolarityFor(id));
        Assert.True(sw.ClickTimes[1] - sw.ClickTimes[0] >= Md11SelectorWalker.StepCapMs);
    }

    [Fact]
    public async Task InhibitedControl_GivesUpAfterTryingBothDirections_WithoutLearning()
    {
        var id = NewId();
        var sw = new FakeSwitch(0) { Inhibited = true };

        Assert.False(await Walk(Switch(id), 1, sw));

        Assert.Equal(new[] { Left, Right }, sw.Clicks);
        Assert.True(Md11SelectorWalker.PolarityFor(id) ?? true);   // still conventional
    }

    /// <summary>
    /// A first no-movement probes the other event; when THAT moves away from the target the asked
    /// direction is simply blocked — the polarity must not flip — and the very next stall ends the
    /// walk (the second no-movement of the same walk), so a stuck control costs two caps, never the
    /// whole step budget.
    /// </summary>
    [Fact]
    public async Task AControlBlockedInTheAskedDirection_ProbesAway_ThenGivesUpOnTheSecondStall_WithoutFlipping()
    {
        var id = NewId();
        var sw = new FakeSwitch(1) { BlockUp = true };

        Assert.False(await Walk(Switch(id), 2, sw));

        // Left (up) stalls; the probe Right moves 1 -> 0, away; Left stalls again -> give up.
        Assert.Equal(new[] { Left, Right, Left }, sw.Clicks);
        Assert.Equal(0, sw.Position);
        Assert.True(Md11SelectorWalker.PolarityFor(id) ?? true);
    }

    [Fact]
    public async Task LaggingReadBack_IsNotMistakenForNoMovement()
    {
        var sw = new FakeSwitch(0) { LagReads = 4 };   // the aircraft shows the old value for four reads

        Assert.True(await Walk(Switch(NewId()), 1, sw));

        Assert.Equal(new[] { Left }, sw.Clicks);      // exactly one click: no retry, no probe
    }

    [Fact]
    public async Task PersistedInvertedPolarity_IsUsedFromTheFirstClick()
    {
        var id = NewId();
        var saves = 0;
        Md11SelectorWalker.LoadPolarity = n => n == id ? false : null;
        Md11SelectorWalker.SavePolarity = (n, _) => { if (n == id) saves++; };
        try
        {
            var sw = new FakeSwitch(0) { LeftIncreases = false };

            Assert.True(await Walk(Switch(id), 1, sw));

            Assert.Equal(new[] { Right }, sw.Clicks);   // the inverted mapping, straight away
            Assert.Equal(0, saves);                       // nothing new was learned
        }
        finally { Md11SelectorWalker.LoadPolarity = null; Md11SelectorWalker.SavePolarity = null; }
    }

    [Fact]
    public async Task ARecentClickOnTheSameControl_IsAllowedToSettleBeforeTheFirstRead()
    {
        var id = NewId();
        var control = Switch(id);
        var sw = new FakeSwitch(0);
        Assert.True(await Walk(control, 1, sw));
        var lastClick = sw.ClickTimes[^1];

        sw.FirstReadAt = -1;                          // the very next walk on this node
        Assert.True(await Walk(control, 2, sw));

        Assert.True(sw.FirstReadAt >= lastClick + Md11SelectorWalker.ClickSettleMs,
            $"first read at {sw.FirstReadAt}, last click at {lastClick}");
    }

    [Fact]
    public async Task BatchCoveredVar_KeepsTheLegacyProtocol_NoProbeOnNoMovement()
    {
        var id = NewId();
        var sw = new FakeSwitch(0) { LeftIncreases = false, Fresh = false };

        Assert.False(await Walk(Switch(id), 2, sw));

        Assert.Equal(new[] { Left }, sw.Clicks);       // one no-movement ends the walk, as before
        Assert.True(Md11SelectorWalker.PolarityFor(id) ?? true);
        Assert.Equal(0, sw.ReadFreshCalls);          // the legacy protocol never takes a fresh read
        Assert.True(sw.RequestReadCalls > 0);
    }

    [Fact]
    public async Task AClickThatLandsTwice_MovesTowardTheTarget_WithoutFlippingPolarity()
    {
        var id = NewId();
        var sw = new FakeSwitch(0) { DetentsPerClick = 2 };

        Assert.True(await Walk(Switch(id), 2, sw));

        Assert.Equal(new[] { Left }, sw.Clicks);
        Assert.True(Md11SelectorWalker.PolarityFor(id) ?? true);
    }

    [Fact]
    public async Task AnUnreachableDetent_ExhaustsTheBudget_WithoutCorruptingPolarity()
    {
        var id = NewId();
        var sw = new FakeSwitch(0) { DetentsPerClick = 2 };   // 0 <-> 2 only; 1 is unreachable

        Assert.False(await Walk(Switch(id), 1, sw));

        Assert.Equal(24, sw.Clicks.Count);                       // MaxSteps, bounded
        Assert.True(Md11SelectorWalker.PolarityFor(id) ?? true); // direction-based learning never flipped
    }

    [Fact]
    public void OnDetent_PointDetentsOnly()
    {
        var control = Switch(NewId());
        var ordered = Md11SelectorWalker.OrderedValues(control);

        Assert.True(Md11SelectorWalker.OnDetent(control, ordered, 1.0));
        Assert.True(Md11SelectorWalker.OnDetent(control, ordered, 1.005));
        Assert.False(Md11SelectorWalker.OnDetent(control, ordered, 1.3));

        var lever = Md11ControlMap.Load().Controls.First(c => c.NodeId == Md11FlapSystem.LeverKey);
        var leverOrdered = Md11SelectorWalker.OrderedValues(lever);
        Assert.True(Md11SelectorWalker.OnDetent(lever, leverOrdered, 70));    // a point detent
        Assert.False(Md11SelectorWalker.OnDetent(lever, leverOrdered, 50));   // Dial-A-Flap is a range, never "settled" by value alone
    }
}
