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
    /// True when every sample on a <paramref name="gridColumns"/> × <paramref name="gridRows"/>
    /// grid is near black (R+G+B ≤ <paramref name="darkThreshold"/>). A bitmap smaller than the
    /// grid is sampled pixel by pixel.
    /// </summary>
    internal static bool LooksBlank(Bitmap bitmap, int gridColumns = 24, int gridRows = 14, int darkThreshold = 30)
    {
        int stepX = Math.Max(1, bitmap.Width / gridColumns);
        int stepY = Math.Max(1, bitmap.Height / gridRows);
        for (int y = 0; y < bitmap.Height; y += stepY)
        {
            for (int x = 0; x < bitmap.Width; x += stepX)
            {
                var c = bitmap.GetPixel(x, y);
                if (c.R + c.G + c.B > darkThreshold) return false;
            }
        }
        return true;
    }
}
