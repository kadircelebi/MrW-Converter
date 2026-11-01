namespace MrWConverter.Core.Tools;

/// <summary>An action on a file other than changing its format, such as removing a background.</summary>
public interface IFileTool
{
    /// <summary>Matches the "tools" lists in the format catalog.</summary>
    string Id { get; }

    /// <summary>Names the result, for example "photo-nobg.png" for "photo".</summary>
    string OutputName(string inputStem);

    /// <param name="progress">Receives the finished fraction of this file, from 0 to 1.</param>
    Task RunAsync(
        string inputPath,
        string outputPath,
        string sourceFormat,
        IProgress<double>? progress,
        CancellationToken cancellationToken);
}
