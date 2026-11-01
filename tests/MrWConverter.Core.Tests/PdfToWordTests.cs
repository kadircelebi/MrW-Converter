using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using ImageMagick;
using MrWConverter.Core.Conversion;
using MrWConverter.Core.Formats;
using MrWConverter.Core.Pdf;
using MrWConverter.Core.Pdf.Word;
using PdfSharp.Pdf;

namespace MrWConverter.Core.Tests;

public class PdfToWordTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static ConversionRunner Runner => new(FormatCatalog.Default, [new PdfConverter()]);

    [Fact]
    public async Task TextBecomesRealParagraphs_NotTextBoxes()
    {
        using var folder = new TemporaryFolder();
        var pdf = TestPdf.Write(folder.File("a.pdf"), [TestPdf.Page(
            TestPdf.Text(72, 700, 12, "Birinci paragraf"),
            TestPdf.Text(72, 600, 12, "Ikinci paragraf"))]);

        var outputs = await Runner.RunAsync([pdf], "docx", folder.Path);

        Assert.Equal([folder.File("a.docx")], outputs);
        var body = BodyOf(outputs[0]);
        Assert.Contains("Birinci paragraf", Texts(body));
        Assert.Contains("Ikinci paragraf", Texts(body));
        Assert.Empty(body.Descendants(W + "txbxContent"));
        Assert.Empty(body.Descendants(W + "tbl"));
    }

    [Fact]
    public async Task TextKeepsItsSize()
    {
        using var folder = new TemporaryFolder();
        var pdf = TestPdf.Write(folder.File("a.pdf"), [TestPdf.Page(TestPdf.Text(72, 700, 18, "Baslik"))]);

        var outputs = await Runner.RunAsync([pdf], "docx", folder.Path);

        var run = BodyOf(outputs[0]).Descendants(W + "r").Single(element => element.Value == "Baslik");
        Assert.Equal("36", run.Element(W + "rPr")!.Element(W + "sz")!.Attribute(W + "val")!.Value);
    }

    [Fact]
    public async Task LinesOfOneText_JoinIntoOneParagraph_SoThatItCanRewrap()
    {
        using var folder = new TemporaryFolder();
        var pdf = TestPdf.Write(folder.File("a.pdf"), [TestPdf.Page(
            TestPdf.Text(72, 700, 12, "Bu birinci satir sayfanin genisligince uzayan bir metin"),
            TestPdf.Text(72, 685, 12, "ve bu da onun devamidir"))]);

        var outputs = await Runner.RunAsync([pdf], "docx", folder.Path);

        var paragraph = BodyOf(outputs[0]).Elements(W + "p").Single(element => element.Value.Contains("birinci"));
        Assert.Equal("Bu birinci satir sayfanin genisligince uzayan bir metin ve bu da onun devamidir", paragraph.Value);
    }

    [Fact]
    public async Task ColumnsWithGaps_BecomeATableOfThoseCells()
    {
        using var folder = new TemporaryFolder();
        var pdf = TestPdf.Write(folder.File("a.pdf"), [TestPdf.Page(
            TestPdf.Text(72, 700, 10, "Adet"), TestPdf.Text(400, 700, 10, "12"),
            TestPdf.Text(72, 680, 10, "Fiyat"), TestPdf.Text(400, 680, 10, "340 TL"),
            TestPdf.Text(72, 660, 10, "Toplam"), TestPdf.Text(400, 660, 10, "4080 TL"))]);

        var outputs = await Runner.RunAsync([pdf], "docx", folder.Path);

        var table = Assert.Single(BodyOf(outputs[0]).Descendants(W + "tbl"));
        var rows = table.Elements(W + "tr").Select(row => row.Elements(W + "tc").Select(cell => cell.Value).ToArray()).ToArray();
        Assert.Equal([["Adet", "12"], ["Fiyat", "340 TL"], ["Toplam", "4080 TL"]], rows);
    }

    [Fact]
    public async Task ColoredBoxBehindACell_BecomesTheCellsShade()
    {
        using var folder = new TemporaryFolder();
        var pdf = TestPdf.Write(folder.File("a.pdf"), [TestPdf.Page(
            TestPdf.Box(390, 650, 120, 70, "0.8 0.8 0.8"),
            TestPdf.Text(72, 700, 10, "Adet"), TestPdf.Text(400, 700, 10, "12"),
            TestPdf.Text(72, 680, 10, "Fiyat"), TestPdf.Text(400, 680, 10, "340 TL"))]);

        var outputs = await Runner.RunAsync([pdf], "docx", folder.Path);

        var cells = BodyOf(outputs[0]).Descendants(W + "tr").First().Elements(W + "tc").ToArray();
        Assert.Null(cells[0].Descendants(W + "shd").FirstOrDefault());
        Assert.Equal("CCCCCC", cells[1].Descendants(W + "shd").Single().Attribute(W + "fill")!.Value);
    }

    [Fact]
    public async Task LineUnderARow_BecomesItsBorder()
    {
        using var folder = new TemporaryFolder();
        var pdf = TestPdf.Write(folder.File("a.pdf"), [TestPdf.Page(
            TestPdf.Text(72, 700, 10, "Adet"), TestPdf.Text(400, 700, 10, "12"),
            TestPdf.Line(72, 692, 500, "0.5 0.5 0.5"),
            TestPdf.Text(72, 680, 10, "Fiyat"), TestPdf.Text(400, 680, 10, "340 TL"))]);

        var outputs = await Runner.RunAsync([pdf], "docx", folder.Path);

        var first = BodyOf(outputs[0]).Descendants(W + "tr").First();
        Assert.All(first.Elements(W + "tc"), cell => Assert.NotNull(cell.Descendants(W + "bottom").FirstOrDefault()));
    }

    [Fact]
    public async Task LettersPrintedTwiceForFakeBold_ComeOutOnce_AndBold()
    {
        using var folder = new TemporaryFolder();
        var pdf = TestPdf.Write(folder.File("a.pdf"), [TestPdf.Page(
            TestPdf.Text(72, 700, 14, "HUZUR"),
            TestPdf.Text(72.2, 700, 14, "HUZUR"))]);

        var outputs = await Runner.RunAsync([pdf], "docx", folder.Path);

        var run = Assert.Single(BodyOf(outputs[0]).Descendants(W + "r"), element => element.Value.Length > 0);
        Assert.Equal("HUZUR", run.Value);
        Assert.NotNull(run.Element(W + "rPr")!.Element(W + "b"));
    }

    [Fact]
    public async Task EachPageStartsANewWordPage()
    {
        using var folder = new TemporaryFolder();
        var pdf = TestPdf.Write(folder.File("a.pdf"),
        [
            TestPdf.Page(TestPdf.Text(72, 700, 12, "Bir")),
            TestPdf.Page(TestPdf.Text(72, 700, 12, "Iki")),
            TestPdf.Page(TestPdf.Text(72, 700, 12, "Uc")),
        ]);

        var outputs = await Runner.RunAsync([pdf], "docx", folder.Path);

        Assert.Equal(2, BodyOf(outputs[0]).Descendants(W + "pageBreakBefore").Count());
    }

    [Fact]
    public async Task PageSizeIsKept()
    {
        using var folder = new TemporaryFolder();
        var pdf = TestPdf.Write(folder.File("a.pdf"), [TestPdf.Page(TestPdf.Text(72, 300, 12, "Yatay"))], width: 842, height: 595);

        var outputs = await Runner.RunAsync([pdf], "docx", folder.Path);

        var size = BodyOf(outputs[0]).Descendants(W + "pgSz").Single();
        Assert.Equal("16840", size.Attribute(W + "w")!.Value);
        Assert.Equal("11900", size.Attribute(W + "h")!.Value);
        Assert.Equal("landscape", size.Attribute(W + "orient")!.Value);
    }

    [Fact]
    public async Task PagesOfDifferentSizes_GetTheirOwnSections()
    {
        using var folder = new TemporaryFolder();
        var first = TestPdf.Write(folder.File("first.pdf"), [TestPdf.Page(TestPdf.Text(72, 700, 12, "Dikey"))]);
        var second = TestPdf.Write(folder.File("second.pdf"), [TestPdf.Page(TestPdf.Text(72, 300, 12, "Yatay"))], width: 842, height: 595);
        var merged = folder.File("merged.pdf");
        PdfFiles.Merge([first, second], FormatCatalog.Default.FormatOf, merged, PdfPageSize.Picture, null, CancellationToken.None);

        var outputs = await Runner.RunAsync([merged], "docx", folder.Path);

        Assert.Equal(2, BodyOf(outputs[0]).Descendants(W + "pgSz").Count());
        AssertValidWordFile(outputs[0]);
    }

    [Fact]
    public async Task TextAtTheVeryBottom_StaysThere_WithoutPushingAPage()
    {
        using var folder = new TemporaryFolder();
        var pdf = TestPdf.Write(folder.File("a.pdf"), [TestPdf.Page(
            TestPdf.Text(72, 700, 12, "Govde"),
            TestPdf.Text(72, 20, 9, "Alt bilgi"))]);

        var outputs = await Runner.RunAsync([pdf], "docx", folder.Path);

        var box = Assert.Single(BodyOf(outputs[0]).Descendants(W + "txbxContent"));
        Assert.Equal("Alt bilgi", box.Value);
    }

    [Fact]
    public async Task PicturesCome_AndWritingOnThemStaysOnThem()
    {
        using var folder = new TemporaryFolder();
        var picture = folder.File("p.png");
        using (var image = new MagickImage(MagickColors.Blue, 400, 200))
        {
            image.Write(picture);
        }

        var pdf = folder.File("a.pdf");
        PdfFiles.Merge([picture], FormatCatalog.Default.FormatOf, pdf, PdfPageSize.Picture, null, CancellationToken.None);

        var outputs = await Runner.RunAsync([pdf], "docx", folder.Path);

        using var zip = ZipFile.OpenRead(outputs[0]);
        Assert.Single(zip.Entries, entry => entry.FullName.StartsWith("word/media/", StringComparison.Ordinal));
        AssertValidWordFile(outputs[0]);
    }

    [Fact]
    public async Task ScannedPage_ComesAsAPictureOfThePage()
    {
        using var folder = new TemporaryFolder();
        var pdf = TestPdf.Write(folder.File("a.pdf"), [TestPdf.Page(TestPdf.Box(100, 100, 300, 300, "1 0 0"))]);

        var outputs = await Runner.RunAsync([pdf], "docx", folder.Path);

        using var zip = ZipFile.OpenRead(outputs[0]);
        Assert.Single(zip.Entries, entry => entry.FullName.StartsWith("word/media/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EverythingTogether_IsAValidWordFile()
    {
        using var folder = new TemporaryFolder();
        var pdf = TestPdf.Write(folder.File("a.pdf"),
        [
            TestPdf.Page(
                TestPdf.Box(60, 690, 480, 30, "0.4 0.3 0.6"),
                TestPdf.Text(72, 700, 14, "Baslik"),
                TestPdf.Text(72, 650, 12, "Bu paragrafin satiri sayfanin genisligince uzayan bir metin"),
                TestPdf.Text(72, 635, 12, "ve devami"),
                TestPdf.Text(72, 600, 10, "Adet"), TestPdf.Text(400, 600, 10, "12"),
                TestPdf.Line(72, 592, 500, "0.5 0.5 0.5"),
                TestPdf.Text(72, 580, 10, "Fiyat"), TestPdf.Text(400, 580, 10, "340 TL"),
                TestPdf.Text(72, 20, 9, "Alt bilgi"), TestPdf.Text(400, 20, 9, "Sayfa")),
            TestPdf.Page(TestPdf.Text(72, 700, 12, "A & B < C > \"D\"")),
        ]);

        var outputs = await Runner.RunAsync([pdf], "docx", folder.Path);

        AssertValidWordFile(outputs[0]);
    }

    [Fact]
    public async Task LockedPdf_SaysItNeedsAPassword()
    {
        using var folder = new TemporaryFolder();
        var locked = folder.File("a.pdf");
        using (var document = new PdfDocument())
        {
            document.AddPage();
            document.SecuritySettings.UserPassword = "secret";
            document.SecuritySettings.OwnerPassword = "owner";
            document.Save(locked);
        }

        var failure = await Assert.ThrowsAsync<ConversionException>(() => Runner.RunAsync([locked], "docx", folder.Path));

        Assert.Equal(PdfProblem.Password, Assert.IsType<PdfFileException>(failure.InnerException).Problem);
        Assert.False(File.Exists(folder.File("a.docx")));
    }

    [Fact]
    public async Task BrokenPdf_Fails_AndLeavesNothing()
    {
        using var folder = new TemporaryFolder();
        var broken = folder.File("a.pdf");
        File.WriteAllText(broken, "not a pdf");

        await Assert.ThrowsAsync<ConversionException>(() => Runner.RunAsync([broken], "docx", folder.Path));

        Assert.False(File.Exists(folder.File("a.docx")));
    }

    [Theory]
    [InlineData("BCDFEE+Calibri", "Calibri")]
    [InlineData("MontserratLightRegular", "Montserrat Light")]
    [InlineData("MontserratBlackRegular", "Montserrat Black")]
    [InlineData("Arial-BoldMT", "Arial")]
    [InlineData("ArialNormal", "Arial")]
    [InlineData("TimesNewRomanPSMT", "Times New Roman")]
    [InlineData(null, "Calibri")]
    public void FontNames_BecomeWhatWordCallsThem(string? name, string family)
    {
        Assert.Equal(family, FontNames.Family(name));
    }

    [Theory]
    [InlineData("Arial-BoldMT", true)]
    [InlineData("MontserratBlackRegular", true)]
    [InlineData("ABCDEF+Calibri", false)]
    [InlineData("ABCDEF+Calibri-Bold", true)]
    public void BoldFonts_AreRecognized(string name, bool bold)
    {
        Assert.Equal(bold, FontNames.IsBold(name));
    }

    [Theory]
    [InlineData("Montserrat Light", "Calibri")]
    [InlineData("Garamond Pro", "Times New Roman")]
    [InlineData("Noto Sans Book", "Calibri")]
    [InlineData("Arial", "Arial")]
    [InlineData("Segoe UI", "Segoe UI")]
    public void FontsThisMachineLacks_GetASimilarOne(string family, string chosen)
    {
        Assert.Equal(chosen, FontNames.ChooseFamily(family, ["Arial (TrueType)", "Segoe UI Light (TrueType)"]));
    }

    private static XElement BodyOf(string docx)
    {
        using var zip = ZipFile.OpenRead(docx);
        using var stream = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(stream).Root!.Element(W + "body")!;
    }

    private static string[] Texts(XElement body) => body.Descendants(W + "p").Select(paragraph => paragraph.Value).Where(text => text.Length > 0).ToArray();

    /// <summary>The checker Microsoft ships for this file format, because a wrong file only shows when Word refuses it.</summary>
    private static void AssertValidWordFile(string docx)
    {
        using var document = WordprocessingDocument.Open(docx, false);
        var errors = new OpenXmlValidator(FileFormatVersions.Microsoft365).Validate(document).ToArray();

        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Take(8).Select(error => $"{error.Path?.XPath}: {error.Description}")));
    }

    /// <summary>Small hand-written PDFs, because PDFsharp needs fonts set up to draw text.</summary>
    private static class TestPdf
    {
        public static string Text(double x, double y, double size, string text) =>
            FormattableString.Invariant($"BT /F1 {size} Tf {x} {y} Td ({text.Replace("(", "\\(").Replace(")", "\\)")}) Tj ET");

        public static string Box(double x, double y, double width, double height, string color) =>
            FormattableString.Invariant($"{color} rg {x} {y} {width} {height} re f 0 0 0 rg");

        public static string Line(double x, double y, double length, string color) =>
            FormattableString.Invariant($"{color} RG 0.5 w {x} {y} m {x + length} {y} l S");

        public static string Page(params string[] content) => string.Join("\n", content);

        public static string Write(string path, string[] pages, double width = 595, double height = 842)
        {
            var objects = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                $"<< /Type /Pages /Kids [{string.Join(" ", pages.Select((_, index) => $"{4 + index * 2} 0 R"))}] /Count {pages.Length} >>",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            };

            for (var index = 0; index < pages.Length; index++)
            {
                objects.Add(FormattableString.Invariant($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] /Contents {5 + index * 2} 0 R /Resources << /Font << /F1 3 0 R >> >> >>"));
                objects.Add($"<< /Length {pages[index].Length} >>\nstream\n{pages[index]}\nendstream");
            }

            var pdf = new StringBuilder("%PDF-1.4\n");
            var offsets = new List<int>();
            for (var index = 0; index < objects.Count; index++)
            {
                offsets.Add(pdf.Length);
                pdf.Append($"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
            }

            var table = pdf.Length;
            pdf.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
            foreach (var offset in offsets)
            {
                pdf.Append($"{offset:D10} 00000 n \n");
            }

            pdf.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{table}\n%%EOF\n");
            File.WriteAllBytes(path, Encoding.ASCII.GetBytes(pdf.ToString()));
            return path;
        }
    }
}
