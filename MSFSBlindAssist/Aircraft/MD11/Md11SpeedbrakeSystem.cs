namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// The MD-11 speedbrake lever, which is THREE variables, not one (TFDi's own tooltip composes
/// them, read from <c>FootPedestalLower.xml</c> and confirmed live 2026-09-06):
///
///   • <c>MD11_SPDBRK_RNG</c>    — the lever's travel: 0 retracted, 17.5 one third, 25 two thirds,
///                                32.5 fully extended. The WHEEL_UP/WHEEL_DOWN events step it,
///                                and only while the lever is not pulled (armed).
///   • <c>MD11_SPDBRK_HANDLE</c> — the pull: 0 down, 1 pulled up with the lever retracted = GROUND
///                                SPOILERS ARMED, 2 = the ground spoilers have auto-extended on
///                                landing. The lever's single click (LEFT_BUTTON_DOWN) toggles it.
///   • <c>MD11_SPDBRK_LATCH</c>  — a visual latch animation only.
///
/// The generated map hung the travel detents on the HANDLE var, so the panel's Spoilers combo
/// could neither read the detent nor reach one (the walk read a var that never moved), and had
/// no notion of "armed" at all. Here the lever row reads the TRAVEL var, streamed so a hardware
/// lever's detent is spoken live, and a second row, Ground spoilers, reads the pull.
/// </summary>
public static class Md11SpeedbrakeSystem
{
    /// <summary>The map control (the panel's Spoilers row); its definition reads <see cref="TravelVar"/>.</summary>
    public const string LeverKey = "MD11_SPDBRK_HANDLE";
    public const string TravelVar = "MD11_SPDBRK_RNG";

    /// <summary>The Ground spoilers row: a definition of this app's own, reading <see cref="ArmVar"/>.</summary>
    public const string ArmKey = "MD11_SPDBRK_ARM";
    public const string ArmVar = "MD11_SPDBRK_HANDLE";

    public const double NotArmed = 0, Armed = 1, Extended = 2;

    /// <summary>How close to a detent the travel value must sit to be named; a sweeping lever in between is silent.</summary>
    public const double DetentTolerance = 2.0;

    public static readonly (double Value, string Name)[] Detents =
    {
        (0, "Retracted"), (17.5, "1/3 extended"), (25, "2/3 extended"), (32.5, "Fully extended"),
    };

    public static Dictionary<double, string> TravelValues
        => Detents.ToDictionary(d => d.Value, d => d.Name);

    public static readonly Dictionary<double, string> ArmValues = new()
    {
        [NotArmed] = "Not armed", [Armed] = "Armed", [Extended] = "Extended",
    };

    /// <summary>The detent a travel value sits in, or null between detents.</summary>
    public static string? DescribeTravel(double rng)
    {
        foreach (var (value, name) in Detents)
            if (Math.Abs(rng - value) <= DetentTolerance) return name;
        return null;
    }

    /// <summary>What the lever row shows: the detent, or the fact that it is between two.</summary>
    public static string DisplayTravel(double rng) => DescribeTravel(rng) ?? "between detents";

    /// <summary>The announcement for a change of the pull var; null for a value the aircraft never produces.</summary>
    public static string? DescribeArm(double handle) => (int)Math.Round(handle) switch
    {
        0 => "Ground spoilers disarmed",
        1 => "Ground spoilers armed",
        2 => "Ground spoilers extended",
        _ => null,
    };

    /// <summary>
    /// Why a Ground spoilers selection is refused before anything is sent, or null when it may go.
    /// The click only toggles the pull, so "Extended" is not a choice, arming needs the lever
    /// retracted (the aircraft ignores the pull otherwise), and disarming an auto-extended set
    /// is done by retracting the lever.
    /// </summary>
    public static string? RefuseArm(double target, double handle, double travel)
    {
        int want = (int)Math.Round(target), have = (int)Math.Round(handle);
        if (want == 2) return "Ground spoilers extend by themselves on landing; arm them instead.";
        if (want == have) return null;                        // nothing to do, and nothing to say
        if (want == 1 && (DescribeTravel(travel) != "Retracted")) return "Retract the spoilers before arming them.";
        if (want == 0 && have == 2) return "The ground spoilers are extended; retract the lever instead.";
        return null;
    }

    /// <summary>Whether the lever must be left alone: the wheel does nothing while the pull is up.</summary>
    public static string? RefuseTravel(double targetTravel, double handle)
        => (int)Math.Round(handle) == 1 && targetTravel > DetentTolerance
            ? "Disarm the ground spoilers before extending the spoilers."
            : null;
}
