using MrWConverter.Core.Formats;
using MrWConverter.Core.Output;
using MrWConverter.Core.Pdf;
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
        return RunCoreAsync(inputPaths, outputDirectory, progress, async inputPath =>
        {
            var sourceFormat = catalog.FormatOf(inputPath);
            var converter = converters.FirstOrDefault(candidate => candidate.CanConvert(sourceFormat, targetFormat))
                ?? throw new NotSupportedException($"No converter handles {sourceFormat} to {targetFormat}.");
            var stem = Path.GetFileNameWithoutExtension(inputPath);

            if (converter is IPagedConverter paged && paged.HasPages(sourceFormat, targetFormat))
            {
                var pages = await paged.CountPagesAsync(inputPath, cancellationToken);
                return Enumerable.Range(0, pages)
                    .Select(page => new Step(
                        pages == 1 ? $"{stem}.{targetFormat}" : $"{stem}-{PdfTools.PageName(page, pages)}.{targetFormat}",
                        (output, _) => paged.ConvertPageAsync(inputPath, output, page, targetFormat, cancellationToken)))
                    .ToArray();
            }

            return
            [
                new Step(
                    $"{stem}.{targetFormat}",
                    (output, fileProgress) => converter.ConvertAsync(inputPath, output, sourceFormat, targetFormat, fileProgress, cancellationToken)),
            ];
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
        return RunCoreAsync(inputPaths, outputDirectory, progress, inputPath => Task.FromResult<IReadOnlyList<Step>>(
            [
                new Step(
                    tool.OutputName(Path.GetFileNameWithoutExtension(inputPath)),
                    (output, fileProgress) => tool.RunAsync(inputPath, output, catalog.FormatOf(inputPath), fileProgress, cancellationToken)),
            ]),
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
        return RunCoreAsync([inputPath], outputDirectory, progress, path => Task.FromResult<IReadOnlyList<Step>>(tools
            .Select(tool => new Step(
                tool.OutputName(Path.GetFileNameWithoutExtension(path)),
                (output, fileProgress) => tool.RunAsync(path, output, catalog.FormatOf(path), fileProgress, cancellationToken)))
            .ToArray()),
            cancellationToken);
    }

    private static async Task<IReadOnlyList<string>> RunCoreAsync(
        IReadOnlyList<string> inputPaths,
        string outputDirectory,
        IProgress<double>? progress,
        Func<string, Task<IReadOnlyList<Step>>> stepsFor,
        CancellationToken cancellationToken)
    {
        using var batch = new OutputBatch(outputDirectory);

        for (var index = 0; index < inputPaths.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var inputPath = inputPaths[index];

            try
            {
                var steps = await stepsFor(inputPath);

                for (var part = 0; part < steps.Count; part++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var share = 1d / inputPaths.Count / steps.Count;
                    var start = (index + (double)part / steps.Count) / inputPaths.Count;
                    var stagedPath = batch.Stage(steps[part].OutputName);

                    await steps[part].Run(stagedPath, progress is null ? null : new FileProgress(progress, start, share));
                    progress?.Report(start + share);
                }
            }
            catch (Exception exception) when (exception is not (OperationCanceledException or ConversionException or NotSupportedException))
            {
                throw new ConversionException(inputPath, exception);
            }
        }

        return batch.Commit();
    }

    private sealed record Step(string OutputName, Func<string, IProgress<double>?, Task> Run);

    /// <summary>Maps one file's progress onto its share of the whole job.</summary>
    private sealed class FileProgress(IProgress<double> job, double start, double share) : IProgress<double>
    {
        public void Report(double value) => job.Report(start + value * share);
    }
}
