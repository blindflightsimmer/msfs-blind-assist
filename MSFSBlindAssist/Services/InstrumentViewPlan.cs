namespace MSFSBlindAssist.Services;

/// <summary>
/// One reading of the simulator camera: <c>CAMERA STATE</c>, <c>CAMERA VIEW TYPE AND INDEX:0</c>
/// (the view type) and <c>:1</c> (the index), as integers. State 2 is a cockpit camera; view type
/// 1 is a pilot view, 2 an instrument view, 3 a quickview; the index is 0-based within its type,
/// so the sim's own "instrument view 1" (Ctrl+1 on MSFS 2020, Shift+1 on MSFS 2024) is index 0.
/// Live-verified on MSFS 2024, 2026-09-08 (docs/md11.md, "AI display reading").
/// </summary>
public readonly record struct CameraViewReading(int State, int ViewType, int ViewIndex);

/// <summary>What <see cref="InstrumentViewPlan.For"/> decided about the camera.</summary>
public enum InstrumentViewOutcome
{
    /// <summary>Not a cockpit camera (external, drone, showcase). Nothing is written; the read is refused.</summary>
    NotInCockpit,
    /// <summary>Already on the wanted instrument view. Nothing to write, nothing to restore.</summary>
    AlreadyThere,
    /// <summary>In the cockpit on some other view: write the instrument view, restore the previous one afterwards.</summary>
    Switch,
    /// <summary>The camera could not be read: write the instrument view anyway, but there is nothing to restore to.</summary>
    Unknown,
}

/// <summary>
/// The pure half of moving the simulator camera to an instrument view for an AI display read:
/// given the camera as it is and the view the display needs, what to write and what to write
/// back afterwards. The async half (writes, read-back verification, settle) is
/// <see cref="InstrumentViewSwitcher"/>. The two simulator constants live here and nowhere else.
/// </summary>
public sealed record InstrumentViewPlan(
    InstrumentViewOutcome Outcome,
    (int Type, int Index)? Writes,
    (int Type, int Index)? Restore)
{
    /// <summary><c>CAMERA STATE</c> for any cockpit camera.</summary>
    public const int CockpitState = 2;

    /// <summary><c>CAMERA VIEW TYPE AND INDEX:0</c> for the instrument views defined in the aircraft's cameras.cfg.</summary>
    public const int InstrumentViewType = 2;

    public static InstrumentViewPlan For(CameraViewReading? current, int wantedIndex)
    {
        if (current is not { } camera)
            return new InstrumentViewPlan(InstrumentViewOutcome.Unknown, (InstrumentViewType, wantedIndex), null);

        if (camera.State != CockpitState)
            return new InstrumentViewPlan(InstrumentViewOutcome.NotInCockpit, null, null);

        if (IsOn(camera, wantedIndex))
            return new InstrumentViewPlan(InstrumentViewOutcome.AlreadyThere, null, null);

        return new InstrumentViewPlan(
            InstrumentViewOutcome.Switch,
            (InstrumentViewType, wantedIndex),
            (camera.ViewType, camera.ViewIndex));
    }

    /// <summary>True when the reading is the wanted instrument view (type and index; the state is not consulted).</summary>
    public static bool IsOn(CameraViewReading reading, int wantedIndex)
        => reading.ViewType == InstrumentViewType && reading.ViewIndex == wantedIndex;
}
