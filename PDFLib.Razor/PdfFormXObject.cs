using System.Text;
using PDFLib.Models;

namespace PDFLib;

/// <summary>
/// Represents a PDF Form XObject, which is a reusable content stream.
/// Used for visual signature appearances.
/// </summary>
public class PdfFormXObject : PdfStreamObject
{
    private readonly StreamWriter _contentWriter;
    private readonly PdfDictionary _fonts;
    private readonly PdfDictionary _resources;
    private readonly MemoryStream _tempStream;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfFormXObject"/> class with specified dimensions.
    /// </summary>
    /// <param name="width">The width of the form object.</param>
    /// <param name="height">The height of the form object.</param>
    public PdfFormXObject(int width, int height) : base(new PdfDictionary(), Array.Empty<byte>())
    {
        _tempStream = new MemoryStream();
        _contentWriter = new StreamWriter(_tempStream, Encoding.ASCII);
        _resources = new PdfDictionary();
        _fonts = new PdfDictionary();
        _resources.Add("/Font", _fonts);

        _dict.Add("/Type", new PdfName("/XObject"));
        _dict.Add("/Subtype", new PdfName("/Form"));

        var bbox = new PdfArray(
            new PdfNumber(0),
            new PdfNumber(0),
            new PdfNumber(width),
            new PdfNumber(height)
        );
        _dict.Add("/BBox", bbox);
        _dict.Add("/Resources", _resources);
    }

    /// <summary>
    /// Adds a font resource to the form object.
    /// </summary>
    /// <param name="alias">The font alias (e.g., "F1").</param>
    /// <param name="fontName">The base font name (e.g., "Helvetica").</param>
    /// <param name="doc">The parent PDF document to register the font with.</param>
    public void AddFont(string alias, string fontName, PdfDocument doc)
    {
        var fontDict = new PdfDictionary();
        fontDict.Add("/Type", new PdfName("/Font"));
        fontDict.Add("/Subtype", new PdfName("/Type1"));
        fontDict.Add("/BaseFont", new PdfName(fontName));

        var fontId = doc.RegisterObject(fontDict);
        _fonts.Add($"/{alias}", new PdfReference(fontId));
    }

    /// <summary>
    /// Draws text within the form object.
    /// </summary>
    /// <param name="fontAlias">The alias of the font to use.</param>
    /// <param name="size">The font size.</param>
    /// <param name="x">The X coordinate.</param>
    /// <param name="y">The Y coordinate.</param>
    /// <param name="text">The text to draw.</param>
    public void DrawText(string fontAlias, int size, int x, int y, string text)
    {
        _contentWriter.Write($"BT /{fontAlias} {size} Tf {x} {y} Td ({text}) Tj ET\n");
    }

    /// <summary>
    /// Draws a line within the form object.
    /// </summary>
    /// <param name="x1">Start X.</param>
    /// <param name="y1">Start Y.</param>
    /// <param name="x2">End X.</param>
    /// <param name="y2">End Y.</param>
    public void DrawLine(int x1, int y1, int x2, int y2)
    {
        _contentWriter.Write($"{x1} {y1} m {x2} {y2} l S\n");
    }

    /// <summary>
    /// Finalizes the form object content.
    /// </summary>
    public void Build()
    {
        _contentWriter.Flush();
        _content = _tempStream.ToArray();
        _dict.Add("/Length", new PdfNumber(_content.Length));
    }
}