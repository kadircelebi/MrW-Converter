using System.Globalization;
using MrWConverter.Core.Tools;

namespace MrWConverter.Core.Media;

/// <summary>Builds FFmpeg command lines. Kept free of process handling so it can be tested alone.</summary>
public static class FfmpegArguments
{
    public const int PreviewSide = 1600;

    public static readonly IReadOnlySet<string> AudioTargets = new HashSet<string> { "mp3", "m4a", "wav", "flac", "ogg", "opus" };

    public static readonly IReadOnlySet<string> VideoTargets = new HashSet<string> { "mp4", "mov", "mkv", "webm", "gif" };

    // Windows' own encoders, used through FFmpeg: the LGPL build has no x264, and these
    // codecs are covered by the Windows license rather than by anything we distribute.
    private const string H264Encoder = "h264_mf";
    private const string AacEncoder = "aac_mf";

    // H.264 with 4:2:0 color needs even dimensions. Padding keeps every source pixel unscaled.
    private const string EvenDimensionsFilter = "pad=ceil(iw/2)*2:ceil(ih/2)*2";

    private const string GifFilter =
        "fps=15,scale='min(960,iw)':-2:flags=lanczos,split[frames][copy];[copy]palettegen[palette];[frames][palette]paletteuse";

    /// <summary>
    /// The only containers FFmpeg may open. Without this, a playlist renamed to .mp4 could
    /// make FFmpeg read other local files into the output or reach the network.
    /// </summary>
    private static readonly string[] SafeInputOptions =
    [
        "-protocol_whitelist", "file",
        "-format_whitelist", "mov,mp4,m4a,3gp,3g2,mj2,matroska,webm,avi,asf,mp3,wav,flac,ogg,aiff,gif",
    ];

    public static IReadOnlyList<string> ForDuration(string input) =>
    [
        "-v", "error",
        .. SafeInputOptions,
        "-show_entries", "format=duration",
        "-of", "csv=p=0",
        input,
    ];

    public static IReadOnlyList<string> ForConversion(string input, string output, string target) =>
    [
        .. Header,
        .. SafeInputOptions,
        "-i", input,
        .. OutputOptions(target),
        output,
    ];

    public static IReadOnlyList<string> ForMediaInfo(string input) =>
    [
        "-v", "error",
        .. SafeInputOptions,
        "-show_entries", "format=duration:stream=codec_type,width,height,duration:stream_side_data=rotation:stream_disposition=attached_pic",
        "-of", "json",
        input,
    ];

    /// <summary>The format a tool's result keeps: the source's, or the nearest one the app can write.</summary>
    public static string EditTarget(string source) => source switch
    {
        "avi" or "wmv" => "mp4",
        "aiff" => "flac",
        "wma" => "m4a",
        _ when AudioTargets.Contains(source) || VideoTargets.Contains(source) => source,
        _ => throw new ArgumentException($"No tool output for format {source}.", nameof(source)),
    };

    /// <summary>Re-encodes, so the cut lands on the chosen frame rather than the nearest keyframe.</summary>
    public static IReadOnlyList<string> ForTrim(string input, string output, string target, double start, double duration) =>
    [
        .. Header,
        "-ss", Time(start),
        "-t", Time(duration),
        .. SafeInputOptions,
        "-i", input,
        .. OutputOptions(target),
        output,
    ];

    public static IReadOnlyList<string> ForVideoCrop(string input, string output, string target, ImageRegion region) =>
    [
        .. Header,
        .. SafeInputOptions,
        "-i", input,
        .. WithVideoFilter(OutputOptions(target), CropFilter(region)),
        output,
    ];

    /// <summary>Copies the video stream, so nothing is re-encoded and no quality is lost.</summary>
    public static IReadOnlyList<string> ForMute(string input, string output, string format) =>
    [
        .. Header,
        .. SafeInputOptions,
        "-i", input,
        "-map", "0:v:0",
        "-c:v", "copy",
        "-an",
        .. format is "mp4" or "mov" ? new[] { "-movflags", "+faststart" } : [],
        output,
    ];

    /// <param name="maxSide">Shrinks the frame to fit, or null for its full size.</param>
    public static IReadOnlyList<string> ForFrame(string input, string output, double seconds, int? maxSide) =>
    [
        .. Header,
        "-ss", Time(seconds),
        .. SafeInputOptions,
        "-i", input,
        "-map", "0:v:0",
        .. maxSide is { } side
            ? new[] { "-vf", $"scale='min({side},iw)':'min({side},ih)':force_original_aspect_ratio=decrease" }
            : [],
        "-frames:v", "1",
        // Without this, a "%d" in the file name would be read as a numbering pattern.
        "-update", "1",
        output,
    ];

    public static IReadOnlyList<string> ForWaveform(string input, string output) =>
    [
        .. Header,
        .. SafeInputOptions,
        "-i", input,
        "-filter_complex", "[0:a:0]aformat=channel_layouts=mono,showwavespic=s=1400x240:colors=0x4CC2FF[wave]",
        "-map", "[wave]",
        "-frames:v", "1",
        "-update", "1",
        output,
    ];

    private static readonly string[] Header =
    [
        "-hide_banner",
        "-nostdin",
        "-loglevel", "error",
        "-progress", "pipe:1",
        "-y",
    ];

    private static string Time(double seconds) => seconds.ToString("0.###", CultureInfo.InvariantCulture);

    // 4:2:0 color needs even sizes. Rounding down keeps the result inside what the user chose.
    private static string CropFilter(ImageRegion region) => string.Create(
        CultureInfo.InvariantCulture,
        $"crop={Math.Max(2, region.Width & ~1)}:{Math.Max(2, region.Height & ~1)}:{region.X}:{region.Y}");

    /// <summary>Adds a filter in front of the ones the format already has.</summary>
    private static string[] WithVideoFilter(string[] options, string filter)
    {
        var existing = Array.IndexOf(options, "-vf");
        if (existing < 0)
        {
            return [.. options, "-vf", filter];
        }

        var combined = (string[])options.Clone();
        combined[existing + 1] = $"{filter},{options[existing + 1]}";
        return combined;
    }

    private static string[] OutputOptions(string target) => target switch
    {
        "mp3" => AudioOnly("-c:a", "libmp3lame", "-q:a", "2"),
        "m4a" => AudioOnly(Aac),
        "wav" => AudioOnly("-c:a", "pcm_s16le"),
        "flac" => AudioOnly("-c:a", "flac"),
        "ogg" => AudioOnly("-c:a", "libvorbis", "-q:a", "5"),
        "opus" => AudioOnly(Opus),
        "mp4" or "mov" => [.. H264Video, .. Aac, "-movflags", "+faststart"],
        "mkv" => [.. H264Video, .. Aac],
        "webm" => [.. Vp9Video, .. Opus],
        "gif" => ["-map", "0:v:0", "-an", "-vf", GifFilter],
        _ => throw new ArgumentException($"Unknown target format {target}.", nameof(target)),
    };

    private static string[] AudioOnly(params string[] codec) => ["-map", "0:a:0", "-vn", .. codec];

    private static readonly string[] Aac = ["-c:a", AacEncoder, "-b:a", "192k", "-ar", "48000", "-ac", "2"];

    private static readonly string[] Opus = ["-c:a", "libopus", "-b:a", "128k"];

    // "0:a:0?": a silent video is fine; the question mark makes the audio stream optional.
    private static readonly string[] H264Video =
    [
        "-map", "0:v:0", "-map", "0:a:0?",
        "-vf", EvenDimensionsFilter,
        // Measured on 720p and 1080p samples: 70 barely improves on 50, yet noisy video grows ninefold.
        "-c:v", H264Encoder, "-rate_control", "quality", "-quality", "50",
    ];

    private static readonly string[] Vp9Video =
    [
        "-map", "0:v:0", "-map", "0:a:0?",
        "-c:v", "libvpx-vp9", "-crf", "32", "-b:v", "0", "-row-mt", "1", "-deadline", "good", "-cpu-used", "4",
        "-pix_fmt", "yuv420p",
    ];
}
