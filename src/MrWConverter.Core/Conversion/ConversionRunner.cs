using MrWConverter.Core.Formats;
using MrWConverter.Core.Output;
using MrWConverter.Core.Tools;

namespace MrWConverter.Core.Conversion;

/// <summary>Runs one job: every input is processed, then all outputs are published together.</summary>
public sealed class ConversionRunner(
    FormatCatalog catalog,
    IReadOnlyList<IFileConverter> converters,
    IReadOnlyList<IFileTool>? tools = null)
{
    public Task<IReadOnlyList<string>> RunAsync(
        IReadOnlyList<string> inputPaths,
        string targetFormat,
        string outputDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return RunCoreAsync(inputPaths, outputDirectory, progress, inputPath =>
        {
            var sourceFormat = catalog.FormatOf(inputPath);
            var converter = converters.FirstOrDefault(candidate => candidate.CanConvert(sourceFormat, targetFormat))
                ?? throw new NotSupportedException($"No converter handles {sourceFormat} to {targetFormat}.");

            return new Step(
                $"{Path.GetFileNameWithoutExtension(inputPath)}.{targetFormat}",
                (output, fileProgress) => converter.ConvertAsync(inputPath, output, sourceFormat, targetFormat, fileProgress, cancellationToken));
        }, cancellationToken);
    }

    public Task<IReadOnlyList<string>> RunToolAsync(
        IReadOnlyList<string> inputPaths,
        string toolId,
        string outputDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var tool = tools?.FirstOrDefault(candidate => candidate.Id == toolId)
            ?? throw new NotSupportedException($"No tool is registered as {toolId}.");

        return RunToolAsync(inputPaths, tool, outputDirectory, progress, cancellationToken);
    }

    /// <summary>Runs a tool made for this one job, such as a crop holding the region the user chose.</summary>
    public Task<IReadOnlyList<string>> RunToolAsync(
        IReadOnlyList<string> inputPaths,
        IFileTool tool,
        string outputDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return RunCoreAsync(inputPaths, outputDirectory, progress, inputPath => new Step(
            tool.OutputName(Path.GetFileNameWithoutExtension(inputPath)),
            (output, fileProgress) => tool.RunAsync(inputPath, output, catalog.FormatOf(inputPath), fileProgress, cancellationToken)),
            cancellationToken);
    }

    /// <summary>Runs several tools on one file, such as saving each clip of an edit, and publishes all or none of the results.</summary>
    public Task<IReadOnlyList<string>> RunToolsAsync(
        string inputPath,
        IReadOnlyList<IFileTool> tools,
        string outputDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var next = 0;
        return RunCoreAsync(Enumerable.Repeat(inputPath, tools.Count).ToArray(), outputDirectory, progress, path =>
        {
            var tool = tools[next++];
            return new Step(
                tool.OutputName(Path.GetFileNameWithoutExtension(path)),
                (output, fileProgress) => tool.RunAsync(path, output, catalog.FormatOf(path), fileProgress, cancellationToken));
        }, cancellationToken);
    }

    private static async Task<IReadOnlyList<string>> RunCoreAsync(
        IReadOnlyList<string> inputPaths,
        string outputDirectory,
        IProgress<double>? progress,
        Func<string, Step> stepFor,
        CancellationToken cancellationToken)
    {
        using var batch = new OutputBatch(outputDirectory);

        for (var index = 0; index < inputPaths.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var inputPath = inputPaths[index];
            var step = stepFor(inputPath);
            var stagedPath = batch.Stage(step.OutputName);
            var fileProgress = progress is null ? null : new FileProgress(progress, index, inputPaths.Count);

            try
            {
                await step.Run(stagedPath, fileProgress);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new ConversionException(inputPath, exception);
            }

            progress?.Report((index + 1d) / inputPaths.Count);
        }

        return batch.Commit();
    }

    private sealed record Step(string OutputName, Func<string, IProgress<double>?, Task> Run);

    /// <summary>Maps one file's progress onto its share of the whole job.</summary>
    private sealed class FileProgress(IProgress<double> job, int index, int count) : IProgress<double>
    {
        public void Report(double value) => job.Report((index + value) / count);
    }
}
