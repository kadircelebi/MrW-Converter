using ImageMagick;
using MrWConverter.Core.Conversion;
using MrWConverter.Core.Formats;
using MrWConverter.Core.Media;
using MrWConverter.Core.Tools;

namespace MrWConverter.Core.Tests;

/// <summary>Skipped until scripts\setup-ffmpeg.ps1 has run; see <see cref="FfmpegFactAttribute"/>.</summary>
public class MediaEditorTests
{
    private const double Tolerance = 0.25;

    private static readonly string Tools = FfmpegFolder.Path;
    private static readonly string Ffmpeg = Path.Combine(FfmpegFolder.FixturesPath, "ffmpeg.exe");
    private static readonly string Ffprobe = Path.Combine(Tools, "ffprobe.exe");

    private static MediaEditor Editor => new(Tools);

    [FfmpegFact]
    public async Task ReadInfo_ReportsTheVideo()
    {
        using var folder = new TemporaryFolder();
        var input = await MakeVideo(folder, "a.mp4");

        var info = await Editor.ReadInfoAsync(input);

        Assert.Equal(320, info.Width);
        Assert.Equal(240, info.Height);
        Assert.True(info.HasVideo);
        Assert.True(info.HasAudio);
        Assert.InRange(info.DurationSeconds, 3 - Tolerance, 3 + Tolerance);
    }

    [FfmpegFact]
    public async Task ReadInfo_OfARotatedVideo_MatchesItsFrames()
    {
        using var folder = new TemporaryFolder();
        var flat = await MakeVideo(folder, "flat.mp4");
        var rotated = folder.File("rotated.mp4");
        await ToolProcess.RunAsync(Ffmpeg, ["-hide_banner", "-loglevel", "error", "-display_rotation", "90", "-i", flat, "-c", "copy", rotated]);

        var info = await Editor.ReadInfoAsync(rotated);
        using var frame = new MagickImage(await Editor.ReadFrameAsync(rotated, 1), MagickFormat.Png);

        Assert.Equal((240, 320), (info.Width, info.Height));
        Assert.Equal((240u, 320u), (frame.Width, frame.Height));
    }

    [FfmpegFact]
    public async Task ReadInfo_OfABrokenFile_Throws()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("a.mp4");
        await File.WriteAllTextAsync(input, "not a video");

        await Assert.ThrowsAsync<ToolFailedException>(() => Editor.ReadInfoAsync(input));
    }

    [FfmpegTheory]
    [InlineData(0.0)]
    [InlineData(1.5)]
    [InlineData(3.0)]
    public async Task ReadFrame_WorksAnywhereIncludingTheVeryEnd(double seconds)
    {
        using var folder = new TemporaryFolder();
        var input = await MakeVideo(folder, "a.mp4");

        using var frame = new MagickImage(await Editor.ReadFrameAsync(input, seconds), MagickFormat.Png);

        Assert.Equal((320u, 240u), (frame.Width, frame.Height));
    }

    [FfmpegFact]
    public async Task ReadFrame_ShrinksLargeVideoToFit()
    {
        using var folder = new TemporaryFolder();
        var input = await MakeVideo(folder, "a.mp4", 3200, 1800);

        using var frame = new MagickImage(await Editor.ReadFrameAsync(input, 0), MagickFormat.Png);

        Assert.Equal((1600u, 900u), (frame.Width, frame.Height));
    }

    [FfmpegFact]
    public async Task ReadWaveform_DrawsTheSound()
    {
        using var folder = new TemporaryFolder();
        var input = await MakeAudio(folder, "a.mp3", "libmp3lame");

        using var wave = new MagickImage(await Editor.ReadWaveformAsync(input), MagickFormat.Png);

        Assert.Equal(1400u, wave.Width);
    }

    [FfmpegTheory]
    [InlineData("mp4", "mp4", "h264", "aac")]
    [InlineData("mov", "mov", "h264", "aac")]
    [InlineData("mkv", "mkv", "h264", "aac")]
    [InlineData("webm", "webm", "vp9", "opus")]
    [InlineData("avi", "mp4", "h264", "aac")]
    [InlineData("wmv", "mp4", "h264", "aac")]
    public async Task TrimVideo_KeepsTheChosenRange(string source, string target, string videoCodec, string audioCodec)
    {
        using var folder = new TemporaryFolder();
        var input = await MakeVideo(folder, $"a.{source}", encoder: EncoderFor(source));
        var tool = Trim(source, 3, 1, 2);
        var output = folder.File(tool.OutputName("a"));

        await tool.RunAsync(input, output, source, null, CancellationToken.None);

        Assert.EndsWith($"a-trim.{target}", output);
        var info = await Editor.ReadInfoAsync(output);
        Assert.InRange(info.DurationSeconds, 1 - Tolerance, 1 + Tolerance);
        var codecs = await StreamCodecs(output);
        Assert.Contains(videoCodec, codecs);
        Assert.Contains(audioCodec, codecs);
    }

    [FfmpegTheory]
    [InlineData("mp3", "libmp3lame", "mp3", "mp3")]
    [InlineData("wav", "pcm_s16le", "wav", "pcm_s16le")]
    [InlineData("flac", "flac", "flac", "flac")]
    [InlineData("aiff", "pcm_s16be", "flac", "flac")]
    [InlineData("wma", "wmav2", "m4a", "aac")]
    public async Task TrimAudio_KeepsTheChosenRange(string source, string encoder, string target, string codec)
    {
        using var folder = new TemporaryFolder();
        var input = await MakeAudio(folder, $"a.{source}", encoder);
        var tool = Trim(source, 3, 0.5, 2);
        var output = folder.File(tool.OutputName("a"));

        await tool.RunAsync(input, output, source, null, CancellationToken.None);

        Assert.EndsWith($"a-trim.{target}", output);
        var info = await Editor.ReadInfoAsync(output);
        Assert.InRange(info.DurationSeconds, 1.5 - Tolerance, 1.5 + Tolerance);
        Assert.Contains(codec, await StreamCodecs(output));
    }

    [FfmpegFact]
    public async Task Trim_ReportsProgressOfTheChosenRange()
    {
        using var folder = new TemporaryFolder();
        var input = await MakeVideo(folder, "a.mp4");
        var tool = Trim("mp4", 3, 0.5, 2.5);
        var reports = new List<double>();

        await tool.RunAsync(input, folder.File("out.mp4"), "mp4", new Inline(reports.Add), CancellationToken.None);

        Assert.NotEmpty(reports);
        Assert.All(reports, value => Assert.InRange(value, 0, 1));
    }

    [FfmpegTheory]
    [InlineData("mp4")]
    [InlineData("webm")]
    public async Task CropVideo_KeepsTheRegionAtEvenSizes(string format)
    {
        using var folder = new TemporaryFolder();
        var input = await MakeVideo(folder, $"a.{format}", encoder: EncoderFor(format));
        var tool = Editor.CropVideo(format, new ImageRegion(10, 20, 101, 51), 3);
        var output = folder.File(tool.OutputName("a"));

        await tool.RunAsync(input, output, format, null, CancellationToken.None);

        var info = await Editor.ReadInfoAsync(output);
        Assert.EndsWith($"a-crop.{format}", output);
        Assert.Equal((100, 50), (info.Width, info.Height));
        Assert.True(info.HasAudio);
    }

    [FfmpegFact]
    public async Task CropVideo_ShowsThePixelsOfTheRegion()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("a.mp4");

        // The left half is white and the right half black.
        await ToolProcess.RunAsync(Ffmpeg, ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=white:s=320x240:r=10:d=1,drawbox=x=160:y=0:w=160:h=240:c=black:t=fill", "-c:v", "mpeg4", "-q:v", "1", input]);
        var tool = Editor.CropVideo("mp4", new ImageRegion(200, 40, 80, 80), 1);
        var output = folder.File("out.mp4");

        await tool.RunAsync(input, output, "mp4", null, CancellationToken.None);

        using var frame = new MagickImage(await Editor.ReadFrameAsync(output, 0), MagickFormat.Png);
        using var pixels = frame.GetPixels();
        Assert.True(pixels.GetPixel(40, 40).ToColor()!.R < 60, "The chosen part of the video is black.");
    }

    [FfmpegTheory]
    [InlineData("mp4")]
    [InlineData("webm")]
    [InlineData("avi")]
    [InlineData("wmv")]
    public async Task Mute_RemovesTheSoundAndCopiesTheVideo(string format)
    {
        using var folder = new TemporaryFolder();
        var input = await MakeVideo(folder, $"a.{format}", encoder: EncoderFor(format));
        var before = await StreamCodecs(input);
        var tool = Editor.Mute(format, 3);
        var output = folder.File(tool.OutputName("a"));

        await tool.RunAsync(input, output, format, null, CancellationToken.None);

        var info = await Editor.ReadInfoAsync(output);
        Assert.EndsWith($"a-muted.{format}", output);
        Assert.False(info.HasAudio);

        // The sample's encoder rather than H.264 or VP9 proves nothing was re-encoded.
        Assert.Contains(before.Split('\n', StringSplitOptions.TrimEntries).First(), await StreamCodecs(output));
    }

    [FfmpegFact]
    public async Task Snapshot_SavesAFullSizePng()
    {
        using var folder = new TemporaryFolder();
        var input = await MakeVideo(folder, "a.mp4", 3200, 1800);
        var tool = Editor.Snapshot(1);
        var output = folder.File(tool.OutputName("a"));

        await tool.RunAsync(input, output, "mp4", null, CancellationToken.None);

        using var frame = new MagickImage(output);
        Assert.EndsWith("a-frame.png", output);
        Assert.Equal((3200u, 1800u), (frame.Width, frame.Height));
    }

    [FfmpegFact]
    public async Task Runner_HandlesSpecialFileNamesAndKeepsTheOriginal()
    {
        using var folder = new TemporaryFolder();
        var input = await MakeVideo(folder, "çığ [1] %d.mp4");
        var before = File.ReadAllBytes(input);
        var runner = new ConversionRunner(FormatCatalog.Default, []);

        foreach (var (tool, name) in new (IFileTool, string)[]
        {
            (Trim("mp4", 3, 0, 1), "çığ [1] %d-trim.mp4"),
            (Editor.CropVideo("mp4", new ImageRegion(0, 0, 100, 100), 3), "çığ [1] %d-crop.mp4"),
            (Editor.Mute("mp4", 3), "çığ [1] %d-muted.mp4"),
            (Editor.Snapshot(1), "çığ [1] %d-frame.png"),
        })
        {
            var outputs = await runner.RunToolAsync([input], tool, folder.Path);
            Assert.Equal([folder.File(name)], outputs);
            Assert.True(new FileInfo(outputs[0]).Length > 0);
        }

        Assert.Equal(before, File.ReadAllBytes(input));
    }

    [FfmpegFact]
    public async Task Runner_WhenCancelled_LeavesNothing()
    {
        using var folder = new TemporaryFolder();
        var input = await MakeVideo(folder, "a.mp4");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var runner = new ConversionRunner(FormatCatalog.Default, []);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runner.RunToolAsync([input], Trim("mp4", 3, 0, 1), folder.Path, null, cancellation.Token));

        Assert.False(File.Exists(folder.File("a-trim.mp4")));
    }

    [FfmpegTheory]
    [InlineData("mp4", "h264", "aac")]
    [InlineData("webm", "vp9", "opus")]
    [InlineData("avi", "h264", "aac")]
    public async Task SaveEdit_JoinsOnlyTheKeptPartsAndSoundStaysWithPicture(string source, string videoCodec, string audioCodec)
    {
        using var folder = new TemporaryFolder();
        var input = await MakeColorSegments(folder, $"a.{source}", EncoderFor(source));
        var info = await Editor.ReadInfoAsync(input);

        // Red with a low tone, lime with a middle one, blue with a high one: the middle goes.
        var edit = new MediaEdit(info.DurationSeconds).SplitAt(1).SplitAt(2).Remove(1);
        var tool = Assert.Single(Editor.SaveEdit(source, edit, info, separateFiles: false));
        var output = folder.File(tool.OutputName("a"));

        await tool.RunAsync(input, output, source, null, CancellationToken.None);

        var result = await Editor.ReadInfoAsync(output);
        var target = FfmpegArguments.EditTarget(source);
        Assert.EndsWith($"a-edit.{target}", output);
        Assert.InRange(result.DurationSeconds, 2 - Tolerance, 2 + Tolerance);
        var codecs = await StreamCodecs(output);
        Assert.Contains(videoCodec, codecs);
        Assert.Contains(audioCodec, codecs);
        await AssertColorAt(output, 0.5, 0);
        await AssertColorAt(output, 1.5, 2);
        Assert.InRange(await ToneAt(folder, output, 0.5), 300, 360);
        Assert.InRange(await ToneAt(folder, output, 1.5), 930, 1050);
    }

    [FfmpegFact]
    public async Task SaveEdit_StartsLateInTheFile_WithoutLosingTheFirstKeptPart()
    {
        using var folder = new TemporaryFolder();
        var input = await MakeColorSegments(folder, "a.mp4", "mpeg4");
        var info = await Editor.ReadInfoAsync(input);

        // Lime and blue stay with a gap between them, and the red before them is skipped by seeking.
        var edit = new MediaEdit(info.DurationSeconds).SplitAt(1).SplitAt(1.5).SplitAt(2).Remove(0).Remove(2);
        var tool = Assert.Single(Editor.SaveEdit("mp4", edit, info, separateFiles: false));
        var output = folder.File(tool.OutputName("a"));

        await tool.RunAsync(input, output, "mp4", null, CancellationToken.None);

        Assert.InRange((await Editor.ReadInfoAsync(output)).DurationSeconds, 1.5 - Tolerance, 1.5 + Tolerance);
        await AssertColorAt(output, 0.25, 1);
        await AssertColorAt(output, 1, 2);
    }

    [FfmpegFact]
    public async Task SaveEdit_OfASilentVideo_Works()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("a.mp4");
        await ToolProcess.RunAsync(Ffmpeg, ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc=size=320x240:rate=10:duration=3", "-c:v", "mpeg4", input]);
        var info = await Editor.ReadInfoAsync(input);
        var edit = new MediaEdit(info.DurationSeconds).SplitAt(1).SplitAt(2).Remove(1);
        var tool = Assert.Single(Editor.SaveEdit("mp4", edit, info, separateFiles: false));
        var output = folder.File(tool.OutputName("a"));

        await tool.RunAsync(input, output, "mp4", null, CancellationToken.None);

        var result = await Editor.ReadInfoAsync(output);
        Assert.False(result.HasAudio);
        Assert.InRange(result.DurationSeconds, 2 - Tolerance, 2 + Tolerance);
    }

    [FfmpegTheory]
    [InlineData("mp3", "libmp3lame", "mp3")]
    [InlineData("wav", "pcm_s16le", "wav")]
    [InlineData("wma", "wmav2", "m4a")]
    public async Task SaveEdit_OfSound_JoinsTheKeptParts(string source, string encoder, string target)
    {
        using var folder = new TemporaryFolder();
        var input = await MakeAudio(folder, $"a.{source}", encoder);
        var info = await Editor.ReadInfoAsync(input);
        var edit = new MediaEdit(info.DurationSeconds).SplitAt(1).SplitAt(2).Remove(1);
        var tool = Assert.Single(Editor.SaveEdit(source, edit, info, separateFiles: false));
        var output = folder.File(tool.OutputName("a"));

        await tool.RunAsync(input, output, source, null, CancellationToken.None);

        Assert.EndsWith($"a-edit.{target}", output);
        var result = await Editor.ReadInfoAsync(output);
        Assert.False(result.HasVideo);
        Assert.InRange(result.DurationSeconds, 2 - Tolerance, 2 + Tolerance);
    }

    [FfmpegFact]
    public async Task SaveEdit_OneKeptPart_IsAPlainCut()
    {
        using var folder = new TemporaryFolder();
        var input = await MakeVideo(folder, "a.mp4");
        var info = await Editor.ReadInfoAsync(input);
        var edit = new MediaEdit(info.DurationSeconds).TrimStart(1).TrimEnd(2);

        var tool = Assert.Single(Editor.SaveEdit("mp4", edit, info, separateFiles: false));

        Assert.Equal("a-trim.mp4", tool.OutputName("a"));
    }

    [FfmpegFact]
    public async Task SaveEdit_SeparateFiles_MakesOneFileForEachKeptClip_ThroughTheRunner()
    {
        using var folder = new TemporaryFolder();
        var input = await MakeVideo(folder, "a.mp4");
        var info = await Editor.ReadInfoAsync(input);

        // Clips side by side stay separate here, because splitting a video is the point.
        var edit = new MediaEdit(info.DurationSeconds).SplitAt(1).SplitAt(2);
        var tools = Editor.SaveEdit("mp4", edit, info, separateFiles: true);
        var runner = new ConversionRunner(FormatCatalog.Default, []);

        var outputs = await runner.RunToolsAsync(input, tools, folder.Path);

        Assert.Equal([folder.File("a-part1.mp4"), folder.File("a-part2.mp4"), folder.File("a-part3.mp4")], outputs);
        foreach (var output in outputs)
        {
            Assert.InRange((await Editor.ReadInfoAsync(output)).DurationSeconds, 1 - Tolerance, 1 + Tolerance);
        }
    }

    [FfmpegFact]
    public async Task RunTools_WhenOnePartFails_PublishesNothing()
    {
        using var folder = new TemporaryFolder();
        var input = await MakeVideo(folder, "a.mp4");
        var info = await Editor.ReadInfoAsync(input);
        var edit = new MediaEdit(info.DurationSeconds).SplitAt(1);
        var tools = Editor.SaveEdit("mp4", edit, info, separateFiles: true).Append(new MediaEditor(folder.File("no-tools")).Snapshot(0)).ToArray();
        var runner = new ConversionRunner(FormatCatalog.Default, []);

        await Assert.ThrowsAsync<ConversionException>(() => runner.RunToolsAsync(input, tools, folder.Path));

        Assert.False(File.Exists(folder.File("a-part1.mp4")));
    }

    [FfmpegFact]
    public async Task ReadFrame_CanMakeASmallThumbnail()
    {
        using var folder = new TemporaryFolder();
        var input = await MakeVideo(folder, "a.mp4", 1280, 720);

        using var frame = new MagickImage(await Editor.ReadFrameAsync(input, 1, CancellationToken.None, 160), MagickFormat.Png);

        Assert.Equal((160u, 90u), (frame.Width, frame.Height));
    }

    [Fact]
    public void Catalog_OffersTheToolsForTheirKinds()
    {
        var catalog = FormatCatalog.Default;

        Assert.Equal(["shrink", "edit", "cropVideo", "muteVideo", "snapshot"], catalog.CommonTools(["a.mp4"]));
        Assert.Equal(["edit"], catalog.CommonTools(["a.mp3"]));
        Assert.Equal(["edit"], catalog.CommonTools(["a.mp4", "b.mp3"]));
        Assert.Empty(catalog.CommonTools(["a.gif"]));
    }

    private static IFileTool Trim(string format, double duration, double start, double end) =>
        Assert.Single(Editor.SaveEdit(format, new MediaEdit(duration).TrimStart(start).TrimEnd(end), new MediaInfo(duration, 0, 0, true, true), separateFiles: false));

    /// <summary>A second each of red at 330 Hz, lime at 660 Hz and blue at 990 Hz, so picture and sound can both be checked.</summary>
    private static async Task<string> MakeColorSegments(TemporaryFolder folder, string name, string encoder)
    {
        var path = folder.File(name);
        var audio = Path.GetExtension(name) == ".webm" ? "libopus" : "libmp3lame";
        await ToolProcess.RunAsync(Ffmpeg,
        [
            "-hide_banner", "-loglevel", "error",
            "-f", "lavfi", "-i", "color=c=red:size=160x120:rate=20:duration=1",
            "-f", "lavfi", "-i", "color=c=lime:size=160x120:rate=20:duration=1",
            "-f", "lavfi", "-i", "color=c=blue:size=160x120:rate=20:duration=1",
            "-f", "lavfi", "-i", "aevalsrc=exprs='0.3*sin(2*PI*if(lt(t,1),330,if(lt(t,2),660,990))*t)':s=48000:d=3",
            "-filter_complex", "[0:v][1:v][2:v]concat=n=3:v=1:a=0[v]",
            "-map", "[v]", "-map", "3:a",
            "-c:v", encoder, "-c:a", audio,
            path,
        ]);
        return path;
    }

    /// <param name="channel">Which of red, green and blue should stand out.</param>
    private static async Task AssertColorAt(string path, double seconds, int channel)
    {
        using var frame = new MagickImage(await Editor.ReadFrameAsync(path, seconds), MagickFormat.Png);
        using var pixels = frame.GetPixels();
        var color = pixels.GetPixel((int)frame.Width / 2, (int)frame.Height / 2).ToColor()!;
        byte[] values = [color.R, color.G, color.B];

        Assert.True(values[channel] > 150 && values.Where((_, index) => index != channel).All(value => value < 110), $"At {seconds} s the color is {color}.");
    }

    /// <summary>Counts the wave's rising crossings over a fifth of a second, which gives its pitch.</summary>
    private static async Task<double> ToneAt(TemporaryFolder folder, string path, double seconds)
    {
        const double Window = 0.2;
        const int Rate = 48000;
        var raw = folder.File($"{Guid.NewGuid():N}.raw");
        await ToolProcess.RunAsync(Ffmpeg,
        [
            "-hide_banner", "-loglevel", "error", "-ss", (seconds - Window / 2).ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-i", path, "-t", Window.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-map", "0:a:0", "-ac", "1", "-ar", Rate.ToString(), "-f", "s16le", raw,
        ]);

        var bytes = await File.ReadAllBytesAsync(raw);
        var crossings = 0;
        for (var offset = 2; offset + 1 < bytes.Length; offset += 2)
        {
            if (BitConverter.ToInt16(bytes, offset - 2) < 0 && BitConverter.ToInt16(bytes, offset) >= 0)
            {
                crossings++;
            }
        }

        return crossings / (bytes.Length / 2d / Rate);
    }

    private static string EncoderFor(string format) => format switch
    {
        "webm" => "libvpx-vp9",
        "wmv" => "wmv2",
        _ => "mpeg4",
    };

    private static async Task<string> MakeVideo(TemporaryFolder folder, string name, int width = 320, int height = 240, string encoder = "mpeg4")
    {
        var path = folder.File(name);
        var audio = Path.GetExtension(name) switch
        {
            ".webm" => "libopus",
            ".wmv" => "wmav2",
            _ => "libmp3lame",
        };

        await ToolProcess.RunAsync(Ffmpeg,
        [
            "-hide_banner", "-loglevel", "error",
            "-f", "lavfi", "-i", $"testsrc=size={width}x{height}:rate=10:duration=3",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=3",
            "-c:v", encoder, "-c:a", audio,
            path,
        ]);
        return path;
    }

    private static async Task<string> MakeAudio(TemporaryFolder folder, string name, string encoder)
    {
        var path = folder.File(name);
        await ToolProcess.RunAsync(Ffmpeg, ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "sine=frequency=440:duration=3", "-c:a", encoder, path]);
        return path;
    }

    private static Task<string> StreamCodecs(string path) =>
        ToolProcess.RunAsync(Ffprobe, ["-v", "error", "-show_entries", "stream=codec_name", "-of", "csv=p=0", path]);

    /// <summary>Reports at once, unlike <see cref="Progress{T}"/>, which posts to a thread pool.</summary>
    private sealed class Inline(Action<double> report) : IProgress<double>
    {
        public void Report(double value)
        {
            lock (report)
            {
                report(value);
            }
        }
    }
}
