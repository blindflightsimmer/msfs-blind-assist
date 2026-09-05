using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Who gets to speak a control's state and when (spec §3.6–3.7): a background lamp change
/// speaks once per new text; the pilot's own press is confirmed once by the feedback path and
/// the lamps it lights must not repeat it.
/// </summary>
public class Md11AnnouncementGateTests
{
    [Fact]
    public void BackgroundChange_SpeaksOnce_ThenStaysQuietForTheSameText()
    {
        var g = new Md11AnnouncementGate();
        Assert.True(g.ShouldSpeakBackground("EXT", "On", 10_000));
        Assert.False(g.ShouldSpeakBackground("EXT", "On", 11_000));
        Assert.True(g.ShouldSpeakBackground("EXT", "Available", 12_000));
    }

    [Fact]
    public void LampChange_InsideTheEchoWindowOfAPress_IsSwallowed()
    {
        var g = new Md11AnnouncementGate();
        g.NotePress("GEN1", 50_000);
        Assert.False(g.ShouldSpeakBackground("GEN1", "Off", 50_000 + Md11AnnouncementGate.EchoWindowMs - 1));
        Assert.True(g.ShouldSpeakBackground("GEN1", "Off", 50_000 + Md11AnnouncementGate.EchoWindowMs + 1));
    }

    [Fact]
    public void Feedback_AlwaysSpeaks_AndSeedsTheDedup()
    {
        var g = new Md11AnnouncementGate();
        Assert.Equal("Armed", g.Feedback("GEN1", "Armed"));
        Assert.Equal("Armed", g.Feedback("GEN1", "Armed"));               // an inert press repeats the unchanged state on purpose
        Assert.False(g.ShouldSpeakBackground("GEN1", "Armed", 99_000));   // the lamp echo of that press stays quiet
    }

    [Fact]
    public void Feedback_ClosesTheEchoWindow_SoALaterCorrectionIsStillSpoken()
    {
        // A guarded press spends 550 ms lifting its cover before the button is written, so its
        // lamp can land after the feedback has already spoken the OLD state. That later lamp is
        // the correction, not an echo: it must speak. Same text still stays quiet.
        var g = new Md11AnnouncementGate();
        g.NotePress("HYD_TEST", 50_000);
        g.Feedback("HYD_TEST", "Off");                                     // spoke the stale state
        Assert.True(g.ShouldSpeakBackground("HYD_TEST", "Test", 50_100));  // the real lamp, inside the old window
        Assert.False(g.ShouldSpeakBackground("HYD_TEST", "Test", 50_200)); // and only once
    }

    [Fact]
    public void Feedback_StillSwallowsTheEchoOfItsOwnPress()
    {
        var g = new Md11AnnouncementGate();
        g.NotePress("GEN1", 50_000);
        g.Feedback("GEN1", "Off");
        Assert.False(g.ShouldSpeakBackground("GEN1", "Off", 50_100));   // same text — the echo
    }

    [Fact]
    public void Reset_ForgetsEverything()
    {
        var g = new Md11AnnouncementGate();
        g.Feedback("X", "On");
        g.Reset();
        Assert.True(g.ShouldSpeakBackground("X", "On", 1));
    }
}
