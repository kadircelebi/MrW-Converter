using UglyToad.PdfPig;
using UglyToad.PdfPig.Exceptions;

namespace MrWConverter.Core.Pdf.Word;

/// <summary>Makes an editable Word file from a PDF: real paragraphs and tables rather than a picture of each page.</summary>
internal static class PdfToWord
{
    /// <param name="progress">Receives the finished fraction, from 0 to 1.</param>
    public static void Write(string pdfPath, string outputPath, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        try
        {
            using var document = PdfDocument.Open(pdfPath);
            var pages = new List<LayoutPage>(document.NumberOfPages);

            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var layout = PdfLayoutReader.Read(page);

                // A scanned page has no letters to read, so it stays what it is: a picture of the page.
                pages.Add(layout.Blocks.Count > 0 ? layout : ScannedPage(pdfPath, page.Number - 1, layout));
                progress?.Report((double)page.Number / document.NumberOfPages);
            }

            DocxWriter.Write(pages, outputPath);
        }
        catch (PdfDocumentEncryptedException exception)
        {
            throw new PdfFileException(PdfProblem.Password, exception.Message);
        }
    }

    private static LayoutPage ScannedPage(string pdfPath, int pageIndex, LayoutPage page) =>
        page with
        {
            Shapes = [],
            Pictures = [new PagePicture(PdfFiles.RenderPage(pdfPath, pageIndex), "png", 0, 0, page.Width, page.Height)],
        };
}
