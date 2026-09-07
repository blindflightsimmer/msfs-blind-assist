using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Typed minimums. Measured live (2026-09-06): MD11_EXTCTL_CAP_MIN / FO_MIN are one-shot inboxes
/// idling at -9999; a write is consumed in either minimums mode but sets the BARO minimums only —
/// typed 500 with the mode on Radio, the export stayed on the 200 ft radio value; switched to Baro,
/// it read 500. The read-back has to say which of those two things happened.
/// </summary>
public class Md11MinimumsTests
{
    [Fact]
    public void Sides_NameTheirKeysAndAnchors()
    {
        Assert.True(Md11Minimums.TryGetSide("MD11_CAP_MINIMUMS_SET", out var cap));
        Assert.Equal("Captain", cap.Name);
        Assert.Equal("MD11_CAP_MINIMUMS", cap.ReadKey);
        Assert.Equal("MD11_EXTCTL_CAP_MIN", cap.WriteVar);
        Assert.Equal("MD11_LECP_MINIMUMS_KB", cap.ModeKey);
        Assert.Equal("MD11_LECP_MINIMUMS_CAP", cap.AnchorKey);
        Assert.Equal("EFIS Captain", cap.PanelName);

        Assert.True(Md11Minimums.TryGetSide("MD11_FO_MINIMUMS_SET", out var fo));
        Assert.Equal("First Officer", fo.Name);
        Assert.Equal("MD11_FO_MINIMUMS", fo.ReadKey);
        Assert.Equal("MD11_EXTCTL_FO_MIN", fo.WriteVar);
        Assert.Equal("MD11_RECP_MINIMUMS_KB", fo.ModeKey);
        Assert.Equal("EFIS First Officer", fo.PanelName);

        Assert.False(Md11Minimums.TryGetSide("MD11_SQUAWK_SET", out _));
        Assert.Equal(2, Md11Minimums.Sides.Length);
        Assert.Contains("_SET", Md11Minimums.Captain.SetKey);   // MainForm's text-box convention
    }

    [Theory]
    [InlineData(500, true, 500, "")]
    [InlineData(1, true, 1, "")]
    [InlineData(15000, true, 15000, "")]
    [InlineData(0, false, 0, Md11Minimums.EmptyMessage)]
    [InlineData(-200, false, 0, Md11Minimums.EmptyMessage)]
    [InlineData(15001, false, 0, Md11Minimums.RangeMessage)]
    [InlineData(250.5, false, 0, Md11Minimums.WholeFeetMessage)]
    public void TryParse_AcceptsWholeFeetInRange(double typed, bool ok, int feet, string error)
    {
        Assert.Equal(ok, Md11Minimums.TryParse(typed, out var parsed, out var message));
        if (ok) Assert.Equal(feet, parsed);
        Assert.Equal(error, message);
    }

    [Fact]
    public void Confirmation_SaysWhatTheDisplayShows()
    {
        var cap = Md11Minimums.Captain;
        Assert.Equal("Captain minimums 500 feet.", Md11Minimums.Confirmation(cap, 500, 500, modeIsBaro: true));
        Assert.Equal("Captain minimums did not change, still 200 feet.", Md11Minimums.Confirmation(cap, 500, 200, modeIsBaro: true));
        Assert.Equal("Captain baro minimums set to 500 feet. Minimums mode is Radio, showing 200 feet.",
                     Md11Minimums.Confirmation(cap, 500, 200, modeIsBaro: false));
        // Mode unknown (its switch has never been read): trust a matching read-back, explain a mismatch.
        Assert.Equal("Captain minimums 500 feet.", Md11Minimums.Confirmation(cap, 500, 500, modeIsBaro: null));
        Assert.Equal("Captain baro minimums set to 500 feet. The minimums display shows 200 feet.",
                     Md11Minimums.Confirmation(cap, 500, 200, modeIsBaro: null));
        Assert.Equal("First Officer baro minimums set to 600 feet, the display did not report back.",
                     Md11Minimums.Confirmation(Md11Minimums.FirstOfficer, 600, null, modeIsBaro: true));
    }

    [Fact]
    public void DescribeReading_CarriesTheModeWordWhenKnown()
    {
        Assert.Equal("200 feet", Md11Minimums.DescribeReading(200, null));
        Assert.Equal("200 feet, radio", Md11Minimums.DescribeReading(200, 0));
        Assert.Equal("500 feet, baro", Md11Minimums.DescribeReading(500, 1));
        Assert.Equal("MD11_LECP_MINIMUMS_KB", Md11Minimums.ModeKeyFor("MD11_CAP_MINIMUMS"));
        Assert.Equal("MD11_RECP_MINIMUMS_KB", Md11Minimums.ModeKeyFor("MD11_FO_MINIMUMS"));
    }

    [Fact]
    public void IsModeKey_NamesOnlyTheTwoModeSwitches()
    {
        Assert.True(Md11Minimums.IsModeKey("MD11_LECP_MINIMUMS_KB"));
        Assert.True(Md11Minimums.IsModeKey("MD11_RECP_MINIMUMS_KB"));
        Assert.False(Md11Minimums.IsModeKey("MD11_LECP_MINIMUMS_CAP"));
        Assert.False(Md11Minimums.IsModeKey("MD11_CAP_MINIMUMS_SET"));
    }
}
