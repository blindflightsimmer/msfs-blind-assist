using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Forms.IniA300;

/// <summary>
/// Accessible MCDU for the iniBuilds pixel-read MCDUs (A330, A321LR — <see cref="CduFormProfile"/>).
/// The aircraft exports no MCDU text, so the screen is READ FROM PIXELS: the MCDU is popped out
/// (forced on open — see A300CduPopout), captured with PrintWindow every 250 ms while this window is
/// visible, and read by the trained fixed-font matcher (A300CduScreenReader). Keys are the aircraft's own
/// <c>INI_MCDU1_&lt;KEY&gt;</c> press flags: write 1, the WASM consumes it and resets it to 0 (writing
/// 0 straight after kills the press, measured). Every write is sequence-prefixed so a repeated
/// key is never coalesced by the MobiFlight channel.
///
/// Same layout and keys as the other Airbus CDU windows: title, label/data pairs prefixed with
/// the line number, scratchpad last; Ctrl/Alt+1..6 (or F1..F12) line keys, Alt+letter pages,
/// Alt+S scratchpad, Backspace CLR. Typed text is paced at 150 ms a key and confirmed by
/// reading the scratchpad back — what is spoken is what the CDU shows.
/// </summary>
public sealed class IniA300CduForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private static readonly LogChannel _log = Log.Channel("a300_cdu");
    private const int KeyGapMs = 150;

    private readonly SimConnectManager _sim;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly A300CduPopout _popout;
    private readonly A300CduScreenReader _reader;
    private readonly CduFormProfile _profile;
    private string _device => _profile.Device;
    private readonly HashSet<string> _loggedUncertain = new();
    private readonly CduScratchpadAnnouncer _scratch = new("Scratchpad cleared", stablePolls: 2);
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 250 };
    private readonly SemaphoreSlim _keyGate = new(1, 1);

    private readonly Label _status = new() { AutoSize = true, Text = "CDU" };
    private readonly ListBox _display = new() { IntegralHeight = false, Font = new Font("Consolas", 11f) };
    private readonly TextBox _scratchpad = new() { AccessibleName = "Scratchpad entry" };

    private IntPtr _previousWindow;
    private bool _reading, _ensuring, _disposed;
    private string _lastTitle = "";
    private string[] _rows = new string[A300CduScreenReader.Rows];
    private int _seq;
    private DateTime _lastFrameUtc = DateTime.MinValue;

    private readonly string _keyPrefix;
    private readonly string _name;

    /// <summary>A CDU window for any iniBuilds pixel-read CDU (the A330's MCDU has its own keys,
    /// grid, glyphs and pop-out — <see cref="CduFormProfile"/>).</summary>
    internal IniA300CduForm(SimConnectManager sim, ScreenReaderAnnouncer announcer, CduFormProfile profile)
    {
        _sim = sim;
        _announcer = announcer;
        _profile = profile;
        _name = profile.Name;
        _keyPrefix = profile.KeyPrefix;
        _popout = new A300CduPopout(profile.Popout);
        _reader = profile.CreateReader();
        string name = profile.Name;
        Text = $"{name} {_device}";
        AccessibleName = $"{name} {_device}";
        KeyPreview = true;
        Width = 760; Height = 640;
        StartPosition = FormStartPosition.CenterScreen;
        BuildLayout();
        _poll.Tick += (_, _) => _ = PollAsync();
        KeyDown += Form_KeyDown;
        _scratchpad.KeyDown += Scratchpad_KeyDown;
        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
            }
        };
    }

    private void BuildLayout()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true };
        foreach (var (label, key, tip) in _profile.PageKeys)
        {
            var b = new Button { Text = label, AutoSize = true, AccessibleDescription = tip };
            string k = key;
            b.Click += (_, _) => { if (k == "CLR") Clear(); else Press(k); };
            panel.Controls.Add(b);
        }
        var scratchRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
        scratchRow.Controls.Add(new Label { Text = "&Scratchpad:", AutoSize = true });
        _scratchpad.Width = 400;
        scratchRow.Controls.Add(_scratchpad);
        _display.Dock = DockStyle.Fill;
        _display.AccessibleName = $"{_device} screen";
        _status.Dock = DockStyle.Top;
        Controls.Add(_display);
        Controls.Add(scratchRow);
        Controls.Add(panel);
        Controls.Add(_status);
    }

    public void ShowForm()
    {
        _previousWindow = GetForegroundWindow();
        bool wasVisible = Visible;
        Show();
        BringToFront();
        Activate();
        _display.Focus();
        if (!wasVisible)
        {
            _scratch.Reset();
            _lastTitle = "";
            _poll.Start();
        }
        _ = EnsurePopoutAsync();
    }

    private async Task EnsurePopoutAsync()
    {
        if (_ensuring || _popout.Find() != IntPtr.Zero) return;
        _ensuring = true;
        SetStatus($"Opening the {_device} pop-out from the simulator...");
        try
        {
            // The pop-out runs on a pool thread, but SimConnect calls belong on the UI thread: the camera
            // read and write are sent back here (an off-thread restore left the camera on the MCDU).
            var ui = SynchronizationContext.Current;
            Task<(int, int)?> ReadCamera()
            {
                var done = new TaskCompletionSource<(int, int)?>(TaskCreationOptions.RunContinuationsAsynchronously);
                async void Read()
                {
                    try { done.TrySetResult(await _sim.ReadCameraViewAsync(1500) is { } r ? (r.ViewType, r.ViewIndex) : null); }
                    catch (Exception ex) { done.TrySetException(ex); }
                }
                if (ui != null) ui.Post(_ => Read(), null); else Read();
                return done.Task;
            }
            void SetCamera(int t, int i)
            {
                if (ui != null) ui.Send(_ => _sim.SetCameraView(t, i), null); else _sim.SetCameraView(t, i);
            }
            string? err = await Task.Run(() => _popout.EnsureAsync(false, ReadCamera, SetCamera));
            if (IsDisposed) return;
            // the pop-out procedure took the foreground; give it back to this window
            Activate();
            _display.Focus();
            if (err != null) { SetStatus(err); _announcer.AnnounceImmediate(err); }
            else SetStatus($"{_device} connected");
        }
        catch (Exception ex)
        {
            _log.Error($"pop-out failed: {ex}");
            SetStatus($"Could not open the {_device} pop-out.");
        }
        finally { _ensuring = false; }
    }

    private void SetStatus(string s)
    {
        if (_status.Text != s) _status.Text = s;
    }

    private async Task PollAsync()
    {
        if (_reading || _ensuring || !Visible) return;
        _reading = true;
        try
        {
            var lines = await Task.Run(() =>
            {
                var frame = _popout.Capture();
                return frame == null ? null : _reader.Read(frame.Lum, frame.Width, frame.Height, _profile.ReadColour ? frame.Rgb : null);
            });
            if (IsDisposed) return;
            if (lines == null)
            {
                SetStatus($"{_device} pop-out not found. Close and reopen this window to pop it out again.");
                return;
            }
            _lastFrameUtc = DateTime.UtcNow;
            foreach (var l in lines)
                foreach (var c in l.Chars.Where(c => c.Uncertain))
                {
                    // once per distinct line: the screen is re-read ~4 times a second and an unchanged
                    // line logged the same warning every poll, burying everything else in the channel
                    string what = $"'{c.Text}' row {l.Row}: '{l.Text.Trim()}'";
                    if (_loggedUncertain.Add(what))
                        _log.Warn($"uncertain glyph '{c.Text}' d={c.Distance:0.000} row {l.Row}: '{l.Text.Trim()}'");
                }
            if (_loggedUncertain.Count > 500) _loggedUncertain.Clear();
            var rows = lines.Select(l => _profile.ReadColour ? CduLineText.WithColour(l) : Tidy(l.Text)).ToArray();
            bool anyText = rows.Any(r => r.Length > 0);
            SetStatus(anyText ? $"{_device} connected" : $"{_device} connected - the screen is blank (aircraft unpowered?)");
            Render(rows);
            var said = _scratch.OnPoll(rows[A300CduScreenReader.Rows - 1], DateTime.UtcNow);
            if (said != null) _announcer.Announce(said);
        }
        catch (Exception ex)
        {
            _log.Error($"poll failed: {ex.Message}");
        }
        finally { _reading = false; }
    }

    /// <summary>Collapses the wide layout gaps between a line's left and right fields to a
    /// readable separator, keeping single word spaces.</summary>
    internal static string Tidy(string text)
    {
        var t = text.Trim();
        return System.Text.RegularExpressions.Regex.Replace(t, " {3,}", "   ");
    }

    private void Render(string[] rows)
    {
        if (rows.SequenceEqual(_rows)) return;
        _rows = rows;
        var lines = new List<string> { rows[0] };
        for (int pair = 0; pair < 6; pair++)
        {
            string label = rows[1 + pair * 2], data = rows[2 + pair * 2];
            if (label.Length > 0) lines.Add("   " + label);
            lines.Add(data.Length > 0 ? $"{pair + 1}: {data}" : $"{pair + 1}:");
        }
        lines.Add(rows[13]);
        int saved = _display.SelectedIndex;
        DisplayList.UpdateInPlace(_display, lines);
        string title = rows[0];
        if (title.Length > 0 && title != _lastTitle)
        {
            // Moving the focused list to the title row makes the screen reader read it; announce only
            // when that will not happen (focus elsewhere, or already on row 0), never both.
            bool selectionSpeaks = _display.Focused && saved != 0 && _display.Items.Count > 0;
            if (_lastTitle.Length > 0 && !selectionSpeaks) _announcer.Announce(title);
            _lastTitle = title;
            if (_display.Items.Count > 0) _display.SelectedIndex = 0;
        }
        else if (saved >= 0 && saved < _display.Items.Count)
            _display.SelectedIndex = saved;
    }

    // ---------------- keys ----------------

    private bool Write(string key)
    {
        if (!_sim.CalcWriteCanLand)
        {
            // keys are sent from a pool thread; the announcer speaks only from the UI thread
            string msg = $"{_device} keys unavailable: the MobiFlight module is not responding.";
            try
            {
                if (InvokeRequired) BeginInvoke(() => _announcer.AnnounceImmediate(msg));
                else _announcer.AnnounceImmediate(msg);
            }
            catch (InvalidOperationException) { }
            return false;
        }
        _sim.ExecuteCalculatorCode($"{Interlocked.Increment(ref _seq)} 0 * 1 (>L:{_keyPrefix}{key})");
        return true;
    }

    private void Press(string key)
    {
        _ = Task.Run(async () =>
        {
            await _keyGate.WaitAsync();
            try { Write(key); await Task.Delay(KeyGapMs); }
            finally { _keyGate.Release(); }
        });
    }

    private void Clear()
    {
        _scratch.SuppressUntil = DateTime.UtcNow.AddMilliseconds(400);
        Press("CLR");
    }

    private async Task TypeAsync(string text)
    {
        var keys = text.ToUpperInvariant().Select(_profile.KeyForChar).Where(k => k != null).ToList();
        if (keys.Count == 0) return;
        _scratch.SuppressUntil = DateTime.UtcNow.AddMilliseconds(keys.Count * KeyGapMs + 600);
        await _keyGate.WaitAsync();
        try
        {
            foreach (var k in keys) { if (!Write(k!)) break; await Task.Delay(KeyGapMs); }
        }
        finally { _keyGate.Release(); }
        // the scratchpad read-back (CduScratchpadAnnouncer) speaks what the CDU now shows
    }

    private void Form_KeyDown(object? sender, KeyEventArgs e)
    {
        bool alt = SettingsManager.Current.MCDUUseAlternateLSKKeys;
        string? key = null;
        if (alt)
        {
            if (!e.Control && !e.Alt && e.KeyCode >= Keys.F1 && e.KeyCode <= Keys.F6) key = $"LSK{e.KeyCode - Keys.F1 + 1}L";
            else if (!e.Control && !e.Alt && e.KeyCode >= Keys.F7 && e.KeyCode <= Keys.F12) key = $"LSK{e.KeyCode - Keys.F7 + 1}R";
        }
        else
        {
            if (e.Control && !e.Alt && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D6) key = $"LSK{e.KeyCode - Keys.D1 + 1}L";
            else if (e.Alt && !e.Control && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D6) key = $"LSK{e.KeyCode - Keys.D1 + 1}R";
        }
        // The Airbus convention the A320 windows set (2026-10-17): Page Up/Down and Alt+Up/Down scroll
        // (the MCDU's up/down slew keys — the F-PLN is what a pilot scrolls most), Alt+Left/Right turn
        // the page (the MCDU's left/right arrow keys).
        if (key == null && !e.Control && ((!e.Alt && e.KeyCode == Keys.PageDown) || (e.Alt && e.KeyCode == Keys.Down))) key = _profile.SlewDown;
        if (key == null && !e.Control && ((!e.Alt && e.KeyCode == Keys.PageUp) || (e.Alt && e.KeyCode == Keys.Up))) key = _profile.SlewUp;
        if (key == null && e.Alt && !e.Control && e.KeyCode == Keys.Right) key = _profile.NextPage;
        if (key == null && e.Alt && !e.Control && e.KeyCode == Keys.Left) key = _profile.PrevPage;
        if (key == null && e.Alt && !e.Control && !e.Shift)
        {
            key = _profile.AltKeys.TryGetValue(e.KeyCode, out var page) ? page : null;
            if (key == null && e.KeyCode == Keys.S) { _scratchpad.Focus(); Handled(e); return; }
            if (key == null && e.KeyCode == Keys.Home) { _display.Focus(); Handled(e); return; }
        }
        if (key == null && e.KeyCode == Keys.Back && !e.Alt && ActiveControl == _display) { Clear(); Handled(e); return; }
        if (key == null && e.KeyCode == Keys.Escape) { Close(); Handled(e); return; }
        if (key != null) { Press(key); Handled(e); }
    }

    private static void Handled(KeyEventArgs e) { e.Handled = true; e.SuppressKeyPress = true; }

    private void Scratchpad_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Return)
        {
            string text = _scratchpad.Text;
            _scratchpad.Clear();
            Handled(e);
            _ = TypeAsync(text);
        }
        else if (e.KeyCode == Keys.Back && _scratchpad.Text.Length == 0)
        {
            Clear();
            Handled(e);
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible) _poll.Stop();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _poll.Stop();
            _poll.Dispose();
            _keyGate.Dispose();
        }
        base.Dispose(disposing);
    }
}
