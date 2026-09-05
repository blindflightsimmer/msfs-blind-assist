using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The MD-11 transponder's typed squawk entry: what is accepted, how the read-back is decoded
/// and worded, and that the panel carries the field and the read-back row instead of the
/// eight digit keys (which stay registered so the entry can press them).
/// </summary>
public class Md11SquawkTests
{
    private static readonly TFDiMD11Definition Def = new();
    private static Dictionary<string, SimVarDefinition> Vars => Def.GetVariables();

    [Theory]
    [InlineData(1200, "1200")]
    [InlineData(421, "0421")]     // the box's "0421" arrives as 421 and is padded back
    [InlineData(7777, "7777")]
    [InlineData(7, "0007")]
    public void AValidCode_IsPaddedToFourOctalDigits(double typed, string expected)
    {
        Assert.True(Md11Squawk.TryParse(typed, out var code, out var error));
        Assert.Equal(expected, code);
        Assert.Equal("", error);
    }

    [Theory]
    [InlineData(1280)]      // an 8
    [InlineData(9)]         // a 9
    [InlineData(12345)]     // five digits
    [InlineData(12.5)]      // not whole
    [InlineData(-1)]
    public void AnInvalidCode_IsRefusedWithGuidance(double typed)
    {
        Assert.False(Md11Squawk.TryParse(typed, out var code, out var error));
        Assert.Equal("", code);
        Assert.NotEqual("", error);
    }

    [Fact]
    public void AnEmptyBox_IsRefused_NotSentAsZeroZeroZeroZero()
    {
        // MainForm passes 0 for an empty or unparseable box; 0000 must never go out by accident.
        Assert.False(Md11Squawk.TryParse(0, out _, out var error));
        Assert.Equal(Md11Squawk.EmptyMessage, error);
    }

    [Theory]
    [InlineData(0x5473, "5473")]   // read live on 2026-09-06
    [InlineData(0x1200, "1200")]
    [InlineData(0x0421, "0421")]
    public void TheStockCode_DecodesFromBco16(int word, string expected)
    {
        Assert.Equal(expected, Md11Squawk.Decode(word));
        Assert.True(Def.TryGetDisplayOverride(Md11Squawk.CodeKey, word, out var shown));
        Assert.Equal(expected, shown);
    }

    [Fact]
    public void TheConfirmation_SaysWhatTheTransponderActuallyReads()
    {
        Assert.Equal("Squawk 1200.", Md11Squawk.Confirmation("1200", "1200"));
        Assert.Equal("Squawk entry did not take, the transponder reads 5473.", Md11Squawk.Confirmation("1200", "5473"));
        Assert.Equal("Squawk 1200 entered, the transponder did not report back.", Md11Squawk.Confirmation("1200", null));
    }

    [Fact]
    public void EachDigit_MapsToItsKeypadButton_WhichStaysARegisteredControl()
    {
        Assert.Equal("MD11_PED_XPNDR_5_BT", Md11Squawk.DigitButton('5'));
        Assert.Equal(8, Md11Squawk.DigitButtons.Length);
        foreach (var button in Md11Squawk.DigitButtons)
            Assert.True(Vars.ContainsKey(button), $"{button} must stay registered: the entry presses it");
    }

    [Fact]
    public void TheTransponderPanel_HasTheFieldAndTheReadBack_NotTheDigitKeys()
    {
        var panel = Def.GetPanelControls()["Transponder"];
        Assert.Contains(Md11Squawk.SetKey, panel);
        Assert.Equal(Md11Squawk.CodeKey, panel[^1]);                       // the read-back is a status row: last
        Assert.Equal(panel.IndexOf("MD11_PED_XPNDR_ABV_BLW_SW") + 1, panel.IndexOf(Md11Squawk.SetKey));   // right after the selectors
        Assert.DoesNotContain(panel, k => Md11Squawk.DigitButtons.Contains(k));
        Assert.Contains("MD11_PED_XPNDR_IDENT_BT", panel);
        Assert.Contains("MD11_PED_XPNDR_CLR_BT", panel);
    }

    [Fact]
    public void TheField_IsATextEntry_AndTheReadBack_IsAReadOnlyStatusRow()
    {
        var set = Vars[Md11Squawk.SetKey];
        Assert.Contains("_SET", Md11Squawk.SetKey);       // MainForm's text box + Set button convention
        Assert.Equal("Squawk", set.DisplayName);
        Assert.False(set.PreventTextInput);

        var code = Vars[Md11Squawk.CodeKey];
        Assert.Equal("TRANSPONDER CODE:1", code.Name);
        Assert.Equal("BCO16", code.Units);
        Assert.Equal(SimVarType.SimVar, code.Type);
        Assert.Equal(UpdateFrequency.OnRequest, code.UpdateFrequency);
        Assert.True(code.RenderAsReadOnlyStatus);
    }

    [Fact]
    public void TheDigitKeys_AreSupersededNotUnplaced()
    {
        // The safety net appends every unlisted control; the keypad is the one deliberate exception.
        var placement = Md11PanelLayout.Place(Md11ControlMap.Load());
        Assert.Empty(placement.Unplaced);
        Assert.All(Md11Squawk.DigitButtons, b => Assert.Contains(b, Md11PanelLayout.SupersededByEntryField));
    }
}
