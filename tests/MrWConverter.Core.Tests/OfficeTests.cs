using System.Text;
using MrWConverter.Core.Conversion;
using MrWConverter.Core.Formats;
using MrWConverter.Core.Office;
using MrWConverter.Core.Pdf;
using UglyToad.PdfPig;

namespace MrWConverter.Core.Tests;

public class OfficeTests
{
    private static readonly FormatCatalog Catalog = FormatCatalog.Default;

    [Theory]
    [InlineData("docx", "pdf", true)]
    [InlineData("doc", "pdf", true)]
    [InlineData("xlsx", "pdf", true)]
    [InlineData("pptx", "pdf", true)]
    [InlineData("csv", "pdf", true)]
    [InlineData("txt", "pdf", true)]
    [InlineData("docx", "png", false)]
    [InlineData("png", "pdf", false)]
    [InlineData("pdf", "pdf", false)]
    public void Converter_TakesOfficeFilesToPdfOnly(string source, string target, bool expected)
    {
        Assert.Equal(expected, new OfficeConverter(new OfficeRuntime("unused", "unused")).CanConvert(source, target));
    }

    [Fact]
    public void Catalog_OffersPdfForOfficeFiles_AndMergingWithEverythingElse()
    {
        Assert.Equal(["pdf"], Catalog.CommonTargets(["a.docx"]));
        Assert.Equal(["mergePdf"], Catalog.CommonTools(["a.docx", "b.xlsx"]));
        Assert.Equal(["mergePdf"], Catalog.CommonTools(["a.docx", "b.png", "c.pdf"]));
        Assert.Contains("pdf", Catalog.CommonTargets(["a.docx", "b.png"]));
        Assert.Equal("office", Catalog.GroupOf("a.DOCX"));
    }

    [Theory]
    [InlineData("a,b,c", ',')]
    [InlineData("a;b;c", ';')]
    [InlineData("1,5;2,5;3,5;4,5", ',')]
    [InlineData("plain", ',')]
    public void CsvSeparator_FollowsTheFirstLine(string line, int separator)
    {
        Assert.Equal(separator, OfficeConverter.CsvSeparator(line));
    }

    [Fact]
    public void Text_InUtf8_StaysAsItIs()
    {
        Assert.Equal("çığ", OfficeConverter.TextInUtf8(Encoding.UTF8.GetBytes("çığ"), 1254));
        Assert.Equal("çığ", OfficeConverter.TextInUtf8([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("çığ")], 1254));
    }

    [Fact]
    public void Text_InTheOldTurkishEncoding_IsRead()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        Assert.Equal("çığ", OfficeConverter.TextInUtf8(Encoding.GetEncoding(1254).GetBytes("çığ"), 1254));
    }

    [Theory]
    [InlineData("LIBREO~1|LibreOffice", "LibreOffice")]
    [InlineData("program", "program")]
    [InlineData("SOFFICE.COM|soffice.com", "soffice.com")]
    [InlineData("PFILES|Target:SOURCE|Source", "Target")]
    [InlineData(".", ".")]
    public void LongName_ReadsThePackageNameForm(string stored, string expected)
    {
        Assert.Equal(expected, MsiPackage.LongName(stored));
    }

    [Fact]
    public void RelativeFolder_FollowsParents_AndSkipsTheRoot()
    {
        var directories = new Dictionary<string, (string Parent, string Name)>
        {
            ["TARGETDIR"] = ("", "SourceDir"),
            ["App"] = ("TARGETDIR", "APP|Application"),
            ["Same"] = ("App", "."),
            ["Program"] = ("Same", "PROG|program"),
        };

        Assert.Equal(Path.Combine("Application", "program"), MsiPackage.RelativeFolder("Program", directories));
        Assert.Equal("Application", MsiPackage.RelativeFolder("Same", directories));
        Assert.Equal("", MsiPackage.RelativeFolder("TARGETDIR", directories));
    }

    [Fact]
    public void RelativeFolder_RefusesALoop()
    {
        var directories = new Dictionary<string, (string Parent, string Name)>
        {
            ["A"] = ("B", "a"),
            ["B"] = ("A", "b"),
        };

        Assert.Throws<InvalidDataException>(() => MsiPackage.RelativeFolder("A", directories));
    }

    [Theory]
    [InlineData(@"..\evil.dll")]
    [InlineData(@"program\..\..\evil.dll")]
    [InlineData(@"C:\Windows\evil.dll")]
    public void SafePath_RefusesPlacesOutsideTheDestination(string relative)
    {
        Assert.Throws<InvalidDataException>(() => MsiPackage.SafePath(@"C:\app\folder", relative));
    }

    [Fact]
    public void SafePath_AllowsPlacesInside()
    {
        Assert.Equal(@"C:\app\folder\program\a.dll", MsiPackage.SafePath(@"C:\app\folder", @"program\a.dll"));
    }

    [Fact]
    public void Runtime_WithNothingAround_IsNotReady_UnlessInstalledInWindows()
    {
        using var folder = new TemporaryFolder();
        var windows = File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LibreOffice", "program", "soffice.com"));

        Assert.Equal(windows, new OfficeRuntime(folder.File("own"), folder.File("package")).IsReady);
    }

    [Fact]
    public void Runtime_PrefersThePackagesOwnCopy()
    {
        using var folder = new TemporaryFolder();
        var inPackage = folder.File(Path.Combine("package", "office", "program", "soffice.com"));
        Directory.CreateDirectory(Path.GetDirectoryName(inPackage)!);
        File.WriteAllText(inPackage, "x");

        Assert.Equal(inPackage, new OfficeRuntime(folder.File("own"), folder.File("package")).Executable);
    }

    [Fact]
    public async Task Converter_WithoutLibreOffice_SaysSo_RatherThanCrashing()
    {
        using var folder = new TemporaryFolder();
        if (File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LibreOffice", "program", "soffice.com")))
        {
            return;
        }

        var input = folder.File("a.txt");
        File.WriteAllText(input, "text");
        var converter = new OfficeConverter(new OfficeRuntime(folder.File("own"), folder.File("package")));

        await Assert.ThrowsAsync<OfficeDownloadException>(() =>
            converter.ConvertAsync(input, folder.File("a.pdf"), "txt", "pdf", null, CancellationToken.None));
    }

    /// <summary>Skipped until the app has fetched LibreOffice on this machine; see <see cref="OfficeFactAttribute"/>.</summary>
    [OfficeFact]
    public async Task Text_BecomesAPdfWithItsWords()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("çığ [1] %d.txt");
        File.WriteAllText(input, "Merhaba dunya", new UTF8Encoding(false));

        var outputs = await Runner.RunAsync([input], "pdf", folder.Path);

        Assert.Equal([folder.File("çığ [1] %d.pdf")], outputs);
        AssertHasText(outputs[0], "Merhaba dunya");
    }

    [OfficeFact]
    public async Task TurkishLetters_InOldEncoding_SurviveIntoThePdf()
    {
        using var folder = new TemporaryFolder();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var input = folder.File("a.txt");
        File.WriteAllBytes(input, Encoding.GetEncoding(1254).GetBytes("Şişe çığ"));

        // The test machine's language decides how old files are read, so it must be Turkish for this.
        if (System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage != 1254)
        {
            return;
        }

        var outputs = await Runner.RunAsync([input], "pdf", folder.Path);

        AssertHasText(outputs[0], "Şişe çığ");
    }

    [OfficeFact]
    public async Task Csv_BecomesATable()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("a.csv");
        File.WriteAllText(input, "ad;fiyat\nkalem;12\n", new UTF8Encoding(false));

        var outputs = await Runner.RunAsync([input], "pdf", folder.Path);

        AssertHasText(outputs[0], "kalem");
        AssertHasText(outputs[0], "fiyat");
        Assert.DoesNotContain(";", TextOf(outputs[0]));
    }

    [OfficeTheory]
    [InlineData("docx")]
    [InlineData("xlsx")]
    [InlineData("pptx")]
    public async Task RealOfficeFiles_BecomePdfs(string format)
    {
        using var folder = new TemporaryFolder();
        var input = await MakeOfficeFile(folder, format);

        var outputs = await Runner.RunAsync([input], "pdf", folder.Path);

        Assert.True(PdfFiles.CountPages(outputs[0]) >= 1);
        AssertHasText(outputs[0], "Merhaba");
    }

    [OfficeFact]
    public async Task Merge_TakesOfficeFiles_InOrderWithPicturesAndPdfs()
    {
        using var folder = new TemporaryFolder();
        var text = folder.File("a.txt");
        File.WriteAllText(text, "Ilk sayfa", new UTF8Encoding(false));
        var picture = folder.File("b.png");
        using (var image = new ImageMagick.MagickImage(ImageMagick.MagickColors.Red, 200, 100))
        {
            image.Write(picture);
        }

        var office = new OfficeConverter(Runtime);
        var tool = PdfTools.Merge(
            [text, picture],
            Catalog.FormatOf,
            PdfPageSize.Picture,
            (path, output, token) => office.ConvertAsync(path, output, Catalog.FormatOf(path), "pdf", null, token));

        var outputs = await Runner.RunToolsAsync(text, [tool], folder.Path);

        Assert.Equal(2, PdfFiles.CountPages(outputs[0]));
        using var document = PdfDocument.Open(outputs[0]);
        Assert.Contains("Ilk sayfa", document.GetPage(1).Text);
    }

    [OfficeFact]
    public async Task BrokenOfficeFile_Fails_AndLeavesNothing()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("a.xlsx");

        // Starts like the zip file every modern Office document is, then falls apart.
        var bytes = new byte[4096];
        new Random(1).NextBytes(bytes);
        bytes[0] = (byte)'P';
        bytes[1] = (byte)'K';
        bytes[2] = 3;
        bytes[3] = 4;
        File.WriteAllBytes(input, bytes);

        await Assert.ThrowsAsync<ConversionException>(() => Runner.RunAsync([input], "pdf", folder.Path));

        Assert.False(File.Exists(folder.File("a.pdf")));
    }

    /// <summary>LibreOffice reads anything it can as text, so a wrong extension gives a readable PDF rather than an error.</summary>
    [OfficeFact]
    public async Task TextNamedAsAWordFile_BecomesAPdfOfThatText()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("a.docx");
        File.WriteAllText(input, "Sadece metin");

        var outputs = await Runner.RunAsync([input], "pdf", folder.Path);

        AssertHasText(outputs[0], "Sadece metin");
    }

    private static OfficeRuntime Runtime => OfficeLocation.Runtime;

    private static ConversionRunner Runner => new(Catalog, [new OfficeConverter(Runtime)]);

    private static string TextOf(string pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Concat(document.GetPages().Select(page => page.Text));
    }

    private static void AssertHasText(string pdf, string text) => Assert.Contains(text.Replace(" ", ""), TextOf(pdf).Replace(" ", ""));

    /// <summary>Has LibreOffice itself write the sample, so it is a real file of that format.</summary>
    private static async Task<string> MakeOfficeFile(TemporaryFolder folder, string format)
    {
        var source = folder.File($"source-{format}.{(format == "xlsx" ? "csv" : "txt")}");
        File.WriteAllText(source, format == "xlsx" ? "Merhaba,1\nDunya,2\n" : "Merhaba dunya\n", new UTF8Encoding(false));

        var (filter, infilter) = format switch
        {
            "docx" => ("docx:MS Word 2007 XML", "Text (encoded):UTF8,LF"),
            "odt" => ("odt", "Text (encoded):UTF8,LF"),
            "xlsx" => ("xlsx:Calc MS Excel 2007 XML", "Text - txt - csv (StarCalc):44,34,76,1"),
            "pptx" => ("pptx:Impress MS PowerPoint 2007 XML", "Text (encoded):UTF8,LF"),
            _ => throw new ArgumentException(format),
        };

        var profile = folder.File($"profile-{format}");
        var work = folder.File($"made-{format}");
        Directory.CreateDirectory(work);

        if (format == "pptx")
        {
            // Text doesn't open as slides, so a document goes through the drawing module.
            var drawing = folder.File("source.fodp");
            File.WriteAllText(drawing, FlatPresentation, new UTF8Encoding(false));
            source = drawing;
            infilter = "";
        }

        List<string> arguments = [$"-env:UserInstallation={new Uri(profile + Path.DirectorySeparatorChar).AbsoluteUri}", "--headless", "--nologo", "--norestore"];
        if (infilter.Length > 0)
        {
            arguments.Add($"--infilter={infilter}");
        }

        arguments.AddRange(["--convert-to", filter, "--outdir", work, source]);
        await Media.ToolProcess.RunAsync(Runtime.Executable!, arguments);

        return Directory.GetFiles(work).Single();
    }

    private const string FlatPresentation = """
        <?xml version="1.0" encoding="UTF-8"?>
        <office:document xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" xmlns:draw="urn:oasis:names:tc:opendocument:xmlns:drawing:1.0" xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0" xmlns:svg="urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0" office:version="1.3" office:mimetype="application/vnd.oasis.opendocument.presentation">
        <office:body><office:presentation><draw:page draw:name="page1">
        <draw:frame svg:width="20cm" svg:height="5cm" svg:x="2cm" svg:y="2cm"><draw:text-box><text:p>Merhaba dunya</text:p></draw:text-box></draw:frame>
        </draw:page></office:presentation></office:body>
        </office:document>
        """;
}

internal static class OfficeLocation
{
    public static OfficeRuntime Runtime { get; } = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MrWConverter", "office"),
        Path.Combine(AppContext.BaseDirectory, "no-package-copy"));

    public const string SkipReason = "LibreOffice is not here yet. Let the app fetch it once by converting an Office file.";
}

/// <summary>The download is too large for a test to do, so these run where the app has already done it.</summary>
public sealed class OfficeFactAttribute : FactAttribute
{
    public OfficeFactAttribute()
    {
        if (!OfficeLocation.Runtime.IsReady)
        {
            Skip = OfficeLocation.SkipReason;
        }
    }
}

public sealed class OfficeTheoryAttribute : TheoryAttribute
{
    public OfficeTheoryAttribute()
    {
        if (!OfficeLocation.Runtime.IsReady)
        {
            Skip = OfficeLocation.SkipReason;
        }
    }
}
