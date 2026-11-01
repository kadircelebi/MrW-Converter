using System.Globalization;
using MrWConverter.Core.Tools;

namespace MrWConverter.Core.Media;

/// <summary>
/// The settings of one attempt to shrink a video. Windows' H.264 encoder ignores a bitrate too
/// low for the picture size (asked for 2 Mbps at 1080p, it made 10 on a busy video), so a small
/// target has to bring the picture size down with it, or it can never be met.
/// </summary>
/// <param name="VideoBitrate">Bits per second, or null to keep the format's usual quality.</param>
/// <param name="AudioBitrate">Bits per second, zero when there is no sound.</param>
/// <param name="FramesPerSecond">A lower frame rate to use, or null to keep the original's.</param>
/// <param name="ScaleTo">The filter that resizes the picture, or null when its size stays.</param>
public sealed record VideoShrinkPlan(int Width, int Height, int? VideoBitrate, int AudioBitrate, double? FramesPerSecond, string? ScaleTo = null)
{
    // The container's own bytes, and what the encoder may overshoot by.
    private const double ContainerHeadroom = 0.93;

    // Measured with that encoder: below this, it stops following the bitrate on busy video.
    private const double LowestBitsPerPixel = 0.08;

    private const int LowestVideoBitrate = 60_000;
    private const int SmallestSide = 160;
    private const double CappedFrameRate = 30;
    private const double AssumedFrameRate = 30;

    // The lowest bitrate Windows' AAC encoder has, and what it takes to sound like the original.
    private const int LowAudioBitrate = 96_000;
    private const int UsualAudioBitrate = 128_000;
    private const int UsualAudioFromTotal = 1_000_000;

    // A miss this small can be down to the bitrate alone. A larger one can mean the encoder ignored it.
    private const double BitrateOnlyMiss = 1.15;
    private const double RetryHeadroom = 0.95;

    private static readonly int[] CommonSides = [1920, 1280, 854, 640, 426];

    /// <summary>Keeps the format's usual quality, with only the picture size changing.</summary>
    public static VideoShrinkPlan KeepingQuality(MediaInfo info, int? maxSide)
    {
        var (width, height) = FitTo(info, Math.Min(maxSide ?? int.MaxValue, Math.Max(info.Width, info.Height)));
        return new VideoShrinkPlan(width, height, null, 0, null, ScaleFilter(info, width, height));
    }

    /// <summary>The largest picture that this many bytes can carry at a watchable quality.</summary>
    public static VideoShrinkPlan For(MediaInfo info, long limitBytes, int? maxSide)
    {
        var total = limitBytes * 8 * ContainerHeadroom / info.DurationSeconds;
        var audio = !info.HasAudio ? 0 : total < UsualAudioFromTotal ? LowAudioBitrate : UsualAudioBitrate;
        var video = total - audio;

        if (video < LowestVideoBitrate)
        {
            throw new CannotShrinkException($"{limitBytes} bytes is too small for {info.DurationSeconds} seconds of video.");
        }

        var longest = Math.Min(maxSide ?? int.MaxValue, Math.Max(info.Width, info.Height));
        var rate = info.FramesPerSecond > 0 ? info.FramesPerSecond : AssumedFrameRate;
        var capped = rate > CappedFrameRate ? CappedFrameRate : (double?)null;

        var sides = CommonSides.Where(side => side < longest).Prepend(longest).ToArray();
        foreach (var side in sides)
        {
            var (width, height) = FitTo(info, side);

            if (video / (width * height * rate) >= LowestBitsPerPixel)
            {
                return Plan(info, width, height, video, audio, null);
            }

            if (capped is not null && video / (width * height * capped.Value) >= LowestBitsPerPixel)
            {
                return Plan(info, width, height, video, audio, capped);
            }
        }

        var (smallestWidth, smallestHeight) = FitTo(info, sides[^1]);
        return Plan(info, smallestWidth, smallestHeight, video, audio, capped);
    }

    /// <summary>The next attempt, after this one came out at <paramref name="measuredBytes"/>.</summary>
    public VideoShrinkPlan Lower(MediaInfo info, double measuredBytes, long limitBytes)
    {
        var share = limitBytes / measuredBytes * RetryHeadroom;
        var video = (VideoBitrate!.Value + AudioBitrate) * share - AudioBitrate;

        var (width, height) = (Width, Height);
        if (measuredBytes > limitBytes * BitrateOnlyMiss)
        {
            var scale = Math.Sqrt(share);
            (width, height) = (Even(Width * scale), Even(Height * scale));
        }

        if (video < LowestVideoBitrate || Math.Max(width, height) < SmallestSide)
        {
            throw new CannotShrinkException($"{limitBytes} bytes is too small for this video.");
        }

        return Plan(info, width, height, video, AudioBitrate, FramesPerSecond);
    }

    private static VideoShrinkPlan Plan(MediaInfo info, int width, int height, double video, int audio, double? framesPerSecond) =>
        new(width, height, (int)video, audio, framesPerSecond, ScaleFilter(info, width, height));

    private static (int Width, int Height) FitTo(MediaInfo info, int side)
    {
        var longest = Math.Max(info.Width, info.Height);
        if (side >= longest)
        {
            return (info.Width, info.Height);
        }

        var scale = (double)side / longest;
        return (Even(info.Width * scale), Even(info.Height * scale));
    }

    private static string? ScaleFilter(MediaInfo info, int width, int height) =>
        (width, height) == (info.Width, info.Height)
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"scale={width}:{height}:flags=lanczos");

    // 4:2:0 color needs even sizes.
    private static int Even(double size) => Math.Max(2, (int)Math.Round(size / 2) * 2);
}
