using ImageMagick;
using MrWConverter.Core.Conversion;

namespace MrWConverter.Core.Tests;

public class ImageConverterTests
{
    private static readonly string[] AllFormats = ["jpg", "png", "webp", "tiff", "bmp", "avif"];

    private static readonly IReadOnlyDictionary<string, MagickFormat> MagickFormats = new Dictionary<string, MagickFormat>
    {
        ["jpg"] = MagickFormat.Jpeg,
        ["png"] = MagickFormat.Png,
        ["webp"] = MagickFormat.WebP,
        ["tiff"] = MagickFormat.Tiff,
        ["bmp"] = MagickFormat.Bmp,
        ["avif"] = MagickFormat.Avif,
    };

    public static TheoryData<string, string> AllPairs()
    {
        var pairs = new TheoryData<string, string>();
        foreach (var source in AllFormats)
        {
            foreach (var target in AllFormats.Where(target => target != source))
            {
                pairs.Add(source, target);
            }
        }

        return pairs;
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public async Task Convert_ProducesARealFileOfTheTargetFormat(string source, string target)
    {
        using var folder = new TemporaryFolder();
        var input = folder.File($"input.{source}");
        var output = folder.File($"output.{target}");
        WriteSample(input, MagickFormats[source], width: 20, height: 10);

        await new ImageConverter().ConvertAsync(input, output, source, target, null, CancellationToken.None);

        using var result = new MagickImage(output);
        Assert.Equal(MagickFormats[target], result.Format);
        Assert.Equal(20u, result.Width);
        Assert.Equal(10u, result.Height);
    }

    [Theory]
    [InlineData("photo[0].png")]
    [InlineData("şğüİı %d çözüm.png")]
    public async Task Convert_HandlesFileNamesWithSpecialCharacters(string name)
    {
        using var folder = new TemporaryFolder();
        var input = folder.File(name);
        var output = folder.File("output.jpg");
        WriteSample(input, MagickFormat.Png, width: 20, height: 10);

        await new ImageConverter().ConvertAsync(input, output, "png", "jpg", null, CancellationToken.None);

        Assert.True(new FileInfo(output).Length > 0);
    }

    [Theory]
    [InlineData("png")]
    [InlineData("webp")]
    [InlineData("tiff")]
    public async Task Convert_BakesOrientationIntoPixelsAndLeavesNoRotatingTag(string target)
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("input.jpg");
        var output = folder.File($"output.{target}");
        using (var image = new MagickImage(MagickColors.Red, 20, 10))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Orientation, (ushort)6);
            image.SetProfile(exif);
            image.Orientation = Orientation.RightTop; // Magick overwrites the tag from this on write.
            image.Write(input, MagickFormat.Jpeg);
        }

        using (var source = new MagickImage(input))
        {
            Assert.Equal(Orientation.RightTop, source.Orientation);
        }

        await new ImageConverter().ConvertAsync(input, output, "jpg", target, null, CancellationToken.None);

        using var result = new MagickImage(output);
        Assert.Equal(10u, result.Width);
        Assert.Equal(20u, result.Height);
        Assert.Contains(result.Orientation, new[] { Orientation.Undefined, Orientation.TopLeft });

        // Magick may keep the tag as "normal" (1), which no viewer rotates.
        var tag = result.GetExifProfile()?.GetValue(ExifTag.Orientation);
        Assert.True(tag is null || tag.Value == 1, $"Expected no rotation, got {tag?.Value}.");
    }

    [Fact]
    public async Task Convert_ToJpeg_PutsTransparentAreasOnWhite()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("input.png");
        var output = folder.File("output.jpg");
        using (var image = new MagickImage(MagickColors.Transparent, 8, 8))
        {
            image.Write(input, MagickFormat.Png);
        }

        await new ImageConverter().ConvertAsync(input, output, "png", "jpg", null, CancellationToken.None);

        using var result = new MagickImage(output);
        using var pixels = result.GetPixels();
        var color = pixels.GetPixel(4, 4).ToColor()!;
        Assert.True(color.R > 250 && color.G > 250 && color.B > 250, $"Expected white, got {color}.");
    }

    [Fact]
    public async Task Convert_RejectsAnImageOverThePixelLimit()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("input.png");
        WriteSample(input, MagickFormat.Png, width: 20, height: 10);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ImageConverter(maxPixels: 199).ConvertAsync(input, folder.File("output.jpg"), "png", "jpg", null, CancellationToken.None));
    }

    [Fact]
    public async Task Convert_RejectsAFileThatIsNotWhatItsExtensionSays()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("input.png");
        File.WriteAllText(input, "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");

        await Assert.ThrowsAnyAsync<MagickException>(() =>
            new ImageConverter().ConvertAsync(input, folder.File("output.jpg"), "png", "jpg", null, CancellationToken.None));
    }

    private static void WriteSample(string path, MagickFormat format, uint width, uint height)
    {
        using var image = new MagickImage(MagickColors.CornflowerBlue, width, height);
        using var stream = File.Create(path);
        image.Write(stream, format);
    }
}
