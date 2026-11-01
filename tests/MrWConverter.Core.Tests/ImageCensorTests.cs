using System.Text;
using ImageMagick;
using MrWConverter.Core.Conversion;
using MrWConverter.Core.Formats;
using MrWConverter.Core.Tools;

namespace MrWConverter.Core.Tests;

public class ImageCensorTests
{
    private const string Secret = "SECRETVALUE";

    [Fact]
    public async Task Png_PaintsExactlyTheRegionsAndNothingElse()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("a.png");
        Write(new MagickImage(MagickColors.Red, 100, 100), input, MagickFormat.Png);
        ImageRegion[] regions = [new(10, 10, 20, 20), new(60, 70, 30, 10)];

        await new ImageCensor(regions, "png").RunAsync(input, folder.File("out.png"), "png", null, CancellationToken.None);

        using var result = new MagickImage(folder.File("out.png"));
        using var pixels = result.GetPixels();
        Assert.Equal(100u, result.Width);
        Assert.Equal(MagickColors.Black, pixels.GetPixel(10, 10).ToColor());
        Assert.Equal(MagickColors.Black, pixels.GetPixel(29, 29).ToColor());
        Assert.Equal(MagickColors.Black, pixels.GetPixel(89, 79).ToColor());
        Assert.Equal(MagickColors.Red, pixels.GetPixel(9, 10).ToColor());
        Assert.Equal(MagickColors.Red, pixels.GetPixel(30, 29).ToColor());
        Assert.Equal(MagickColors.Red, pixels.GetPixel(90, 79).ToColor());
    }

    [Theory]
    [InlineData("jpg", MagickFormat.Jpeg)]
    [InlineData("png", MagickFormat.Png)]
    [InlineData("webp", MagickFormat.WebP)]
    [InlineData("tiff", MagickFormat.Tiff)]
    [InlineData("bmp", MagickFormat.Bmp)]
    [InlineData("avif", MagickFormat.Avif)]
    public async Task EveryFormat_HidesTheRegionAndKeepsItsFormat(string format, MagickFormat magickFormat)
    {
        using var folder = new TemporaryFolder();
        var input = folder.File($"a.{format}");
        var output = folder.File($"out.{format}");
        Write(new MagickImage(MagickColors.White, 200, 200), input, magickFormat);

        await new ImageCensor([new ImageRegion(40, 40, 100, 100)], format).RunAsync(input, output, format, null, CancellationToken.None);

        using var stream = File.OpenRead(output);
        using var result = new MagickImage(stream, new MagickReadSettings { Format = magickFormat });
        using var pixels = result.GetPixels();

        // A margin, because lossy formats may blur the box's very edge by a pixel or two.
        for (var y = 44; y < 136; y += 4)
        {
            for (var x = 44; x < 136; x += 4)
            {
                var color = pixels.GetPixel(x, y).ToColor()!;
                Assert.True(color.R < 12 && color.G < 12 && color.B < 12, $"({x},{y}) is {color}");
            }
        }

        Assert.True(pixels.GetPixel(10, 10).ToColor()!.R > 240, "Outside the box the image should stay.");
    }

    [Fact]
    public async Task Noise_IsTheSameWhateverTheImageShows()
    {
        using var folder = new TemporaryFolder();
        Write(new MagickImage(MagickColors.Red, 300, 300), folder.File("a.png"), MagickFormat.Png);
        Write(new MagickImage(MagickColors.Blue, 300, 300), folder.File("b.png"), MagickFormat.Png);
        ImageRegion[] regions = [new(20, 30, 200, 100)];

        foreach (var name in new[] { "a", "b" })
        {
            var censor = new ImageCensor(regions, "png", CensorStyle.Noise) { NoiseSource = new Random(42) };
            await censor.RunAsync(folder.File($"{name}.png"), folder.File($"{name}-out.png"), "png", null, CancellationToken.None);
        }

        using var first = new MagickImage(folder.File("a-out.png"));
        using var second = new MagickImage(folder.File("b-out.png"));
        using var firstPixels = first.GetPixels();
        using var secondPixels = second.GetPixels();

        // Identical grain from different images proves the original has no part in it.
        Assert.Equal(firstPixels.GetArea(20, 30, 200, 100), secondPixels.GetArea(20, 30, 200, 100));
        Assert.Equal(MagickColors.Red, firstPixels.GetPixel(19, 30).ToColor());
        Assert.Equal(MagickColors.Blue, secondPixels.GetPixel(19, 30).ToColor());
    }

    [Fact]
    public async Task Noise_IsVisibleGrainOfOpaqueGrays()
    {
        using var folder = new TemporaryFolder();
        Write(new MagickImage(MagickColors.Transparent, 200, 200), folder.File("a.png"), MagickFormat.Png);

        await new ImageCensor([new ImageRegion(0, 0, 200, 200)], "png", CensorStyle.Noise)
            .RunAsync(folder.File("a.png"), folder.File("out.png"), "png", null, CancellationToken.None);

        using var result = new MagickImage(folder.File("out.png"));
        using var pixels = result.GetPixels();
        var colors = Enumerable.Range(0, 100).Select(step => pixels.GetPixel(step * 2, step * 2).ToColor()!).ToArray();

        Assert.All(colors, color => Assert.Equal(255, color.A));
        Assert.All(colors, color => Assert.True(color.R == color.G && color.G == color.B));
        Assert.True(colors.Select(color => color.R).Distinct().Count() > 20, "Noise should not be one flat color.");
    }

    [Theory]
    [InlineData("jpg", MagickFormat.Jpeg)]
    [InlineData("png", MagickFormat.Png)]
    [InlineData("webp", MagickFormat.WebP)]
    [InlineData("tiff", MagickFormat.Tiff)]
    [InlineData("bmp", MagickFormat.Bmp)]
    [InlineData("avif", MagickFormat.Avif)]
    public async Task EveryFormat_TakesEveryStyle(string format, MagickFormat magickFormat)
    {
        using var folder = new TemporaryFolder();
        var input = folder.File($"a.{format}");
        Write(new MagickImage(MagickColors.White, 200, 200), input, magickFormat);

        foreach (var style in Enum.GetValues<CensorStyle>())
        {
            var output = folder.File($"{style}.{format}");
            await new ImageCensor([new ImageRegion(40, 40, 100, 100)], format, style).RunAsync(input, output, format, null, CancellationToken.None);

            using var stream = File.OpenRead(output);
            using var result = new MagickImage(stream, new MagickReadSettings { Format = magickFormat });
            Assert.Equal(200u, result.Width);
        }
    }

    [Fact]
    public async Task Mosaic_IsSmallTilesThatHideDetailButKeepTheAverageColor()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("a.png");
        var image = new MagickImage(MagickColors.White, 800, 800);
        using (var dot = new MagickImage(MagickColors.Black, 2, 2))
        {
            image.Composite(dot, 200, 200, CompositeOperator.Copy);
        }

        Write(image, input, MagickFormat.Png);

        await new ImageCensor([new ImageRegion(0, 0, 800, 800)], "png", CensorStyle.Mosaic)
            .RunAsync(input, folder.File("out.png"), "png", null, CancellationToken.None);

        using var result = new MagickImage(folder.File("out.png"));
        using var pixels = result.GetPixels();
        var tile = pixels.GetPixel(200, 200).ToColor()!;

        // 2x2 of black spread over an 8x8 tile: no longer black, and the whole tile matches.
        Assert.True(tile.R > 200, $"The dot should be averaged away, but is {tile}.");
        Assert.Equal(tile, pixels.GetPixel(207, 207).ToColor());
        Assert.Equal(MagickColors.White, pixels.GetPixel(208, 208).ToColor());
        Assert.Equal(800u, result.Width);
    }

    [Theory]
    [InlineData(800u, 5.0, 40)]
    [InlineData(800u, 2.5, 20)]
    [InlineData(400u, 0.5, 4)]
    public async Task Mosaic_TilesFollowTheChosenSize(uint side, double percent, int tile)
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("a.png");
        var image = new MagickImage(MagickColors.White, side, side);
        using (var dot = new MagickImage(MagickColors.Black, (uint)tile / 2, (uint)tile / 2))
        {
            image.Composite(dot, 200, 200, CompositeOperator.Copy);
        }

        Write(image, input, MagickFormat.Png);

        var censor = new ImageCensor([new ImageRegion(0, 0, (int)side, (int)side)], "png", CensorStyle.Mosaic) { MosaicTilePercent = percent };
        await censor.RunAsync(input, folder.File("out.png"), "png", null, CancellationToken.None);

        using var result = new MagickImage(folder.File("out.png"));
        using var pixels = result.GetPixels();
        var first = pixels.GetPixel(200, 200).ToColor()!;
        var last = pixels.GetPixel(200 + tile - 1, 200 + tile - 1).ToColor()!;

        // A quarter of the tile is black, so it stands out from the white tiles on either side.
        Assert.True(first.R is > 150 and < 230, $"The tile should be averaged, but is {first}.");
        Assert.Equal(first, last);
        Assert.Equal(MagickColors.White, pixels.GetPixel(199, 199).ToColor());
        Assert.Equal(MagickColors.White, pixels.GetPixel(200 + tile, 200 + tile).ToColor());
    }

    [Theory]
    [InlineData(CensorStyle.Black)]
    [InlineData(CensorStyle.Noise)]
    [InlineData(CensorStyle.Mosaic)]
    public void Preview_IsTheSameSizeAsTheImagePreview(CensorStyle style)
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("a.png");
        Write(new MagickImage(MagickColors.Red, 4000, 2000), input, MagickFormat.Png);
        var preview = ImagePreview.Read(input, "png");

        using var rendered = new MagickImage(ImageCensor.RenderPreview(preview, style), MagickFormat.Png);
        using var shown = new MagickImage(preview.Png, MagickFormat.Png);

        Assert.Equal(shown.Width, rendered.Width);
        Assert.Equal(shown.Height, rendered.Height);
    }

    [Fact]
    public async Task TransparentPixelsUnderTheRegion_BecomeOpaqueBlack()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("a.png");
        Write(new MagickImage(MagickColors.Transparent, 50, 50), input, MagickFormat.Png);

        await new ImageCensor([new ImageRegion(0, 0, 10, 10)], "png").RunAsync(input, folder.File("out.png"), "png", null, CancellationToken.None);

        using var result = new MagickImage(folder.File("out.png"));
        using var pixels = result.GetPixels();
        Assert.Equal(MagickColors.Black, pixels.GetPixel(5, 5).ToColor());
        Assert.Equal(0, pixels.GetPixel(20, 20).ToColor()!.A);
    }

    [Theory]
    [InlineData("jpg", MagickFormat.Jpeg)]
    [InlineData("png", MagickFormat.Png)]
    [InlineData("webp", MagickFormat.WebP)]
    [InlineData("tiff", MagickFormat.Tiff)]
    public async Task Metadata_DoesNotReachTheOutput(string format, MagickFormat magickFormat)
    {
        using var folder = new TemporaryFolder();
        var input = folder.File($"a.{format}");
        var output = folder.File($"out.{format}");

        var image = new MagickImage(MagickColors.White, 100, 100);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Software, Secret);
        exif.SetValue(ExifTag.ImageDescription, Secret);
        image.SetProfile(exif);
        image.SetProfile(new XmpProfile(Encoding.UTF8.GetBytes($"<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><secret>{Secret}</secret></x:xmpmeta>")));
        image.Comment = Secret;
        Write(image, input, magickFormat);
        Assert.Contains(Secret, Encoding.Latin1.GetString(File.ReadAllBytes(input)));

        await new ImageCensor([new ImageRegion(0, 0, 10, 10)], format).RunAsync(input, output, format, null, CancellationToken.None);

        Assert.DoesNotContain(Secret, Encoding.Latin1.GetString(File.ReadAllBytes(output)));
    }

    [Fact]
    public async Task ColorProfile_Stays()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("a.jpg");
        var image = new MagickImage(MagickColors.White, 50, 50);
        image.SetProfile(ColorProfiles.SRGB);
        Write(image, input, MagickFormat.Jpeg);

        await new ImageCensor([new ImageRegion(0, 0, 10, 10)], "jpg").RunAsync(input, folder.File("out.jpg"), "jpg", null, CancellationToken.None);

        using var result = new MagickImage(folder.File("out.jpg"));
        Assert.NotNull(result.GetColorProfile());
    }

    [Fact]
    public async Task Png_HasNoTimestamps()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("a.png");
        Write(new MagickImage(MagickColors.White, 50, 50), input, MagickFormat.Png);

        await new ImageCensor([new ImageRegion(0, 0, 10, 10)], "png").RunAsync(input, folder.File("out.png"), "png", null, CancellationToken.None);

        Assert.DoesNotContain("date:", Encoding.Latin1.GetString(File.ReadAllBytes(folder.File("out.png"))));
    }

    [Fact]
    public async Task Orientation_MatchesWhatThePreviewShowed()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("a.jpg");
        var image = new MagickImage(MagickColors.White, 200, 100);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Orientation, (ushort)6);
        image.SetProfile(exif);
        image.Orientation = Orientation.RightTop;
        Write(image, input, MagickFormat.Jpeg);

        // This region only fits once the picture is upright.
        await new ImageCensor([new ImageRegion(0, 150, 100, 50)], "jpg").RunAsync(input, folder.File("out.jpg"), "jpg", null, CancellationToken.None);

        using var result = new MagickImage(folder.File("out.jpg"));
        using var pixels = result.GetPixels();
        Assert.Equal(100u, result.Width);
        Assert.Equal(200u, result.Height);
        Assert.True(pixels.GetPixel(50, 175).ToColor()!.R < 12);
        Assert.True(pixels.GetPixel(50, 50).ToColor()!.R > 240);
    }

    [Fact]
    public async Task ARegionOutsideTheImage_LeavesNoFile()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("a.png");
        Write(new MagickImage(MagickColors.White, 50, 50), input, MagickFormat.Png);
        ImageRegion[] regions = [new(0, 0, 10, 10), new(45, 45, 10, 10)];

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ImageCensor(regions, "png").RunAsync(input, folder.File("out.png"), "png", null, CancellationToken.None));

        Assert.False(File.Exists(folder.File("out.png")));
    }

    [Fact]
    public async Task NoRegions_IsRefusedRatherThanCopyingTheImage()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("a.png");
        Write(new MagickImage(MagickColors.White, 50, 50), input, MagickFormat.Png);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ImageCensor([], "png").RunAsync(input, folder.File("out.png"), "png", null, CancellationToken.None));

        Assert.False(File.Exists(folder.File("out.png")));
    }

    [Fact]
    public async Task Runner_KeepsTheOriginalAndNamesTheResult()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("a b [1] %d.png");
        Write(new MagickImage(MagickColors.White, 50, 50), input, MagickFormat.Png);
        var before = File.ReadAllBytes(input);
        var runner = new ConversionRunner(FormatCatalog.Default, []);

        var outputs = await runner.RunToolAsync([input], new ImageCensor([new ImageRegion(0, 0, 10, 10)], "png"), folder.Path);

        Assert.Equal([folder.File("a b [1] %d-censored.png")], outputs);
        Assert.Equal(before, File.ReadAllBytes(input));
    }

    private static void Write(MagickImage image, string path, MagickFormat format)
    {
        using (image)
        {
            // A stream, because Magick would read "%d" in a path as its own syntax.
            using var stream = File.Create(path);
            image.Write(stream, format);
        }
    }
}
