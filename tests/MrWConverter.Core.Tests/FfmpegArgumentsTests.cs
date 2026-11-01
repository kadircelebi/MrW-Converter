using System.Globalization;
using MrWConverter.Core.Media;

namespace MrWConverter.Core.Tests;

public class FfmpegArgumentsTests
{
    private const string Input = @"C:\in\a.mp4";
    private const string Output = @"C:\out\a.out";

    public static TheoryData<string> AllTargets()
    {
        var targets = new TheoryData<string>();
        foreach (var target in FfmpegArguments.AudioTargets.Concat(FfmpegArguments.VideoTargets))
        {
            targets.Add(target);
        }

        return targets;
    }

    [Theory]
    [MemberData(nameof(AllTargets))]
    public void ForConversion_RestrictsInputBeforeItIsOpened(string target)
    {
        var arguments = FfmpegArguments.ForConversion(Input, Output, target).ToList();

        var input = arguments.IndexOf("-i");
        Assert.InRange(arguments.IndexOf("-protocol_whitelist"), 0, input - 1);
        Assert.InRange(arguments.IndexOf("-format_whitelist"), 0, input - 1);
        Assert.Equal(Input, arguments[input + 1]);
        Assert.Equal(Output, arguments[^1]);
    }

    [Fact]
    public void Whitelists_DoNotAllowPlaylistsOrNetworkProtocols()
    {
        var arguments = FfmpegArguments.ForDuration(Input).ToList();

        Assert.Equal("file", arguments[arguments.IndexOf("-protocol_whitelist") + 1]);
        var formats = arguments[arguments.IndexOf("-format_whitelist") + 1].Split(',');
        Assert.DoesNotContain("hls", formats);
        Assert.DoesNotContain("concat", formats);
        Assert.DoesNotContain("image2", formats);
    }

    [Theory]
    [InlineData("mp3")]
    [InlineData("m4a")]
    [InlineData("wav")]
    [InlineData("flac")]
    [InlineData("ogg")]
    [InlineData("opus")]
    public void AudioTargets_LeaveOutVideoSuchAsCoverArt(string target)
    {
        var arguments = FfmpegArguments.ForConversion(Input, Output, target);

        Assert.Contains("-vn", arguments);
        Assert.DoesNotContain("0:v:0", arguments);
    }

    [Theory]
    [InlineData("mp4")]
    [InlineData("mov")]
    [InlineData("mkv")]
    public void H264Targets_MakeDimensionsEven(string target)
    {
        var arguments = FfmpegArguments.ForConversion(Input, Output, target).ToList();

        Assert.Contains("ceil(iw/2)*2", arguments[arguments.IndexOf("-vf") + 1]);
    }

    [Fact]
    public void NoTarget_UsesAGplEncoder()
    {
        foreach (var target in FfmpegArguments.AudioTargets.Concat(FfmpegArguments.VideoTargets))
        {
            var arguments = FfmpegArguments.ForConversion(Input, Output, target);

            Assert.DoesNotContain("libx264", arguments);
            Assert.DoesNotContain("libx265", arguments);
        }
    }

    [Fact]
    public void ForConversion_RejectsAnUnknownTarget()
    {
        Assert.Throws<ArgumentException>(() => FfmpegArguments.ForConversion(Input, Output, "exe"));
    }

    [Theory]
    [InlineData("12.5\r\n", 12.5)]
    [InlineData("N/A\r\n", null)]
    [InlineData("", null)]
    [InlineData("0", null)]
    [InlineData("nan", null)]
    public void ParseDuration_ReadsFfprobeOutput(string output, double? expected)
    {
        Assert.Equal(expected, FfmpegProgress.ParseDuration(output));
    }

    [Fact]
    public void Progress_IsNotAffectedByTurkishCulture()
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
        try
        {
            // Under tr-TR "," is the decimal separator, and "." would be read as a thousands separator.
            Assert.Equal(12.5, FfmpegProgress.ParseDuration("12.5"));

            var reports = new List<double>();
            new FfmpegProgress(10, new ImmediateProgress(reports.Add)).ReadLine("out_time_us=5000000");
            Assert.Equal([0.5], reports);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Progress_OnlyMovesForwardAndStopsShortOfFull()
    {
        var reports = new List<double>();
        var parser = new FfmpegProgress(10, new ImmediateProgress(reports.Add));

        parser.ReadLine("frame=12");
        parser.ReadLine("out_time_us=N/A");
        parser.ReadLine("out_time_us=5000000");
        parser.ReadLine("out_time_us=2000000");
        parser.ReadLine("out_time_us=99000000");

        Assert.Equal([0.5, 0.99], reports);
    }

    [Fact]
    public void Progress_WithoutADuration_ReportsNothing()
    {
        var reports = new List<double>();

        new FfmpegProgress(null, new ImmediateProgress(reports.Add)).ReadLine("out_time_us=5000000");

        Assert.Empty(reports);
    }

    private sealed class ImmediateProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
