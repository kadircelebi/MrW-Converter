namespace MrWConverter.Core.Pdf.Word;

/// <summary>A color as it goes in a Word file, such as "6B4FA0".</summary>
internal readonly record struct WordColor(byte Red, byte Green, byte Blue)
{
    public string Hex => $"{Red:X2}{Green:X2}{Blue:X2}";

    public bool IsNearWhite => Red > 235 && Green > 235 && Blue > 235;
}

/// <summary>A stretch of text with one look.</summary>
internal sealed record TextRun(string Text, string Font, double Size, bool Bold, bool Italic, WordColor Color);

internal enum TextAlignment
{
    Left,
    Center,
    Right,
}

internal abstract record PageBlock(double Top, double Bottom);

/// <summary>Lines that read as one paragraph. Positions are in points from the page's top left.</summary>
internal sealed record ParagraphBlock(
    double Top,
    double Bottom,
    IReadOnlyList<TextRun> Runs,
    double Left,
    double Right,
    TextAlignment Alignment,
    double LinePitch,
    bool SeveralLines,
    bool FixedOnPage = false) : PageBlock(Top, Bottom);

internal sealed record TableCell(IReadOnlyList<TextRun> Runs, TextAlignment Alignment, WordColor? Shade, double Indent);

internal sealed record TableRow(IReadOnlyList<TableCell> Cells, double Height, WordColor? BottomRule);

internal sealed record TableBlock(
    double Top,
    double Bottom,
    double Left,
    IReadOnlyList<double> ColumnWidths,
    IReadOnlyList<TableRow> Rows) : PageBlock(Top, Bottom);

/// <summary>A picture at its place on the page.</summary>
internal sealed record PagePicture(byte[] Bytes, string Extension, double Left, double Top, double Width, double Height);

/// <summary>A flat colored box at its place on the page.</summary>
internal sealed record PageShape(WordColor Color, double Left, double Top, double Width, double Height);

internal sealed record LayoutPage(
    double Width,
    double Height,
    IReadOnlyList<PageBlock> Blocks,
    IReadOnlyList<PagePicture> Pictures,
    IReadOnlyList<PageShape> Shapes);
