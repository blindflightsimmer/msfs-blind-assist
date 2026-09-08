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
}
