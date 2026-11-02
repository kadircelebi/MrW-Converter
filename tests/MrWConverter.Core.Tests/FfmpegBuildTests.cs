using MrWConverter.Core.Conversion;
using MrWConverter.Core.Media;

namespace MrWConverter.Core.Tests;

/// <summary>
/// The FFmpeg the app ships holds only what the app uses, so a codec left out would surface only as a
/// user's failed file. Samples come from the full FFmpeg in third_party\ffmpeg\fixtures-win-x64.
/// </summary>
public class FfmpegBuildTests
{
    private static readonly string Tools = FfmpegFolder.Path;
    private static readonly string Shipped = Path.Combine(Tools, "ffmpeg.exe");
    private static readonly string Maker = Path.Combine(FfmpegFolder.FixturesPath, "ffmpeg.exe");

    private static readonly string[] Picture = ["-f", "lavfi", "-i", "testsrc=size=320x240:rate=10:duration=1"];
    private static readonly string[] Sound = ["-f", "lavfi", "-i", "sine=frequency=440:duration=1"];

    private static FfmpegConverter Converter => new(Tools);

    [FfmpegTheory]
    [InlineData("h264.mp4", "mp4", "-c:v h264_mf -c:a aac_mf -ar 48000 -ac 2")]
    [InlineData("hevc.mp4", "mp4", "-c:v libkvazaar -c:a aac_mf -ar 48000 -ac 2")]
    [InlineData("av1.mp4", "mp4", "-c:v libaom-av1 -cpu-used 8 -c:a aac_mf -ar 48000 -ac 2")]
    [InlineData("mpeg4.mp4", "mp4", "-c:v mpeg4 -c:a alac")]
    [InlineData("mjpeg.mov", "mov", "-c:v mjpeg -c:a pcm_s16le")]
    [InlineData("prores.mov", "mov", "-c:v prores_ks -c:a pcm_s24le")]
    [InlineData("qtrle.mov", "mov", "-c:v qtrle -c:a aac_mf -ar 48000 -ac 2")]
    [InlineData("h264.mkv", "mkv", "-c:v h264_mf -c:a ac3")]
    [InlineData("mpeg2.mkv", "mkv", "-c:v mpeg2video -c:a mp2")]
    [InlineData("theora.mkv", "mkv", "-c:v libtheora -c:a flac")]
    [InlineData("hevc.mkv", "mkv", "-c:v libkvazaar -c:a eac3")]
    [InlineData("ffv1.mkv", "mkv", "-c:v ffv1 -c:a pcm_s16le")]
    [InlineData("vp8.webm", "webm", "-c:v libvpx -c:a libvorbis")]
    [InlineData("vp9.webm", "webm", "-c:v libvpx-vp9 -deadline realtime -cpu-used 8 -c:a libopus")]
    [InlineData("av1.webm", "webm", "-c:v libaom-av1 -cpu-used 8 -c:a libopus")]
    [InlineData("mpeg4.avi", "avi", "-c:v mpeg4 -c:a libmp3lame")]
    [InlineData("msmpeg4.avi", "avi", "-c:v msmpeg4v3 -c:a pcm_s16le")]
    [InlineData("mjpeg.avi", "avi", "-c:v mjpeg -c:a adpcm_ms")]
    [InlineData("h264.avi", "avi", "-c:v h264_mf -c:a ac3")]
    [InlineData("wmv1.wmv", "wmv", "-c:v wmv1 -c:a wmav1")]
    [InlineData("wmv2.wmv", "wmv", "-c:v wmv2 -c:a wmav2")]
    public async Task EveryCommonVideo_Converts(string name, string format, string codecs)
    {
        using var folder = new TemporaryFolder();
        var input = await Make(folder, name, [.. Picture, .. Sound], codecs);

        var output = folder.File("out.mp4");
        await Converter.ConvertAsync(input, output, format, "mp4", null, default);

        var info = await ReadInfo(output);
        Assert.True(info.HasVideo, name);
        Assert.True(info.HasAudio, name);
        Assert.InRange(info.DurationSeconds, 0.5, 2);
    }

    [FfmpegTheory]
    [InlineData("a.mp3", "mp3", "-c:a libmp3lame")]
    [InlineData("a.m4a", "m4a", "-c:a aac_mf -ar 48000 -ac 2")]
    [InlineData("alac.m4a", "m4a", "-c:a alac")]
    [InlineData("a.wav", "wav", "-c:a pcm_s16le")]
    [InlineData("s24.wav", "wav", "-c:a pcm_s24le")]
    [InlineData("float.wav", "wav", "-c:a pcm_f32le")]
    [InlineData("adpcm.wav", "wav", "-c:a adpcm_ima_wav")]
    [InlineData("a.flac", "flac", "-c:a flac")]
    [InlineData("a.ogg", "ogg", "-c:a libvorbis")]
    [InlineData("opus.ogg", "ogg", "-c:a libopus")]
    [InlineData("a.opus", "opus", "-c:a libopus")]
    [InlineData("a.aiff", "aiff", "-c:a pcm_s16be")]
    [InlineData("a.wma", "wma", "-c:a wmav2")]
    public async Task EveryCommonSound_Converts(string name, string format, string codecs)
    {
        using var folder = new TemporaryFolder();
        var input = await Make(folder, name, Sound, codecs);

        var output = folder.File("out.flac");
        await Converter.ConvertAsync(input, output, format, "flac", null, default);

        Assert.True((await ReadInfo(output)).HasAudio, name);
    }

    [FfmpegFact]
    public async Task Mp3WithCoverArt_Converts()
    {
        using var folder = new TemporaryFolder();
        var cover = await Make(folder, "cover.png", ["-f", "lavfi", "-i", "color=c=red:size=64x64:d=1"], "-frames:v 1 -update 1");
        var input = folder.File("a.mp3");
        await ToolProcess.RunAsync(Maker,
        [
            "-hide_banner", "-loglevel", "error", "-y", .. Sound, "-i", cover,
            "-map", "0:a", "-map", "1:v", "-c:a", "libmp3lame", "-c:v", "copy", "-disposition:v", "attached_pic", input,
        ]);

        var output = folder.File("out.flac");
        await Converter.ConvertAsync(input, output, "mp3", "flac", null, default);

        Assert.True((await ReadInfo(output)).HasAudio);
    }

    [FfmpegFact]
    public async Task Gif_Converts()
    {
        using var folder = new TemporaryFolder();
        var input = await Make(folder, "a.gif", Picture, "");

        var output = folder.File("out.mp4");
        await Converter.ConvertAsync(input, output, "gif", "mp4", null, default);

        Assert.True((await ReadInfo(output)).HasVideo);
    }

    [FfmpegTheory]
    [InlineData("mov")]
    [InlineData("mkv")]
    [InlineData("webm")]
    [InlineData("gif")]
    [InlineData("mp3")]
    [InlineData("m4a")]
    public async Task Video_ReachesEveryTarget(string target)
    {
        using var folder = new TemporaryFolder();
        var input = await Make(folder, "a.mp4", [.. Picture, .. Sound], "-c:v h264_mf -c:a aac_mf -ar 48000 -ac 2");

        var output = folder.File($"out.{target}");
        await Converter.ConvertAsync(input, output, "mp4", target, null, default);

        var info = await ReadInfo(output);
        Assert.Equal(target is not ("mp3" or "m4a"), info.HasVideo);
        Assert.Equal(target is not "gif", info.HasAudio);
    }

    [FfmpegTheory]
    [InlineData("mp3")]
    [InlineData("m4a")]
    [InlineData("wav")]
    [InlineData("flac")]
    [InlineData("ogg")]
    [InlineData("opus")]
    public async Task Sound_ReachesEveryTarget(string target)
    {
        using var folder = new TemporaryFolder();
        var input = await Make(folder, "in.wav", Sound, "-c:a pcm_s16le");

        var output = folder.File($"out.{target}");
        await Converter.ConvertAsync(input, output, "wav", target, null, default);

        Assert.True((await ReadInfo(output)).HasAudio);
    }

    [FfmpegFact]
    public async Task License_IsLgpl21WithNothingStronger()
    {
        var printed = await ToolProcess.RunAsync(Shipped, ["-hide_banner", "-L"]);
        var license = string.Join(' ', printed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var configuration = await ToolProcess.RunAsync(Shipped, ["-hide_banner", "-buildconf"]);

        Assert.Contains("GNU Lesser General Public License", license);
        Assert.Contains("version 2.1", license);
        Assert.DoesNotContain("--enable-gpl", configuration);
        Assert.DoesNotContain("--enable-nonfree", configuration);
        Assert.DoesNotContain("--enable-version3", configuration);
    }

    [FfmpegFact]
    public async Task Build_CannotReachTheNetwork()
    {
        var protocols = await ToolProcess.RunAsync(Shipped, ["-hide_banner", "-protocols"]);

        Assert.DoesNotContain("http", protocols);
        Assert.DoesNotContain("tcp", protocols);
        Assert.DoesNotContain("rtmp", protocols);
    }

    private static Task<MediaInfo> ReadInfo(string path) => new MrWConverter.Core.Tools.MediaEditor(Tools).ReadInfoAsync(path);

    private static async Task<string> Make(TemporaryFolder folder, string name, string[] sources, string codecs)
    {
        var path = folder.File(name);
        await ToolProcess.RunAsync(Maker,
        [
            "-hide_banner", "-loglevel", "error", "-y", .. sources,
            .. codecs.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            path,
        ]);
        return path;
    }
}
