using MrWConverter.Core.Conversion;
using MrWConverter.Core.Media;

namespace MrWConverter.Core.Tests;

/// <summary>Skipped until scripts\setup-ffmpeg.ps1 has run; see <see cref="FfmpegFactAttribute"/>.</summary>
public class FfmpegConverterTests
{
    private static readonly string Tools = FfmpegFolder.Path;
    private static readonly string Ffmpeg = Path.Combine(Tools, "ffmpeg.exe");
    private static readonly string Ffprobe = Path.Combine(Tools, "ffprobe.exe");

    private static FfmpegConverter Converter => new(Tools);

    [FfmpegTheory]
    [InlineData("mp3", "mp3")]
    [InlineData("m4a", "aac")]
    [InlineData("wav", "pcm_s16le")]
    [InlineData("flac", "flac")]
    [InlineData("ogg", "vorbis")]
    [InlineData("opus", "opus")]
    public async Task Audio_ConvertsToTheExpectedCodec(string target, string codec)
    {
        using var folder = new TemporaryFolder();
        var input = await MakeAudio(folder, "input.wav");
        var output = folder.File($"output.{target}");

        await Converter.ConvertAsync(input, output, "wav", target, null, CancellationToken.None);

        Assert.Contains(codec, await StreamCodecs(output));
    }

    [FfmpegTheory]
    [InlineData("mp4", "h264", "aac")]
    [InlineData("mov", "h264", "aac")]
    [InlineData("mkv", "h264", "aac")]
    [InlineData("webm", "vp9", "opus")]
    public async Task Video_WithOddDimensions_ConvertsToTheExpectedCodecs(string target, string videoCodec, string audioCodec)
    {
        using var folder = new TemporaryFolder();
        var input = await MakeVideo(folder, "input.avi", width: 321, height: 241);
        var output = folder.File($"output.{target}");

        await Converter.ConvertAsync(input, output, "avi", target, null, CancellationToken.None);

        var codecs = await StreamCodecs(output);
        Assert.Contains(videoCodec, codecs);
        Assert.Contains(audioCodec, codecs);
    }

    /// <summary>Guards the container whitelist: every source format in the catalog must still open.</summary>
    [FfmpegTheory]
    [InlineData("mp3", "libmp3lame", "mp3")]
    [InlineData("m4a", "aac", "mp3")]
    [InlineData("flac", "flac", "mp3")]
    [InlineData("ogg", "libvorbis", "mp3")]
    [InlineData("opus", "libopus", "mp3")]
    [InlineData("aiff", "pcm_s16be", "mp3")]
    [InlineData("wma", "wmav2", "mp3")]
    public async Task EveryAudioSource_Opens(string source, string encoder, string target)
    {
        using var folder = new TemporaryFolder();
        var input = folder.File($"input.{source}");
        await ToolProcess.RunAsync(Ffmpeg, ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "sine=frequency=440:duration=1", "-c:a", encoder, input]);
        var output = folder.File($"output.{target}");

        await Converter.ConvertAsync(input, output, source, target, null, CancellationToken.None);

        Assert.Contains("mp3", await StreamCodecs(output));
    }

    [FfmpegTheory]
    [InlineData("mp4", "mpeg4")]
    [InlineData("mov", "mpeg4")]
    [InlineData("mkv", "mpeg4")]
    [InlineData("webm", "libvpx-vp9")]
    [InlineData("avi", "mpeg4")]
    [InlineData("wmv", "wmv2")]
    public async Task EveryVideoSource_Opens(string source, string encoder)
    {
        using var folder = new TemporaryFolder();
        var input = folder.File($"input.{source}");
        await ToolProcess.RunAsync(Ffmpeg, ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc=size=320x240:rate=10:duration=1", "-c:v", encoder, input]);
        var output = folder.File("output.gif");

        await Converter.ConvertAsync(input, output, source, "gif", null, CancellationToken.None);

        Assert.Contains("gif", await StreamCodecs(output));
    }

    [FfmpegFact]
    public async Task Runner_WhenCancelledDuringConversion_StopsFfmpegAndLeavesNothing()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("input.mkv");
        await ToolProcess.RunAsync(Ffmpeg, ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=30:duration=30", "-c:v", "ffv1", input]);
        var outputFolder = folder.File("out");
        using var cancellation = new CancellationTokenSource();
        var runner = new ConversionRunner(Formats.FormatCatalog.Default, [Converter]);

        // Cancels on the first progress report, so FFmpeg is certainly mid-file.
        var progress = new ImmediateProgress(_ => cancellation.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runner.RunAsync([input], "webm", outputFolder, progress, cancellation.Token));

        Assert.Empty(Directory.GetFileSystemEntries(outputFolder));
    }

    [FfmpegFact]
    public async Task Video_ToGif_ProducesAnAnimatedGif()
    {
        using var folder = new TemporaryFolder();
        var input = await MakeVideo(folder, "input.avi", width: 320, height: 240);
        var output = folder.File("output.gif");

        await Converter.ConvertAsync(input, output, "avi", "gif", null, CancellationToken.None);

        Assert.Contains("gif", await StreamCodecs(output));
    }

    [FfmpegFact]
    public async Task Gif_ToWebm_Converts()
    {
        using var folder = new TemporaryFolder();
        var video = await MakeVideo(folder, "input.avi", width: 320, height: 240);
        var gif = folder.File("input.gif");
        await Converter.ConvertAsync(video, gif, "avi", "gif", null, CancellationToken.None);
        var output = folder.File("output.webm");

        await Converter.ConvertAsync(gif, output, "gif", "webm", null, CancellationToken.None);

        Assert.Contains("vp9", await StreamCodecs(output));
    }

    [FfmpegFact]
    public async Task Video_ToMp3_KeepsOnlyTheSound()
    {
        using var folder = new TemporaryFolder();
        var input = await MakeVideo(folder, "input.avi", width: 320, height: 240);
        var output = folder.File("output.mp3");

        await Converter.ConvertAsync(input, output, "avi", "mp3", null, CancellationToken.None);

        Assert.Equal(["mp3"], await StreamCodecs(output));
    }

    [FfmpegFact]
    public async Task FileNames_WithSpecialCharacters_Work()
    {
        using var folder = new TemporaryFolder();
        var input = await MakeAudio(folder, "şğüİı [1] %d çözüm.wav");
        var output = folder.File("output.mp3");

        await Converter.ConvertAsync(input, output, "wav", "mp3", null, CancellationToken.None);

        Assert.Contains("mp3", await StreamCodecs(output));
    }

    [FfmpegFact]
    public async Task PlaylistRenamedToMp4_IsRefused()
    {
        using var folder = new TemporaryFolder();
        var secret = folder.File("secret.txt");
        File.WriteAllText(secret, "private");
        var input = folder.File("input.mp4");
        File.WriteAllText(input, $"#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:0\n#EXTINF:10.0,\n{secret}\n#EXT-X-ENDLIST\n");

        await Assert.ThrowsAsync<ToolFailedException>(() =>
            Converter.ConvertAsync(input, folder.File("output.mp3"), "mp4", "mp3", null, CancellationToken.None));
    }

    [FfmpegFact]
    public async Task Progress_MovesForwardDuringConversion()
    {
        using var folder = new TemporaryFolder();
        var input = await MakeAudio(folder, "input.wav", seconds: 30);
        var reports = new List<double>();

        await Converter.ConvertAsync(input, folder.File("output.flac"), "wav", "flac", new ImmediateProgress(reports.Add), CancellationToken.None);

        Assert.NotEmpty(reports);
        Assert.Equal(reports.Order(), reports);
        Assert.All(reports, value => Assert.InRange(value, 0, 0.99));
    }

    [Fact]
    public async Task MissingTools_FailInsteadOfDoingNothing()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("input.wav");
        File.WriteAllText(input, "x");

        await Assert.ThrowsAnyAsync<Exception>(() =>
            new FfmpegConverter(folder.File("no-tools")).ConvertAsync(input, folder.File("output.mp3"), "wav", "mp3", null, CancellationToken.None));

        Assert.False(File.Exists(folder.File("output.mp3")));
    }

    private static async Task<string> MakeAudio(TemporaryFolder folder, string name, int seconds = 1)
    {
        var path = folder.File(name);
        await ToolProcess.RunAsync(Ffmpeg, ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", $"sine=frequency=440:duration={seconds}", path]);
        return path;
    }

    /// <summary>Uses encoders built into FFmpeg, so the sample doesn't depend on what is being tested.</summary>
    private static async Task<string> MakeVideo(TemporaryFolder folder, string name, int width, int height)
    {
        var path = folder.File(name);
        await ToolProcess.RunAsync(Ffmpeg,
        [
            "-hide_banner", "-loglevel", "error",
            "-f", "lavfi", "-i", $"testsrc=size={width}x{height}:rate=10:duration=1",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
            "-c:v", "mpeg4", "-c:a", "pcm_s16le", path,
        ]);
        return path;
    }

    private static async Task<string[]> StreamCodecs(string path)
    {
        var output = await ToolProcess.RunAsync(Ffprobe, ["-v", "error", "-show_entries", "stream=codec_name", "-of", "csv=p=0", path]);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private sealed class ImmediateProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}

internal static class FfmpegFolder
{
    public static string Path { get; } = Find();

    public static bool IsSetUp => File.Exists(System.IO.Path.Combine(Path, "ffmpeg.exe"));

    public const string SkipReason = @"FFmpeg is not set up. Run scripts\setup-ffmpeg.ps1.";

    private static string Find()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(folder.FullName, "MrWConverter.slnx")))
            {
                return System.IO.Path.Combine(folder.FullName, "third_party", "ffmpeg", "win-x64");
            }
        }

        throw new DirectoryNotFoundException("Could not find the solution folder.");
    }
}

/// <summary>The binaries aren't in the repository; a fresh checkout shows these tests as skipped, not passed.</summary>
public sealed class FfmpegFactAttribute : FactAttribute
{
    public FfmpegFactAttribute()
    {
        if (!FfmpegFolder.IsSetUp)
        {
            Skip = FfmpegFolder.SkipReason;
        }
    }
}

public sealed class FfmpegTheoryAttribute : TheoryAttribute
{
    public FfmpegTheoryAttribute()
    {
        if (!FfmpegFolder.IsSetUp)
        {
            Skip = FfmpegFolder.SkipReason;
        }
    }
}
