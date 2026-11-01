using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using ImageMagick;
using MrWConverter.Core.Conversion;
using MrWConverter.Core.Office;
using MrWConverter.Core.Tools;
using PdfSharp.Drawing;
using PdfSharp.Pdf.IO;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;
using Windows.Data.Pdf;
using Windows.Storage.Streams;
using SharpDocument = PdfSharp.Pdf.PdfDocument;
using WindowsDocument = Windows.Data.Pdf.PdfDocument;

namespace MrWConverter.Core.Pdf;

public enum PdfProblem
{
    Password,
    NoText,
}

/// <summary>A PDF that can't be used for a reason the user can do something about.</summary>
public sealed class PdfFileException(PdfProblem problem, string message) : Exception(message)
{
    public PdfProblem Problem { get; } = problem;
}

public enum PdfPageSize
{
    /// <summary>Each picture gets a page of its own size.</summary>
    Picture,

    /// <summary>Each picture is fitted on an A4 page that faces the same way.</summary>
    A4,
}

/// <summary>
/// Reading and writing PDF files. Pages are drawn by Windows, which keeps its PDF engine
/// patched without an app update; PDFsharp copies pages and PdfPig reads text.
/// </summary>
public static class PdfFiles
{
    public const uint RenderDpi = 200;

    private const double PointsPerInch = 72;
    private const double DeviceIndependentPixelsPerInch = 96;
    private const double A4Width = 595.28;
    private const double A4Height = 841.89;
    private const uint MaximumRenderSide = 8000;
    private const int WrongPassword = unchecked((int)0x8007052B);
    private const int PngWidthOffset = 16;
    private const int AllowedSizeError = 2;

    private static double _displayScale = 1;

    public static int CountPages(string path)
    {
        using var document = OpenForCopying(path);
        return document.PageCount;
    }

    /// <summary>Draws one page as a PNG. With a size, the longest side fits it; otherwise it is print quality.</summary>
    public static byte[] RenderPage(string path, int pageIndex, uint? longestSide = null) =>
        RenderPageAsync(path, pageIndex, longestSide).GetAwaiter().GetResult();

    public static void WriteImagePdf(string imagePath, string imageFormat, string outputPath, PdfPageSize size = PdfPageSize.Picture, ulong maxPixels = ImageConverter.DefaultMaxPixels)
    {
        using var document = new SharpDocument();
        AddImagePage(document, imagePath, imageFormat, size, maxPixels);
        document.Save(outputPath);
    }

    /// <summary>Puts the given pictures and PDFs in one file, in this order.</summary>
    /// <param name="formatOf">The format of each file, from the catalog.</param>
    /// <param name="progress">Receives the finished fraction, from 0 to 1.</param>
    public static void Merge(
        IReadOnlyList<string> paths,
        Func<string, string> formatOf,
        string outputPath,
        PdfPageSize size,
        IProgress<double>? progress,
        CancellationToken cancellationToken,
        Func<string, string, CancellationToken, Task>? convertToPdf = null,
        ulong maxPixels = ImageConverter.DefaultMaxPixels)
    {
        using var document = new SharpDocument();

        for (var index = 0; index < paths.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = paths[index];

            try
            {
                if (formatOf(path) == "pdf")
                {
                    AddPdfPages(document, path);
                }
                else if (OfficeConverter.Handles(formatOf(path)))
                {
                    var converted = Path.Combine(Path.GetTempPath(), $"mrwc-{Guid.NewGuid():N}.pdf");
                    try
                    {
                        var convert = convertToPdf ?? throw new InvalidOperationException("Office files can't be merged here.");
                        convert(path, converted, cancellationToken).GetAwaiter().GetResult();
                        AddPdfPages(document, converted);
                    }
                    finally
                    {
                        File.Delete(converted);
                    }
                }
                else
                {
                    AddImagePage(document, path, formatOf(path), size, maxPixels);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Names the file that failed, rather than the first one, which names the job.
                throw new ConversionException(path, exception);
            }

            progress?.Report((index + 1d) / paths.Count);
        }

        document.Save(outputPath);
    }

    private static void AddPdfPages(SharpDocument document, string path)
    {
        using var source = OpenForCopying(path);
        foreach (var page in source.Pages)
        {
            document.AddPage(page);
        }
    }

    public static void WritePagePdf(string path, int pageIndex, string outputPath)
    {
        using var source = OpenForCopying(path);
        using var page = new SharpDocument();
        page.AddPage(source.Pages[pageIndex]);
        page.Save(outputPath);
    }

    /// <summary>Writes one page as a picture, with no metadata.</summary>
    public static void WritePageImage(string path, int pageIndex, string outputPath, string imageFormat)
    {
        using var image = new MagickImage(RenderPage(path, pageIndex), MagickFormat.Png);
        ImageFile.WriteWithoutMetadata(image, outputPath, imageFormat);
    }

    /// <summary>Writes the PDF's text in reading order. A scan is only pictures, so it has none to give.</summary>
    public static void WriteText(string path, string outputPath)
    {
        try
        {
            using var document = UglyToad.PdfPig.PdfDocument.Open(path);
            var pages = document.GetPages().Select(page => ContentOrderTextExtractor.GetText(page)).ToArray();

            if (pages.All(string.IsNullOrWhiteSpace))
            {
                throw new PdfFileException(PdfProblem.NoText, "The PDF has no text, so it is probably a scan.");
            }

            File.WriteAllText(outputPath, string.Join(Environment.NewLine + Environment.NewLine, pages));
        }
        catch (PdfDocumentEncryptedException exception)
        {
            throw new PdfFileException(PdfProblem.Password, exception.Message);
        }
    }

    private static async Task<byte[]> RenderPageAsync(string path, int pageIndex, uint? longestSide)
    {
        using var input = File.OpenRead(path);
        using var inputStream = input.AsRandomAccessStream();

        WindowsDocument document;
        try
        {
            document = await WindowsDocument.LoadFromStreamAsync(inputStream);
        }
        catch (COMException exception) when (exception.HResult == WrongPassword)
        {
            throw new PdfFileException(PdfProblem.Password, exception.Message);
        }
        catch (COMException exception)
        {
            throw new InvalidDataException($"Windows can't read this PDF: {exception.Message}", exception);
        }

        using var page = document.GetPage((uint)pageIndex);
        var scale = longestSide is { } side
            ? side / Math.Max(page.Size.Width, page.Size.Height)
            : RenderDpi / DeviceIndependentPixelsPerInch;
        scale = Math.Min(scale, MaximumRenderSide / Math.Max(page.Size.Width, page.Size.Height));

        var (width, height) = (Math.Max(1, Math.Round(page.Size.Width * scale)), Math.Max(1, Math.Round(page.Size.Height * scale)));
        var png = await DrawAsync(page, width / _displayScale, height / _displayScale);

        // Windows scales what it draws by the display's scale, so the same page would come out
        // bigger on a 150% screen. Measuring that once keeps the result the same on every machine.
        var drawnWidth = PngWidth(png);
        if (Math.Abs(drawnWidth - width) > AllowedSizeError)
        {
            _displayScale = _displayScale * drawnWidth / width;
            png = await DrawAsync(page, width / _displayScale, height / _displayScale);
        }

        return png;
    }

    private static async Task<byte[]> DrawAsync(PdfPage page, double width, double height)
    {
        using var output = new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(output, new PdfPageRenderOptions
        {
            DestinationWidth = (uint)Math.Max(1, Math.Round(width)),
            DestinationHeight = (uint)Math.Max(1, Math.Round(height)),
        });

        var bytes = new byte[output.Size];
        output.Seek(0);
        await output.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);
        return bytes;
    }

    /// <summary>A PNG stores its width as four bytes, right after the signature and the header's name and length.</summary>
    private static int PngWidth(byte[] png) => BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(PngWidthOffset, sizeof(int)));

    private static SharpDocument OpenForCopying(string path)
    {
        var needsPassword = false;

        try
        {
            using var input = File.OpenRead(path);
            var document = PdfReader.Open(input, PdfDocumentOpenMode.Import, arguments =>
            {
                needsPassword = true;
                arguments.Abort = true;
            });

            // Asked for a password, PDFsharp gives up by returning nothing.
            return needsPassword || document is null
                ? throw new PdfFileException(PdfProblem.Password, "The PDF needs a password.")
                : document;
        }
        catch (PdfReaderException exception) when (needsPassword)
        {
            throw new PdfFileException(PdfProblem.Password, exception.Message);
        }
    }

    private static void AddImagePage(SharpDocument document, string path, string format, PdfPageSize size, ulong maxPixels)
    {
        using var stream = new MemoryStream(ImageBytes(path, format, maxPixels, out var width, out var height));
        using var picture = XImage.FromStream(stream);

        var (pictureWidth, pictureHeight) = (width * PointsPerInch / DeviceIndependentPixelsPerInch, height * PointsPerInch / DeviceIndependentPixelsPerInch);
        var (pageWidth, pageHeight) = size == PdfPageSize.A4
            ? (width > height ? (A4Height, A4Width) : (A4Width, A4Height))
            : (pictureWidth, pictureHeight);

        var page = document.AddPage();
        page.Width = XUnit.FromPoint(pageWidth);
        page.Height = XUnit.FromPoint(pageHeight);

        var scale = Math.Min(pageWidth / pictureWidth, pageHeight / pictureHeight);
        var (drawnWidth, drawnHeight) = (pictureWidth * scale, pictureHeight * scale);

        using var graphics = XGraphics.FromPdfPage(page);
        graphics.DrawImage(picture, (pageWidth - drawnWidth) / 2, (pageHeight - drawnHeight) / 2, drawnWidth, drawnHeight);
    }

    /// <summary>
    /// A JPEG goes in as it is, so a photo loses nothing, unless it has to be turned upright.
    /// Lossless pictures stay lossless, because sharp text suffers most from JPEG. The formats
    /// PDFsharp can't take become a high quality JPEG, or a PNG if they have transparency.
    /// </summary>
    private static byte[] ImageBytes(string path, string format, ulong maxPixels, out uint width, out uint height)
    {
        var turned = false;
        using (var input = File.OpenRead(path))
        using (var original = new MagickImage())
        {
            original.Ping(input, new MagickReadSettings { Format = ImageFile.Formats[format], FrameCount = 1 });
            turned = original.Orientation is not (Orientation.Undefined or Orientation.TopLeft);
        }

        using var image = ImageFile.Read(path, format, maxPixels);
        (width, height) = (image.Width, image.Height);

        if (format == "jpg" && !turned && image.ColorSpace == ColorSpace.sRGB)
        {
            return JpegMetadata.Without(File.ReadAllBytes(path));
        }

        image.Strip();
        image.Quality = ImageFile.LossyQuality;

        var lossy = format is "jpg" or "webp" or "avif" && !image.HasAlpha;

        using var output = new MemoryStream();
        image.Write(output, lossy ? MagickFormat.Jpeg : image.HasAlpha ? MagickFormat.Png32 : MagickFormat.Png24);
        return output.ToArray();
    }
}
