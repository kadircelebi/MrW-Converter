using System.Globalization;
using MrWConverter.Core.Media;

namespace MrWConverter.Core.Tools;

/// <summary>
/// Makes a video fit a size, as close to its format as the app can write. Encoders only
/// approximate a bitrate, so each result is measured, and missing the limit means another try.
/// </summary>
public sealed class VideoShrinker(string toolsFolder, ShrinkOptions options, string sourceFormat) : IFileTool
{
    private const int MaxAttempts = 4;

    // Once this much is encoded, the size so far says enough to give up on an attempt that won't fit.
    private const double JudgeFrom = 0.2;
    private const double GiveUpOver = 1.25;

    private const string WrittenPrefix = "total_size=";
    private const string TimePrefix = "out_time_us=";

    private readonly string _target = FfmpegArguments.EditTarget(sourceFormat);

    public string Id => ShrinkOptions.ToolId;

    public string OutputName(string inputStem) => $"{inputStem}-small.{_target}";

    public async Task RunAsync(string inputPath, string outputPath, string sourceFormat, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var info = MediaInfo.Parse(await ToolProcess.RunAsync(Path.Combine(toolsFolder, "ffprobe.exe"), FfmpegArguments.ForMediaInfo(inputPath), null, cancellationToken));
        if (!info.HasVideo)
        {
            throw new InvalidDataException("The file has no video.");
        }

        var sourceBytes = new FileInfo(inputPath).Length;
        var limit = options.LimitFor(sourceBytes);
        var resizes = options.Resizes(Math.Max(info.Width, info.Height));

        if (_target == sourceFormat && !resizes && sourceBytes <= limit)
        {
            File.Copy(inputPath, outputPath, overwrite: true);
            return;
        }

        var bar = new AttemptsProgress(progress);
        var plan = limit < sourceBytes
            ? VideoShrinkPlan.For(info, limit, options.MaxSide)
            : VideoShrinkPlan.KeepingQuality(info, options.MaxSide);

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            bar.StartAttempt();
            var measured = await EncodeAsync(inputPath, outputPath, plan, info, limit, bar, cancellationToken);

            if (measured <= limit)
            {
                return;
            }

            plan = plan.VideoBitrate is null
                ? VideoShrinkPlan.For(info, limit, options.MaxSide)
                : plan.Lower(info, measured, limit);
        }

        throw new CannotShrinkException($"Could not get this video under {limit} bytes.");
    }

    /// <summary>The finished size, or the expected one for an attempt that was stopped early.</summary>
    private async Task<double> EncodeAsync(string inputPath, string outputPath, VideoShrinkPlan plan, MediaInfo info, long limit, IProgress<double> progress, CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var parser = new FfmpegProgress(info.DurationSeconds, progress);
        var written = 0d;
        var expected = 0d;

        void ReadLine(string line)
        {
            parser.ReadLine(line);

            if (Number(line, WrittenPrefix) is { } size)
            {
                written = size;
            }
            else if (Number(line, TimePrefix) is { } microseconds)
            {
                var done = microseconds / 1_000_000 / info.DurationSeconds;

                if (done >= JudgeFrom && written / done > limit * GiveUpOver && !stop.IsCancellationRequested)
                {
                    expected = written / done;
                    stop.Cancel();
                }
            }
        }

        try
        {
            await ToolProcess.RunAsync(Path.Combine(toolsFolder, "ffmpeg.exe"), FfmpegArguments.ForShrink(inputPath, outputPath, _target, plan), ReadLine, stop.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return expected;
        }

        return new FileInfo(outputPath).Length;
    }

    private static double? Number(string line, string prefix) =>
        line.StartsWith(prefix, StringComparison.Ordinal)
        && double.TryParse(line.AsSpan(prefix.Length), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    /// <summary>Keeps the bar moving forward when an attempt has to start over.</summary>
    private sealed class AttemptsProgress(IProgress<double>? job) : IProgress<double>
    {
        // Most of the bar goes to the next attempt, which is usually the last.
        private const double AttemptShare = 0.7;

        private double _start;
        private double _reached;

        public void StartAttempt() => _start = _reached;

        public void Report(double value)
        {
            _reached = _start + (1 - _start) * AttemptShare * value;
            job?.Report(_reached);
        }
    }
}
