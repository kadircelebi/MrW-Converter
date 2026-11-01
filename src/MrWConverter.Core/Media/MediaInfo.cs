using System.Globalization;
using System.Text.Json;

namespace MrWConverter.Core.Media;

/// <summary>What the tools need to know about a media file. The size is as the video appears, after any rotation.</summary>
public sealed record MediaInfo(double DurationSeconds, int Width, int Height, bool HasVideo, bool HasAudio)
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

        var (width, height) = hasVideo ? (Number(video, "width"), Number(video, "height")) : (0, 0);
        if (hasVideo && IsSideways(video))
        {
            (width, height) = (height, width);
        }

        return new MediaInfo(duration.Value, width, height, hasVideo, streams.Any(stream => IsCodecType(stream, "audio")));
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

    private static bool IsSideways(JsonElement stream)
    {
        if (!stream.TryGetProperty("side_data_list", out var sideData))
        {
            return false;
        }

        foreach (var item in sideData.EnumerateArray())
        {
            if (item.TryGetProperty("rotation", out var rotation) && rotation.TryGetDouble(out var degrees))
            {
                return Math.Abs(Math.Abs(degrees) % 180 - 90) < 1;
            }
        }

        return false;
    }
}
