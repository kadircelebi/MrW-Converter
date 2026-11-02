using ImageMagick;
using MrWConverter.Core.Conversion;
using MrWConverter.Core.Formats;
using MrWConverter.Core.Media;
using MrWConverter.Core.Tools;

namespace MrWConverter.Core.Tests;

public class ShrinkTests
{
    private static readonly string Tools = FfmpegFolder.Path;
    private static readonly string Ffmpeg = Path.Combine(FfmpegFolder.FixturesPath, "ffmpeg.exe");
    private static readonly string Ffprobe = Path.Combine(Tools, "ffprobe.exe");

    private static MediaEditor Editor => new(Tools);

    [Theory]
    [InlineData("jpg")]
    [InlineData("webp")]
    [InlineData("png")]
    [InlineData("tiff")]
    [InlineData("bmp")]
    public async Task Image_MeetsTheLimit(string format)
    {
        using var folder = new TemporaryFolder();
        var input = MakeImage(folder, $"a.{format}", format, 1600, 1200);
        var limit = new FileInfo(input).Length / 3;
        var output = folder.File("out");

        await new ImageShrinker(new ShrinkOptions(limit), format).RunAsync(input, output, format, null, default);

        Assert.InRange(new FileInfo(output).Length, 1, limit);
        using var result = new MagickImage(output, ImageFile.Formats[format]);
        Assert.Equal(1600d / 1200, (double)result.Width / result.Height, 2);
    }

    [Fact]
    public async Task Image_WithALossyFormat_KeepsItsPixelsWhenQualityIsEnough()
    {
        using var folder = new TemporaryFolder();
        var input = MakeImage(folder, "a.jpg", "jpg", 800, 600);
        var output = folder.File("out.jpg");

        await new ImageShrinker(new ShrinkOptions(SizeShare: 0.7), "jpg").RunAsync(input, output, "jpg", null, default);

        using var result = new MagickImage(output);
        Assert.Equal((800u, 600u), (result.Width, result.Height));
    }

    [Fact]
    public async Task Image_WithALimitBelowLowestQuality_GetsFewerPixels()
    {
        using var folder = new TemporaryFolder();
        var input = MakeImage(folder, "a.jpg", "jpg", 1600, 1200);
        var limit = new FileInfo(input).Length / 20;
        var output = folder.File("out.jpg");

        await new ImageShrinker(new ShrinkOptions(limit), "jpg").RunAsync(input, output, "jpg", null, default);

        using var result = new MagickImage(output);
        Assert.True(result.Width < 1600);
        Assert.InRange(new FileInfo(output).Length, 1, limit);
    }

    [Fact]
    public async Task Image_MaxSide_FitsTheLongestSideWithoutASizeTarget()
    {
        using var folder = new TemporaryFolder();
        var input = MakeImage(folder, "a.jpg", "jpg", 600, 1200);
        var output = folder.File("out.jpg");

        await new ImageShrinker(new ShrinkOptions(MaxSide: 400), "jpg").RunAsync(input, output, "jpg", null, default);

        using var result = new MagickImage(output);
        Assert.Equal((200u, 400u), (result.Width, result.Height));
        Assert.True(new FileInfo(output).Length <= new FileInfo(input).Length);
    }

    [Fact]
    public async Task Image_AlreadyWithinTheLimit_IsCopiedAsItIs()
    {
        using var folder = new TemporaryFolder();
        var input = MakeImage(folder, "a.jpg", "jpg", 200, 100);
        var output = folder.File("out.jpg");

        await new ImageShrinker(new ShrinkOptions(new FileInfo(input).Length + 1, MaxSide: 4000), "jpg").RunAsync(input, output, "jpg", null, default);

        Assert.Equal(await File.ReadAllBytesAsync(input), await File.ReadAllBytesAsync(output));
    }

    [Fact]
    public async Task Image_WithAnImpossibleLimit_SaysSo()
    {
        using var folder = new TemporaryFolder();
        var input = MakeImage(folder, "a.png", "png", 400, 300);

        await Assert.ThrowsAsync<CannotShrinkException>(() =>
            new ImageShrinker(new ShrinkOptions(10), "png").RunAsync(input, folder.File("out.png"), "png", null, default));
    }

    [Fact]
    public async Task Image_LosesItsMetadata()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("a.jpg");
        using (var image = Noise(800, 600))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Software, "secret");
            image.SetProfile(exif);
            image.Write(input, MagickFormat.Jpeg);
        }

        var output = folder.File("out.jpg");
        await new ImageShrinker(new ShrinkOptions(SizeShare: 0.5), "jpg").RunAsync(input, output, "jpg", null, default);

        using var result = new MagickImage(output);
        Assert.Null(result.GetExifProfile());
    }

    [Fact]
    public async Task Image_Cancelled_Stops()
    {
        using var folder = new TemporaryFolder();
        var input = MakeImage(folder, "a.jpg", "jpg", 400, 300);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ImageShrinker(new ShrinkOptions(SizeShare: 0.5), "jpg").RunAsync(input, folder.File("out.jpg"), "jpg", null, cancellation.Token));
    }

    [Fact]
    public void Plan_WithRoomToSpare_KeepsThePicture()
    {
        var info = new MediaInfo(60, 1920, 1080, true, true, 30);

        var plan = VideoShrinkPlan.For(info, 100_000_000, null);

        Assert.Equal((1920, 1080), (plan.Width, plan.Height));
        Assert.Null(plan.ScaleTo);
        Assert.Null(plan.FramesPerSecond);
        Assert.Equal(128_000, plan.AudioBitrate);
    }

    [Fact]
    public void Plan_ForATightLimit_BringsThePictureDown()
    {
        var info = new MediaInfo(60, 1920, 1080, true, true, 30);

        var plan = VideoShrinkPlan.For(info, 10_000_000, null);

        Assert.True(plan.Width < 1920);
        Assert.Equal(0, plan.Width % 2);
        Assert.Equal(0, plan.Height % 2);
        Assert.Equal(16d / 9, (double)plan.Width / plan.Height, 1);
        Assert.Equal($"scale={plan.Width}:{plan.Height}:flags=lanczos", plan.ScaleTo);
    }

    [Fact]
    public void Plan_TriesFewerFramesBeforeAnEvenSmallerPicture()
    {
        var info = new MediaInfo(60, 1920, 1080, true, false, 60);

        // Enough for 1080p at 30 frames per second, and not at 60.
        var plan = VideoShrinkPlan.For(info, 45_000_000, null);

        Assert.Equal((1920, 1080), (plan.Width, plan.Height));
        Assert.Equal(30, plan.FramesPerSecond);
    }

    [Fact]
    public void Plan_NeverGrowsThePicture()
    {
        var info = new MediaInfo(60, 320, 240, true, true, 30);

        var plan = VideoShrinkPlan.For(info, 100_000_000, 1920);

        Assert.Equal((320, 240), (plan.Width, plan.Height));
    }

    [Fact]
    public void Plan_RespectsTheMaxSide()
    {
        var info = new MediaInfo(60, 1080, 1920, true, true, 30);

        var plan = VideoShrinkPlan.For(info, 500_000_000, 1280);

        Assert.Equal((720, 1280), (plan.Width, plan.Height));
    }

    [Fact]
    public void Plan_WithTooFewBytesForTheLength_SaysSoBeforeEncoding()
    {
        var info = new MediaInfo(3600, 1920, 1080, true, true, 30);

        Assert.Throws<CannotShrinkException>(() => VideoShrinkPlan.For(info, 10_000_000, null));
    }

    [Fact]
    public void Plan_Lower_ChangesOnlyTheBitrateForASmallMiss()
    {
        var info = new MediaInfo(60, 1920, 1080, true, true, 30);
        var plan = VideoShrinkPlan.For(info, 100_000_000, null);

        var lower = plan.Lower(info, 105_000_000, 100_000_000);

        Assert.Equal((plan.Width, plan.Height), (lower.Width, lower.Height));
        Assert.True(lower.VideoBitrate < plan.VideoBitrate);
    }

    [Fact]
    public void Plan_Lower_AlsoShrinksThePictureForALargeMiss()
    {
        var info = new MediaInfo(60, 1920, 1080, true, true, 30);
        var plan = VideoShrinkPlan.For(info, 100_000_000, null);

        var lower = plan.Lower(info, 200_000_000, 100_000_000);

        Assert.True(lower.Width < plan.Width);
        Assert.Equal(0, lower.Width % 2);
        Assert.Equal(0, lower.Height % 2);
    }

    [Fact]
    public void Plan_Lower_GivesUpWhenTooLittleIsLeft()
    {
        var info = new MediaInfo(60, 640, 360, true, true, 30);
        var plan = VideoShrinkPlan.For(info, 2_000_000, null);

        Assert.Throws<CannotShrinkException>(() => plan.Lower(info, 50_000_000, 2_000_000));
    }

    [Fact]
    public void Arguments_CarryTheBitratesAndTheFilters()
    {
        var plan = new VideoShrinkPlan(1280, 720, 900_000, 96_000, 30, "scale=1280:720:flags=lanczos");

        var arguments = FfmpegArguments.ForShrink("in.mp4", "out.mp4", "mp4", plan);

        Assert.Equal("900000", After(arguments, "-b:v"));
        Assert.Equal("96000", After(arguments, "-b:a"));
        Assert.Equal("fps=30,scale=1280:720:flags=lanczos,pad=ceil(iw/2)*2:ceil(ih/2)*2", After(arguments, "-vf"));
    }

    [Fact]
    public void Arguments_ForWebm_UseItsOwnCodecs()
    {
        var plan = new VideoShrinkPlan(640, 360, 300_000, 96_000, null, null);

        var arguments = FfmpegArguments.ForShrink("in.webm", "out.webm", "webm", plan);

        Assert.Equal("libvpx-vp9", After(arguments, "-c:v"));
        Assert.Equal("libopus", After(arguments, "-c:a"));
        Assert.DoesNotContain("-vf", arguments);
    }

    [FfmpegTheory]
    [InlineData("mp4")]
    [InlineData("mov")]
    [InlineData("mkv")]
    [InlineData("webm")]
    public async Task Video_MeetsTheLimit(string format)
    {
        using var folder = new TemporaryFolder();
        var input = await MakeVideo(folder, $"a.{format}", format);
        var limit = new FileInfo(input).Length / 3;
        var output = folder.File($"out.{format}");

        await Editor.Shrink(format, new ShrinkOptions(limit)).RunAsync(input, output, format, null, default);

        Assert.InRange(new FileInfo(output).Length, 1, limit);
        var info = await Editor.ReadInfoAsync(output);
        Assert.True(info.HasVideo);
        Assert.True(info.HasAudio);
        Assert.InRange(info.DurationSeconds, 3.5, 4.5);
    }

    [FfmpegFact]
    public async Task Video_ThatIgnoresTheBitrate_StillEndsUpUnderTheLimit()
    {
        using var folder = new TemporaryFolder();
        var input = await MakeVideo(folder, "busy.mp4", "mp4", 1920, 1080);
        var limit = 2_000_000;
        var output = folder.File("out.mp4");
        var last = 0d;

        await Editor.Shrink("mp4", new ShrinkOptions(limit)).RunAsync(input, output, "mp4", new InlineProgress(value =>
        {
            Assert.True(value >= last);
            last = value;
        }), default);

        Assert.InRange(new FileInfo(output).Length, 1, limit);
        Assert.InRange(last, 0.01, 1);
    }

    [FfmpegFact]
    public async Task Video_MaxSide_ResizesWithoutASizeTarget()
    {
        using var folder = new TemporaryFolder();
        var input = await MakeVideo(folder, "a.mp4", "mp4", 1280, 720);
        var output = folder.File("out.mp4");

        await Editor.Shrink("mp4", new ShrinkOptions(MaxSide: 640)).RunAsync(input, output, "mp4", null, default);

        var info = await Editor.ReadInfoAsync(output);
        Assert.Equal((640, 360), (info.Width, info.Height));
        Assert.True(new FileInfo(output).Length <= new FileInfo(input).Length);
    }

    [FfmpegFact]
    public async Task Video_WithAGenerousLimit_DoesNotOutgrowTheOriginal()
    {
        using var folder = new TemporaryFolder();
        var input = await MakeVideo(folder, "a.mp4", "mp4", 1280, 720);
        var output = folder.File("out.mp4");

        await Editor.Shrink("mp4", new ShrinkOptions(1_000_000_000, MaxSide: 640)).RunAsync(input, output, "mp4", null, default);

        Assert.InRange(new FileInfo(output).Length, 1, new FileInfo(input).Length);
    }

    [FfmpegFact]
    public async Task Video_AlreadyWithinTheLimit_IsCopiedAsItIs()
    {
        using var folder = new TemporaryFolder();
        var input = await MakeVideo(folder, "a.mp4", "mp4");
        var output = folder.File("out.mp4");

        await Editor.Shrink("mp4", new ShrinkOptions(new FileInfo(input).Length)).RunAsync(input, output, "mp4", null, default);

        Assert.Equal(await File.ReadAllBytesAsync(input), await File.ReadAllBytesAsync(output));
    }

    [FfmpegFact]
    public async Task Video_RotatedByANote_KeepsHowItLooks()
    {
        using var folder = new TemporaryFolder();
        var flat = await MakeVideo(folder, "flat.mp4", "mp4", 1280, 720);
        var rotated = folder.File("rotated.mp4");
        await ToolProcess.RunAsync(Ffmpeg, ["-hide_banner", "-loglevel", "error", "-display_rotation", "90", "-i", flat, "-c", "copy", rotated]);
        var output = folder.File("out.mp4");

        await Editor.Shrink("mp4", new ShrinkOptions(MaxSide: 640)).RunAsync(rotated, output, "mp4", null, default);

        var info = await Editor.ReadInfoAsync(output);
        Assert.Equal((360, 640), (info.Width, info.Height));
    }

    [FfmpegFact]
    public void Video_NamesItsResultInAFormatTheAppCanWrite()
    {
        Assert.Equal("a-small.mp4", Editor.Shrink("avi", new ShrinkOptions(SizeShare: 0.5)).OutputName("a"));
        Assert.Equal("a-small.webm", Editor.Shrink("webm", new ShrinkOptions(SizeShare: 0.5)).OutputName("a"));
    }

    [FfmpegFact]
    public async Task Video_Cancelled_Stops()
    {
        using var folder = new TemporaryFolder();
        var input = await MakeVideo(folder, "a.mp4", "mp4");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Editor.Shrink("mp4", new ShrinkOptions(SizeShare: 0.5)).RunAsync(input, folder.File("out.mp4"), "mp4", null, cancellation.Token));
    }

    [FfmpegFact]
    public async Task Runner_ShrinksPicturesAndVideosInOneJobUnderTheirOwnFormats()
    {
        using var folder = new TemporaryFolder();
        var picture = MakeImage(folder, "pic.jpg", "jpg", 800, 600);
        var video = await MakeVideo(folder, "clip.mp4", "mp4");
        var catalog = FormatCatalog.Default;
        var options = new ShrinkOptions(SizeShare: 0.5);
        var runner = new ConversionRunner(catalog, []);

        var outputs = await runner.RunToolAsync(
            [picture, video],
            path => catalog.GroupOf(path) == "image" ? new ImageShrinker(options, catalog.FormatOf(path)) : Editor.Shrink(catalog.FormatOf(path), options),
            folder.File("results"));

        Assert.Equal(["pic-small.jpg", "clip-small.mp4"], outputs.Select(Path.GetFileName));
    }

    [Fact]
    public void Catalog_OffersItForPicturesAndVideosTogether()
    {
        var catalog = FormatCatalog.Default;

        Assert.Contains(ShrinkOptions.ToolId, catalog.CommonTools(["a.png"]));
        Assert.Contains(ShrinkOptions.ToolId, catalog.CommonTools(["a.mp4"]));
        Assert.Equal([ShrinkOptions.ToolId], catalog.CommonTools(["a.png", "b.mp4"]));
        Assert.DoesNotContain(ShrinkOptions.ToolId, catalog.CommonTools(["a.mp3"]));
        Assert.DoesNotContain(ShrinkOptions.ToolId, catalog.CommonTools(["a.gif"]));
    }

    [Fact]
    public void Options_WorkOutTheLimit()
    {
        Assert.Equal(25, new ShrinkOptions(25).LimitFor(1000));
        Assert.Equal(1000, new ShrinkOptions(5000).LimitFor(1000));
        Assert.Equal(500, new ShrinkOptions(SizeShare: 0.5).LimitFor(1000));
        Assert.Equal(1000, new ShrinkOptions(MaxSide: 100).LimitFor(1000));
        Assert.True(new ShrinkOptions(MaxSide: 100).Resizes(200));
        Assert.False(new ShrinkOptions(MaxSide: 100).Resizes(100));
        Assert.False(new ShrinkOptions(25).Resizes(100));
    }

    private static string After(IReadOnlyList<string> arguments, string option) =>
        arguments[arguments.ToList().IndexOf(option) + 1];

    private static MagickImage Noise(uint width, uint height)
    {
        var image = new MagickImage("xc:gray", width, height);
        image.AddNoise(NoiseType.Gaussian);
        return image;
    }

    private static string MakeImage(TemporaryFolder folder, string name, string format, uint width, uint height)
    {
        var path = folder.File(name);
        using var image = Noise(width, height);
        image.Quality = 95;
        image.Write(path, ImageFile.Formats[format]);
        return path;
    }

    private static async Task<string> MakeVideo(TemporaryFolder folder, string name, string format, int width = 640, int height = 360)
    {
        var path = folder.File(name);
        string[] video = format == "webm"
            ? ["-c:v", "libvpx-vp9", "-crf", "20", "-b:v", "0", "-deadline", "realtime", "-cpu-used", "8", "-c:a", "libopus"]
            : ["-c:v", "h264_mf", "-rate_control", "quality", "-quality", "70", "-c:a", "aac_mf", "-ar", "48000", "-ac", "2"];

        await ToolProcess.RunAsync(Ffmpeg,
        [
            "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", $"testsrc2=s={width}x{height}:r=30:d=4",
            "-f", "lavfi", "-i", "sine=f=440:d=4",
            "-vf", "noise=alls=25:allf=t",
            .. video,
            path,
        ]);

        return path;
    }

    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
