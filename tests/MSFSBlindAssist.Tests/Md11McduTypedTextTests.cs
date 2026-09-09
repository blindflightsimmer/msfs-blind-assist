using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// A typed MCDU entry holding a character the keyboard lacks is REFUSED whole and the character
/// is named — never sent with that character dropped. "N123*" went in as N123 and "KJFK,KLAX" as
/// KJFKKLAX with nothing spoken, on an aircraft whose screens a blind pilot cannot read; the
/// window's own rule (a press that cannot be delivered is spoken, never swallowed) stopped one
/// layer below the character mapping. These pin the pure half the window now validates with.
/// </summary>
public class Md11McduTypedTextTests
{
    [Theory]
    [InlineData("")]
    [InlineData("KJFK/KLAX")]
    [InlineData("N123")]
    [InlineData("FL350 +10 -5")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789./+- ")]
    public void UntypeableCharacters_IsEmptyWhenEveryCharacterHasAKey(string text)
    {
        Assert.Equal("", Md11McduKeys.UntypeableCharacters(text));
        Assert.Null(Md11McduKeys.RefusalFor(text));
    }

    [Theory]
    [InlineData("N123*", "*")]
    [InlineData("KJFK,KLAX", ",")]
    [InlineData("N123*,*", "*,")]      // each offender once, in order of first appearance
    [InlineData("#A%B#", "#%")]
    public void UntypeableCharacters_ListsEachOffenderOnceInOrder(string text, string expected)
    {
        Assert.Equal(expected, Md11McduKeys.UntypeableCharacters(text));
    }

    /// <summary>
    /// The character is NAMED, not echoed: a screen reader's symbol level decides whether a bare
    /// "*" is read as "star" or as nothing, and "," is routinely swallowed as punctuation.
    /// </summary>
    [Theory]
    [InlineData("KJFK,KLAX", "Not sent. The MCDU keyboard has no comma key.")]
    [InlineData("N123*", "Not sent. The MCDU keyboard has no asterisk key.")]
    [InlineData("N123*,", "Not sent. The MCDU keyboard has no asterisk or comma key.")]
    [InlineData("N123*,#", "Not sent. The MCDU keyboard has no asterisk, comma or hash key.")]
    public void RefusalFor_NamesEveryMissingKeyAndSendsNothing(string text, string expected)
    {
        Assert.Equal(expected, Md11McduKeys.RefusalFor(text));
    }

    /// <summary>A character with no spoken name is still reported — as itself, never dropped.</summary>
    [Fact]
    public void RefusalFor_SpeaksAnUnnamedCharacterAsItself()
    {
        Assert.Equal("Not sent. The MCDU keyboard has no É key.", Md11McduKeys.RefusalFor("ÉCOLE"));
    }

    /// <summary>
    /// The refusal and the key table must agree by construction: whatever ForChar rejects is what
    /// the refusal reports, so a key added to one can never be silently dropped by the other.
    /// </summary>
    [Fact]
    public void UntypeableCharacters_AgreesWithForChar()
    {
        for (var c = (char)32; c < 127; c++)
        {
            var reported = Md11McduKeys.UntypeableCharacters(c.ToString());
            Assert.Equal(Md11McduKeys.ForChar(c) == null, reported.Length == 1);
        }
    }
}
