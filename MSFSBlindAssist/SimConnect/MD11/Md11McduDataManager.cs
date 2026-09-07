using System.Runtime.InteropServices;
using System.Text;
using Microsoft.FlightSimulator.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect.MD11;

/// <summary>
/// Reads the TFDi MD-11's three MCDU screens as TEXT over the SimConnect client data area
/// <c>MD11MCDU</c>.
///
/// This is the same mechanism PMDG uses — <see cref="PMDG777DataManager"/> is the reference
/// implementation — with one structural difference that matters: PMDG publishes a SEPARATE area
/// per CDU (<c>PMDG_777X_CDU_0/1/2</c>), whereas the MD-11 packs all three into ONE area at
/// offsets 0 / 1012 / 2024 (see <see cref="Md11McduLayout"/> — the packed 1012-byte size). So there
/// is a single MapClientDataNameToID and three definitions that differ only by their offset into it.
///
/// Why this exists at all: the MD-11's displays are WASM-rendered canvases with no HTML DOM, so
/// the Coherent scrape that reads the PMDG/FBW/HS787 CDUs cannot work here. That fact once led to
/// a wrong conclusion that the CDU was unreadable — it is not; it is merely readable somewhere
/// else. See docs/md11.md §2.
/// </summary>
public sealed class Md11McduDataManager : IDisposable
{
    /// <summary>
    /// IDs are namespaced well away from the PMDG managers' ranges. Both can be registered
    /// against the same SimConnect handle across an aircraft switch, and a collision would cross
    /// the wires silently.
    /// </summary>
    private enum ClientDataId : uint { Mcdu = 0x4D443131 }   // 'MD11'

    private enum DefineId : uint { LMcdu = 0x4D443101, CMcdu = 0x4D443102, RMcdu = 0x4D443103 }

    /// <summary>
    /// One SUBSCRIPTION id and one SNAPSHOT id per unit, and they must stay different: SimConnect
    /// treats a request re-issued under an existing id as a REPLACEMENT, so a one-shot read on
    /// the subscription's own id would cancel the subscription and freeze the window on its
    /// first page. See <see cref="RequestAll"/> for why there is a snapshot at all.
    /// </summary>
    private enum RequestId : uint
    {
        LMcdu = 0x4D443201, CMcdu = 0x4D443202, RMcdu = 0x4D443203,
        LMcduSnapshot = 0x4D443211, CMcduSnapshot = 0x4D443212, RMcduSnapshot = 0x4D443213,
    }

    /// <summary>The subscription and snapshot request ids for a unit. Public so the pairing is testable.</summary>
    public static (uint Subscription, uint Snapshot) RequestIdsFor(Md11McduUnit unit) => unit switch
    {
        Md11McduUnit.Left => ((uint)RequestId.LMcdu, (uint)RequestId.LMcduSnapshot),
        Md11McduUnit.Center => ((uint)RequestId.CMcdu, (uint)RequestId.CMcduSnapshot),
        _ => ((uint)RequestId.RMcdu, (uint)RequestId.RMcduSnapshot),
    };

    private static DefineId DefineFor(Md11McduUnit unit) => unit switch
    {
        Md11McduUnit.Left => DefineId.LMcdu,
        Md11McduUnit.Center => DefineId.CMcdu,
        _ => DefineId.RMcdu,
    };

    /// <summary>The unit a delivery belongs to, or null when the request id is not one of ours.</summary>
    public static Md11McduUnit? UnitForRequest(uint requestId) => (RequestId)requestId switch
    {
        RequestId.LMcdu or RequestId.LMcduSnapshot => Md11McduUnit.Left,
        RequestId.CMcdu or RequestId.CMcduSnapshot => Md11McduUnit.Center,
        RequestId.RMcdu or RequestId.RMcduSnapshot => Md11McduUnit.Right,
        _ => null,
    };

    private readonly Microsoft.FlightSimulator.SimConnect.SimConnect _simConnect;
    private readonly Md11McduScreen?[] _screens = new Md11McduScreen?[3];
    private readonly object _lock = new();

    private bool _registered;

    /// <summary>Raised when a screen's CONTENT changes (not on every delivery).</summary>
    public event EventHandler<Md11McduScreen>? ScreenUpdated;

    /// <summary>True once at least one screen has arrived — the "is ready" gate.</summary>
    public bool IsReady { get; private set; }

    public Md11McduDataManager(Microsoft.FlightSimulator.SimConnect.SimConnect simConnect)
    {
        _simConnect = simConnect;
    }

    /// <summary>Latest decoded screen for a unit, or null if none has arrived yet.</summary>
    public Md11McduScreen? GetScreen(Md11McduUnit unit)
    {
        lock (_lock) return _screens[(int)unit];
    }

    // ------------------------------------------------------------------
    // Registration
    // ------------------------------------------------------------------

    /// <summary>
    /// Maps the area and defines the three per-MCDU windows into it. Safe to call once per
    /// connection; re-registering the same name is what an aircraft switch would do and SimConnect
    /// does not like it, hence the latch.
    /// </summary>
    public void Register()
    {
        if (_registered) return;

        // Fail loudly and early if the struct ever stops matching TFDi's declaration. Getting
        // this wrong does not throw — it silently reads misaligned bytes and renders the CDU as
        // garbage, which is far harder to diagnose than an exception at startup. (The real packed
        // MCDU_DATA_SIZE is 1012 — see Md11McduLayout for why earlier 8068/1348 answers were wrong.)
        var marshalled = Marshal.SizeOf<Md11McduExportData>();
        if (marshalled != Md11McduLayout.DataSize)
        {
            Log.Error("MD11",
                $"MCDUEXPORTDATA marshals to {marshalled} bytes, expected {Md11McduLayout.DataSize}. " +
                "Refusing to register — the CDU would read garbage. Check the [MarshalAs(U1)] bools " +
                "and Pack/Size on Md11McduExportData.");
            return;
        }

        try
        {
            _simConnect.MapClientDataNameToID(Md11McduLayout.AreaName, ClientDataId.Mcdu);

            const uint size = Md11McduLayout.DataSize;
            _simConnect.AddToClientDataDefinition(DefineId.LMcdu, Md11McduLayout.OffsetLeft, size, 0, 0);
            _simConnect.AddToClientDataDefinition(DefineId.CMcdu, Md11McduLayout.OffsetCenter, size, 0, 0);
            _simConnect.AddToClientDataDefinition(DefineId.RMcdu, Md11McduLayout.OffsetRight, size, 0, 0);

            _simConnect.RegisterStruct<SIMCONNECT_RECV_CLIENT_DATA, Md11McduExportData>(DefineId.LMcdu);
            _simConnect.RegisterStruct<SIMCONNECT_RECV_CLIENT_DATA, Md11McduExportData>(DefineId.CMcdu);
            _simConnect.RegisterStruct<SIMCONNECT_RECV_CLIENT_DATA, Md11McduExportData>(DefineId.RMcdu);

            _registered = true;
            Log.Info("MD11", $"MCDU client data area '{Md11McduLayout.AreaName}' registered ({size} bytes x3).");
        }
        catch (Exception ex)
        {
            Log.Error("MD11", $"Failed to register the MCDU client data area: {ex.Message}");
        }
    }

    /// <summary>
    /// Subscribes to all three MCDUs, and reads each one ONCE for what is on it right now.
    ///
    /// SET_CHANGED, not a poll: the CDU only changes when a key is pressed or the FMS repaints,
    /// so a periodic request would burn bandwidth re-delivering an identical 4 KB screen many
    /// times a second. CHANGED gives near-zero steady-state traffic and an immediate update on
    /// the edge that matters.
    ///
    /// The snapshot is not optional. An ON_SET subscription is handed only the WRITES THAT FOLLOW
    /// it, and the MD-11 writes this area only when a screen changes — so a pilot who starts the
    /// app in flight got nothing until the FMS next redrew, which is usually their first key
    /// press. Measured on the live aircraft (2026-09-07 16:56, read-only probe): the subscription
    /// alone delivered nothing in 8 s and nothing in a further 10 s, while a PERIOD.ONCE read on
    /// the same definitions returned all three current pages within 22 ms. The app's own logs
    /// show the same gap on two starts: registered 16:23:19, first delivery 16:23:40; registered
    /// 10:38:13, first delivery 10:39:11 — all three units at once, nothing having pressed a key.
    /// (PMDG's managers read their CDU areas by exactly this ONCE request, on a timer.)
    ///
    /// Subscription first, snapshot second, so no write can fall between the snapshot and the
    /// subscription's start: a write that lands between the two calls is delivered by the
    /// subscription and repeated by the snapshot, and <see cref="HandleClientData"/> drops the
    /// repeat by content. The snapshot rides its OWN request id — see <see cref="RequestId"/>.
    /// </summary>
    public void RequestAll()
    {
        if (!_registered) return;

        foreach (var unit in new[] { Md11McduUnit.Left, Md11McduUnit.Center, Md11McduUnit.Right })
        {
            var (subscription, snapshot) = RequestIdsFor(unit);
            var define = DefineFor(unit);
            Request(unit, subscription, define, SIMCONNECT_CLIENT_DATA_PERIOD.ON_SET, SIMCONNECT_CLIENT_DATA_REQUEST_FLAG.CHANGED);
            Request(unit, snapshot, define, SIMCONNECT_CLIENT_DATA_PERIOD.ONCE, SIMCONNECT_CLIENT_DATA_REQUEST_FLAG.DEFAULT);
        }
    }

    private void Request(Md11McduUnit unit, uint request, DefineId define,
        SIMCONNECT_CLIENT_DATA_PERIOD period, SIMCONNECT_CLIENT_DATA_REQUEST_FLAG flag)
    {
        try
        {
            _simConnect.RequestClientData(ClientDataId.Mcdu, (RequestId)request, define, period, flag, 0, 0, 0);
        }
        catch (Exception ex)
        {
            Log.Debug("MD11", $"MCDU RequestClientData({unit}, {period}) failed: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------
    // Receive
    // ------------------------------------------------------------------

    /// <summary>
    /// Handles a SIMCONNECT_RECV_CLIENT_DATA. Returns true if it was one of ours, so the caller's
    /// dispatch can stop looking.
    /// </summary>
    public bool HandleClientData(SIMCONNECT_RECV_CLIENT_DATA data)
    {
        var unit = UnitForRequest(data.dwRequestID);
        if (unit == null) return false;

        try
        {
            if (data.dwData == null || data.dwData.Length == 0) return true;
            if (data.dwData[0] is not Md11McduExportData raw) return true;

            var screen = Decode(unit.Value, raw);

            bool changed;
            bool firstEver;
            lock (_lock)
            {
                var prev = _screens[(int)unit.Value];
                firstEver = prev == null;
                changed = prev == null || !SameContent(prev, screen);
                _screens[(int)unit.Value] = screen;
            }

            // First delivery per unit is logged: on a fresh (unverified) install this line is the
            // proof the MD11MCDU export is actually reaching us — its ABSENCE, with registration
            // logged OK, means the aircraft isn't publishing the area (nothing to decode).
            if (firstEver)
            {
                int glyphs = screen.Lines.Sum(l => l?.Length ?? 0);
                Log.Info("MD11", $"MCDU {unit} first delivery: dspy={screen.Dspy} fail={screen.Fail} " +
                    $"{glyphs} glyphs, title='{screen.Title}'.");
            }

            // Blank frames are DELIBERATELY not logged here, and this manager deliberately keeps
            // no blank/content state of its own.
            //
            // The MD-11 erases its CDU before redrawing, so a blank frame arrives on every page
            // change (37 measured, 202-438 ms, every one followed by content — none has ever
            // persisted). Logging that edge answered the question it was added for and then went
            // on writing two lines per page change for no further gain. Logging only a blank that
            // outlasts Md11McduPresence.BlankSettleMs was the next attempt and is no better: it
            // records an event never once observed, while implying to a later reader that someone
            // has seen one. A settled blank already reaches the pilot as a row in the window,
            // which is the channel that matters and the one they can report from.
            IsReady = true;
            if (changed) ScreenUpdated?.Invoke(this, screen);
        }
        catch (Exception ex)
        {
            Log.Debug("MD11", $"Failed to decode MCDU {unit}: {ex.Message}");
        }

        return true;
    }

    /// <summary>
    /// Turns the raw 14×24 grid into lines.
    ///
    /// Cells are UTF-16 code units; 0 is an empty cell and must become a SPACE, not be dropped —
    /// the MCDU is a fixed-pitch grid where horizontal position carries meaning (a value sits
    /// under its label, right-aligned fields align to column 23). Collapsing blanks would destroy
    /// the column relationship the layout encodes. Trailing blanks ARE trimmed, since padding to
    /// 24 columns just makes a screen reader announce a run of spaces.
    /// </summary>
    private static Md11McduScreen Decode(Md11McduUnit unit, Md11McduExportData raw)
    {
        var lines = new string[Md11McduLayout.Rows];
        var isLarge = new bool[Md11McduLayout.Rows];

        for (var row = 0; row < Md11McduLayout.Rows; row++)
        {
            var sb = new StringBuilder(Md11McduLayout.Cols);
            var large = 0;
            var glyphs = 0;

            for (var col = 0; col < Md11McduLayout.Cols; col++)
            {
                var cell = raw.Text[row * Md11McduLayout.Cols + col];
                var ch = cell.Value == 0 ? ' ' : (char)cell.Value;
                sb.Append(ch);

                if (ch != ' ')
                {
                    glyphs++;
                    if (cell.Large) large++;
                }
            }

            lines[row] = sb.ToString().TrimEnd();
            // "Predominantly large" rather than "any large": a line is a title/value line or a
            // label line as a whole, and a stray large glyph shouldn't flip its role.
            isLarge[row] = glyphs > 0 && large * 2 > glyphs;
        }

        return new Md11McduScreen
        {
            Unit = unit,
            Dspy = raw.Dspy,
            Fail = raw.Fail,
            Msg = raw.Msg,
            Ofst = raw.Ofst,
            Lines = lines,
            LineIsLarge = isLarge,
        };
    }

    /// <summary>
    /// Content equality. Compares the flags too: `msg` lighting up is a real event with no text
    /// change behind it, and a blind pilot has no other way to notice it.
    /// </summary>
    private static bool SameContent(Md11McduScreen a, Md11McduScreen b)
    {
        if (a.Dspy != b.Dspy || a.Fail != b.Fail || a.Msg != b.Msg || a.Ofst != b.Ofst) return false;
        for (var i = 0; i < Md11McduLayout.Rows; i++)
            if (!string.Equals(a.Lines[i], b.Lines[i], StringComparison.Ordinal)) return false;
        return true;
    }

    public void Dispose()
    {
        ScreenUpdated = null;
        lock (_lock) Array.Clear(_screens);
    }
}
