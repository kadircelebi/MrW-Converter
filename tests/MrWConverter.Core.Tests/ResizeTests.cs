using ImageMagick;
using MrWConverter.Core.Conversion;
using MrWConverter.Core.Formats;
using MrWConverter.Core.Tools;

namespace MrWConverter.Core.Tests;

public class ResizeTests
{
    [Theory]
    [InlineData(50, 2000, 1000, 1000, 500)]
    [InlineData(200, 300, 200, 600, 400)]
    [InlineData(33, 101, 7, 33, 2)]
    [InlineData(1, 50, 30, 1, 1)]
    public void Plan_ByPercent_ScalesBothSides(double percent, int width, int height, int expectedWidth, int expectedHeight)
    {
        Assert.Equal(new ResizePlan(expectedWidth, expectedHeight, expectedWidth, expectedHeight), ResizeOptions.ByPercent(percent).Plan(width, height));
    }

    [Theory]
    [InlineData(1920, 4000, 3000, 1920, 1440)]
    [InlineData(1920, 3000, 4000, 1440, 1920)]
    [InlineData(1920, 800, 600, 1920, 1440)]
    public void Plan_ByLongestSide_KeepsTheShape(int side, int width, int height, int expectedWidth, int expectedHeight)
    {
        Assert.Equal(new ResizePlan(expectedWidth, expectedHeight, expectedWidth, expectedHeight), ResizeOptions.ByLongestSide(side).Plan(width, height));
    }

    [Fact]
    public void Plan_Fit_StaysInsideTheBox()
    {
        Assert.Equal(new ResizePlan(1080, 608, 1080, 608), ResizeOptions.ToBox(1080, 1080, fill: false).Plan(1920, 1080));
        Assert.Equal(new ResizePlan(608, 1080, 608, 1080), ResizeOptions.ToBox(1080, 1080, fill: false).Plan(1080, 1920));
        Assert.Equal(new ResizePlan(1080, 1080, 1080, 1080), ResizeOptions.ToBox(1080, 1080, fill: false).Plan(500, 500));
    }

    [Fact]
    public void Plan_Fill_CoversTheBoxAndCutsTheRest()
    {
        var plan = ResizeOptions.ToBox(1080, 1080, fill: true).Plan(1920, 1080);

        Assert.Equal(new ResizePlan(1920, 1080, 1080, 1080), plan);
        Assert.True(plan.Crops);
    }

    [Fact]
    public void Plan_Fill_NeverLeavesTheBoxAPixelShort()
    {
        // 333 x 333 * (1000 / 333) rounds to 1000 on one side and could round under on the other.
        for (var width = 300; width < 340; width++)
        {
            var plan = ResizeOptions.ToBox(1000, 777, fill: true).Plan(width, 333);
            Assert.True(plan.ScaledWidth >= 1000 && plan.ScaledHeight >= 777, $"{width}: {plan}");
            Assert.Equal((1000, 777), (plan.Width, plan.Height));
        }
    }

    [Theory]
    [InlineData(ResizeMode.Percent, 0, 0, 0)]
    [InlineData(ResizeMode.Percent, 1001, 0, 0)]
    [InlineData(ResizeMode.LongestSide, 0, 0, 0)]
    [InlineData(ResizeMode.Fit, 0, 0, 100)]
    [InlineData(ResizeMode.Fill, 0, 100, 70_000)]
    public void Options_OutOfRange_AreNotValid(ResizeMode mode, double value, int width, int height)
    {
        var options = new ResizeOptions(mode, value, width, height);

        Assert.False(options.IsValid);
        Assert.Throws<InvalidOperationException>(() => options.Plan(100, 100));
    }

    [Theory]
    [InlineData("jpg")]
    [InlineData("png")]
    [InlineData("webp")]
    [InlineData("tiff")]
    [InlineData("bmp")]
    [InlineData("avif")]
    public async Task Image_GetsThePlannedSizeInTheSameFormat(string format)
    {
        using var folder = new TemporaryFolder();
        var input = MakeImage(folder, $"a.{format}", format, 400, 300);
        var output = folder.File($"out.{format}");

        await new ImageResizer(ResizeOptions.ByPercent(50), format).RunAsync(input, output, format, null, default);

        using var result = new MagickImage(output, ImageFile.Formats[format]);
        Assert.Equal((200u, 150u), (result.Width, result.Height));
    }

    [Fact]
    public async Task Image_CanGetLarger()
    {
        using var folder = new TemporaryFolder();
        var input = MakeImage(folder, "a.png", "png", 300, 200);
        var output = folder.File("out.png");

        await new ImageResizer(ResizeOptions.ByLongestSide(1200), "png").RunAsync(input, output, "png", null, default);

        using var result = new MagickImage(output);
        Assert.Equal((1200u, 800u), (result.Width, result.Height));
    }

    [Fact]
    public async Task Image_Fill_CutsEvenlyFromBothEdges()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("a.png");

        // Red, then green, then blue thirds: a square cut from the middle is all green.
        using (var image = new MagickImage(MagickColors.Green, 300, 100))
        {
            using var red = new MagickImage(MagickColors.Red, 100, 100);
            using var blue = new MagickImage(MagickColors.Blue, 100, 100);
            image.Composite(red, 0, 0, CompositeOperator.Over);
            image.Composite(blue, 200, 0, CompositeOperator.Over);
            image.Write(input);
        }

        var output = folder.File("out.png");
        await new ImageResizer(ResizeOptions.ToBox(50, 50, fill: true), "png").RunAsync(input, output, "png", null, default);

        using var result = new MagickImage(output);
        using var pixels = result.GetPixels();
        Assert.Equal((50u, 50u), (result.Width, result.Height));
        Assert.Equal(MagickColors.Green, pixels.GetPixel(5, 25).ToColor());
        Assert.Equal(MagickColors.Green, pixels.GetPixel(44, 25).ToColor());
    }

    [Fact]
    public async Task Image_KeepsTransparency()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("a.png");
        using (var image = new MagickImage(MagickColors.Transparent, 200, 200))
        {
            using var red = new MagickImage(MagickColors.Red, 100, 100);
            image.Composite(red, 50, 50, CompositeOperator.Over);
            image.Write(input);
        }

        var output = folder.File("out.png");
        await new ImageResizer(ResizeOptions.ByPercent(50), "png").RunAsync(input, output, "png", null, default);

        using var result = new MagickImage(output);
        using var pixels = result.GetPixels();
        Assert.True(result.HasAlpha);
        Assert.Equal(0, pixels.GetPixel(2, 2).ToColor()!.A);
        Assert.Equal(MagickColors.Red, pixels.GetPixel(50, 50).ToColor());
    }

    [Fact]
    public async Task Image_TurnedPhoto_IsSizedAsItShows()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("a.jpg");
        using (var image = new MagickImage(MagickColors.Gray, 400, 200))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Orientation, (ushort)6);
            image.SetProfile(exif);
            image.Orientation = Orientation.RightTop;
            image.Write(input, MagickFormat.Jpeg);
        }

        Assert.Equal((200, 400), ImageResizer.ReadSize(input, "jpg"));

        var output = folder.File("out.jpg");
        await new ImageResizer(ResizeOptions.ByLongestSide(100), "jpg").RunAsync(input, output, "jpg", null, default);

        using var result = new MagickImage(output);
        Assert.Equal((50u, 100u), (result.Width, result.Height));
        Assert.Null(result.GetExifProfile()?.GetValue(ExifTag.Orientation));
    }

    [Fact]
    public async Task Image_RemovesLocationAndCameraData()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("a.jpg");
        using (var image = new MagickImage(MagickColors.Gray, 300, 200))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Make, "SecretCamera");
            image.SetProfile(exif);
            image.Write(input, MagickFormat.Jpeg);
        }

        var output = folder.File("out.jpg");
        await new ImageResizer(ResizeOptions.ByPercent(50), "jpg").RunAsync(input, output, "jpg", null, default);

        Assert.DoesNotContain("SecretCamera", System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(output)));
    }

    [Fact]
    public async Task Image_AlreadyThatSize_IsCopiedAsItIs()
    {
        using var folder = new TemporaryFolder();
        var input = MakeImage(folder, "a.jpg", "jpg", 640, 480);
        var output = folder.File("out.jpg");

        await new ImageResizer(ResizeOptions.ByLongestSide(640), "jpg").RunAsync(input, output, "jpg", null, default);

        Assert.Equal(File.ReadAllBytes(input), File.ReadAllBytes(output));
    }

    [Fact]
    public async Task Image_TooLarge_FailsBeforeMakingAFile()
    {
        using var folder = new TemporaryFolder();
        var input = MakeImage(folder, "a.png", "png", 1000, 1000);
        var output = folder.File("out.png");

        await Assert.ThrowsAsync<ResizeTooLargeException>(() =>
            new ImageResizer(ResizeOptions.ByPercent(1000), "png", maxPixels: 50_000_000).RunAsync(input, output, "png", null, default));
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task Image_OverTheWebPLimit_Fails()
    {
        using var folder = new TemporaryFolder();
        var input = MakeImage(folder, "a.webp", "webp", 2000, 10);
        var output = folder.File("out.webp");

        await Assert.ThrowsAsync<ResizeTooLargeException>(() =>
            new ImageResizer(ResizeOptions.ByLongestSide(20_000), "webp").RunAsync(input, output, "webp", null, default));
    }

    [Fact]
    public void Tool_NamesItsResult()
    {
        Assert.Equal("photo-resized.webp", new ImageResizer(ResizeOptions.ByPercent(50), "webp").OutputName("photo"));
    }

    [Fact]
    public void Catalog_OffersItForPicturesOnly()
    {
        var catalog = FormatCatalog.Default;

        Assert.Contains(ResizeOptions.ToolId, catalog.CommonTools(["a.jpg", "b.png"]));
        Assert.DoesNotContain(ResizeOptions.ToolId, catalog.CommonTools(["a.mp4"]));
        Assert.DoesNotContain(ResizeOptions.ToolId, catalog.CommonTools(["a.png", "b.mp4"]));
        Assert.DoesNotContain(ResizeOptions.ToolId, catalog.CommonTools(["a.gif"]));
    }

    private static string MakeImage(TemporaryFolder folder, string name, string format, int width, int height)
    {
        var path = folder.File(name);
        using var image = new MagickImage("plasma:", (uint)width, (uint)height);
        image.Write(path, ImageFile.Formats[format]);
        return path;
    }
}
