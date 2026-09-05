using System.Globalization;

namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// The MD-11 transponder's code entry, done the way the aircraft does it: the panel's own digit
/// keys pressed in order over CEVENT (the only supported way in), then the simulator's
/// <c>TRANSPONDER CODE:1</c> read back to confirm — TFDi drives that stock variable (it read
/// 0x5473, squawk 5473, on a live aircraft on 2026-09-06).
///
/// On the panel a typed four-digit code replaces the eight digit buttons: one field with a Set
/// button is one control and one spoken confirmation, where the buttons were four presses with no
/// read-back and no way to hear what had been entered so far. The digit buttons stay registered
/// (the entry presses them); they are only no longer listed.
/// </summary>
public static class Md11Squawk
{
    /// <summary>The panel's text field. A key containing "_SET" is what MainForm renders as a text box plus Set button.</summary>
    public const string SetKey = "MD11_SQUAWK_SET";

    /// <summary>The read-back row: the stock <c>TRANSPONDER CODE:1</c>, BCO16, decoded to four digits.</summary>
    public const string CodeKey = "MD11_SQUAWK";

    /// <summary>Guidance spoken when the entry is refused; the field stays untouched.</summary>
    public const string InvalidMessage = "Squawk must be four digits, each 0 to 7.";
    public const string EmptyMessage = "Type a four-digit squawk first.";

    /// <summary>The eight keypad buttons, digit 0 to 7, as the control map names them.</summary>
    public static readonly string[] DigitButtons =
        Enumerable.Range(0, 8).Select(d => DigitButton((char)('0' + d))).ToArray();

    public static string DigitButton(char digit) => $"MD11_PED_XPNDR_{digit}_BT";

    /// <summary>
    /// Parses what the pilot typed. MainForm hands the box's text over as a double, so "0421"
    /// arrives as 421 and is padded back to four digits; an empty or unparseable box arrives as
    /// 0, which is refused rather than sent as 0000 (a code nobody types by accident is worth a
    /// refusal, a blanked box set to 0000 is not).
    /// </summary>
    public static bool TryParse(double typed, out string code, out string error)
    {
        code = ""; error = "";
        if (double.IsNaN(typed) || typed <= 0) { error = EmptyMessage; return false; }
        if (typed > 7777 || Math.Abs(typed - Math.Round(typed)) > 1e-9) { error = InvalidMessage; return false; }
        var digits = ((int)Math.Round(typed)).ToString("0000", CultureInfo.InvariantCulture);
        if (digits.Any(ch => ch > '7')) { error = InvalidMessage; return false; }
        code = digits;
        return true;
    }

    /// <summary><c>TRANSPONDER CODE:1</c> in BCO16 is one octal digit per hex nibble: 0x5473 → "5473".</summary>
    public static string Decode(double bco16)
    {
        int w = (int)Math.Round(bco16);
        return $"{(w >> 12) & 0xF}{(w >> 8) & 0xF}{(w >> 4) & 0xF}{w & 0xF}";
    }

    /// <summary>What the confirmation says once the aircraft has been read back.</summary>
    public static string Confirmation(string requested, string? readBack)
    {
        if (readBack == null) return $"Squawk {requested} entered, the transponder did not report back.";
        if (readBack == requested) return $"Squawk {requested}.";
        return $"Squawk entry did not take, the transponder reads {readBack}.";
    }
}
