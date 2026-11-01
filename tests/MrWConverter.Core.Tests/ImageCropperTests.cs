using ImageMagick;
using MrWConverter.Core.Conversion;
using MrWConverter.Core.Formats;
using MrWConverter.Core.Tools;

namespace MrWConverter.Core.Tests;

public class ImageCropperTests
{
    [Fact]
    public async Task Crop_KeepsExactlyTheChosenPixels()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("a.png");
        WriteTwoHalves(input);

        await new ImageCropper(new ImageRegion(50, 10, 40, 20), "png").RunAsync(input, folder.File("out.png"), "png", null, CancellationToken.None);

        using var result = new MagickImage(folder.File("out.png"));
        using var pixels = result.GetPixels();
        Assert.Equal(40u, result.Width);
        Assert.Equal(20u, result.Height);
        Assert.Equal(MagickColors.Blue, pixels.GetPixel(0, 0).ToColor());
        Assert.Equal(MagickColors.Blue, pixels.GetPixel(39, 19).ToColor());
    }

    [Fact]
    public async Task Crop_FollowsTheOrientationThePreviewShowed()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("a.jpg");
        using (var image = new MagickImage(MagickColors.Gray, 200, 100))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Orientation, (ushort)6);
            image.SetProfile(exif);
            image.Orientation = Orientation.RightTop;
            image.Write(input, MagickFormat.Jpeg);
        }

        var preview = ImagePreview.Read(input, "jpg");
        Assert.Equal(100, preview.ImageWidth);
        Assert.Equal(200, preview.ImageHeight);

        // This region only fits once the picture is upright.
        await new ImageCropper(new ImageRegion(0, 150, 100, 50), "jpg").RunAsync(input, folder.File("out.jpg"), "jpg", null, CancellationToken.None);

        using var result = new MagickImage(folder.File("out.jpg"));
        Assert.Equal(100u, result.Width);
        Assert.Equal(50u, result.Height);
    }

    [Fact]
    public async Task Crop_RemovesMetadataThatCouldShowTheWholePicture()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("a.jpg");
        using (var image = new MagickImage(MagickColors.Gray, 100, 100))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Software, "camera");
            image.SetProfile(exif);
            image.Write(input, MagickFormat.Jpeg);
        }

        await new ImageCropper(new ImageRegion(0, 0, 50, 50), "jpg").RunAsync(input, folder.File("out.jpg"), "jpg", null, CancellationToken.None);

        using var result = new MagickImage(folder.File("out.jpg"));
        Assert.Null(result.GetExifProfile());
    }

    [Theory]
    [InlineData(-1, 0, 10, 10)]
    [InlineData(0, 0, 101, 10)]
    [InlineData(95, 0, 10, 10)]
    [InlineData(0, 0, 0, 10)]
    public async Task Crop_OutsideTheImage_IsRefused(int x, int y, int width, int height)
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("a.png");
        WriteTwoHalves(input);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ImageCropper(new ImageRegion(x, y, width, height), "png").RunAsync(input, folder.File("out.png"), "png", null, CancellationToken.None));
    }

    [Fact]
    public async Task Runner_KeepsTheFormatAndTheOriginal()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("a b [1] %d.png");
        WriteTwoHalves(input);
        var before = File.ReadAllBytes(input);
        var runner = new ConversionRunner(FormatCatalog.Default, []);

        var outputs = await runner.RunToolAsync([input], new ImageCropper(new ImageRegion(0, 0, 10, 10), "png"), folder.Path);

        Assert.Equal([folder.File("a b [1] %d-crop.png")], outputs);
        Assert.Equal(before, File.ReadAllBytes(input));
    }

    [Fact]
    public void Preview_IsSmallButReportsTheRealSize()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("a.png");
        using (var image = new MagickImage(MagickColors.Red, 4000, 2000))
        {
            image.Write(input, MagickFormat.Png);
        }

        var preview = ImagePreview.Read(input, "png");

        using var small = new MagickImage(preview.Png);
        Assert.Equal(4000, preview.ImageWidth);
        Assert.Equal(2000, preview.ImageHeight);
        Assert.Equal(1600u, small.Width);
    }

    private static void WriteTwoHalves(string path)
    {
        using var image = new MagickImage(MagickColors.Blue, 100, 50);
        using var red = new MagickImage(MagickColors.Red, 50, 50);
        image.Composite(red, 0, 0, CompositeOperator.Over);

        // A stream, because Magick would read "%d" in a path as its own syntax.
        using var stream = File.Create(path);
        image.Write(stream, MagickFormat.Png);
    }
}

public class ImageRegionTests
{
    private const int ImageWidth = 1000;
    private const int ImageHeight = 500;

    private static readonly ImageRegion Middle = new(400, 200, 200, 100);

    [Fact]
    public void Moved_StaysInsideTheImage()
    {
        Assert.Equal(new ImageRegion(0, 0, 200, 100), Middle.Moved(-5000, -5000, ImageWidth, ImageHeight));
        Assert.Equal(new ImageRegion(800, 400, 200, 100), Middle.Moved(5000, 5000, ImageWidth, ImageHeight));
    }

    [Fact]
    public void Resized_MovesOnlyTheGivenEdges()
    {
        var region = Middle.Resized(RegionEdges.Left | RegionEdges.Bottom, -100, 50, null, ImageWidth, ImageHeight);

        Assert.Equal(new ImageRegion(300, 200, 300, 150), region);
    }

    [Fact]
    public void Resized_StopsAtTheImageAndAtTheSmallestSize()
    {
        var widest = Middle.Resized(RegionEdges.Right, 5000, 0, null, ImageWidth, ImageHeight);
        var narrowest = Middle.Resized(RegionEdges.Right, -5000, 0, null, ImageWidth, ImageHeight);

        Assert.Equal(ImageWidth, widest.X + widest.Width);
        Assert.Equal(new ImageRegion(400, 200, 16, 100), narrowest);
    }

    [Theory]
    [InlineData(RegionEdges.Right, 100, 0)]
    [InlineData(RegionEdges.Bottom, 0, 60)]
    [InlineData(RegionEdges.Left | RegionEdges.Top, -80, -10)]
    [InlineData(RegionEdges.Right | RegionEdges.Bottom, 5000, 5000)]
    [InlineData(RegionEdges.Left | RegionEdges.Top, 5000, 5000)]
    public void Resized_WithARatio_KeepsTheShapeAndStaysInside(RegionEdges edges, double dx, double dy)
    {
        var region = Middle.Resized(edges, dx, dy, 2, ImageWidth, ImageHeight);

        Assert.InRange(region.Width / (double)region.Height, 1.9, 2.1);
        Assert.True(region.X >= 0 && region.Y >= 0);
        Assert.True(region.X + region.Width <= ImageWidth);
        Assert.True(region.Y + region.Height <= ImageHeight);
    }

    [Fact]
    public void Resized_WithARatio_KeepsTheOppositeCornerStill()
    {
        var region = Middle.Resized(RegionEdges.Left | RegionEdges.Top, -100, -50, 2, ImageWidth, ImageHeight);

        Assert.Equal(Middle.X + Middle.Width, region.X + region.Width);
        Assert.Equal(Middle.Y + Middle.Height, region.Y + region.Height);
    }

    [Theory]
    [InlineData(1, 250, 0, 500, 500)]
    [InlineData(4, 0, 125, 1000, 250)]
    public void Fitted_IsTheLargestCenteredRegion(double ratio, int x, int y, int width, int height)
    {
        Assert.Equal(new ImageRegion(x, y, width, height), ImageRegion.Fitted(ratio, ImageWidth, ImageHeight));
    }

    [Fact]
    public void Between_AcceptsAnyDragDirectionAndStopsAtTheImage()
    {
        Assert.Equal(new ImageRegion(10, 20, 30, 40), ImageRegion.Between(40, 60, 10, 20, ImageWidth, ImageHeight));
        Assert.Equal(new ImageRegion(0, 0, 40, 60), ImageRegion.Between(-100, -100, 40, 60, ImageWidth, ImageHeight));
        Assert.Equal(new ImageRegion(900, 400, 100, 100), ImageRegion.Between(900, 400, 5000, 5000, ImageWidth, ImageHeight));
    }

    [Theory]
    [InlineData(0, 0, 100, 50, true)]
    [InlineData(1, 0, 100, 50, false)]
    [InlineData(0, 1, 100, 50, false)]
    [InlineData(-1, 0, 10, 10, false)]
    [InlineData(0, 0, 0, 10, false)]
    public void FitsInside_ChecksEveryEdge(int x, int y, int width, int height, bool expected)
    {
        Assert.Equal(expected, new ImageRegion(x, y, width, height).FitsInside(100, 50));
    }

    [Fact]
    public void Resized_OnATinyImage_DoesNotAskForMoreThanExists()
    {
        var tiny = ImageRegion.Whole(5, 5);

        var region = tiny.Resized(RegionEdges.Right, -100, 0, null, 5, 5);

        Assert.Equal(5, region.Width);
    }
}
