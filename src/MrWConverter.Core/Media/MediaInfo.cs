using System.Globalization;
using System.Text.Json;

namespace MrWConverter.Core.Media;

/// <summary>What the tools need to know about a media file. The size is as the video appears, after any rotation.</summary>
/// <param name="FramesPerSecond">Zero when the file doesn't say.</param>
/// <param name="Rotation">The file's own rotation note, as FFmpeg reports it: counterclockwise degrees.</param>
public sealed record MediaInfo(double DurationSeconds, int Width, int Height, bool HasVideo, bool HasAudio, double FramesPerSecond = 0, double Rotation = 0)
{
    /// <summary>Reads the JSON that <see cref="FfmpegArguments.ForMediaInfo"/> makes ffprobe print.</summary>
    public static MediaInfo Parse(string ffprobeJson)
    {
        using var document = JsonDocument.Parse(ffprobeJson);
        var root = document.RootElement;
        var streams = root.TryGetProperty("streams", out var list) ? list.EnumerateArray().ToArray() : [];

        // A cover picture in a music file reports itself as a video stream.
        var video = streams.FirstOrDefault(stream => IsCodecType(stream, "video") && !IsAttachedPicture(stream));
        var hasVideo = video.ValueKind == JsonValueKind.Object;

        var duration = root.TryGetProperty("format", out var format) ? Seconds(format) : null;
        duration ??= streams.Select(Seconds).Max();

        if (duration is not > 0)
        {
            throw new InvalidDataException("The file has no readable duration.");
        }

        var rotation = hasVideo ? RotationOf(video) : 0;
        var (width, height) = hasVideo ? (Number(video, "width"), Number(video, "height")) : (0, 0);
        if (Math.Abs(Math.Abs(rotation) % 180 - 90) < 1)
        {
            (width, height) = (height, width);
        }

        return new MediaInfo(
            duration.Value,
            width,
            height,
            hasVideo,
            streams.Any(stream => IsCodecType(stream, "audio")),
            hasVideo ? FrameRate(video) : 0,
            rotation);
    }

    /// <summary>FFmpeg writes it as a fraction, such as "30000/1001", or "0/0" when unknown.</summary>
    private static double FrameRate(JsonElement stream)
    {
        var parts = (stream.TryGetProperty("avg_frame_rate", out var value) ? value.GetString() ?? "" : "").Split('/');

        return parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var frames)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            && seconds > 0
                ? frames / seconds
                : 0;
    }

    private static bool IsCodecType(JsonElement stream, string type) =>
        stream.TryGetProperty("codec_type", out var value) && value.GetString() == type;

    private static bool IsAttachedPicture(JsonElement stream) =>
        stream.TryGetProperty("disposition", out var disposition)
        && disposition.TryGetProperty("attached_pic", out var value)
        && value.GetInt32() == 1;

    private static double? Seconds(JsonElement element) =>
        element.TryGetProperty("duration", out var value)
        && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
        && double.IsFinite(seconds)
            ? seconds
            : null;

    private static int Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetInt32() : 0;

    private static double RotationOf(JsonElement stream)
    {
        if (stream.TryGetProperty("side_data_list", out var sideData))
        {
            foreach (var item in sideData.EnumerateArray())
            {
                if (item.TryGetProperty("rotation", out var rotation) && rotation.TryGetDouble(out var degrees))
                {
                    return degrees;
                }
            }
        }

        return 0;
    }
}
