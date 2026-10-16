using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>Brightness image of a captured CDU window: max(R,G,B) per pixel, row-major, plus the
/// R,G,B bytes the colour of each character is read from.</summary>
internal sealed record A300CduFrame(float[] Lum, int Width, int Height, byte[]? Rgb = null)
{
    public static A300CduFrame FromBitmap(Bitmap bmp)
    {
        using var rgb = bmp.PixelFormat == PixelFormat.Format24bppRgb
            ? (Bitmap)bmp.Clone()
            : bmp.Clone(new Rectangle(0, 0, bmp.Width, bmp.Height), PixelFormat.Format24bppRgb);
        var data = rgb.LockBits(new Rectangle(0, 0, rgb.Width, rgb.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            int w = rgb.Width, h = rgb.Height, stride = data.Stride;
            var row = new byte[stride];
            var lum = new float[w * h];
            var rgbOut = new byte[w * h * 3];
            for (int y = 0; y < h; y++)
            {
                Marshal.Copy(data.Scan0 + y * stride, row, 0, stride);
                for (int x = 0; x < w; x++)
                {
                    int o = x * 3;
                    lum[y * w + x] = Math.Max(row[o], Math.Max(row[o + 1], row[o + 2]));
                    int q = (y * w + x) * 3;
                    rgbOut[q] = row[o + 2]; rgbOut[q + 1] = row[o + 1]; rgbOut[q + 2] = row[o];   // BGR -> RGB
                }
            }
            return new A300CduFrame(lum, w, h, rgbOut);
        }
        finally { rgb.UnlockBits(data); }
    }

    public static A300CduFrame FromFile(string path)
    {
        using var bmp = new Bitmap(path);
        return FromBitmap(bmp);
    }

    /// <summary>
    /// Cheap per-row fingerprint used to skip re-reading rows that did not change between polls
    /// (a scratchpad keystroke changes one row; most polls change none).
    /// </summary>
    public ulong[] RowHashes(int rowCount, double y0, double pitchY, int top)
    {
        var hashes = new ulong[rowCount];
        for (int r = 0; r < rowCount; r++)
        {
            int a = Math.Max(0, (int)(y0 + (r - 0.6) * pitchY)), b = Math.Min(Height - top - 1, (int)(y0 + (r + 0.6) * pitchY));
            ulong hsh = 1469598103934665603UL;
            for (int y = a; y <= b; y++)
            {
                int o = (y + top) * Width;
                for (int x = 0; x < Width; x += 1)
                {
                    float v = Lum[o + x];
                    if (v > 60) { hsh ^= (ulong)((y << 12) ^ x ^ ((int)v << 20)); hsh *= 1099511628211UL; }
                }
            }
            hashes[r] = hsh;
        }
        return hashes;
    }
}
