using System.Globalization;
using MrWConverter.Core.Media;
using MrWConverter.Core.Tools;

namespace MrWConverter.Core.Tests;

public class MediaInfoTests
{
    [Fact]
    public void Video_ReportsItsSizeDurationAndSound()
    {
        var info = MediaInfo.Parse("""
            { "streams": [
                { "codec_type": "video", "width": 1920, "height": 1080, "disposition": { "attached_pic": 0 } },
                { "codec_type": "audio", "disposition": { "attached_pic": 0 } } ],
              "format": { "duration": "12.500000" } }
            """);

        Assert.Equal(new MediaInfo(12.5, 1920, 1080, true, true), info);
    }

    [Theory]
    [InlineData("30000/1001", 29.97)]
    [InlineData("25/1", 25)]
    [InlineData("0/0", 0)]
    public void FrameRate_IsReadFromItsFraction(string fraction, double expected)
    {
        var info = MediaInfo.Parse($$"""
            { "streams": [ { "codec_type": "video", "width": 2, "height": 2, "avg_frame_rate": "{{fraction}}" } ],
              "format": { "duration": "1" } }
            """);

        Assert.Equal(expected, info.FramesPerSecond, 2);
    }

    [Theory]
    [InlineData(90)]
    [InlineData(-90)]
    [InlineData(270)]
    public void SidewaysVideo_ReportsTheSizeItAppearsAt(int degrees)
    {
        var info = MediaInfo.Parse($$"""
            { "streams": [ { "codec_type": "video", "width": 1920, "height": 1080,
                             "side_data_list": [ { "rotation": {{degrees}} } ] } ],
              "format": { "duration": "1" } }
            """);

        Assert.Equal((1080, 1920), (info.Width, info.Height));
        Assert.Equal(degrees, info.Rotation);
    }

    [Fact]
    public void UpsideDownVideo_KeepsItsSize()
    {
        var info = MediaInfo.Parse("""
            { "streams": [ { "codec_type": "video", "width": 1920, "height": 1080,
                             "side_data_list": [ { "rotation": 180 } ] } ],
              "format": { "duration": "1" } }
            """);

        Assert.Equal((1920, 1080), (info.Width, info.Height));
    }

    [Fact]
    public void CoverPictureInMusic_IsNotVideo()
    {
        var info = MediaInfo.Parse("""
            { "streams": [
                { "codec_type": "audio", "disposition": { "attached_pic": 0 } },
                { "codec_type": "video", "width": 500, "height": 500, "disposition": { "attached_pic": 1 } } ],
              "format": { "duration": "3" } }
            """);

        Assert.False(info.HasVideo);
        Assert.True(info.HasAudio);
    }

    [Fact]
    public void MissingFormatDuration_FallsBackToTheLongestStream()
    {
        var info = MediaInfo.Parse("""
            { "streams": [ { "codec_type": "audio", "duration": "2.0" }, { "codec_type": "video", "width": 2, "height": 2, "duration": "4.0" } ],
              "format": { } }
            """);

        Assert.Equal(4.0, info.DurationSeconds);
    }

    [Theory]
    [InlineData("""{ "streams": [], "format": { } }""")]
    [InlineData("""{ "streams": [], "format": { "duration": "N/A" } }""")]
    [InlineData("""{ "streams": [], "format": { "duration": "0" } }""")]
    public void NoDuration_IsRefused(string json)
    {
        Assert.Throws<InvalidDataException>(() => MediaInfo.Parse(json));
    }

    [Fact]
    public void Trim_WritesTimesTheSameWayUnderTurkishCulture()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

        try
        {
            var arguments = FfmpegArguments.ForTrim("in.mp4", "out.mp4", "mp4", 1.5, 2.25);

            Assert.Equal("1.5", arguments[arguments.ToList().IndexOf("-ss") + 1]);
            Assert.Equal("2.25", arguments[arguments.ToList().IndexOf("-t") + 1]);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Theory]
    [InlineData("mp4", "mp4")]
    [InlineData("webm", "webm")]
    [InlineData("avi", "mp4")]
    [InlineData("wmv", "mp4")]
    [InlineData("mp3", "mp3")]
    [InlineData("aiff", "flac")]
    [InlineData("wma", "m4a")]
    public void EditTarget_KeepsTheFormatOrPicksTheNearest(string source, string target)
    {
        Assert.Equal(target, FfmpegArguments.EditTarget(source));
    }

    [Fact]
    public void VideoCrop_RoundsToEvenSizesAndKeepsTheFormatsOwnFilter()
    {
        var arguments = FfmpegArguments.ForVideoCrop("in.mp4", "out.mp4", "mp4", new ImageRegion(10, 20, 101, 51)).ToList();

        var filter = arguments[arguments.IndexOf("-vf") + 1];
        Assert.StartsWith("crop=100:50:10:20,pad=", filter);
    }

    [Fact]
    public void VideoCrop_ToWebmAddsTheFilter()
    {
        var arguments = FfmpegArguments.ForVideoCrop("in.webm", "out.webm", "webm", new ImageRegion(0, 0, 100, 50)).ToList();

        Assert.Equal("crop=100:50:0:0", arguments[arguments.IndexOf("-vf") + 1]);
    }
}
