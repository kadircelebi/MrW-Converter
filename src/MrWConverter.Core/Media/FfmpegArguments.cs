namespace MrWConverter.Core.Media;

/// <summary>Builds FFmpeg command lines. Kept free of process handling so it can be tested alone.</summary>
public static class FfmpegArguments
{
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
        "-hide_banner",
        "-nostdin",
        "-loglevel", "error",
        "-progress", "pipe:1",
        "-y",
        .. SafeInputOptions,
        "-i", input,
        .. OutputOptions(target),
        output,
    ];

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
