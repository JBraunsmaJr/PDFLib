using System.IO.Compression;
using System.Text;
using PDFLib.Models;

namespace PDFLib;

/// <summary>
/// Represents a single page within a <see cref="PdfDocument"/> and provides methods for drawing content.
/// </summary>
public class PdfPage
{
    private readonly StreamWriter _contentWriter;
    private readonly PdfDocument _doc;
    private readonly PdfDictionary _fonts;
    private readonly PdfDictionary _resources;
    private readonly Stream _tempStream;
    private readonly PdfDictionary _xobjects;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfPage"/> class.
    /// </summary>
    /// <param name="doc">The parent PDF document.</param>
    /// <param name="pageId">The object ID for this page.</param>
    /// <param name="parentId">The object ID of the parent /Pages node.</param>
    public PdfPage(PdfDocument doc, int pageId, int parentId)
    {
        _doc = doc;
        _tempStream = new MemoryStream();
        _contentWriter = new StreamWriter(_tempStream, Encoding.ASCII);
        _resources = new PdfDictionary();
        _fonts = new PdfDictionary();
        _xobjects = new PdfDictionary();
        _resources.Add("/Font", _fonts);
        _resources.Add("/XObject", _xobjects);

        PageDict = new PdfDictionary();
        PageDict.ObjectId = pageId;
        PageDict.Add("/Type", new PdfName("/Page"));
        PageDict.Add("/Parent", new PdfReference(parentId));

        var mediaBox = new PdfArray();
        string[] a4 = ["0", "0", "595", "842"];

        foreach (var i in a4)
            mediaBox.Add(new PdfNumber(i));

        PageDict.Add("/MediaBox", mediaBox);
        PageDict.Add("/Resources", _resources);
    }

    /// <summary>
    /// Gets the dictionary representing this page's properties.
    /// </summary>
    public PdfDictionary PageDict { get; }

    /// <summary>
    /// Adds a font resource to the page.
    /// </summary>
    /// <param name="alias">The alias name to use in drawing operations (e.g., "F1").</param>
    /// <param name="fontName">The name of the base font (e.g., "Helvetica").</param>
    public void AddFont(string alias, string fontName)
    {
        var fontDict = new PdfDictionary();
        fontDict.Add("/Type", new PdfName("/Font"));
        fontDict.Add("/Subtype", new PdfName("/Type1"));
        fontDict.Add("/BaseFont", new PdfName(fontName));

        var fontId = _doc.RegisterObject(fontDict);
        _fonts.Add("/" + alias, new PdfReference(fontId));
    }

    /// <summary>
    /// Draws text on the page.
    /// </summary>
    /// <param name="fontAlias">The alias of the font to use.</param>
    /// <param name="size">The font size.</param>
    /// <param name="x">The X coordinate.</param>
    /// <param name="y">The Y coordinate.</param>
    /// <param name="text">The text to draw.</param>
    /// <param name="color">The text color (hex or named).</param>
    public void DrawText(string fontAlias, int size, int x, int y, string text, string? color = null)
    {
        SetFillColor(color ?? "black");
        _contentWriter.Write($"BT /{fontAlias} {size} Tf {x} {y} Td ({text}) Tj ET\n");
    }

    /// <summary>
    /// Draws a line between two points.
    /// </summary>
    /// <param name="x1">The start X coordinate.</param>
    /// <param name="y1">The start Y coordinate.</param>
    /// <param name="x2">The end X coordinate.</param>
    /// <param name="y2">The end Y coordinate.</param>
    public void DrawLine(int x1, int y1, int x2, int y2)
    {
        _contentWriter.Write($"{x1} {y1} m {x2} {y2} l S\n");
    }

    /// <summary>
    /// Draws an image on the page.
    /// </summary>
    /// <param name="alias">The alias name for the image resource.</param>
    /// <param name="image">The image object to draw.</param>
    /// <param name="x">The X coordinate.</param>
    /// <param name="y">The Y coordinate.</param>
    /// <param name="width">The width of the image.</param>
    /// <param name="height">The height of the image.</param>
    public void DrawImage(string alias, PdfImage image, int x, int y, int width, int height)
    {
        if (image.ObjectId == null)
            _doc.RegisterObject(image);

        if (image.ObjectId.HasValue)
        {
            _xobjects.Add("/" + alias, new PdfReference(image.ObjectId.Value));
            _contentWriter.Write($"q {width} 0 0 {height} {x} {y} cm /{alias} Do Q\n");
        }
    }

    /// <summary>
    /// Renders a table on the page.
    /// </summary>
    /// <param name="table">The table to render.</param>
    /// <param name="x">The X coordinate of the top-left corner of the table.</param>
    /// <param name="y">The Y coordinate of the top-left corner of the table.</param>
    public void DrawTable(PdfTable table, int x, int y)
    {
        table.Render(this, x, y);
    }

    /// <summary>
    /// Draws a rectangle on the page.
    /// </summary>
    /// <param name="x">The X coordinate.</param>
    /// <param name="y">The Y coordinate.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="fillColor">The fill color (optional).</param>
    /// <param name="strokeColor">The stroke color (optional).</param>
    public void DrawRectangle(int x, int y, int width, int height, string? fillColor = null, string? strokeColor = null)
    {
        if (fillColor != null)
        {
            SetFillColor(fillColor);
            _contentWriter.Write($"{x} {y} {width} {height} re f\n");
        }

        if (strokeColor != null)
        {
            SetStrokeColor(strokeColor);
            _contentWriter.Write($"{x} {y} {width} {height} re s\n");
        }
        else if (fillColor == null)
        {
            _contentWriter.Write($"{x} {y} {width} {height} re S\n");
        }
    }

    /// <summary>
    /// Sets the fill color for subsequent drawing operations.
    /// </summary>
    /// <param name="color">The color to set (hex or named).</param>
    public void SetFillColor(string color)
    {
        var (r, g, b) = ParseColor(color);
        _contentWriter.Write($"{r:0.###} {g:0.###} {b:0.###} rg\n");
    }

    /// <summary>
    /// Sets the stroke color for subsequent drawing operations.
    /// </summary>
    /// <param name="color">The color to set (hex or named).</param>
    public void SetStrokeColor(string color)
    {
        var (r, g, b) = ParseColor(color);
        _contentWriter.Write($"{r:0.###} {g:0.###} {b:0.###} RG\n");
    }

    private (double r, double g, double b) ParseColor(string color)
    {
        if (color.StartsWith("#"))
        {
            color = color.TrimStart('#');
            if (color.Length == 6)
            {
                var r = Convert.ToInt32(color.Substring(0, 2), 16) / 255.0;
                var g = Convert.ToInt32(color.Substring(2, 2), 16) / 255.0;
                var b = Convert.ToInt32(color.Substring(4, 2), 16) / 255.0;
                return (r, g, b);
            }

            if (color.Length == 3)
            {
                var r = Convert.ToInt32(new string(color[0], 2), 16) / 255.0;
                var g = Convert.ToInt32(new string(color[1], 2), 16) / 255.0;
                var b = Convert.ToInt32(new string(color[2], 2), 16) / 255.0;
                return (r, g, b);
            }
        }

        // Basic named colors support
        return color.ToLower() switch
        {
            "black" => (0, 0, 0),
            "white" => (1, 1, 1),
            "red" => (1, 0, 0),
            "green" => (0, 1, 0),
            "blue" => (0, 0, 1),
            "gray" => (0.5, 0.5, 0.5),
            "lightgray" => (0.75, 0.75, 0.75),
            "yellow" => (1, 1, 0),
            "cyan" => (0, 1, 1),
            "magenta" => (1, 0, 1),
            "orange" => (1, 0.65, 0),
            "purple" => (0.5, 0, 0.5),
            _ => (0, 0, 0)
        };
    }

    /// <summary>
    /// Finalizes the page content and writes it to the document.
    /// </summary>
    /// <param name="compress">Whether to compress the page content using FlateDecode.</param>
    public void Build(bool compress = false)
    {
        _contentWriter.Flush();
        _tempStream.Seek(0, SeekOrigin.Begin);

        var streamDict = new PdfDictionary();
        byte[] finalContent;

        if (compress)
        {
            using var ms = new MemoryStream();
            using (var ds = new ZLibStream(ms, CompressionLevel.Optimal))
            {
                _tempStream.CopyTo(ds);
            }

            finalContent = ms.ToArray();
            streamDict.Add("/Filter", new PdfName("/FlateDecode"));
        }
        else
        {
            using var ms = new MemoryStream();
            _tempStream.CopyTo(ms);
            finalContent = ms.ToArray();
        }

        streamDict.Add("/Length", new PdfNumber(finalContent.Length));

        var streamObj = new PdfStreamObject(streamDict, finalContent);
        var streamId = _doc.RegisterObject(streamObj);
        PageDict.Add("/Contents", new PdfReference(streamId));

        _doc.WriteObject(PageDict);
    }
}