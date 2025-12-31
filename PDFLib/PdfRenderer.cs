using System.Xml.Linq;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PDFLib;

public class PdfRenderer
{
    private readonly IServiceProvider _serviceProvider;

    public PdfRenderer()
    {
        var services = new ServiceCollection();
        services.AddLogging(l => l.AddProvider(NullLoggerProvider.Instance));
        _serviceProvider = services.BuildServiceProvider();
    }

    public async Task RenderToDocumentAsync<TComponent>(PdfDocument doc, IDictionary<string, object?> parameters) where TComponent : IComponent
    {
        await using var renderer = new HtmlRenderer(_serviceProvider, NullLoggerFactory.Instance);
        
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters));
            return output.ToHtmlString();
        });

        var docXml = XElement.Parse($"<root>{html}</root>");
        
        // Root could be <document> or it might directly contain <page> elements
        var root = docXml.Element("document") ?? docXml;
        var globalHeader = root.Element("header");
        var globalFooter = root.Element("footer");

        var pageElements = root.Elements("page").ToList();
        var totalPages = pageElements.Count;

        for (int i = 0; i < pageElements.Count; i++)
        {
            var pageElement = pageElements[i];
            var isFirstPage = i == 0;
            var pageNumber = i + 1;

            var pdfPage = doc.AddPage();
            pdfPage.AddFont("F1", "Helvetica");
            pdfPage.AddFont("F2", "Helvetica-Bold");

            var padding = int.Parse(pageElement.Attribute("padding")?.Value ?? "20");
            var currentY = 842 - padding;
            var usableWidth = 595 - (padding * 2);

            // Handle Header
            var firstPageHeader = pageElement.Element("first-page-header");
            var header = pageElement.Element("header") ?? globalHeader;
            var headerToRender = (isFirstPage && firstPageHeader != null) ? firstPageHeader : header;

            if (headerToRender != null)
            {
                currentY = RenderElement(doc, pdfPage, headerToRender, padding, currentY, usableWidth, pageNumber, totalPages);
                pdfPage.DrawLine(padding, currentY - 5, 595 - padding, currentY - 5);
                currentY -= 20;
            }

            // Handle Body (everything else except header/footer/first-page elements)
            var specialElements = new[] { "header", "footer", "first-page-header", "first-page-footer" };
            foreach (var element in pageElement.Elements().Where(e => !specialElements.Contains(e.Name.LocalName)))
            {
                currentY = RenderElement(doc, pdfPage, element, padding, currentY, usableWidth, pageNumber, totalPages);
            }

            // Handle Footer
            var firstPageFooter = pageElement.Element("first-page-footer");
            var footer = pageElement.Element("footer") ?? globalFooter;
            var footerToRender = (isFirstPage && firstPageFooter != null) ? firstPageFooter : footer;

            if (footerToRender != null)
            {
                /*
                 * Render foot near the bottom of the page
                 * The footer content can grow upwards, so we might need to handle it better.
                 * For not, let's just use a fixed low Y
                 */
                var footerY = padding + 60; 
                RenderElement(doc, pdfPage, footerToRender, padding, footerY, usableWidth, pageNumber, totalPages);
            }

            pdfPage.Build(compress: true);
        }
    }

    private int RenderElement(PdfDocument doc, PdfPage page, XElement element, int x, int y, int width, int pageNumber, int totalPages)
    {
        switch (element.Name.LocalName)
        {
            case "row":
                return RenderRow(doc, page, element, x, y, width, pageNumber, totalPages);
            case "stack":
            case "header": 
            case "footer":
            case "first-page-header":
            case "first-page-footer":
                return RenderStack(doc, page, element, x, y, width, pageNumber, totalPages);
            case "text":
                return RenderText(page, element, x, y, width, pageNumber, totalPages);
            case "image":
                return RenderImage(page, element, x, y, width);
            case "table":
                return RenderTable(page, element, x, y, width);
            case "signature":
                return RenderSignature(doc, page, element, x, y, width);
            case "page-number":
                return RenderPageNumber(page, element, x, y, width, pageNumber, totalPages);
            default:
                // If it's an unknown element, we still might want to render its children
                foreach (var child in element.Elements())
                {
                    y = RenderElement(doc, page, child, x, y, width, pageNumber, totalPages);
                }
                return y;
        }
    }

    private int RenderRow(PdfDocument doc, PdfPage page, XElement element, int x, int y, int width, int pageNumber, int totalPages)
    {
        var children = element.Elements().ToList();
        if (children.Count == 0) return y;

        var childWidth = width / children.Count;
        var maxY = y;
        var startX = x;

        foreach (var child in children)
        {
            var resultY = RenderElement(doc, page, child, startX, y, childWidth, pageNumber, totalPages);
            if (resultY < maxY) maxY = resultY;
            startX += childWidth;
        }

        return maxY;
    }

    private int RenderStack(PdfDocument doc, PdfPage page, XElement element, int x, int y, int width, int pageNumber, int totalPages)
    {
        foreach (var child in element.Elements())
        {
            y = RenderElement(doc, page, child, x, y, width, pageNumber, totalPages);
        }
        return y;
    }

    private int RenderText(PdfPage page, XElement element, int x, int y, int width, int pageNumber, int totalPages)
    {
        if (!int.TryParse(element.Attribute("fontsize")?.Value, out var fontSize))
            fontSize = 12;
        
        var align = element.Attribute("align")?.Value ?? "Left";
        var text = element.Value.Trim();
        
        // Replace placeholders if any (though usually text doesn't have them, but maybe?)
        text = text.Replace("{n}", pageNumber.ToString()).Replace("{x}", totalPages.ToString());

        var fontAlias = fontSize > 15 ? "F2" : "F1";

        var lines = TextMeasurer.WrapText(text, width, fontSize);
        var currentY = y;

        foreach (var line in lines)
        {
            // Simple alignment (not perfect, doesn't measure text width)
            var textX = x;
            if (align == "Right")
            {
                textX = x + width - (line.Length * (fontSize / 2)); // Very rough estimation
            }

            page.DrawText(fontAlias, fontSize, textX, currentY - fontSize, line);
            currentY -= (fontSize + 2);
        }

        return currentY - 3;
    }

    /// <summary>
    /// Renders an image
    /// </summary>
    /// <param name="page">Page image should render on</param>
    /// <param name="element"></param>
    /// <param name="x">X coordinate</param>
    /// <param name="y">Y coordinate</param>
    /// <param name="width">Default width if "width" attribute is not provided</param>
    /// <param name="height">Default height if "height" attribute is not provided</param>
    /// <returns></returns>
    private int RenderImage(PdfPage page, XElement element, int x, int y, int width=100, int height=100)
    {
        var source = element.Attribute("source")?.Value;
        
        if (string.IsNullOrEmpty(source)) return y;

        if (!int.TryParse(element.Attribute("width")?.Value, out var imgWidth))
            imgWidth = width;
        
        if(!int.TryParse(element.Attribute("height")?.Value, out var imgHeight))
            imgHeight = height;
        
        try
        {
            using var img = PdfImage.FromFile(source);
            page.DrawImage("Img" + Guid.NewGuid().ToString("N"), img, x, y - imgHeight, imgWidth, imgHeight);
            return y - imgHeight - 5;
        }
        catch
        {
            page.DrawText("F1", 10, x, y - 10, $"[Image not found: {source}]");
            return y - 15;
        }
    }

    private int RenderTable(PdfPage page, XElement element, int x, int y, int width)
    {
        var columnWidthsStr = element.Attribute("columnwidths")?.Value;
        float[] columnWidths;

        if (!string.IsNullOrEmpty(columnWidthsStr))
        {
            columnWidths = columnWidthsStr.Split(',').Select(float.Parse).ToArray();
        }
        else
        {
            // Default to equal widths based on children of the first row
            var firstRow = element.Element("tr");
            var cellCount = firstRow?.Elements("td").Count() ?? 1;
            columnWidths = Enumerable.Repeat((float)width / cellCount, cellCount).ToArray();
        }

        var table = new PdfTable(columnWidths);
        foreach (var rowElement in element.Elements("tr"))
        {
            var cells = rowElement.Elements("td").Select(e => e.Value.Trim()).ToArray();
            table.AddRow(cells);
        }

        return table.Render(page, x, y) - 10;
    }

    private int RenderSignature(PdfDocument doc, PdfPage page, XElement element, int x, int y, int width)
    {
        var name = element.Attribute("name")?.Value ?? "default";
        var sigX = int.Parse(element.Attribute("x")?.Value ?? x.ToString());
        var sigY = int.Parse(element.Attribute("y")?.Value ?? (y - 50).ToString());
        var sigWidth = int.Parse(element.Attribute("width")?.Value ?? "150");
        var sigHeight = int.Parse(element.Attribute("height")?.Value ?? "50");

        doc.SetSignatureAppearance(name, sigX, sigY, sigWidth, sigHeight);
        doc.AddSignatureToPage(page, name);
        
        return sigY; 
    }

    private int RenderPageNumber(PdfPage page, XElement element, int x, int y, int width, int pageNumber, int totalPages)
    {
        var format = element.Attribute("format")?.Value ?? "Page {n} of {x}";
        var align = element.Attribute("align")?.Value ?? "Left";
        var text = format.Replace("{n}", pageNumber.ToString()).Replace("{x}", totalPages.ToString());
        
        var fontSize = 10;
        var textX = x;
        if (align == "Right")
        {
            textX = x + width - (text.Length * (fontSize / 2));
        }

        page.DrawText("F1", fontSize, textX, y - fontSize, text);
        return y - 15;
    }
}
