using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The MD-11 speedbrake: the lever row reads the travel var and names its detents, the Ground
/// spoilers row reads the pull, selections the aircraft would ignore are refused with a reason,
/// and both rows are registered so a hardware lever is spoken live.
/// </summary>
public class Md11SpeedbrakeTests
{
    private static readonly TFDiMD11Definition Def = new();
    private static Dictionary<string, SimVarDefinition> Vars => Def.GetVariables();

    [Theory]
    [InlineData(0, "Retracted")]
    [InlineData(17.5, "1/3 extended")]
    [InlineData(26.4, "2/3 extended")]      // within tolerance of 25
    [InlineData(32.5, "Fully extended")]
    public void ATravelValue_NamesItsDetent(double rng, string expected)
    {
        Assert.Equal(expected, Md11SpeedbrakeSystem.DescribeTravel(rng));
        Assert.True(Def.TryGetDisplayOverride(Md11SpeedbrakeSystem.LeverKey, rng, out var shown));
        Assert.Equal(expected, shown);
    }

    [Fact]
    public void BetweenDetents_IsSilent_ButDisplayed()
    {
        Assert.Null(Md11SpeedbrakeSystem.DescribeTravel(10));
        Assert.Equal("between detents", Md11SpeedbrakeSystem.DisplayTravel(10));
    }

    [Theory]
    [InlineData(0, "Ground spoilers disarmed")]
    [InlineData(1, "Ground spoilers armed")]
    [InlineData(2, "Ground spoilers extended")]
    public void ThePullVar_IsSpokenAsGroundSpoilers(double handle, string expected)
    {
        Assert.Equal(expected, Md11SpeedbrakeSystem.DescribeArm(handle));
    }

    [Fact]
    public void SelectionsTheAircraftWouldIgnore_AreRefusedWithAReason()
    {
        Assert.Null(Md11SpeedbrakeSystem.RefuseArm(1, 0, 0));                      // arm from retracted: fine
        Assert.Null(Md11SpeedbrakeSystem.RefuseArm(0, 1, 0));                      // disarm: fine
        Assert.Null(Md11SpeedbrakeSystem.RefuseArm(1, 1, 0));                      // already armed: nothing to do
        Assert.NotNull(Md11SpeedbrakeSystem.RefuseArm(1, 0, 17.5));                // lever out: the click is ignored
        Assert.NotNull(Md11SpeedbrakeSystem.RefuseArm(2, 0, 0));                   // "Extended" is not a choice
        Assert.NotNull(Md11SpeedbrakeSystem.RefuseArm(0, 2, 32.5));                // auto-extended: retract instead
        Assert.NotNull(Md11SpeedbrakeSystem.RefuseTravel(17.5, 1));                // wheel dead while armed
        Assert.Null(Md11SpeedbrakeSystem.RefuseTravel(0, 1));                      // retracting is always allowed
        Assert.Null(Md11SpeedbrakeSystem.RefuseTravel(25, 0));
    }

    [Fact]
    public void TheLeverRow_ReadsTheTravelVar_StreamedLikeTheFlapLever()
    {
        var d = Vars[Md11SpeedbrakeSystem.LeverKey];
        Assert.Equal(Md11SpeedbrakeSystem.TravelVar, d.Name);
        Assert.Equal(SimVarType.LVar, d.Type);
        Assert.Equal(UpdateFrequency.Continuous, d.UpdateFrequency);
        Assert.True(d.IsAnnounced);
        Assert.True(d.ExcludeFromBatch);
        Assert.True(d.HighFrequency);
        Assert.False(d.ExcludeFromMonitorManager);
        Assert.Equal(Md11SpeedbrakeSystem.TravelValues, d.ValueDescriptions);
        Assert.Equal("Spoilers", d.DisplayName);
    }

    [Fact]
    public void TheGroundSpoilersRow_ReadsThePull_AndIsAnnounced()
    {
        var d = Vars[Md11SpeedbrakeSystem.ArmKey];
        Assert.Equal(Md11SpeedbrakeSystem.ArmVar, d.Name);
        Assert.Equal(SimVarType.LVar, d.Type);
        Assert.Equal(UpdateFrequency.Continuous, d.UpdateFrequency);
        Assert.True(d.IsAnnounced);
        Assert.False(d.ExcludeFromMonitorManager);
        Assert.Equal("Ground spoilers", d.DisplayName);
        Assert.Equal(Md11SpeedbrakeSystem.ArmValues, d.ValueDescriptions);
    }

    [Fact]
    public void TheSpeedbrakePanel_ListsTheLeverThenGroundSpoilers()
    {
        var panel = Def.GetPanelControls()["Speedbrake"];
        Assert.Equal(new[] { Md11SpeedbrakeSystem.LeverKey, Md11SpeedbrakeSystem.ArmKey }, panel);
    }

    [Fact]
    public void NoOtherBatchEntry_SharesThePullVarsName()
    {
        // The lever row moved off the pull var onto the travel var, so the Ground spoilers row is
        // the ONLY batch entry named MD11_SPDBRK_HANDLE (two batch entries with one name shift
        // every later slot — the VarNameCollision invariant).
        var named = Vars.Values.Where(v => v.Name == Md11SpeedbrakeSystem.ArmVar
                                        && v.UpdateFrequency == UpdateFrequency.Continuous && v.IsAnnounced && !v.ExcludeFromBatch);
        Assert.Single(named);
    }
}
