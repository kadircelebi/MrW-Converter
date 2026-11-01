using System.Globalization;

namespace MrWConverter.Core.Media;

/// <summary>Turns the "-progress" lines FFmpeg prints into a fraction of the file's duration.</summary>
public sealed class FfmpegProgress(double? durationSeconds, IProgress<double>? progress)
{
    private const string Prefix = "out_time_us=";

    // The file is not finished until FFmpeg exits, so the bar stops just short of full.
    private const double MaxBeforeExit = 0.99;

    private double _last;

    public void ReadLine(string line)
    {
        if (progress is null || durationSeconds is not > 0 || !line.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return;
        }

        // FFmpeg always prints an invariant number, or "N/A" before the first frame.
        if (!double.TryParse(line.AsSpan(Prefix.Length), NumberStyles.Float, CultureInfo.InvariantCulture, out var microseconds))
        {
            return;
        }

        var fraction = Math.Min(MaxBeforeExit, microseconds / 1_000_000 / durationSeconds.Value);
        if (fraction > _last)
        {
            _last = fraction;
            progress.Report(fraction);
        }
    }

    public static double? ParseDuration(string ffprobeOutput) =>
        double.TryParse(ffprobeOutput.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
        && double.IsFinite(seconds)
        && seconds > 0
            ? seconds
            : null;
}
