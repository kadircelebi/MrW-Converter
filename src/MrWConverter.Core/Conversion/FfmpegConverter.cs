using MrWConverter.Core.Media;

namespace MrWConverter.Core.Conversion;

/// <summary>Converts audio and video with the FFmpeg that ships in <paramref name="toolsFolder"/>.</summary>
public sealed class FfmpegConverter(string toolsFolder) : IFileConverter
{
    private static readonly HashSet<string> Sources =
        ["mp3", "m4a", "wav", "flac", "ogg", "opus", "aiff", "wma", "mp4", "mov", "mkv", "webm", "avi", "wmv", "gif"];

    private string Ffmpeg => Path.Combine(toolsFolder, "ffmpeg.exe");

    private string Ffprobe => Path.Combine(toolsFolder, "ffprobe.exe");

    public bool CanConvert(string sourceFormat, string targetFormat) =>
        Sources.Contains(sourceFormat)
        && (FfmpegArguments.AudioTargets.Contains(targetFormat) || FfmpegArguments.VideoTargets.Contains(targetFormat));

    public async Task ConvertAsync(
        string inputPath,
        string outputPath,
        string sourceFormat,
        string targetFormat,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var durationOutput = await ToolProcess.RunAsync(Ffprobe, FfmpegArguments.ForDuration(inputPath), null, cancellationToken);
        var parser = new FfmpegProgress(FfmpegProgress.ParseDuration(durationOutput), progress);

        await ToolProcess.RunAsync(
            Ffmpeg,
            FfmpegArguments.ForConversion(inputPath, outputPath, targetFormat),
            parser.ReadLine,
            cancellationToken);
    }
}
