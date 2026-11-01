using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MrWConverter.Core.Pdf.Word;

/// <summary>Turns the names fonts have inside a PDF, such as "BCDFEE+Calibri-Bold", into what Word calls them.</summary>
internal static partial class FontNames
{
    private const string FontsKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts";

    // Narrow and on every Windows with Office, so that text from a font this machine lacks still fits its place.
    private const string PlainStandIn = "Calibri";
    private const string SerifStandIn = "Times New Roman";

    private static readonly string[] BoldWords = ["Bold", "Black", "Heavy", "Semibold", "SemiBold", "Demi", "ExtraBold"];
    private static readonly string[] SerifWords = ["Serif", "Times", "Georgia", "Garamond", "Palatino", "Cambria", "Minion", "Book"];

    private static readonly Lazy<IReadOnlyList<string>> Installed = new(ReadInstalled);

    /// <summary>The family as written in the PDF, if Windows here has it, or a similar one that it does.</summary>
    public static string ForWord(string? pdfName) => ChooseFamily(Family(pdfName), Installed.Value);

    internal static string ChooseFamily(string family, IReadOnlyList<string> installed)
    {
        // "Montserrat Light" counts as there if "Montserrat Light Regular" or the like is.
        if (installed.Any(font => font.StartsWith(family, StringComparison.OrdinalIgnoreCase)))
        {
            return family;
        }

        return SerifWords.Any(word => family.Contains(word, StringComparison.OrdinalIgnoreCase)) && !family.Contains("Sans", StringComparison.OrdinalIgnoreCase)
            ? SerifStandIn
            : PlainStandIn;
    }

    public static string Family(string? pdfName)
    {
        var name = WithoutSubset(pdfName);

        // "MontserratLightRegular" and "Arial-BoldMT": only the style words at the end go.
        name = StyleSuffix().Replace(name, "");
        name = name.Replace('-', ' ').Replace(',', ' ').Replace('_', ' ').Trim();
        name = CaseChange().Replace(name, "$1 $2");

        return name.Length == 0 ? PlainStandIn : name;
    }

    public static bool IsBold(string? pdfName)
    {
        var name = WithoutSubset(pdfName);
        return BoldWords.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsItalic(string? pdfName)
    {
        var name = WithoutSubset(pdfName);
        return name.Contains("Italic", StringComparison.OrdinalIgnoreCase) || name.Contains("Oblique", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A PDF that embeds part of a font marks it with six capitals and a plus sign.</summary>
    private static string WithoutSubset(string? pdfName)
    {
        var name = pdfName ?? "";
        var plus = name.IndexOf('+');
        return plus == 6 ? name[(plus + 1)..] : name;
    }

    /// <summary>Windows lists fonts for the machine and for the user, each as a name like "Arial (TrueType)".</summary>
    private static IReadOnlyList<string> ReadInstalled()
    {
        var names = new List<string>();

        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using var key = hive.OpenSubKey(FontsKey);
            names.AddRange(key?.GetValueNames() ?? []);
        }

        return names;
    }

    [GeneratedRegex("[-, _]?(MT|PSMT|PS|Regular|Normal|Bold|Italic|Oblique)+$")]
    private static partial Regex StyleSuffix();

    [GeneratedRegex("([a-zçğıöşü])([A-ZÇĞİÖŞÜ])")]
    private static partial Regex CaseChange();
}
