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
    public void Reset_ForgetsEverything()
    {
        var g = new Md11AnnouncementGate();
        g.Feedback("X", "On");
        g.Reset();
        Assert.True(g.ShouldSpeakBackground("X", "On", 1));
    }
}
