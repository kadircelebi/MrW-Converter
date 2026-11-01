using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace MrWConverter.Core.Pdf.Word;

/// <summary>
/// Writes pages as a Word file. Text and tables flow normally, so they can be edited, and sit where
/// they did through spacing. Pictures and colored boxes are anchored to the page behind the text.
/// </summary>
internal static class DocxWriter
{
    private const int TwipsPerPoint = 20;
    private const long EmusPerPoint = 12700;
    private const int MinimumSpacing = 0;
    private const double FixedWidthSlack = 12;
    private const double SideSlack = 4;
    private const double CellMarginPoints = 3;

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Pic = "http://schemas.openxmlformats.org/drawingml/2006/picture";
    private static readonly XNamespace Wps = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
    private static readonly XNamespace Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    public static void Write(IReadOnlyList<LayoutPage> pages, string outputPath)
    {
        var media = new MediaFiles();
        var drawingId = 1;
        var margins = Margins.Of(pages);
        var body = new XElement(W + "body");

        for (var index = 0; index < pages.Count; index++)
        {
            var page = pages[index];

            // A page size change closes a section, whose settings go on its last paragraph.
            if (index > 0 && (pages[index - 1].Width != page.Width || pages[index - 1].Height != page.Height))
            {
                body.Add(new XElement(W + "p", new XElement(W + "pPr", Section(pages[index - 1], margins))));
            }

            body.Add(PageAnchor(page, index > 0, margins, media, ref drawingId));

            var cursor = margins.Top;
            foreach (var block in page.Blocks)
            {
                var gap = Math.Max(MinimumSpacing, block.Top - cursor);

                switch (block)
                {
                    // Already placed on the page, in a text box.
                    case ParagraphBlock { FixedOnPage: true }:
                        continue;

                    case ParagraphBlock paragraph:
                        body.Add(Paragraph(paragraph, gap, page, margins));
                        break;

                    case TableBlock table:
                        // A table has no space before it, so an empty line of the right height makes it.
                        body.Add(Spacer(gap));
                        body.Add(Table(table, margins));
                        break;
                }

                cursor = block.Bottom;
            }
        }

        // Word needs a paragraph after a final table, and the last section's settings close the body.
        body.Add(TinyParagraph(false));
        body.Add(Section(pages[^1], margins));

        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(
                W + "document",
                new XAttribute(XNamespace.Xmlns + "w", W),
                new XAttribute(XNamespace.Xmlns + "r", R),
                new XAttribute(XNamespace.Xmlns + "wp", Wp),
                new XAttribute(XNamespace.Xmlns + "a", A),
                new XAttribute(XNamespace.Xmlns + "pic", Pic),
                new XAttribute(XNamespace.Xmlns + "wps", Wps),
                new XAttribute(XNamespace.Xmlns + "mc", Mc),
                new XAttribute(Mc + "Ignorable", "wps"),
                body));

        WritePackage(document, media, outputPath);
    }

    private static XElement Section(LayoutPage page, Margins margins) => new(
        W + "sectPr",
        new XElement(
            W + "pgSz",
            new XAttribute(W + "w", Twips(page.Width)),
            new XAttribute(W + "h", Twips(page.Height)),
            page.Width > page.Height ? new XAttribute(W + "orient", "landscape") : null),
        new XElement(
            W + "pgMar",
            new XAttribute(W + "top", Twips(margins.Top)),
            new XAttribute(W + "right", Twips(Math.Max(0, page.Width - margins.Right))),
            new XAttribute(W + "bottom", 0),
            new XAttribute(W + "left", Twips(margins.Left)),
            new XAttribute(W + "header", 0),
            new XAttribute(W + "footer", 0),
            new XAttribute(W + "gutter", 0)));

    /// <summary>Starts a page, and holds what is anchored to it: boxes first and the largest lowest.</summary>
    private static XElement PageAnchor(LayoutPage page, bool newPage, Margins margins, MediaFiles media, ref int drawingId)
    {
        var paragraph = TinyParagraph(newPage);
        var order = 1;

        foreach (var shape in page.Shapes.OrderByDescending(shape => shape.Width * shape.Height))
        {
            paragraph.Add(ShapeDrawing(shape, order++, drawingId++));
        }

        foreach (var picture in page.Pictures.OrderByDescending(picture => picture.Width * picture.Height))
        {
            paragraph.Add(PictureDrawing(picture, media.RelationshipFor(picture), order++, drawingId++));
        }

        foreach (var text in page.Blocks.OfType<ParagraphBlock>().Where(block => block.FixedOnPage))
        {
            paragraph.Add(TextBoxDrawing(text, order++, drawingId++));
        }

        return paragraph;
    }

    private static XElement TinyParagraph(bool pageBreakBefore) => new(
        W + "p",
        new XElement(
            W + "pPr",
            pageBreakBefore ? new XElement(W + "pageBreakBefore") : null,
            new XElement(W + "spacing", new XAttribute(W + "before", 0), new XAttribute(W + "after", 0), new XAttribute(W + "line", 20), new XAttribute(W + "lineRule", "exact")),
            new XElement(W + "rPr", new XElement(W + "sz", new XAttribute(W + "val", 2)))));

    private static XElement Spacer(double height) => new(
        W + "p",
        new XElement(
            W + "pPr",
            new XElement(W + "spacing", new XAttribute(W + "before", 0), new XAttribute(W + "after", 0), new XAttribute(W + "line", Math.Max(1, Twips(height))), new XAttribute(W + "lineRule", "exact")),
            new XElement(W + "rPr", new XElement(W + "sz", new XAttribute(W + "val", 2)))));


    private static XElement Paragraph(ParagraphBlock paragraph, double gap, LayoutPage page, Margins margins)
    {
        var properties = new XElement(
            W + "pPr",
            new XElement(
                W + "spacing",
                new XAttribute(W + "before", Twips(gap)),
                new XAttribute(W + "after", 0),
                new XAttribute(W + "line", Twips(paragraph.LinePitch)),
                new XAttribute(W + "lineRule", "exact")));

        // Several lines wrap where they did, in a paragraph as wide as its widest line was. A single line
        // has no reason to wrap at all, and a centered one is centered on the page.
        var left = paragraph.Left - margins.Left - SideSlack;
        var right = page.Width - paragraph.Right - SideSlack;
        if (!paragraph.SeveralLines)
        {
            (left, right) = paragraph.Alignment == TextAlignment.Center ? (0d, margins.Left) : (left + SideSlack, 0d);
        }

        properties.Add(new XElement(
            W + "ind",
            new XAttribute(W + "left", Twips(Math.Max(0, left))),
            new XAttribute(W + "right", Twips(Math.Max(0, right)))));

        if (paragraph.Alignment != TextAlignment.Left)
        {
            properties.Add(new XElement(W + "jc", new XAttribute(W + "val", paragraph.Alignment == TextAlignment.Center ? "center" : "right")));
        }

        return new XElement(W + "p", properties, paragraph.Runs.Select(Run));
    }

    private static XElement Table(TableBlock table, Margins margins)
    {
        var element = new XElement(
            W + "tbl",
            new XElement(
                W + "tblPr",
                new XElement(W + "tblW", new XAttribute(W + "w", Twips(table.ColumnWidths.Sum())), new XAttribute(W + "type", "dxa")),
                // Word measures this to the text inside the first cell, not to the table's edge.
                new XElement(W + "tblInd", new XAttribute(W + "w", Twips(Math.Max(0, table.Left - margins.Left + CellMarginPoints))), new XAttribute(W + "type", "dxa")),
                new XElement(W + "tblLayout", new XAttribute(W + "type", "fixed")),
                new XElement(
                    W + "tblCellMar",
                    new XElement(W + "left", new XAttribute(W + "w", Twips(CellMarginPoints)), new XAttribute(W + "type", "dxa")),
                    new XElement(W + "right", new XAttribute(W + "w", Twips(CellMarginPoints)), new XAttribute(W + "type", "dxa")))),
            new XElement(W + "tblGrid", table.ColumnWidths.Select(width => new XElement(W + "gridCol", new XAttribute(W + "w", Twips(width))))));

        for (var index = 0; index < table.Rows.Count; index++)
        {
            var row = table.Rows[index];
            element.Add(new XElement(
                W + "tr",
                new XElement(W + "trPr", new XElement(W + "trHeight", new XAttribute(W + "val", Twips(row.Height)), new XAttribute(W + "hRule", "atLeast"))),
                row.Cells.Select((cell, column) => Cell(cell, table.ColumnWidths[column], row.BottomRule))));
        }

        return element;
    }

    private static XElement Cell(TableCell cell, double width, WordColor? bottomRule)
    {
        var properties = new XElement(W + "tcPr", new XElement(W + "tcW", new XAttribute(W + "w", Twips(width)), new XAttribute(W + "type", "dxa")));

        if (bottomRule is { } rule)
        {
            properties.Add(new XElement(
                W + "tcBorders",
                new XElement(W + "bottom", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", 4), new XAttribute(W + "space", 0), new XAttribute(W + "color", rule.Hex))));
        }

        if (cell.Shade is { } shade)
        {
            properties.Add(new XElement(W + "shd", new XAttribute(W + "val", "clear"), new XAttribute(W + "color", "auto"), new XAttribute(W + "fill", shade.Hex)));
        }

        properties.Add(new XElement(W + "vAlign", new XAttribute(W + "val", "center")));

        var paragraphProperties = new XElement(W + "pPr", new XElement(W + "spacing", new XAttribute(W + "before", 0), new XAttribute(W + "after", 0)));
        if (cell.Indent > CellMarginPoints)
        {
            paragraphProperties.Add(new XElement(W + "ind", new XAttribute(W + "left", Twips(cell.Indent - CellMarginPoints))));
        }

        if (cell.Alignment != TextAlignment.Left)
        {
            paragraphProperties.Add(new XElement(W + "jc", new XAttribute(W + "val", cell.Alignment == TextAlignment.Center ? "center" : "right")));
        }

        return new XElement(W + "tc", properties, new XElement(W + "p", paragraphProperties, cell.Runs.Select(Run)));
    }

    private static XElement Run(TextRun run)
    {
        var properties = new XElement(
            W + "rPr",
            new XElement(
                W + "rFonts",
                new XAttribute(W + "ascii", run.Font),
                new XAttribute(W + "hAnsi", run.Font),
                new XAttribute(W + "cs", run.Font),
                new XAttribute(W + "eastAsia", run.Font)),
            run.Bold ? new XElement(W + "b") : null,
            run.Italic ? new XElement(W + "i") : null,
            new XElement(W + "color", new XAttribute(W + "val", run.Color.Hex)),
            new XElement(W + "sz", new XAttribute(W + "val", Math.Max(2, (int)Math.Round(run.Size * 2)))));

        return new XElement(
            W + "r",
            properties,
            new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), XmlSafe(run.Text)));
    }

    private static XElement Anchor(double left, double top, double width, double height, int order, int id, string name, XElement graphic, bool behindText = true) => new(
        W + "r",
        new XElement(
            W + "drawing",
            new XElement(
                Wp + "anchor",
                new XAttribute("distT", 0),
                new XAttribute("distB", 0),
                new XAttribute("distL", 0),
                new XAttribute("distR", 0),
                new XAttribute("simplePos", 0),
                new XAttribute("relativeHeight", order),
                new XAttribute("behindDoc", behindText ? 1 : 0),
                new XAttribute("locked", 0),
                new XAttribute("layoutInCell", 1),
                new XAttribute("allowOverlap", 1),
                new XElement(Wp + "simplePos", new XAttribute("x", 0), new XAttribute("y", 0)),
                new XElement(Wp + "positionH", new XAttribute("relativeFrom", "page"), new XElement(Wp + "posOffset", Emus(left))),
                new XElement(Wp + "positionV", new XAttribute("relativeFrom", "page"), new XElement(Wp + "posOffset", Emus(top))),
                new XElement(Wp + "extent", new XAttribute("cx", Emus(width)), new XAttribute("cy", Emus(height))),
                new XElement(Wp + "effectExtent", new XAttribute("l", 0), new XAttribute("t", 0), new XAttribute("r", 0), new XAttribute("b", 0)),
                new XElement(Wp + "wrapNone"),
                new XElement(Wp + "docPr", new XAttribute("id", id), new XAttribute("name", name)),
                new XElement(Wp + "cNvGraphicFramePr"),
                new XElement(A + "graphic", graphic))));

    private static XElement PictureDrawing(PagePicture picture, string relationship, int order, int id) => Anchor(
        picture.Left,
        picture.Top,
        picture.Width,
        picture.Height,
        order,
        id,
        $"Picture {id}",
        new XElement(
            A + "graphicData",
            new XAttribute("uri", Pic.NamespaceName),
            new XElement(
                Pic + "pic",
                new XElement(Pic + "nvPicPr", new XElement(Pic + "cNvPr", new XAttribute("id", id), new XAttribute("name", $"Picture {id}")), new XElement(Pic + "cNvPicPr")),
                new XElement(Pic + "blipFill", new XElement(A + "blip", new XAttribute(R + "embed", relationship)), new XElement(A + "stretch", new XElement(A + "fillRect"))),
                new XElement(
                    Pic + "spPr",
                    new XElement(A + "xfrm", new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)), new XElement(A + "ext", new XAttribute("cx", Emus(picture.Width)), new XAttribute("cy", Emus(picture.Height)))),
                    new XElement(A + "prstGeom", new XAttribute("prst", "rect"), new XElement(A + "avLst"))))));

    /// <summary>Writing that sits at one place on the page: it grows with its text rather than wrapping, and nothing flows around it.</summary>
    private static XElement TextBoxDrawing(ParagraphBlock text, int order, int id)
    {
        var (width, height) = (text.Right - text.Left + FixedWidthSlack, text.LinePitch);

        return Anchor(
            text.Left,
            text.Top,
            width,
            height,
            order,
            id,
            $"Text {id}",
            new XElement(
                A + "graphicData",
                new XAttribute("uri", Wps.NamespaceName),
                new XElement(
                    Wps + "wsp",
                    new XElement(Wps + "cNvSpPr", new XAttribute("txBox", 1)),
                    new XElement(
                        Wps + "spPr",
                        new XElement(A + "xfrm", new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)), new XElement(A + "ext", new XAttribute("cx", Emus(width)), new XAttribute("cy", Emus(height)))),
                        new XElement(A + "prstGeom", new XAttribute("prst", "rect"), new XElement(A + "avLst")),
                        new XElement(A + "noFill"),
                        new XElement(A + "ln", new XElement(A + "noFill"))),
                    new XElement(
                        Wps + "txbx",
                        new XElement(
                            W + "txbxContent",
                            new XElement(
                                W + "p",
                                new XElement(W + "pPr", new XElement(W + "spacing", new XAttribute(W + "before", 0), new XAttribute(W + "after", 0))),
                                text.Runs.Select(Run)))),
                    new XElement(
                        Wps + "bodyPr",
                        new XAttribute("wrap", "none"),
                        new XAttribute("lIns", 0),
                        new XAttribute("tIns", 0),
                        new XAttribute("rIns", 0),
                        new XAttribute("bIns", 0),
                        new XElement(A + "spAutoFit")))),
            behindText: false);
    }

    private static XElement ShapeDrawing(PageShape shape, int order, int id) => Anchor(
        shape.Left,
        shape.Top,
        shape.Width,
        shape.Height,
        order,
        id,
        $"Shape {id}",
        new XElement(
            A + "graphicData",
            new XAttribute("uri", Wps.NamespaceName),
            new XElement(
                Wps + "wsp",
                new XElement(Wps + "cNvSpPr"),
                new XElement(
                    Wps + "spPr",
                    new XElement(A + "xfrm", new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)), new XElement(A + "ext", new XAttribute("cx", Emus(shape.Width)), new XAttribute("cy", Emus(shape.Height)))),
                    new XElement(A + "prstGeom", new XAttribute("prst", "rect"), new XElement(A + "avLst")),
                    new XElement(A + "solidFill", new XElement(A + "srgbClr", new XAttribute("val", shape.Color.Hex))),
                    new XElement(A + "ln", new XElement(A + "noFill"))),
                new XElement(Wps + "bodyPr"))));

    private static void WritePackage(XDocument document, MediaFiles media, string outputPath)
    {
        using var output = File.Create(outputPath);
        using var zip = new ZipArchive(output, ZipArchiveMode.Create);

        void Add(string name, string content)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }

        Add("[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
            <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
            <Default Extension="xml" ContentType="application/xml"/>
            <Default Extension="png" ContentType="image/png"/>
            <Default Extension="jpeg" ContentType="image/jpeg"/>
            <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
            </Types>
            """);

        Add("_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
            <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """);

        Add("word/document.xml", document.Declaration + Environment.NewLine + document.ToString(SaveOptions.DisableFormatting));
        Add("word/_rels/document.xml.rels", media.Relationships());

        foreach (var (name, bytes) in media.Files)
        {
            var entry = zip.CreateEntry($"word/media/{name}", CompressionLevel.NoCompression);
            using var stream = entry.Open();
            stream.Write(bytes);
        }
    }

    private static int Twips(double points) => (int)Math.Round(points * TwipsPerPoint);

    private static long Emus(double points) => (long)Math.Round(points * EmusPerPoint);

    /// <summary>Some PDFs carry control characters that XML doesn't allow at all.</summary>
    private static string XmlSafe(string text)
    {
        var safe = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (XmlConvert.IsXmlChar(character) || char.IsSurrogate(character))
            {
                safe.Append(character);
            }
        }

        return safe.ToString();
    }

    /// <summary>The margins fit all the text, so that nothing depends on them but its starting place.</summary>
    private sealed record Margins(double Left, double Top, double Right)
    {
        public static Margins Of(IReadOnlyList<LayoutPage> pages)
        {
            var blocks = pages.SelectMany(page => page.Blocks).ToArray();
            if (blocks.Length == 0)
            {
                return new Margins(0, 0, pages.Max(page => page.Width));
            }

            var lefts = blocks.Select(block => block switch { ParagraphBlock paragraph => paragraph.Left, TableBlock table => table.Left, _ => 0 });
            return new Margins(Math.Max(0, lefts.Min() - 1), Math.Max(0, blocks.Min(block => block.Top) - 1), pages.Max(page => page.Width));
        }
    }

    /// <summary>Each distinct picture once, however many pages repeat it.</summary>
    private sealed class MediaFiles
    {
        private readonly Dictionary<string, string> _relationshipByHash = [];
        private readonly StringBuilder _relationships = new();

        public List<(string Name, byte[] Bytes)> Files { get; } = [];

        public string RelationshipFor(PagePicture picture)
        {
            var hash = Convert.ToHexString(SHA256.HashData(picture.Bytes));
            if (_relationshipByHash.TryGetValue(hash, out var existing))
            {
                return existing;
            }

            var id = $"rId{Files.Count + 1}";
            var name = $"image{Files.Count + 1}.{picture.Extension}";
            Files.Add((name, picture.Bytes));
            _relationships.Append($"""<Relationship Id="{id}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/{name}"/>""");
            _relationshipByHash[hash] = id;
            return id;
        }

        public string Relationships() =>
            $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">{_relationships}</Relationships>""";
    }
}
