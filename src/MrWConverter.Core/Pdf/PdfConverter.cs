using MrWConverter.Core.Conversion;
using MrWConverter.Core.Pdf.Word;

namespace MrWConverter.Core.Pdf;

/// <summary>Pictures to PDF, and PDF to pictures, text or Word. A PDF becomes one picture per page.</summary>
public sealed class PdfConverter : IPagedConverter
{
    private static readonly HashSet<string> PageImages = ["png", "jpg"];

    public bool CanConvert(string sourceFormat, string targetFormat) =>
        (sourceFormat != "pdf" && targetFormat == "pdf" && ImageFile.Formats.ContainsKey(sourceFormat))
        || (sourceFormat == "pdf" && (PageImages.Contains(targetFormat) || targetFormat is "txt" or "docx"));

    public bool HasPages(string sourceFormat, string targetFormat) => sourceFormat == "pdf" && PageImages.Contains(targetFormat);

    public Task<int> CountPagesAsync(string inputPath, CancellationToken cancellationToken) =>
        Task.Run(() => PdfFiles.CountPages(inputPath), cancellationToken);

    public Task ConvertPageAsync(string inputPath, string outputPath, int pageIndex, string targetFormat, CancellationToken cancellationToken) =>
        Task.Run(() => PdfFiles.WritePageImage(inputPath, pageIndex, outputPath, targetFormat), cancellationToken);

    public Task ConvertAsync(string inputPath, string outputPath, string sourceFormat, string targetFormat, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                if (targetFormat == "pdf")
                {
                    PdfFiles.WriteImagePdf(inputPath, sourceFormat, outputPath);
                }
                else if (targetFormat == "docx")
                {
                    PdfToWord.Write(inputPath, outputPath, progress, cancellationToken);
                }
                else
                {
                    PdfFiles.WriteText(inputPath, outputPath);
                }
            },
            cancellationToken);
}
