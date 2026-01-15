using System.Text.Json.Serialization;

namespace PDFLib.Chromium;

/// <summary>
/// Represents a detected signature zone in the HTML DOM, with coordinates and dimensions.
/// </summary>
public class SignatureZone
{
    /// <summary>
    /// Gets or sets the ID of the HTML element representing the signature area.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; }

    /// <summary>
    /// Gets or sets the X-coordinate of the signature area in browser pixels.
    /// </summary>
    [JsonPropertyName("x")]
    public double X { get; set; }

    /// <summary>
    /// Gets or sets the Y-coordinate of the signature area in browser pixels.
    /// </summary>
    [JsonPropertyName("y")]
    public double Y { get; set; }

    /// <summary>
    /// Gets or sets the width of the signature area in browser pixels.
    /// </summary>
    [JsonPropertyName("width")]
    public double Width { get; set; }

    /// <summary>
    /// Gets or sets the height of the signature area in browser pixels.
    /// </summary>
    [JsonPropertyName("height")]
    public double Height { get; set; }
    

    /// <summary>
    /// Converts the browser pixel coordinates into PDF points (1/72 inch).
    /// </summary>
    /// <param name="pdfPageHeight">The height of the PDF page in points (e.g., 841.89 for A4).</param>
    /// <returns>A tuple containing (x, y, width, height, pageNumber) in PDF-compatible coordinates.</returns>
    public (double x, double y, double width, double height, int pageNumber) ToPdfCoordinates(
        double pdfPageHeight = 792.0)
    {
        var scale = 0.75;

        var xPts = X * scale;
        var yPtsTotal = Y * scale;
        var widthPts = Width * scale;
        var heightPts = Height * scale;

        var pageNumber = (int)Math.Floor(yPtsTotal / pdfPageHeight) + 1;

        /*
         * Invert the y-axis
         * Browsers: 0 is the top, while for PDFs it is the bottom.
         * We find the Y position relative to the current page then
         * subtract from the page height
         */
        
        var yRelativePageTop = yPtsTotal % pdfPageHeight;
        var yPdfBottom = pdfPageHeight - yRelativePageTop - heightPts;

        return (xPts, yPdfBottom, widthPts, heightPts, pageNumber);
    }
}