// A330 MCDU harness (adapted from the iniBuilds A300 harness; same INI_MCDU1_ keys): presses keys through the MobiFlight calc channel (seq-prefixed so repeats
// never coalesce) and captures the popped-out CDU window with PrintWindow. Reads a script:
//   key NAME        press INI_MCDU1_NAME
//   type TEXT       press one key per character (A-Z 0-9 . / - + and space)
//   clr N           press CLR N times
//   wait MS
//   cap PATH        capture the WASMINSTRUMENT window to PATH (png)
//   raw RPN         send arbitrary calculator code
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.FlightSimulator.SimConnect;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
struct CommandData { [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)] public string command; }

static class P
{
    enum CDA { Cmd = 1000 }
    enum DEF { Cmd = 2000 }
    static SimConnect sc;
    static int seq = (int)(DateTime.UtcNow.Ticks % 100000);
    static int keyGap = 160;

    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr v);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

    static void Send(string rpn)
    {
        var cmd = new CommandData { command = $"MF.SimVars.Set.{++seq} 0 * {rpn}" };
        sc.SetClientData(CDA.Cmd, DEF.Cmd, SIMCONNECT_CLIENT_DATA_SET_FLAG.DEFAULT, 0, cmd);
    }

    static void Key(string name) { Send($"1 (>L:INI_MCDU1_{name})"); Thread.Sleep(keyGap); }

    static string KeyFor(char c) => c switch
    {
        >= 'A' and <= 'Z' => c.ToString(),
        >= '0' and <= '9' => c.ToString(),
        '.' => "DECIMAL", '/' => "SLASH", '-' => "PLUS", '+' => "PLUS", ' ' => "SPACE",
        _ => throw new ArgumentException($"no key for '{c}'")
    };

    static void Cap(string path)
    {
        var h = FindWindow(null, "WASMINSTRUMENT");
        if (h == IntPtr.Zero) { Console.WriteLine("  no WASMINSTRUMENT window"); return; }
        GetWindowRect(h, out var r);
        using var bmp = new Bitmap(r.R - r.L, r.B - r.T, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp)) { var hdc = g.GetHdc(); PrintWindow(h, hdc, 2); g.ReleaseHdc(hdc); }
        bmp.Save(path, ImageFormat.Png);
        Console.WriteLine($"  cap {path} {bmp.Width}x{bmp.Height}");
    }

    static int Main(string[] args)
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        sc = new SimConnect("A330Harness", IntPtr.Zero, 0, null, 0);
        sc.MapClientDataNameToID("MobiFlight.Command", CDA.Cmd);
        sc.AddToClientDataDefinition(DEF.Cmd, 0, 1024, 0, 0);
        var pump = new Thread(() => { while (true) { try { sc.ReceiveMessage(); } catch { } Thread.Sleep(15); } }) { IsBackground = true };
        pump.Start();
        Thread.Sleep(300);
        var lines = args.Length > 0 ? File.ReadAllLines(args[0]) : Console.In.ReadToEnd().Split('\n');
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith("#")) continue;
            int sp = line.IndexOf(' ');
            string op = sp < 0 ? line : line[..sp], arg = sp < 0 ? "" : line[(sp + 1)..];
            switch (op)
            {
                case "key": Key(arg); break;
                case "type": foreach (var c in arg) Key(KeyFor(c)); break;
                case "clr": for (int i = 0; i < int.Parse(arg); i++) Key("CLR"); break;
                case "wait": Thread.Sleep(int.Parse(arg)); break;
                case "gap": keyGap = int.Parse(arg); break;
                case "cap": Thread.Sleep(250); Cap(arg); break;
                case "raw": Send(arg); Thread.Sleep(keyGap); break;
                default: Console.WriteLine($"  ?? {line}"); break;
            }
        }
        Thread.Sleep(200);
        sc.Dispose();
        return 0;
    }
}
