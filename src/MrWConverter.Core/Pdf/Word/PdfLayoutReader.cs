using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using UglyToad.PdfPig.Graphics.Colors;
using PdfWord = UglyToad.PdfPig.Content.Word;

namespace MrWConverter.Core.Pdf.Word;

/// <summary>
/// Works out a page's paragraphs and tables. A PDF stores only letters at positions, so this is
/// informed guessing: letters on one baseline are a line, a wide gap splits it into cells, and
/// lines in a row that share their gaps are a table.
/// </summary>
internal static class PdfLayoutReader
{
    private const double BaselineTolerance = 0.3;
    private const double CellGapInFontSizes = 1.4;
    private const double TableRowGapInFontSizes = 3.6;
    private const double ParagraphGapInFontSizes = 1.7;
    private const double MinimumColumnGutter = 4;
    private const double CenteredTolerance = 3;
    private const double FullLineShare = 0.6;
    private const double RuleThickness = 1.5;
    private const double RuleReach = 8;
    private const double RuleMinimumWidth = 40;
    private const double LineHeight = 1.25;
    private const double MinimumShapeSide = 2;
    private const double FooterShare = 0.93;
    private const double BarHeightInFontSizes = 3;
    private const double SameLetterShare = 0.3;

    public static LayoutPage Read(Page page)
    {
        var height = page.Height;
        var fills = Fills(page, height);
        var rules = Rules(page, height);
        var pictures = Pictures(page, height);
        var lines = Lines(page, height);

        var usedFills = new HashSet<Fill>();
        var blocks = new List<PageBlock>();
        var index = 0;

        // Some text belongs to a place on the page rather than to the flow of the text: a footer, which
        // must not end up on a new page, and writing on a picture or a colored bar, which must stay on it.
        bool Fixed(Line line) =>
            line.Top >= height * FooterShare
            || OnPicture(line, pictures)
            || (line.Segments.Count == 1 && OnBar(line, fills));

        var pinned = lines.Where(Fixed).ToArray();
        blocks.AddRange(pinned.SelectMany(FixedBlocks));
        lines.RemoveAll(line => Fixed(line));
        var textWidth = lines.Count == 0 ? page.Width : lines.Max(line => line.Right) - lines.Min(line => line.Left);

        while (index < lines.Count)
        {
            var row = RowsFrom(lines, index, pinned);
            if (row.Count > 0)
            {
                blocks.Add(BuildTable(row, fills, rules, usedFills));
                index += row.Count;
                continue;
            }

            var paragraph = ParagraphFrom(lines, index, textWidth);
            blocks.Add(BuildParagraph(lines.GetRange(index, paragraph), page.Width));
            index += paragraph;
        }

        var shapes = fills
            .Where(fill => !usedFills.Contains(fill))
            .Select(fill => new PageShape(fill.Color, fill.Left, fill.Top, fill.Right - fill.Left, fill.Bottom - fill.Top))
            .ToArray();

        return new LayoutPage(page.Width, height, ReadableOnPage(blocks, fills, pictures), pictures, shapes);
    }

    private static bool OnPicture(Line line, IReadOnlyList<PagePicture> pictures) => pictures.Any(picture =>
        line.Center >= picture.Left && line.Center <= picture.Left + picture.Width
        && line.Baseline >= picture.Top && line.Baseline <= picture.Top + picture.Height
        && picture.Width >= line.Right - line.Left);

    /// <summary>A strip about one line high, such as a colored heading, rather than a whole colored page.</summary>
    private static bool OnBar(Line line, IReadOnlyList<Fill> fills) => fills.Any(fill =>
        fill.Contains(line.Center, line.Baseline - line.Size * 0.3) && fill.Bottom - fill.Top <= line.Size * BarHeightInFontSizes);

    /// <summary>White text whose background didn't survive would be invisible, so it turns black.</summary>
    private static List<PageBlock> ReadableOnPage(List<PageBlock> blocks, IReadOnlyList<Fill> fills, IReadOnlyList<PagePicture> pictures)
    {
        bool Backed(double x, double y) =>
            fills.Any(fill => !fill.Color.IsNearWhite && fill.Contains(x, y))
            || pictures.Any(picture => x >= picture.Left && x <= picture.Left + picture.Width && y >= picture.Top && y <= picture.Top + picture.Height);

        return blocks.Select(block => block switch
        {
            ParagraphBlock paragraph when !Backed(paragraph.Left + 2, (paragraph.Top + paragraph.Bottom) / 2) =>
                paragraph with { Runs = paragraph.Runs.Select(Darkened).ToArray() },
            _ => block,
        }).ToList();
    }

    private static IEnumerable<ParagraphBlock> FixedBlocks(Line line) => line.Segments.Select(segment => new ParagraphBlock(
        line.Top,
        line.Bottom,
        segment.Runs,
        segment.Left,
        segment.Right,
        TextAlignment.Left,
        line.Size * LineHeight,
        SeveralLines: false,
        FixedOnPage: true));

    private static TextRun Darkened(TextRun run) => run.Color.IsNearWhite ? run with { Color = new WordColor(0, 0, 0) } : run;

    private static List<Line> Lines(Page page, double height)
    {
        var (letters, thickened) = WithoutRepeats(page.Letters);

        var words = NearestNeighbourWordExtractor.Instance.GetWords(letters)
            .Where(word => word.Letters.Count > 0 && !string.IsNullOrWhiteSpace(word.Text))
            .Select(word => new PlacedWord(word, word.Letters[0].StartBaseLine.Y, word.Letters[0].PointSize))
            .OrderByDescending(word => word.Baseline)
            .ToList();

        var groups = new List<List<PlacedWord>>();
        foreach (var word in words)
        {
            var group = groups.LastOrDefault();
            if (group is not null && Math.Abs(group[0].Baseline - word.Baseline) <= Math.Max(group[0].Size, word.Size) * BaselineTolerance)
            {
                group.Add(word);
            }
            else
            {
                groups.Add([word]);
            }
        }

        return groups.Select(group => BuildLine(group, height, thickened)).ToList();
    }

    /// <summary>
    /// Some PDFs fake bold by printing a letter twice, nearly on top of itself. Read naively, that
    /// spells "HHUUZZUURR". Each one is kept once, and remembered as bold.
    /// </summary>
    internal static (List<Letter> Letters, HashSet<Letter> Thickened) WithoutRepeats(IReadOnlyList<Letter> all)
    {
        var kept = new List<Letter>(all.Count);
        var thickened = new HashSet<Letter>();
        var byValue = new Dictionary<string, List<Letter>>();

        foreach (var letter in all)
        {
            if (!byValue.TryGetValue(letter.Value, out var same))
            {
                byValue[letter.Value] = same = [];
            }

            var reach = Math.Max(letter.BoundingBox.Width, letter.PointSize * 0.5) * SameLetterShare;
            var original = same.Find(other =>
                Math.Abs(other.StartBaseLine.X - letter.StartBaseLine.X) <= reach
                && Math.Abs(other.StartBaseLine.Y - letter.StartBaseLine.Y) <= reach);

            if (original is not null)
            {
                thickened.Add(original);
                continue;
            }

            same.Add(letter);
            kept.Add(letter);
        }

        return (kept, thickened);
    }

    private static Line BuildLine(List<PlacedWord> words, double height, HashSet<Letter> thickened)
    {
        var ordered = words.OrderBy(word => word.Word.BoundingBox.Left).ToList();
        var size = ordered.Max(word => word.Size);
        var segments = new List<List<PlacedWord>> { new() { ordered[0] } };

        for (var index = 1; index < ordered.Count; index++)
        {
            var gap = ordered[index].Word.BoundingBox.Left - ordered[index - 1].Word.BoundingBox.Right;
            if (gap > Math.Max(ordered[index].Size, ordered[index - 1].Size) * CellGapInFontSizes)
            {
                segments.Add([]);
            }

            segments[^1].Add(ordered[index]);
        }

        var baseline = height - ordered[0].Baseline;
        return new Line(
            segments.Select(segment => new Segment(
                segment[0].Word.BoundingBox.Left,
                segment[^1].Word.BoundingBox.Right,
                Runs(segment.Select(word => word.Word), thickened))).ToArray(),
            baseline - size,
            baseline + size * 0.25,
            baseline,
            size);
    }

    private static List<TextRun> Runs(IEnumerable<PdfWord> words, HashSet<Letter> thickened)
    {
        var runs = new List<TextRun>();

        foreach (var word in words)
        {
            var letter = word.Letters[0];
            var bold = FontNames.IsBold(letter.FontName) || thickened.Contains(letter);
            var italic = FontNames.IsItalic(letter.FontName);
            var run = new TextRun(word.Text, FontNames.ForWord(letter.FontName), Math.Round(letter.PointSize * 2) / 2, bold, italic, ToColor(letter.Color));

            if (runs.Count > 0 && runs[^1] with { Text = run.Text } == run)
            {
                runs[^1] = runs[^1] with { Text = $"{runs[^1].Text} {run.Text}" };
            }
            else
            {
                runs.Add(runs.Count > 0 ? run with { Text = $" {run.Text}" } : run);
            }
        }

        return runs;
    }

    /// <summary>The lines from here on that form a table: each split in cells, and close to the one before.</summary>
    private static List<Line> RowsFrom(List<Line> lines, int start, IReadOnlyList<Line> pinned)
    {
        var rows = new List<Line>();

        for (var index = start; index < lines.Count; index++)
        {
            var line = lines[index];
            var closeEnough = rows.Count == 0 || line.Top - rows[^1].Bottom <= line.Size * TableRowGapInFontSizes;

            // A heading pinned between two rows means two tables, however close they sit.
            var headingBetween = rows.Count > 0 && pinned.Any(other => other.Baseline > rows[^1].Baseline && other.Baseline < line.Baseline);
            if (line.Segments.Count < 2 || !closeEnough || headingBetween)
            {
                break;
            }

            rows.Add(line);
        }

        return rows;
    }

    /// <summary>How many lines from here on read as one paragraph.</summary>
    private static int ParagraphFrom(List<Line> lines, int start, double textWidth)
    {
        var first = lines[start];
        var count = 1;

        for (var index = start + 1; index < lines.Count; index++)
        {
            var (previous, next) = (lines[index - 1], lines[index]);
            var sameLook = next.Segments.Count == 1 && Math.Abs(next.Size - first.Size) < 0.6 && FirstRun(next).Font == FirstRun(first).Font;
            var close = next.Baseline - previous.Baseline <= first.Size * ParagraphGapInFontSizes;
            var aligned = Math.Abs(next.Left - first.Left) < 2 || Math.Abs(next.Center - previous.Center) < CenteredTolerance;
            var previousFull = previous.Right - previous.Left >= textWidth * FullLineShare;

            if (!sameLook || !close || !aligned || !previousFull)
            {
                break;
            }

            count++;
        }

        return count;
    }

    private static TextRun FirstRun(Line line) => line.Segments[0].Runs[0];

    private static ParagraphBlock BuildParagraph(List<Line> lines, double pageWidth)
    {
        var runs = new List<TextRun>();
        foreach (var line in lines)
        {
            foreach (var segment in line.Segments)
            {
                foreach (var run in segment.Runs)
                {
                    runs.Add(runs.Count > 0 && ReferenceEquals(run, segment.Runs[0]) ? run with { Text = $" {run.Text.TrimStart()}" } : run);
                }
            }
        }

        var left = lines.Min(line => line.Left);
        var right = lines.Max(line => line.Right);
        var pitch = lines.Count > 1 ? (lines[^1].Baseline - lines[0].Baseline) / (lines.Count - 1) : lines[0].Size * LineHeight;

        // Measured the way Word will lay it out, so that the gaps below add up and nothing creeps down the page.
        var bottom = lines[0].Top + pitch * lines.Count;

        return new ParagraphBlock(lines[0].Top, bottom, Merged(runs), left, right, AlignmentOf(lines, pageWidth), pitch, lines.Count > 1);
    }

    private static List<TextRun> Merged(List<TextRun> runs)
    {
        var merged = new List<TextRun>();
        foreach (var run in runs)
        {
            if (merged.Count > 0 && merged[^1] with { Text = run.Text } == run)
            {
                merged[^1] = merged[^1] with { Text = merged[^1].Text + run.Text };
            }
            else
            {
                merged.Add(run);
            }
        }

        return merged;
    }

    private static TextAlignment AlignmentOf(List<Line> lines, double pageWidth)
    {
        if (lines.Count > 1)
        {
            var sameLeft = lines.Max(line => line.Left) - lines.Min(line => line.Left) < 2;
            var sameCenter = lines.Max(line => line.Center) - lines.Min(line => line.Center) < CenteredTolerance;
            var sameRight = lines.Max(line => line.Right) - lines.Min(line => line.Right) < 2;

            return sameLeft ? TextAlignment.Left : sameCenter ? TextAlignment.Center : sameRight ? TextAlignment.Right : TextAlignment.Left;
        }

        return Math.Abs(lines[0].Center - pageWidth / 2) < CenteredTolerance ? TextAlignment.Center : TextAlignment.Left;
    }

    private static TableBlock BuildTable(List<Line> lines, IReadOnlyList<Fill> fills, IReadOnlyList<Rule> rules, HashSet<Fill> usedFills)
    {
        var edges = ColumnEdges(lines, fills);
        var widths = edges.Zip(edges.Skip(1), (start, end) => end - start).ToArray();
        var rows = new List<TableRow>();
        var tableBottom = lines[0].Top;

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];

            // A row runs from halfway to the row above to halfway to the row below.
            var top = index == 0 ? line.Top : (lines[index - 1].Bottom + line.Top) / 2;
            var bottom = index == lines.Count - 1 ? line.Bottom + (line.Top - top) : (line.Bottom + lines[index + 1].Top) / 2;
            var cells = new List<TableCell>();

            for (var column = 0; column < widths.Length; column++)
            {
                var (start, end) = (edges[column], edges[column + 1]);
                var inCell = line.Segments.Where(segment => segment.Center >= start && segment.Center < end).ToArray();
                var shade = fills
                    .Where(fill => fill.Contains((start + end) / 2, line.Baseline - line.Size * 0.3))
                    .OrderBy(fill => fill.Area)
                    .FirstOrDefault();

                if (shade is not null)
                {
                    usedFills.Add(shade);
                }

                var runs = inCell.SelectMany((segment, order) => order == 0
                    ? segment.Runs
                    : segment.Runs.Select((run, at) => at == 0 ? run with { Text = $" {run.Text}" } : run)).ToList();

                var alignment = CellAlignment(inCell, start, end);
                var indent = alignment == TextAlignment.Left && inCell.Length > 0 ? Math.Max(0, inCell[0].Left - start) : 0;
                cells.Add(new TableCell(Merged(runs), alignment, shade?.Color, indent));
            }

            var rule = rules.FirstOrDefault(candidate => candidate.Y >= line.Baseline && candidate.Y <= bottom + RuleReach && candidate.Right - candidate.Left > (edges[^1] - edges[0]) / 3);
            rows.Add(new TableRow(cells, bottom - top, rule?.Color));
            tableBottom = bottom;
        }

        return new TableBlock(lines[0].Top, tableBottom, edges[0], widths, rows);
    }

    /// <summary>
    /// Columns are where no text falls in any row. A colored cell's edge inside such a gap gives
    /// the exact line, and otherwise it goes down the middle.
    /// </summary>
    private static List<double> ColumnEdges(List<Line> lines, IReadOnlyList<Fill> allFills)
    {
        // Only what lies behind these rows says anything about their columns.
        var (top, bottom) = (lines[0].Top, lines[^1].Bottom);
        var fills = allFills.Where(fill => fill.Top < bottom && fill.Bottom > top).ToArray();
        var spans = lines.SelectMany(line => line.Segments).OrderBy(segment => segment.Left).ToList();
        var edges = new List<double>();
        var reach = spans[0].Right;

        foreach (var span in spans.Skip(1))
        {
            if (span.Left - reach >= MinimumColumnGutter)
            {
                var (from, to) = (reach, span.Left);
                var fillEdge = fills
                    .SelectMany(fill => new[] { fill.Left, fill.Right })
                    .Where(edge => edge > from && edge < to)
                    .Cast<double?>()
                    .FirstOrDefault();
                edges.Add(fillEdge ?? (from + to) / 2);
            }

            reach = Math.Max(reach, span.Right);
        }

        // A colored row can run far beyond its text, as when a heading is centered in it, and then it sets the table's sides.
        var size = lines.Max(line => line.Size);
        var rowBars = fills.Where(fill => fill.Bottom - fill.Top <= size * BarHeightInFontSizes && lines.Any(line => line.Segments.Any(segment => fill.Contains(segment.Center, line.Baseline - line.Size * 0.3)))).ToArray();
        var left = rowBars.Select(fill => fill.Left).Append(spans[0].Left).Min();
        var right = rowBars.Select(fill => fill.Right).Append(reach).Max();

        return [left, .. edges, right];
    }

    private static TextAlignment CellAlignment(IReadOnlyList<Segment> segments, double start, double end)
    {
        if (segments.Count == 0)
        {
            return TextAlignment.Left;
        }

        var (left, right) = (segments[0].Left - start, end - segments[^1].Right);
        return Math.Abs(left - right) < CenteredTolerance * 2 ? TextAlignment.Center : right < left ? TextAlignment.Right : TextAlignment.Left;
    }

    private static List<Fill> Fills(Page page, double height)
    {
        var fills = new List<Fill>();

        foreach (var path in page.Paths.Where(path => path.IsFilled))
        {
            if (path.GetBoundingRectangle() is not { } box || box.Width < MinimumShapeSide || box.Height < MinimumShapeSide)
            {
                continue;
            }

            var color = ToColor(path.FillColor);

            // White on a white page says nothing, and only gets in the way of editing.
            if (!color.IsNearWhite)
            {
                fills.Add(new Fill(box.Left, height - box.Top, box.Right, height - box.Bottom, color));
            }
        }

        return fills;
    }

    private static List<Rule> Rules(Page page, double height)
    {
        var rules = new List<Rule>();

        foreach (var path in page.Paths)
        {
            if (path.GetBoundingRectangle() is { } box && Math.Abs(box.Height) <= RuleThickness && Math.Abs(box.Width) >= RuleMinimumWidth)
            {
                var color = path.IsStroked ? path.StrokeColor : path.FillColor;
                rules.Add(new Rule(box.Left, box.Right, height - box.Bottom, ToColor(color)));
            }
        }

        return rules;
    }

    private static List<PagePicture> Pictures(Page page, double height)
    {
        var pictures = new List<PagePicture>();

        foreach (var image in page.GetImages())
        {
            var box = image.BoundingBox;
            if (box.Width < MinimumShapeSide || box.Height < MinimumShapeSide)
            {
                continue;
            }

            if (image.TryGetPng(out var png))
            {
                pictures.Add(new PagePicture(png, "png", box.Left, height - box.Top, box.Width, box.Height));
            }
            else if (image.RawMemory.Span is [0xFF, 0xD8, ..])
            {
                pictures.Add(new PagePicture(image.RawMemory.ToArray(), "jpeg", box.Left, height - box.Top, box.Width, box.Height));
            }
        }

        return pictures;
    }

    private static WordColor ToColor(IColor? color)
    {
        if (color is null)
        {
            return new WordColor(0, 0, 0);
        }

        try
        {
            var (red, green, blue) = color.ToRGBValues();
            return new WordColor(ToByte(red), ToByte(green), ToByte(blue));
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or NotImplementedException)
        {
            // A pattern has no single color, so a middling gray stands in for it.
            return new WordColor(128, 128, 128);
        }
    }

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);

    private sealed record PlacedWord(PdfWord Word, double Baseline, double Size);

    private sealed record Segment(double Left, double Right, IReadOnlyList<TextRun> Runs)
    {
        public double Center => (Left + Right) / 2;
    }

    private sealed record Line(IReadOnlyList<Segment> Segments, double Top, double Bottom, double Baseline, double Size)
    {
        public double Left => Segments[0].Left;

        public double Right => Segments[^1].Right;

        public double Center => (Left + Right) / 2;
    }

    private sealed record Fill(double Left, double Top, double Right, double Bottom, WordColor Color)
    {
        public double Area => (Right - Left) * (Bottom - Top);

        public bool Contains(double x, double y) => x >= Left && x <= Right && y >= Top && y <= Bottom;
    }

    private sealed record Rule(double Left, double Right, double Y, WordColor Color);
}
