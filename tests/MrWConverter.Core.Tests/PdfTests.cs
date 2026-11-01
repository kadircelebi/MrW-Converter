using System.Text;
using ImageMagick;
using MrWConverter.Core.Conversion;
using MrWConverter.Core.Formats;
using MrWConverter.Core.Pdf;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace MrWConverter.Core.Tests;

public class PdfTests
{
    private const string Secret = "SECRETVALUE";

    private static readonly FormatCatalog Catalog = FormatCatalog.Default;

    private static ConversionRunner Runner => new(Catalog, [new PdfConverter(), new ImageConverter()]);

    [Fact]
    public async Task Picture_ToPdf_MakesOnePageOfItsSizeShowingThePicture()
    {
        using var folder = new TemporaryFolder();
        var input = WriteImage(folder, "a.png", MagickColors.Red, 960, 480);

        var outputs = await Runner.RunAsync([input], "pdf", folder.Path);

        Assert.Equal([folder.File("a.pdf")], outputs);
        using var document = PdfReader.Open(outputs[0], PdfDocumentOpenMode.Import);
        Assert.Equal(1, document.PageCount);

        // 96 pixels make an inch, which is 72 points.
        Assert.Equal(720, document.Pages[0].Width.Point, 1);
        Assert.Equal(360, document.Pages[0].Height.Point, 1);
        AssertPageColor(outputs[0], 0, channel: 0);
    }

    [Theory]
    [InlineData("jpg")]
    [InlineData("png")]
    [InlineData("webp")]
    [InlineData("tiff")]
    [InlineData("bmp")]
    [InlineData("avif")]
    public async Task EveryPictureFormat_BecomesAPdf(string format)
    {
        using var folder = new TemporaryFolder();
        var input = WriteImage(folder, $"a.{format}", MagickColors.Blue, 200, 100);

        var outputs = await Runner.RunAsync([input], "pdf", folder.Path);

        AssertPageColor(outputs[0], 0, channel: 2);
    }

    [Fact]
    public async Task ManyPictures_ToPdf_MakeOnePdfEach()
    {
        using var folder = new TemporaryFolder();
        var first = WriteImage(folder, "a.png", MagickColors.Red, 100, 100);
        var second = WriteImage(folder, "b.jpg", MagickColors.Blue, 100, 100);

        var outputs = await Runner.RunAsync([first, second], "pdf", folder.Path);

        Assert.Equal([folder.File("a.pdf"), folder.File("b.pdf")], outputs);
    }

    [Fact]
    public async Task TransparentPicture_KeepsAWhiteBackground()
    {
        using var folder = new TemporaryFolder();
        var input = WriteImage(folder, "a.png", MagickColors.Transparent, 200, 200);

        var outputs = await Runner.RunAsync([input], "pdf", folder.Path);

        using var page = new MagickImage(PdfFiles.RenderPage(outputs[0], 0), MagickFormat.Png);
        using var pixels = page.GetPixels();
        var color = pixels.GetPixel((int)page.Width / 2, (int)page.Height / 2).ToColor()!;
        Assert.True(color.R > 240 && color.G > 240 && color.B > 240, $"The page is {color}.");
    }

    [Fact]
    public async Task SidewaysPhoto_IsUprightInThePdf()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("a.jpg");
        using (var image = new MagickImage(MagickColors.White, 200, 100))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Orientation, (ushort)6);
            image.SetProfile(exif);
            image.Orientation = Orientation.RightTop;
            image.Write(input);
        }

        var outputs = await Runner.RunAsync([input], "pdf", folder.Path);

        using var document = PdfReader.Open(outputs[0], PdfDocumentOpenMode.Import);
        Assert.True(document.Pages[0].Height.Point > document.Pages[0].Width.Point);
    }

    [Theory]
    [InlineData("jpg", MagickFormat.Jpeg)]
    [InlineData("png", MagickFormat.Png)]
    [InlineData("webp", MagickFormat.WebP)]
    public async Task Metadata_DoesNotReachThePdf(string format, MagickFormat magickFormat)
    {
        using var folder = new TemporaryFolder();
        var input = folder.File($"a.{format}");
        using (var image = new MagickImage(MagickColors.White, 100, 100))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Software, Secret);
            image.SetProfile(exif);
            image.SetProfile(new XmpProfile(Encoding.UTF8.GetBytes($"<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><s>{Secret}</s></x:xmpmeta>")));
            image.Comment = Secret;
            using var stream = File.Create(input);
            image.Write(stream, magickFormat);
        }

        Assert.Contains(Secret, Encoding.Latin1.GetString(File.ReadAllBytes(input)));

        var outputs = await Runner.RunAsync([input], "pdf", folder.Path);

        Assert.DoesNotContain(Secret, Encoding.Latin1.GetString(File.ReadAllBytes(outputs[0])));
    }

    [Fact]
    public void JpegMetadata_KeepsThePictureData_Exactly()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("a.jpg");
        using (var image = new MagickImage(MagickColors.Green, 64, 64))
        {
            image.Comment = Secret;
            image.Write(input);
        }

        var original = File.ReadAllBytes(input);
        var cleaned = JpegMetadata.Without(original);

        Assert.True(cleaned.Length < original.Length);
        using var before = new MagickImage(original);
        using var after = new MagickImage(cleaned);
        Assert.Equal(0, before.Compare(after, ErrorMetric.Absolute));
    }

    [Fact]
    public void JpegMetadata_RefusesOtherFiles()
    {
        Assert.Throws<InvalidDataException>(() => JpegMetadata.Without([1, 2, 3, 4, 5]));
    }

    [Fact]
    public async Task Merge_KeepsTheChosenOrder_AcrossPicturesAndPdfs()
    {
        using var folder = new TemporaryFolder();
        var red = WriteImage(folder, "red.png", MagickColors.Red, 200, 100);
        var blue = WriteImage(folder, "blue.jpg", MagickColors.Blue, 200, 100);
        var green = WriteImage(folder, "green.png", MagickColors.Lime, 200, 100);
        var twoPages = folder.File("two.pdf");
        PdfFiles.Merge([red, green], Catalog.FormatOf, twoPages, PdfPageSize.Picture, null, CancellationToken.None);

        string[] order = [blue, twoPages, green];
        var outputs = await Runner.RunToolsAsync(order[0], [PdfTools.Merge(order, Catalog.FormatOf, PdfPageSize.Picture)], folder.Path);

        Assert.Equal([folder.File("blue-merged.pdf")], outputs);
        Assert.Equal(4, PdfFiles.CountPages(outputs[0]));
        AssertPageColor(outputs[0], 0, channel: 2);
        AssertPageColor(outputs[0], 1, channel: 0);
        AssertPageColor(outputs[0], 2, channel: 1);
        AssertPageColor(outputs[0], 3, channel: 1);
    }

    [Theory]
    [InlineData(800, 400, 841.89, 595.28)]
    [InlineData(400, 800, 595.28, 841.89)]
    public async Task Merge_OnA4_FacesTheWayThePictureDoes(int width, int height, double pageWidth, double pageHeight)
    {
        using var folder = new TemporaryFolder();
        var input = WriteImage(folder, "a.png", MagickColors.Red, width, height);
        var second = WriteImage(folder, "b.png", MagickColors.Red, width, height);

        var outputs = await Runner.RunToolsAsync(input, [PdfTools.Merge([input, second], Catalog.FormatOf, PdfPageSize.A4)], folder.Path);

        using var document = PdfReader.Open(outputs[0], PdfDocumentOpenMode.Import);
        Assert.Equal(pageWidth, document.Pages[0].Width.Point, 1);
        Assert.Equal(pageHeight, document.Pages[0].Height.Point, 1);
        AssertPageColor(outputs[0], 0, channel: 0);
    }

    [Fact]
    public async Task Merge_WhenAFileIsBroken_NamesThatFileAndLeavesNothing()
    {
        using var folder = new TemporaryFolder();
        var good = WriteImage(folder, "good.png", MagickColors.Red, 100, 100);
        var broken = folder.File("broken.pdf");
        File.WriteAllText(broken, "not a pdf");

        var failure = await Assert.ThrowsAsync<ConversionException>(() =>
            Runner.RunToolsAsync(good, [PdfTools.Merge([good, broken], Catalog.FormatOf, PdfPageSize.Picture)], folder.Path));

        Assert.Equal(broken, failure.FilePath);
        Assert.False(File.Exists(folder.File("good-merged.pdf")));
    }

    [Fact]
    public async Task Merge_WhenCancelled_LeavesNothing()
    {
        using var folder = new TemporaryFolder();
        var input = WriteImage(folder, "a.png", MagickColors.Red, 100, 100);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Runner.RunToolsAsync(input, [PdfTools.Merge([input, input], Catalog.FormatOf, PdfPageSize.Picture)], folder.Path, null, cancellation.Token));

        Assert.False(File.Exists(folder.File("a-merged.pdf")));
    }

    [Fact]
    public async Task Pdf_ToPictures_MakesOneFilePerPage_InOrder()
    {
        using var folder = new TemporaryFolder();
        var pdf = MakePdf(folder, "a.pdf", (MagickColors.Red, 400, 200), (MagickColors.Blue, 400, 200));

        var outputs = await Runner.RunAsync([pdf], "png", folder.Path);

        Assert.Equal([folder.File("a-page01.png"), folder.File("a-page02.png")], outputs);
        AssertCenterColor(outputs[0], 0);
        AssertCenterColor(outputs[1], 2);
    }

    [Fact]
    public async Task Pdf_ToPictures_DrawsAtPrintQuality()
    {
        using var folder = new TemporaryFolder();

        // 400 pixels at 96 per inch is a little over four inches, so about 833 pixels at 200 per inch.
        var pdf = MakePdf(folder, "a.pdf", (MagickColors.Red, 400, 200));

        var outputs = await Runner.RunAsync([pdf], "jpg", folder.Path);

        Assert.Equal([folder.File("a.jpg")], outputs);
        using var image = new MagickImage(outputs[0]);
        Assert.InRange(image.Width, 825u, 840u);
        Assert.InRange(image.Height, 410u, 425u);
    }

    [Fact]
    public void RenderPage_CanMakeAThumbnail()
    {
        using var folder = new TemporaryFolder();
        var pdf = MakePdf(folder, "a.pdf", (MagickColors.Red, 400, 200));

        using var image = new MagickImage(PdfFiles.RenderPage(pdf, 0, 100), MagickFormat.Png);

        Assert.InRange(image.Width, 98u, 102u);
        Assert.InRange(image.Height, 48u, 52u);
    }

    [Fact]
    public async Task Split_MakesAPdfOfEachPage_AndKeepsTheOriginal()
    {
        using var folder = new TemporaryFolder();
        var pdf = MakePdf(folder, "çığ [1] %d.pdf", (MagickColors.Red, 400, 200), (MagickColors.Blue, 400, 200), (MagickColors.Lime, 400, 200));
        var before = File.ReadAllBytes(pdf);

        var outputs = await Runner.RunToolsAsync(pdf, await PdfTools.SplitAsync(pdf), folder.Path);

        Assert.Equal(
            [folder.File("çığ [1] %d-page01.pdf"), folder.File("çığ [1] %d-page02.pdf"), folder.File("çığ [1] %d-page03.pdf")],
            outputs);
        Assert.All(outputs, output => Assert.Equal(1, PdfFiles.CountPages(output)));
        AssertPageColor(outputs[1], 0, channel: 2);
        Assert.Equal(before, File.ReadAllBytes(pdf));
    }

    [Theory]
    [InlineData(0, 3, "page01")]
    [InlineData(8, 12, "page09")]
    [InlineData(99, 120, "page100")]
    [InlineData(4, 120, "page005")]
    public void PageNames_SortInPageOrder(int index, int count, string name)
    {
        Assert.Equal(name, PdfTools.PageName(index, count));
    }

    [Fact]
    public async Task Pdf_ToText_ReadsItsWords()
    {
        using var folder = new TemporaryFolder();
        var pdf = folder.File("a.pdf");
        File.WriteAllBytes(pdf, PdfWithText("Merhaba dunya"));

        var outputs = await Runner.RunAsync([pdf], "txt", folder.Path);

        Assert.Equal([folder.File("a.txt")], outputs);
        Assert.Contains("Merhaba dunya", File.ReadAllText(outputs[0]));
    }

    [Fact]
    public async Task Scan_ToText_SaysThereIsNoText()
    {
        using var folder = new TemporaryFolder();
        var pdf = MakePdf(folder, "a.pdf", (MagickColors.Red, 400, 200));

        var failure = await Assert.ThrowsAsync<ConversionException>(() => Runner.RunAsync([pdf], "txt", folder.Path));

        Assert.Equal(PdfProblem.NoText, Assert.IsType<PdfFileException>(failure.InnerException).Problem);
        Assert.False(File.Exists(folder.File("a.txt")));
    }

    [Fact]
    public async Task LockedPdf_SaysItNeedsAPassword_ForEveryUse()
    {
        using var folder = new TemporaryFolder();
        var locked = folder.File("locked.pdf");
        var other = WriteImage(folder, "other.png", MagickColors.Red, 100, 100);
        using (var document = new PdfDocument())
        {
            document.AddPage();
            document.SecuritySettings.UserPassword = "secret";
            document.SecuritySettings.OwnerPassword = "owner";
            document.Save(locked);
        }

        foreach (var target in new[] { "png", "txt" })
        {
            var failure = await Assert.ThrowsAsync<ConversionException>(() => Runner.RunAsync([locked], target, folder.Path));
            Assert.Equal(PdfProblem.Password, Assert.IsType<PdfFileException>(failure.InnerException).Problem);
        }

        var merging = await Assert.ThrowsAsync<ConversionException>(() =>
            Runner.RunToolsAsync(other, [PdfTools.Merge([other, locked], Catalog.FormatOf, PdfPageSize.Picture)], folder.Path));
        Assert.Equal(locked, merging.FilePath);
        Assert.IsType<PdfFileException>(merging.InnerException);

        await Assert.ThrowsAsync<PdfFileException>(() => PdfTools.SplitAsync(locked));
    }

    [Theory]
    [InlineData("png")]
    [InlineData("txt")]
    public async Task BrokenPdf_Fails_AndLeavesNothing(string target)
    {
        using var folder = new TemporaryFolder();
        var broken = folder.File("a.pdf");
        File.WriteAllText(broken, "not a pdf");

        await Assert.ThrowsAsync<ConversionException>(() => Runner.RunAsync([broken], target, folder.Path));

        Assert.False(File.Exists(folder.File($"a.{target}")));
        Assert.False(File.Exists(folder.File($"a-page01.{target}")));
    }

    [Fact]
    public async Task ManyPages_ReportProgressThatOnlyMovesForward()
    {
        using var folder = new TemporaryFolder();
        var pdf = MakePdf(folder, "a.pdf", (MagickColors.Red, 100, 100), (MagickColors.Red, 100, 100), (MagickColors.Red, 100, 100), (MagickColors.Red, 100, 100));
        var reports = new List<double>();

        await Runner.RunAsync([pdf], "png", folder.Path, new Immediate(reports.Add));

        Assert.Equal(reports.Order(), reports);
        Assert.Equal(1, reports[^1], 6);
        Assert.Equal(4, reports.Count);
    }

    [Fact]
    public void Catalog_OffersPdfFormatsAndTools()
    {
        Assert.Contains("pdf", Catalog.CommonTargets(["a.png"]));
        Assert.Equal(["docx", "png", "jpg", "txt"], Catalog.CommonTargets(["a.pdf"]));
        Assert.Equal(["mergePdf", "splitPdf"], Catalog.CommonTools(["a.pdf"]));
        Assert.Equal(["mergePdf"], Catalog.CommonTools(["a.png", "b.pdf"]));
        Assert.Empty(Catalog.CommonTools(["a.mp4", "b.pdf"]));
    }

    [Fact]
    public void Converter_LeavesOtherConversionsToOthers()
    {
        var converter = new PdfConverter();

        Assert.True(converter.CanConvert("png", "pdf"));
        Assert.True(converter.CanConvert("pdf", "jpg"));
        Assert.True(converter.CanConvert("pdf", "txt"));
        Assert.False(converter.CanConvert("pdf", "pdf"));
        Assert.False(converter.CanConvert("mp4", "pdf"));
        Assert.False(converter.CanConvert("png", "jpg"));
        Assert.False(new ImageConverter().CanConvert("png", "pdf"));
    }

    private static string WriteImage(TemporaryFolder folder, string name, MagickColor color, int width, int height)
    {
        var path = folder.File(name);
        using var image = new MagickImage(color, (uint)width, (uint)height);
        image.Write(path);
        return path;
    }

    private static string MakePdf(TemporaryFolder folder, string name, params (MagickColor Color, int Width, int Height)[] pages)
    {
        var pictures = pages.Select((page, index) => WriteImage(folder, $"{Guid.NewGuid():N}{index}.png", page.Color, page.Width, page.Height)).ToArray();
        var path = folder.File(name);
        PdfFiles.Merge(pictures, Catalog.FormatOf, path, PdfPageSize.Picture, null, CancellationToken.None);
        return path;
    }

    /// <param name="channel">Which of red, green and blue should stand out.</param>
    private static void AssertPageColor(string pdf, int page, int channel)
    {
        using var image = new MagickImage(PdfFiles.RenderPage(pdf, page, 200), MagickFormat.Png);
        AssertColor(image, channel);
    }

    private static void AssertCenterColor(string picture, int channel)
    {
        using var image = new MagickImage(picture);
        AssertColor(image, channel);
    }

    private static void AssertColor(MagickImage image, int channel)
    {
        using var pixels = image.GetPixels();
        var color = pixels.GetPixel((int)image.Width / 2, (int)image.Height / 2).ToColor()!;
        byte[] values = [color.R, color.G, color.B];

        Assert.True(values[channel] > 200 && values.Where((_, index) => index != channel).All(value => value < 60), $"The page is {color}.");
    }

    /// <summary>A small hand-written PDF, because PDFsharp needs fonts set up to draw text.</summary>
    private static byte[] PdfWithText(string text)
    {
        var stream = $"BT /F1 24 Tf 72 700 Td ({text}) Tj ET";
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {stream.Length} >>\nstream\n{stream}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        ];

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        var table = pdf.Length;
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            pdf.Append($"{offset:D10} 00000 n \n");
        }

        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{table}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    private sealed class Immediate(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
