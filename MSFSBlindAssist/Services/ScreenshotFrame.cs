using System.Drawing;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Decides whether a captured frame carries a picture at all. PrintWindow can hand back a black
/// frame on some fullscreen setups; that frame must fall through to the screen copy rather than
/// reach the AI, which would confidently read "no display visible".
/// </summary>
internal static class ScreenshotFrame
{
    /// <summary>
    /// True when the frame is a failed capture: every sample on a <paramref name="gridColumns"/> ×
    /// <paramref name="gridRows"/> grid is near black (R+G+B ≤ <paramref name="darkThreshold"/>)
    /// AND the samples are uniform (their R+G+B values span at most <paramref name="uniformSpread"/>).
    /// A failed PrintWindow frame is flat black. A night cockpit is dark too, but its dim lighting
    /// varies from sample to sample, and a darkness-only test sent that real picture to the screen
    /// copy — which captures whatever is on top of the simulator, an MSFSBA window included. A
    /// bitmap smaller than the grid is sampled pixel by pixel.
    /// </summary>
    internal static bool LooksBlank(Bitmap bitmap, int gridColumns = 24, int gridRows = 14, int darkThreshold = 30,
        int uniformSpread = 6)
    {
        int stepX = Math.Max(1, bitmap.Width / gridColumns);
        int stepY = Math.Max(1, bitmap.Height / gridRows);
        int darkest = int.MaxValue;
        int brightest = int.MinValue;
        for (int y = 0; y < bitmap.Height; y += stepY)
        {
            for (int x = 0; x < bitmap.Width; x += stepX)
            {
                var c = bitmap.GetPixel(x, y);
                int sum = c.R + c.G + c.B;
                if (sum > darkThreshold) return false;
                darkest = Math.Min(darkest, sum);
                brightest = Math.Max(brightest, sum);
                if (brightest - darkest > uniformSpread) return false;
            }
        }
        return true;
    }
}
