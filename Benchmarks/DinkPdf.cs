using DinkToPdf;
using DinkToPdf.Contracts;

namespace Benchmarks;

public class DinkPdf : IConverter
{
    /*
     * wkhtmltopdf must be initialised and used from one and the same thread. SynchronizedConverter owns a
     * dedicated worker thread and marshals every conversion onto it. With the in-process toolchain each
     * benchmark case runs on a new thread, and BasicConverter hung on the second case.
     */
    private static readonly SynchronizedConverter Converter = new(new PdfTools());
    private HtmlToPdfDocument? _doc;

    public DinkPdf()
    {
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
        Converter.Convert(doc);
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