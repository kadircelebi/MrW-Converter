using MrWConverter.Core.Formats;

namespace MrWConverter.Core.Tests;

public class FormatCatalogTests
{
    private readonly FormatCatalog _catalog = FormatCatalog.Default;

    [Theory]
    [InlineData(@"C:\a\PHOTO.JPEG", "jpg")]
    [InlineData("scan.TIF", "tiff")]
    [InlineData("image.png", "png")]
    public void FormatOf_ReturnsCanonicalFormat(string path, string expected)
    {
        Assert.Equal(expected, _catalog.FormatOf(path));
    }

    [Fact]
    public void FormatOf_IsNotAffectedByTurkishCulture()
    {
        var original = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
        try
        {
            // ToLower under tr-TR would turn "I" into a dotless "ı".
            Assert.Equal("tiff", _catalog.FormatOf("SCAN.TIFF"));
            Assert.Equal("avif", _catalog.FormatOf("PHOTO.AVIF"));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [Fact]
    public void CommonTargets_LeavesOutTheFormatOfTheFile()
    {
        var targets = _catalog.CommonTargets(["a.png"]);

        Assert.DoesNotContain("png", targets);
        Assert.Contains("jpg", targets);
    }

    [Fact]
    public void CommonTargets_ForMixedFiles_ReturnsOnlySharedTargets()
    {
        var targets = _catalog.CommonTargets(["a.png", "b.jpg"]);

        Assert.DoesNotContain("png", targets);
        Assert.DoesNotContain("jpg", targets);
        Assert.Contains("webp", targets);
    }

    [Fact]
    public void CommonTargets_WithAnUnsupportedFile_IsEmpty()
    {
        Assert.Empty(_catalog.CommonTargets(["a.png", "b.exe"]));
    }

    [Fact]
    public void CommonTools_OfferBackgroundRemovalForImagesOnly()
    {
        Assert.Contains("removeBackground", _catalog.CommonTools(["a.png", "b.jpg"]));
        Assert.Empty(_catalog.CommonTools(["a.mp3"]));
        Assert.Empty(_catalog.CommonTools(["a.png", "b.mp4"]));
        Assert.Empty(_catalog.CommonTools(["a.exe"]));
    }

    [Theory]
    [InlineData("a.jpeg", "image")]
    [InlineData("a.flac", "audio")]
    [InlineData("a.m4v", "video")]
    [InlineData("a.gif", "animation")]
    public void GroupOf_ReturnsTheCatalogGroup(string path, string expected)
    {
        Assert.Equal(expected, _catalog.GroupOf(path));
    }

    [Fact]
    public void CommonTargets_ForImageAndAudio_IsEmpty()
    {
        Assert.Empty(_catalog.CommonTargets(["a.png", "b.mp3"]));
    }

    [Fact]
    public void CommonTargets_ForVideoAndAudio_ReturnsAudioFormatsTheyShare()
    {
        var targets = _catalog.CommonTargets(["a.mp4", "b.wav"]);

        Assert.Contains("mp3", targets);
        Assert.DoesNotContain("mp4", targets);
    }

    [Fact]
    public void CommonTargets_ForGif_HasNoAudioFormats()
    {
        var targets = _catalog.CommonTargets(["a.gif"]);

        Assert.Contains("mp4", targets);
        Assert.DoesNotContain("mp3", targets);
    }

    [Fact]
    public void EveryTarget_HasAConverter()
    {
        IReadOnlyList<Conversion.IFileConverter> converters = [new Conversion.ImageConverter(), new Conversion.FfmpegConverter("unused")];

        foreach (var input in _catalog.InputFormats)
        {
            foreach (var target in _catalog.CommonTargets([$"a.{input}"]))
            {
                Assert.True(converters.Any(converter => converter.CanConvert(input, target)), $"{input} to {target}");
            }
        }
    }

    [Fact]
    public void CommonTargets_WithoutFiles_IsEmpty()
    {
        Assert.Empty(_catalog.CommonTargets([]));
    }

    [Fact]
    public void InputExtensions_IncludeAliases()
    {
        Assert.Contains("jpeg", _catalog.InputExtensions);
        Assert.Contains("jpg", _catalog.InputExtensions);
    }
}
