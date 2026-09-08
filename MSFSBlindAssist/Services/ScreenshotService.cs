using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MSFSBlindAssist.Utils;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Service for capturing screenshots of the Microsoft Flight Simulator window.
/// </summary>
public class ScreenshotService
{
    #region Win32 API Imports

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
        IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    /// <summary>Ask DWM for the window's full composed content, not just what is on screen.</summary>
    private const uint PW_RENDERFULLCONTENT = 0x00000002;

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private const int SRCCOPY = 0x00CC0020;

    #endregion

    /// <summary>
    /// Captures the MSFS window as PNG bytes, or null if the window is not found.
    ///
    /// PrintWindow with full-content rendering first: it asks the compositor for the window's own
    /// pixels, so a window sitting on top of the simulator — one of this app's, typically — does
    /// not end up in the picture (live-verified 2026-09-08 with the sim fully hidden behind
    /// another app). Some fullscreen setups hand back a black frame instead; that falls through
    /// to the screen copy this method always used, so nothing is worse than before.
    /// </summary>
    public async Task<byte[]?> CaptureAsync()
    {
        return await Task.Run(() =>
        {
            IntPtr hwnd = FindMsfsWindow();
            if (hwnd == IntPtr.Zero)
            {
                return null;
            }

            if (!GetWindowRect(hwnd, out RECT rect))
            {
                return null;
            }

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;

            if (width <= 0 || height <= 0)
            {
                return null;
            }

            return CaptureByPrintWindow(hwnd, width, height) ?? CaptureByScreenCopy(rect, width, height);
        });
    }

    /// <summary>The window's composed content via PrintWindow; null when it fails or comes back blank.</summary>
    private static byte[]? CaptureByPrintWindow(IntPtr hwnd, int width, int height)
    {
        try
        {
            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                IntPtr hdc = graphics.GetHdc();
                bool rendered;
                try
                {
                    rendered = PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT);
                }
                finally
                {
                    graphics.ReleaseHdc(hdc);
                }
                if (!rendered)
                {
                    Log.Debug("Services", "PrintWindow returned false; falling back to the screen copy");
                    return null;
                }
            }

            if (ScreenshotFrame.LooksBlank(bitmap))
            {
                Log.Debug("Services", "PrintWindow frame is blank; falling back to the screen copy");
                return null;
            }

            using var memoryStream = new MemoryStream();
            bitmap.Save(memoryStream, ImageFormat.Png);
            return memoryStream.ToArray();
        }
        catch (Exception ex)
        {
            Log.Debug("Services", $"PrintWindow capture failed; falling back to the screen copy: {ex.Message}");
            return null;
        }
    }

    /// <summary>The screen area under the window, as this service captured it before 2026-09.</summary>
    private static byte[]? CaptureByScreenCopy(RECT rect, int width, int height)
    {
        // Get device context of the screen
        IntPtr hdcScreen = GetDC(IntPtr.Zero);
        if (hdcScreen == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            // Create compatible device context
            IntPtr hdcMemory = CreateCompatibleDC(hdcScreen);
            if (hdcMemory == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                // Create compatible bitmap
                IntPtr hBitmap = CreateCompatibleBitmap(hdcScreen, width, height);
                if (hBitmap == IntPtr.Zero)
                {
                    return null;
                }

                try
                {
                    // Select bitmap into memory DC
                    IntPtr hOldBitmap = SelectObject(hdcMemory, hBitmap);

                    // Copy from screen to memory DC using screen coordinates
                    bool success = BitBlt(hdcMemory, 0, 0, width, height,
                                        hdcScreen, rect.Left, rect.Top, SRCCOPY);

                    if (!success)
                    {
                        return null;
                    }

                    // Select old bitmap back
                    SelectObject(hdcMemory, hOldBitmap);

                    // Convert HBITMAP to Bitmap and then to PNG byte array
                    using (var bitmap = Image.FromHbitmap(hBitmap))
                    {
                        using (var memoryStream = new MemoryStream())
                        {
                            bitmap.Save(memoryStream, ImageFormat.Png);
                            return memoryStream.ToArray();
                        }
                    }
                }
                finally
                {
                    DeleteObject(hBitmap);
                }
            }
            finally
            {
                DeleteDC(hdcMemory);
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdcScreen);
        }
    }

    /// <summary>
    /// Finds the MSFS window handle by detecting the running simulator process.
    /// </summary>
    private IntPtr FindMsfsWindow()
    {
        try
        {
            // Detect which simulator is running
            string simulatorVersion = SimulatorDetector.DetectRunningSimulator();
            if (simulatorVersion == "Unknown")
            {
                return IntPtr.Zero;
            }

            // Get the process name for the detected simulator
            string? processName = SimulatorDetector.GetProcessName(simulatorVersion);
            if (string.IsNullOrEmpty(processName))
            {
                return IntPtr.Zero;
            }

            // Find the process
            Process[] processes = Process.GetProcessesByName(processName);
            if (processes == null || processes.Length == 0)
            {
                return IntPtr.Zero;
            }

            // Get the main window handle of the first matching process
            IntPtr hwnd = processes[0].MainWindowHandle;

            // Clean up process objects
            foreach (var process in processes)
            {
                process.Dispose();
            }

            return hwnd;
        }
        catch (Exception ex)
        {
            Log.Debug("Services", $"Error finding MSFS window: {ex.Message}");
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// Checks if the MSFS window is currently available.
    /// </summary>
    public bool IsMsfsWindowAvailable()
    {
        return FindMsfsWindow() != IntPtr.Zero;
    }
}
