using MrWConverter.Core.Media;

namespace MrWConverter.Core.Tools;

/// <summary>
/// Audio and video tools, and what their windows need to show. It all goes through the
/// FFmpeg that ships in <paramref name="toolsFolder"/>, so previews work for every format
/// the app accepts, whatever codecs Windows happens to have.
/// </summary>
public sealed class MediaEditor(string toolsFolder)
{
    public const string TrimId = "trim";
    public const string CropVideoId = "cropVideo";
    public const string MuteVideoId = "muteVideo";
    public const string SnapshotId = "snapshot";

    public const double MinimumTrimSeconds = 0.1;

    // How far back to look when a time near the end has no frame left to show.
    private static readonly double[] FrameRetries = [0, 1, double.PositiveInfinity];

    private string Ffmpeg => Path.Combine(toolsFolder, "ffmpeg.exe");

    private string Ffprobe => Path.Combine(toolsFolder, "ffprobe.exe");

    public async Task<MediaInfo> ReadInfoAsync(string path, CancellationToken cancellationToken = default) =>
        MediaInfo.Parse(await ToolProcess.RunAsync(Ffprobe, FfmpegArguments.ForMediaInfo(path), null, cancellationToken));

    /// <summary>One frame as a PNG, at most <see cref="FfmpegArguments.PreviewSide"/> pixels on its longest side.</summary>
    public Task<byte[]> ReadFrameAsync(string path, double seconds, CancellationToken cancellationToken = default) =>
        ReadPngAsync(
            output => WriteFrameAsync(path, output, seconds, FfmpegArguments.PreviewSide, cancellationToken),
            cancellationToken);

    /// <summary>A picture of the sound, transparent around the wave.</summary>
    public Task<byte[]> ReadWaveformAsync(string path, CancellationToken cancellationToken = default) =>
        ReadPngAsync(
            output => ToolProcess.RunAsync(Ffmpeg, FfmpegArguments.ForWaveform(path, output), null, cancellationToken),
            cancellationToken);

    public IFileTool Trim(string sourceFormat, double start, double end)
    {
        var target = FfmpegArguments.EditTarget(sourceFormat);
        return new MediaTool(
            TrimId,
            stem => $"{stem}-trim.{target}",
            end - start,
            (input, output, parser, token) => ToolProcess.RunAsync(Ffmpeg, FfmpegArguments.ForTrim(input, output, target, start, end - start), parser, token));
    }

    public IFileTool CropVideo(string sourceFormat, ImageRegion region, double durationSeconds)
    {
        var target = FfmpegArguments.EditTarget(sourceFormat);
        return new MediaTool(
            CropVideoId,
            stem => $"{stem}-crop.{target}",
            durationSeconds,
            (input, output, parser, token) => ToolProcess.RunAsync(Ffmpeg, FfmpegArguments.ForVideoCrop(input, output, target, region), parser, token));
    }

    public IFileTool Mute(string sourceFormat, double durationSeconds) => new MediaTool(
        MuteVideoId,
        stem => $"{stem}-muted.{sourceFormat}",
        durationSeconds,
        (input, output, parser, token) => ToolProcess.RunAsync(Ffmpeg, FfmpegArguments.ForMute(input, output, sourceFormat), parser, token));

    public IFileTool Snapshot(double seconds) => new MediaTool(
        SnapshotId,
        stem => $"{stem}-frame.png",
        null,
        (input, output, _, token) => WriteFrameAsync(input, output, seconds, null, token));

    private static async Task<byte[]> ReadPngAsync(Func<string, Task> write, CancellationToken cancellationToken)
    {
        // A file rather than the output stream, because ToolProcess reads output as text.
        var output = Path.Combine(Path.GetTempPath(), $"mrwconverter-{Guid.NewGuid():N}.png");

        try
        {
            await write(output);
            return await File.ReadAllBytesAsync(output, cancellationToken);
        }
        finally
        {
            File.Delete(output);
        }
    }

    private async Task WriteFrameAsync(string input, string output, double seconds, int? maxSide, CancellationToken cancellationToken)
    {
        foreach (var back in FrameRetries)
        {
            var time = double.IsInfinity(back) ? 0 : Math.Max(0, seconds - back);
            await ToolProcess.RunAsync(Ffmpeg, FfmpegArguments.ForFrame(input, output, time, maxSide), null, cancellationToken);

            // FFmpeg exits happily with no file when no frame follows the time it was given.
            if (File.Exists(output) && new FileInfo(output).Length > 0)
            {
                return;
            }
        }

        throw new InvalidDataException("The file has no frame to show.");
    }

    private sealed class MediaTool(
        string id,
        Func<string, string> outputName,
        double? durationSeconds,
        Func<string, string, Action<string>, CancellationToken, Task> run) : IFileTool
    {
        public string Id => id;

        public string OutputName(string inputStem) => outputName(inputStem);

        public Task RunAsync(string inputPath, string outputPath, string sourceFormat, IProgress<double>? progress, CancellationToken cancellationToken) =>
            run(inputPath, outputPath, new FfmpegProgress(durationSeconds, progress).ReadLine, cancellationToken);
    }
}
