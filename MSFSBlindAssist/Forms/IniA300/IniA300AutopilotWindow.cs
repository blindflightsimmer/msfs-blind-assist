using System.Runtime.InteropServices;

namespace MSFSBlindAssist.Forms.IniA300;

/// <summary>
/// The iniBuilds Ctrl+P autopilot window (A330, A321LR): every FCU mode button and knob push/pull as a
/// button labelled with its live state (from the FCU's own button lights / engage flags), plus a
/// Read Flight Modes button. Pressing a button fires the aircraft's pulse L:var; the resulting
/// mode change is announced by the definition's flight-mode monitor, not by this window (the
/// screen reader already reads the button).
/// </summary>
public sealed class IniA300AutopilotWindow : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    public sealed record Row(string Label, Func<string> State, Action Press);

    private readonly List<(Button button, Row row)> _buttons = new();
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 500 };
    private IntPtr _previousWindow;

    public IniA300AutopilotWindow(IEnumerable<Row> rows, string aircraftName)
    {
        Text = $"{aircraftName} Autopilot";
        AccessibleName = $"{aircraftName} Autopilot";
        KeyPreview = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        StartPosition = FormStartPosition.CenterScreen;
        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill, Padding = new Padding(8),
        };
        foreach (var r in rows)
        {
            var b = new Button { AutoSize = true, MinimumSize = new Size(320, 0), TextAlign = ContentAlignment.MiddleLeft };
            b.Text = Caption(r);
            b.Click += (_, _) => r.Press();
            _buttons.Add((b, r));
            panel.Controls.Add(b);
        }
        var close = new Button { Text = "Close", AutoSize = true };
        close.Click += (_, _) => Close();
        panel.Controls.Add(close);
        Controls.Add(panel);
        CancelButton = close;
        _refresh.Tick += (_, _) => RefreshStates();
        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
            }
        };
        VisibleChanged += (_, _) => { if (Visible) _refresh.Start(); else _refresh.Stop(); };
    }

    private static string Caption(Row r)
    {
        string s = "";
        try { s = r.State(); } catch { }
        return s.Length > 0 ? $"{r.Label}: {s}" : r.Label;
    }

    private void RefreshStates()
    {
        foreach (var (b, r) in _buttons)
        {
            string c = Caption(r);
            if (b.Text != c) b.Text = c;
        }
    }

    public void ShowForm()
    {
        _previousWindow = GetForegroundWindow();
        RefreshStates();
        Show();
        BringToFront();
        Activate();
        if (_buttons.Count > 0) _buttons[0].button.Focus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _refresh.Stop(); _refresh.Dispose(); }
        base.Dispose(disposing);
    }
}
