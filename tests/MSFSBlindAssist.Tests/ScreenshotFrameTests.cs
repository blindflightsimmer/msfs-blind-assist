using System.Drawing;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The blank-frame check that decides whether a PrintWindow capture is usable or the screen copy
/// must be taken instead. Sampled on a grid, so a 4K frame costs a few hundred pixel reads.
/// </summary>
public class ScreenshotFrameTests
{
    private static Bitmap Filled(int width, int height, Color colour)
    {
        var bitmap = new Bitmap(width, height);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(colour);
        return bitmap;
    }

    [Fact]
    public void AnAllBlackFrame_LooksBlank()
    {
        using var bitmap = Filled(640, 360, Color.Black);
        Assert.True(ScreenshotFrame.LooksBlank(bitmap));
    }

    [Fact]
    public void ANearBlackFrame_StillLooksBlank()
    {
        using var bitmap = Filled(640, 360, Color.FromArgb(8, 8, 8));
        Assert.True(ScreenshotFrame.LooksBlank(bitmap));
    }

    [Fact]
    public void ABlackFrameWithOneLitPanel_DoesNotLookBlank()
    {
        using var bitmap = Filled(640, 360, Color.Black);
        using (var g = Graphics.FromImage(bitmap))
            g.FillRectangle(Brushes.LimeGreen, new Rectangle(300, 150, 120, 80));

        Assert.False(ScreenshotFrame.LooksBlank(bitmap));
    }

    [Fact]
    public void ACockpitColouredFrame_DoesNotLookBlank()
    {
        using var bitmap = Filled(1920, 1080, Color.FromArgb(40, 44, 52));
        Assert.False(ScreenshotFrame.LooksBlank(bitmap));
    }

    [Fact]
    public void ATinyFrame_IsSampledPixelByPixel_WithoutThrowing()
    {
        using var black = Filled(1, 1, Color.Black);
        using var white = Filled(1, 1, Color.White);

        Assert.True(ScreenshotFrame.LooksBlank(black));
        Assert.False(ScreenshotFrame.LooksBlank(white));
    }

    // A night cockpit: nothing brighter than R+G+B 25 anywhere, so every sample passes the dark
    // test on its own, yet the dim lighting varies across the frame, which a failed capture never
    // does. Before the uniformity rule this real picture was thrown away for the screen copy.
    [Fact]
    public void ADarkButVariedFrame_DoesNotLookBlank()
    {
        using var bitmap = DarkRamp(640, 360, topBlue: 25);
        Assert.False(ScreenshotFrame.LooksBlank(bitmap));
    }

    // The uniformity allowance is a spread of R+G+B across the samples, inclusive: a failed frame
    // may carry a little noise, one step more is a (very dark) picture.
    [Theory]
    [InlineData(2, 2, 2, true)]    // R+G+B 0 beside 6: within the spread, still a failed frame
    [InlineData(2, 2, 3, false)]   // 0 beside 7: one past it, a picture
    public void ADarkFrameInTwoHalves_LooksBlankOnlyWithinTheUniformSpread(int r, int g, int b, bool blank)
    {
        using var bitmap = Filled(640, 360, Color.Black);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(Color.FromArgb(r, g, b)))
            graphics.FillRectangle(brush, new Rectangle(320, 0, 320, 360));

        Assert.Equal(blank, ScreenshotFrame.LooksBlank(bitmap));
    }

    // One lit sample is enough: the rest of the frame being flat black does not outvote it. The
    // bitmap is exactly grid-sized, so every pixel is a sample and the lit one cannot be skipped.
    [Fact]
    public void OneLitSampleOnAFlatBlackFrame_DoesNotLookBlank()
    {
        using var bitmap = Filled(24, 14, Color.Black);
        bitmap.SetPixel(11, 6, Color.White);
        Assert.False(ScreenshotFrame.LooksBlank(bitmap));
    }

    /// <summary>A left-to-right ramp in the blue channel only, 0 at the left edge up to <paramref name="topBlue"/> at the right, so each pixel's R+G+B is its blue value.</summary>
    private static Bitmap DarkRamp(int width, int height, int topBlue)
    {
        var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        using var brush = new SolidBrush(Color.Black);
        for (int x = 0; x < width; x++)
        {
            brush.Color = Color.FromArgb(0, 0, x * topBlue / (width - 1));
            graphics.FillRectangle(brush, x, 0, 1, height);
        }
        return bitmap;
    }
}
