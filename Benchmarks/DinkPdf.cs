using DinkToPdf;

namespace Benchmarks;

public class DinkPdf : IConverter
{
    private readonly BasicConverter _converter;
    private HtmlToPdfDocument? _doc;

    public DinkPdf()
    {
        _converter = new BasicConverter(new PdfTools());
    }

    public Task ConvertAsync(string html)
    {
        var doc = new HtmlToPdfDocument
        {
            GlobalSettings =
            {
                ColorMode = ColorMode.Color,
                Orientation = Orientation.Landscape,
                PaperSize = PaperKind.A4
            },
            Objects =
            {
                new ObjectSettings
                {
                    PagesCount = true,
                    HtmlContent = html,
                    WebSettings = { DefaultEncoding = "utf-8" },
                    HeaderSettings =
                    {
                        FontSize = 9,
                        Right = "Page [page] of [toPage]",
                        Line = true,
                        Spacing = 2.812
                    }
                }
            }
        };
        _converter.Convert(doc);
        return Task.CompletedTask;
    }

    public Task IterationSetupAsync(string html)
    {
        return Task.CompletedTask;
    }

    public Task GlobalSetupAsync()
    {
        return Task.CompletedTask;
    }

    public Task IterationCleanupAsync()
    {
        _doc = null;
        return Task.CompletedTask;
    }

    public Task GlobalCleanupAsync()
    {
        return Task.CompletedTask;
    }
}