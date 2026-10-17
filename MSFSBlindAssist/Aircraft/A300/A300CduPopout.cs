using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>
/// Creates, finds and captures the popped-out A300 CDU window the pixel reader works from.
///
/// MSFS pops a gauge out with Right-Alt + left click ON the gauge, and there is no SimVar, key
/// event or API for it. So, when the CDU window is opened and no pop-out exists, this:
///   1. switches the camera to the aircraft's own "MCDU CPT"/"MCDU FO" instrument view
///      (cameras.cfg instrument views 22/23, 0-based — the SimVar index the camera code writes);
///   2. brings the sim to the foreground — a click only reaches the sim when it is the active
///      window (measured: with this app's window on top, the click landed on the app);
///   3. finds the CDU screen in a capture of the sim window by its green text, falling back to the
///      measured screen-relative position for that view when the CDU is dark;
///   4. holds Right-Alt, clicks it, waits for the new top-level "WASMINSTRUMENT" window;
///   5. resizes it to 3x (the WASM gauge RE-RENDERS at the window size — crisp, not upscaled —
///      which is what makes the glyph matcher exact) and parks it beyond the virtual screen, where
///      it keeps rendering (verified live) and can never be clicked by accident;
///   6. restores the previous camera view (when it was a built-in one), the cursor position and the
///      foreground window.
/// The pop-out is identified by its client aspect ratio (450x369, the FMGS gauge's size), which
/// no other A300 gauge shares. Capture is PrintWindow(PW_RENDERFULLCONTENT): it works with the
/// window covered or off-screen.
/// </summary>
internal sealed class A300CduPopout
{
    public const int TargetWidth = 1350, TargetHeight = 1138;   // 3x the 450x400 gauge window
    private const int InstrumentViewType = 2;

    private readonly CduPopoutProfile _profile;

    public A300CduPopout(CduPopoutProfile profile) => _profile = profile;

    private static readonly LogChannel _log = Log.Channel("a300_cdu");
    private IntPtr _hwnd;

    public bool HasWindow => _hwnd != IntPtr.Zero && IsWindow(_hwnd);

    /// <summary>Existing CDU pop-out of the sim process (the one this class made, else any match).</summary>
    public IntPtr Find()
    {
        if (HasWindow) return _hwnd;
        _hwnd = FindCandidates().FirstOrDefault();
        return _hwnd;
    }

    private List<IntPtr> FindCandidates()
    {
        var found = new List<IntPtr>();
        var proc = SimProcess();
        if (proc == null) return found;
        uint pid = (uint)proc.Id;
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out uint p);
            if (p != pid || !IsWindowVisible(h)) return true;
            var sb = new StringBuilder(64);
            GetWindowText(h, sb, 64);
            if (sb.ToString() != "WASMINSTRUMENT") return true;
            if (GetClientRect(h, out var rc) && rc.B > 0)
            {
                double aspect = (double)rc.R / rc.B;
                if (Math.Abs(aspect - _profile.ClientAspect) < 0.03) found.Add(h);
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static Process? SimProcess() =>
        Process.GetProcessesByName("FlightSimulator").FirstOrDefault()
        ?? Process.GetProcessesByName("FlightSimulator2024").FirstOrDefault();

    /// <summary>Sizes the pop-out to 3x and parks it off the desktop. Idempotent.</summary>
    public void Park(IntPtr h)
    {
        int tw = _profile.WindowWidth, th = _profile.WindowHeight;
        int x = SystemInformation.VirtualScreen.Left - tw - 200;
        int y = SystemInformation.VirtualScreen.Top;
        GetWindowRect(h, out var r);
        if (r.R - r.L == tw && r.B - r.T == th && r.L == x) return;
        SetWindowPos(h, HWND_BOTTOM, x, y, tw, th, SWP_NOACTIVATE);
    }

    /// <summary>
    /// Makes sure a CDU pop-out exists, creating it if needed. Returns null on success, otherwise the
    /// reason to speak. Runs on a worker thread; the camera calls go through the supplied delegates.
    /// </summary>
    public async Task<string?> EnsureAsync(bool firstOfficer, Func<Task<(int type, int index)?>> readCamera,
        Action<int, int> setCamera, CancellationToken ct = default)
    {
        var existing = Find();
        if (existing != IntPtr.Zero) { Park(existing); return null; }
        var proc = SimProcess();
        if (proc == null) return "Flight simulator window not found.";
        IntPtr sim = proc.MainWindowHandle;
        if (sim == IntPtr.Zero) return "Flight simulator window not found.";

        var before = await readCamera();
        IntPtr prevForeground = GetForegroundWindow();
        GetCursorPos(out var prevCursor);
        var oldCandidates = FindCandidates();
        try
        {
            int view = firstOfficer ? _profile.FirstOfficerViewIndex : _profile.CaptainViewIndex;
            var tried = new List<string>();
            // Two rounds: each re-selects the camera, waits for it to stop moving and locates the
            // screen afresh. Measured 2026-10-16 (A330, FS2024): a camera still gliding in from the
            // pilot view put both clicks of one round off the screen while the next round succeeded.
            for (int round = 0; round < 2; round++)
            {
                setCamera(InstrumentViewType, view);
                await Task.Delay(1200, ct);   // camera move + a rendered frame
                await WaitForStillCameraAsync(sim, ct);
                if (!BringToFront(sim)) return "Could not bring the simulator to the front to pop out the CDU.";
                await Task.Delay(300, ct);

                foreach (var target in LocateCdu(sim).Take(3))
                {
                    if (GetForegroundWindow() != sim && !BringToFront(sim)) break;
                    _log.Info($"pop-out: round {round + 1}, clicking CDU at {target.X},{target.Y} (source {target.Source})");
                    tried.Add($"{target.X},{target.Y} ({target.Source})");
                    ClickWithRightAlt(target.X, target.Y);
                    for (int i = 0; i < 25; i++)
                    {
                        await Task.Delay(120, ct);
                        var now = FindCandidates().Except(oldCandidates).FirstOrDefault();
                        if (now != IntPtr.Zero)
                        {
                            _hwnd = now;
                            await Task.Delay(300, ct);
                            Park(now);
                            _log.Info("pop-out: created and parked");
                            LastDiagnostic = "clicked " + string.Join("; ", tried);
                            return null;
                        }
                    }
                }
            }
            LastDiagnostic = "no window after clicks at " + string.Join("; ", tried);
            _log.Warn("pop-out: " + LastDiagnostic);
            return "The CDU did not pop out. Make sure the cockpit view is active and try again.";
        }
        finally
        {
            await RestoreCameraAsync(before, readCamera, setCamera);
            SetCursorPos(prevCursor.X, prevCursor.Y);
            if (prevForeground != IntPtr.Zero) BringToFront(prevForeground);
        }
    }

    /// <summary>Takes the camera out of the MCDU close-up again. The pilot's own view is tried first;
    /// when that write does not take — a user-saved custom cockpit view reads as type 1 with an index
    /// the write refuses, and an instrument view (type 2) is not where anyone sits — the camera goes
    /// to the default pilot seat (1, 0) rather than staying on the MCDU. Left in the close-up, the
    /// cockpit's positioned ambience (avionics fans) went silent (A330, 2026-10-16). Views outside the
    /// cockpit (external, drone) are left alone, as before.</summary>
    private static async Task RestoreCameraAsync((int type, int index)? before,
        Func<Task<(int type, int index)?>> readCamera, Action<int, int> setCamera)
    {
        if (before is not { } b || (b.type != 1 && b.type != InstrumentViewType)) return;
        try
        {
            // Every write is checked by reading the camera back and repeated until it holds: measured
            // live 2026-10-17 (A330, FS2024), a single type+index write sent straight after the pop-out
            // was reported sent yet left the camera on the MCDU close-up — twice in one flight, the
            // cockpit ambience silent each time. Writing again once the type has taken is what lands.
            if (b.type == 1 && await SetAndConfirmAsync(b.index)) return;
            if (await SetAndConfirmAsync(0))
            {
                if (b.type == 1) _log.Info($"pop-out: camera {b.type}/{b.index} could not be restored; returned to the pilot seat");
                return;
            }
            _log.Warn("pop-out: the camera did not leave the CDU view; the pilot seat could not be restored");
        }
        catch (Exception ex) { _log.Warn($"pop-out: camera restore failed: {ex.Message}"); }

        async Task<bool> SetAndConfirmAsync(int index)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                setCamera(1, index);
                await Task.Delay(400);
                if (await readCamera() is { type: 1 } now && now.index == index) return true;
            }
            return false;
        }
    }

    /// <summary>Waits (up to ~3 s) until two captures 300 ms apart nearly match: the camera has
    /// stopped moving, so the picture the screen is located in is the one that will be clicked.</summary>
    private static async Task WaitForStillCameraAsync(IntPtr sim, CancellationToken ct)
    {
        GetWindowRect(sim, out var r);
        int w = r.R - r.L, h = r.B - r.T;
        byte[]? prev = null;
        for (int i = 0; i < 10; i++)
        {
            using var bmp = CaptureWindow(sim, w, h);
            if (bmp == null) return;
            var cur = Thumbnail(bmp);
            if (prev != null)
            {
                double diff = 0;
                for (int k = 0; k < cur.Length; k++) diff += Math.Abs(cur[k] - prev[k]);
                if (diff / cur.Length < 3) return;
            }
            prev = cur;
            await Task.Delay(300, ct);
        }
    }

    /// <summary>A 64x64 brightness thumbnail (cheap motion check).</summary>
    private static byte[] Thumbnail(Bitmap bmp)
    {
        using var small = new Bitmap(bmp, 64, 64);
        var t = new byte[64 * 64];
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                var c = small.GetPixel(x, y);
                t[y * 64 + x] = Math.Max(c.R, Math.Max(c.G, c.B));
            }
        return t;
    }

    private readonly record struct ClickTarget(int X, int Y, string Source);

    /// <summary>Last attempt's click points, for the log and the status line.</summary>
    public string LastDiagnostic { get; private set; } = "";

    /// <summary>
    /// Candidate CDU-screen centres in screen coordinates, best first: clusters of CDU-green text in a
    /// capture of the sim window, NEAREST THE VIEW CENTRE first (the MCDU camera is centred on its own
    /// CDU; the other pilot's CDU and the ECAM are also green — the largest cluster was measured to
    /// be the first officer's CDU at the screen edge), then the measured position of the screen in
    /// that view for a dark CDU.
    /// </summary>
    private IEnumerable<ClickTarget> LocateCdu(IntPtr sim)
    {
        GetWindowRect(sim, out var r);
        int w = r.R - r.L, h = r.B - r.T;
        var list = new List<ClickTarget>();
        using (var bmp = CaptureWindow(sim, w, h))
        {
            if (bmp != null)
            {
                var expected = new Point((int)(w * _profile.FallbackX), (int)(h * _profile.FallbackY));
                var points = _profile.TextOnDark ? FindDarkGlass(bmp, expected) : FindGreenText(bmp);
                foreach (var p in points)
                    list.Add(new ClickTarget(r.L + p.X, r.T + p.Y, _profile.TextOnDark ? "screen glass" : "green text"));
            }
        }
        list.Add(new ClickTarget(r.L + (int)(w * _profile.FallbackX), r.T + (int)(h * _profile.FallbackY), "fixed position"));
        return list;
    }

    /// <summary>
    /// The screen of a CDU that draws on BLACK (the A330), found as the dark glass around where it is
    /// expected in its camera view: pixels whose 9x9 neighbourhood is mostly near-black (so the text
    /// inside the glass stays part of it), the patch nearest <paramref name="expected"/>, flooded to its
    /// extent and clicked in the middle. Measured 2026-10-16 in view 12: the patch is exactly the MCDU
    /// screen at every darkness threshold from 30 to 40. Text-based searches failed there — the radio
    /// panel's lit digits and the MCDU's own keypad letters both read as text on glass.
    /// </summary>
    private static List<Point> FindDarkGlass(Bitmap bmp, Point expected)
    {
        int w = bmp.Width, h = bmp.Height;
        var lum = new byte[w * h];
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            var row = new byte[data.Stride];
            for (int y = 0; y < h; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, data.Stride);
                for (int x = 0; x < w; x++)
                    lum[y * w + x] = Math.Max(row[x * 3], Math.Max(row[x * 3 + 1], row[x * 3 + 2]));
            }
        }
        finally { bmp.UnlockBits(data); }
        const int Dark = 35, K = 9, Half = K / 2;
        // summed-area table of dark pixels
        var sat = new int[(w + 1) * (h + 1)];
        for (int y = 0; y < h; y++)
        {
            int rowSum = 0;
            for (int x = 0; x < w; x++)
            {
                rowSum += lum[y * w + x] <= Dark ? 1 : 0;
                sat[(y + 1) * (w + 1) + x + 1] = sat[y * (w + 1) + x + 1] + rowSum;
            }
        }
        bool Glass(int x, int y)
        {
            int x0 = Math.Max(0, x - Half), y0 = Math.Max(0, y - Half), x1 = Math.Min(w, x + Half + 1), y1 = Math.Min(h, y + Half + 1);
            int s = sat[y1 * (w + 1) + x1] - sat[y0 * (w + 1) + x1] - sat[y1 * (w + 1) + x0] + sat[y0 * (w + 1) + x0];
            return s >= 0.6 * (x1 - x0) * (y1 - y0);
        }
        // nearest glass pixel to the expected place (search outward on a coarse grid, then flood)
        Point? start = null;
        for (int rad = 0; rad < Math.Max(w, h) / 3 && start == null; rad += 4)
            for (int dy = -rad; dy <= rad && start == null; dy += 4)
                foreach (int dx in new[] { -rad, rad })
                {
                    int x = expected.X + dx, y = expected.Y + dy;
                    if (x >= 0 && y >= 0 && x < w && y < h && Glass(x, y)) { start = new Point(x, y); break; }
                    x = expected.X + dy; y = expected.Y + dx;
                    if (x >= 0 && y >= 0 && x < w && y < h && Glass(x, y)) { start = new Point(x, y); break; }
                }
        if (start is not { } s0) return new List<Point>();
        var seen = new bool[w * h];
        var q = new Queue<Point>();
        q.Enqueue(s0); seen[s0.Y * w + s0.X] = true;
        int minX = s0.X, maxX = s0.X, minY = s0.Y, maxY = s0.Y, count = 0;
        while (q.Count > 0)
        {
            var p = q.Dequeue();
            count++;
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X); minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = p.X + dx, ny = p.Y + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h || seen[ny * w + nx]) continue;
                seen[ny * w + nx] = true;
                if (Glass(nx, ny)) q.Enqueue(new Point(nx, ny));
            }
        }
        double bw = maxX - minX + 1, bh = maxY - minY + 1;
        // a screen, not a sliver or the whole night sky: 1-25 % of the picture, roughly CDU-shaped
        if (count < 0.01 * w * h || count > 0.25 * w * h || bw / bh < 0.8 || bw / bh > 2.4)
        {
            _log.Warn($"pop-out: dark patch at {s0} rejected ({bw}x{bh}, {count} px)");
            return new List<Point>();
        }
        return new List<Point> { new((minX + maxX) / 2, (minY + maxY) / 2) };
    }

    private static List<Point> FindGreenText(Bitmap bmp)
    {
        const int cell = 16;
        int cw = bmp.Width / cell, ch = bmp.Height / cell;
        var hits = new int[cw, ch];
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            var row = new byte[data.Stride];
            for (int y = 0; y < ch * cell; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, data.Stride);
                for (int x = 0; x < cw * cell; x++)
                {
                    int b = row[x * 3], g = row[x * 3 + 1], rr = row[x * 3 + 2];
                    // CDU text green (~100,235,115); the VFR map's land green is much less saturated
                    if (g >= 190 && g - rr >= 90 && g - b >= 70) hits[x / cell, y / cell]++;
                }
            }
        }
        finally { bmp.UnlockBits(data); }
        var seen = new bool[cw, ch];
        var clusters = new List<(int count, Rectangle box)>();
        for (int x = 0; x < cw; x++)
            for (int y = 0; y < ch; y++)
            {
                if (seen[x, y] || hits[x, y] < 3) continue;
                int count = 0, x0 = x, x1 = x, y0 = y, y1 = y;
                var q = new Queue<(int, int)>();
                q.Enqueue((x, y)); seen[x, y] = true;
                while (q.Count > 0)
                {
                    var (a, bq) = q.Dequeue();
                    count++;
                    x0 = Math.Min(x0, a); x1 = Math.Max(x1, a); y0 = Math.Min(y0, bq); y1 = Math.Max(y1, bq);
                    for (int dx = -2; dx <= 2; dx++)          // text lines are a cell or two apart
                        for (int dy = -2; dy <= 2; dy++)
                        {
                            int nx = a + dx, ny = bq + dy;
                            if (nx < 0 || ny < 0 || nx >= cw || ny >= ch || seen[nx, ny] || hits[nx, ny] < 3) continue;
                            seen[nx, ny] = true; q.Enqueue((nx, ny));
                        }
                }
                clusters.Add((count, Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1)));
            }
        double cx = bmp.Width / 2.0, cy = bmp.Height / 2.0;
        return clusters.Where(c => c.count >= 12)
            .Select(c => new Point((c.box.Left + c.box.Right) * cell / 2, (c.box.Top + c.box.Bottom) * cell / 2))
            .OrderBy(p => Math.Pow(p.X - cx, 2) + Math.Pow(p.Y - cy, 2))
            .ToList();
    }

    /// <summary>Captures the pop-out as a brightness frame, or null when it is gone or blank.</summary>
    public A300CduFrame? Capture()
    {
        var h = Find();
        if (h == IntPtr.Zero) return null;
        GetWindowRect(h, out var r);
        int w = r.R - r.L, hh = r.B - r.T;
        if (w != _profile.WindowWidth || hh != _profile.WindowHeight) { Park(h); GetWindowRect(h, out r); w = r.R - r.L; hh = r.B - r.T; }
        using var bmp = CaptureWindow(h, w, hh);
        return bmp == null ? null : A300CduFrame.FromBitmap(bmp);
    }

    /// <summary>Closes the pop-out (the gauge returns to the cockpit).</summary>
    public void Close()
    {
        if (HasWindow) PostMessage(_hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        _hwnd = IntPtr.Zero;
    }

    private static Bitmap? CaptureWindow(IntPtr h, int w, int hh)
    {
        if (w <= 0 || hh <= 0) return null;
        var bmp = new Bitmap(w, hh, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            var hdc = g.GetHdc();
            bool ok = PrintWindow(h, hdc, PW_RENDERFULLCONTENT);
            g.ReleaseHdc(hdc);
            if (!ok) { bmp.Dispose(); return null; }
        }
        return bmp;
    }

    private static bool BringToFront(IntPtr h)
    {
        if (GetForegroundWindow() == h) return true;
        // Windows only lets the process that owns the foreground change it; a synthetic Alt tap
        // satisfies the "last input" rule, the documented way around the foreground lock.
        keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
        keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        if (IsIconic(h)) ShowWindow(h, SW_RESTORE);
        BringWindowToTop(h);
        SetForegroundWindow(h);
        for (int i = 0; i < 10 && GetForegroundWindow() != h; i++) Thread.Sleep(50);
        if (GetForegroundWindow() == h) return true;
        // Second way (measured 2026-10-16 on FS2024: the Alt tap alone was refused from a process
        // that did not own the foreground): attach to the foreground window's input queue, which
        // lets SetForegroundWindow through, and detach again whatever happens.
        IntPtr fg = GetForegroundWindow();
        uint fgThread = fg == IntPtr.Zero ? 0 : GetWindowThreadProcessId(fg, out _);
        uint me = GetCurrentThreadId();
        bool attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
        try
        {
            BringWindowToTop(h);
            SetForegroundWindow(h);
        }
        finally
        {
            if (attached) AttachThreadInput(me, fgThread, false);
        }
        for (int i = 0; i < 10 && GetForegroundWindow() != h; i++) Thread.Sleep(50);
        if (GetForegroundWindow() == h) return true;
        // Third and fourth ways: the shell's own window switch, then Windows Script Host's
        // AppActivate — the one that DID bring FS2024 forward from a background process (measured
        // 2026-10-16 when the two above were refused).
        try { SwitchToThisWindow(h, true); } catch { }
        for (int i = 0; i < 10 && GetForegroundWindow() != h; i++) Thread.Sleep(50);
        if (GetForegroundWindow() == h) return true;
        try
        {
            GetWindowThreadProcessId(h, out uint pid);
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type != null && Activator.CreateInstance(type) is object shell)
            {
                type.InvokeMember("AppActivate", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { (int)pid });
                Marshal.FinalReleaseComObject(shell);
            }
            SetForegroundWindow(h);
        }
        catch (Exception ex) { _log.Warn($"AppActivate failed: {ex.Message}"); }
        for (int i = 0; i < 20 && GetForegroundWindow() != h; i++) Thread.Sleep(50);
        bool ok = GetForegroundWindow() == h;
        if (!ok) _log.Warn("could not bring the window to the front by any of four ways");
        return ok;
    }

    private static void ClickWithRightAlt(int x, int y)
    {
        SetCursorPos(x - 30, y);
        for (int i = 0; i < 30; i++) { mouse_event(MOUSEEVENTF_MOVE, 1, 0, 0, UIntPtr.Zero); Thread.Sleep(15); }
        Thread.Sleep(300);
        keybd_event(VK_RMENU, 0x38, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
        Thread.Sleep(250);
        try
        {
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(120);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(250);
        }
        finally
        {
            keybd_event(VK_RMENU, 0x38, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);  // never leave Alt held
        }
    }

    // ---------------- Win32 ----------------
    private const uint PW_RENDERFULLCONTENT = 2, SWP_NOACTIVATE = 0x0010, WM_CLOSE = 0x0010;
    private const uint MOUSEEVENTF_MOVE = 0x1, MOUSEEVENTF_LEFTDOWN = 0x2, MOUSEEVENTF_LEFTUP = 0x4;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2;
    private const byte VK_MENU = 0x12, VK_RMENU = 0xA5;
    private const int SW_RESTORE = 9;
    private static readonly IntPtr HWND_BOTTOM = new(1);

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern void SwitchToThisWindow(IntPtr h, bool altTab);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint f, UIntPtr e);
}
