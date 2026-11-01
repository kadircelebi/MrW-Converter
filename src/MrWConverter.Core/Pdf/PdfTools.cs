using MrWConverter.Core.Tools;

namespace MrWConverter.Core.Pdf;

/// <summary>The PDF tools, made for one job because each holds what the user chose.</summary>
public static class PdfTools
{
    public const string MergeId = "mergePdf";
    public const string SplitId = "splitPdf";

    /// <summary>Makes one PDF from all these files. It runs once, and names the result after the first file.</summary>
    /// <param name="convertToPdf">Turns an Office file into a PDF at the given path, so its pages can join in.</param>
    public static IFileTool Merge(IReadOnlyList<string> paths, Func<string, string> formatOf, PdfPageSize size, Func<string, string, CancellationToken, Task>? convertToPdf = null) => new PdfTool(
        MergeId,
        stem => $"{stem}-merged.pdf",
        (_, output, progress, token) => PdfFiles.Merge(paths, formatOf, output, size, progress, token, convertToPdf));

    /// <summary>One tool for each page of this PDF, each writing a PDF of that page.</summary>
    public static async Task<IReadOnlyList<IFileTool>> SplitAsync(string path)
    {
        var pages = await Task.Run(() => PdfFiles.CountPages(path));

        return Enumerable.Range(0, pages)
            .Select(index => new PdfTool(
                SplitId,
                stem => $"{stem}-{PageName(index, pages)}.pdf",
                (input, output, _, _) => PdfFiles.WritePagePdf(input, index, output)))
            .ToArray();
    }

    /// <summary>"page01", padded so that the files of a long PDF sort in page order.</summary>
    internal static string PageName(int index, int pageCount) =>
        $"page{(index + 1).ToString().PadLeft(Math.Max(2, pageCount.ToString().Length), '0')}";

    private sealed class PdfTool(
        string id,
        Func<string, string> outputName,
        Action<string, string, IProgress<double>?, CancellationToken> run) : IFileTool
    {
        public string Id => id;

        public string OutputName(string inputStem) => outputName(inputStem);

        public Task RunAsync(string inputPath, string outputPath, string sourceFormat, IProgress<double>? progress, CancellationToken cancellationToken) =>
            Task.Run(() => run(inputPath, outputPath, progress, cancellationToken), cancellationToken);
    }
}
