using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The input-event reads (SimConnectManager.InputEventReads.cs) take their GetInputEvent request ids
/// from a rotating range starting at REQUEST_INPUT_EVENT_READ. SimConnect treats a request issued
/// under an existing id as a REPLACEMENT, so nothing else may live in that range.
/// </summary>
public class InputEventReadRequestIdTests
{
    private static uint First => (uint)SimConnectManager.DATA_REQUESTS.REQUEST_INPUT_EVENT_READ;
    private static uint Last => First + SimConnectManager.InputEventReadIdCount - 1;

    [Fact]
    public void The_range_is_701_to_890()
    {
        Assert.Equal(701u, First);
        Assert.Equal(890u, Last);
    }

    [Fact]
    public void No_other_named_request_id_falls_inside_the_range()
    {
        var inside = Enum.GetValues<SimConnectManager.DATA_REQUESTS>()
            .Select(v => (uint)(int)v)
            .Where(id => id != First && id >= First && id <= Last)
            .ToList();
        Assert.Empty(inside);
    }

    [Fact]
    public void The_range_sits_below_the_individual_variable_ids()
        => Assert.True(Last < (uint)SimConnectManager.DATA_REQUESTS.INDIVIDUAL_VARIABLE_BASE);
}
