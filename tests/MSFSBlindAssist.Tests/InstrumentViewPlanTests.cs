using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins what an AI display read does with the simulator camera before it captures. The numbers
/// come from a live MSFS 2024 session (2026-09-08): CAMERA STATE 2 is a cockpit camera, view
/// type 2 is an instrument view, the index is 0-based (the sim's "instrument view 1" is index 0).
/// </summary>
public class InstrumentViewPlanTests
{
    [Fact]
    public void AnExternalCamera_IsRefused_WithNothingWritten()
    {
        var plan = InstrumentViewPlan.For(new CameraViewReading(State: 3, ViewType: 0, ViewIndex: 0), wantedIndex: 2);

        Assert.Equal(InstrumentViewOutcome.NotInCockpit, plan.Outcome);
        Assert.Null(plan.Writes);
        Assert.Null(plan.Restore);
    }

    [Fact]
    public void TheWantedInstrumentView_NeedsNoWriteAndNoRestore()
    {
        var plan = InstrumentViewPlan.For(new CameraViewReading(2, 2, 2), wantedIndex: 2);

        Assert.Equal(InstrumentViewOutcome.AlreadyThere, plan.Outcome);
        Assert.Null(plan.Writes);
        Assert.Null(plan.Restore);
    }

    [Fact]
    public void APilotView_SwitchesAndRestoresToThePilotView()
    {
        var plan = InstrumentViewPlan.For(new CameraViewReading(2, 1, 0), wantedIndex: 0);

        Assert.Equal(InstrumentViewOutcome.Switch, plan.Outcome);
        Assert.Equal((2, 0), plan.Writes);
        Assert.Equal((1, 0), plan.Restore);
    }

    [Fact]
    public void AnotherInstrumentView_SwitchesAndRestoresToIt()
    {
        var plan = InstrumentViewPlan.For(new CameraViewReading(2, 2, 0), wantedIndex: 2);

        Assert.Equal(InstrumentViewOutcome.Switch, plan.Outcome);
        Assert.Equal((2, 2), plan.Writes);
        Assert.Equal((2, 0), plan.Restore);
    }

    [Fact]
    public void AQuickview_IsRestoredAsAQuickview()
    {
        var plan = InstrumentViewPlan.For(new CameraViewReading(2, 3, 5), wantedIndex: 3);

        Assert.Equal(InstrumentViewOutcome.Switch, plan.Outcome);
        Assert.Equal((3, 5), plan.Restore);
    }

    [Fact]
    public void AnUnreadableCamera_StillWrites_ButHasNothingToRestoreTo()
    {
        var plan = InstrumentViewPlan.For(null, wantedIndex: 3);

        Assert.Equal(InstrumentViewOutcome.Unknown, plan.Outcome);
        Assert.Equal((2, 3), plan.Writes);
        Assert.Null(plan.Restore);
    }

    [Fact]
    public void IsOn_MatchesTypeAndIndexOnly()
    {
        Assert.True(InstrumentViewPlan.IsOn(new CameraViewReading(2, 2, 4), 4));
        Assert.False(InstrumentViewPlan.IsOn(new CameraViewReading(2, 1, 4), 4));
        Assert.False(InstrumentViewPlan.IsOn(new CameraViewReading(2, 2, 3), 4));
    }

    [Fact]
    public void TheConstants_AreTheSimulatorsNumbers()
    {
        Assert.Equal(2, InstrumentViewPlan.CockpitState);
        Assert.Equal(2, InstrumentViewPlan.InstrumentViewType);
    }
}
