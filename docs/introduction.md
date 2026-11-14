# Basic Usage

PDFLib provides a simple way to convert HTML and CSS to PDF using a headless Chromium browser.

## Initializing the Browser

The `ChromiumBrowser` class manages the lifecycle of the Chromium process. It's recommended to maintain a single instance of the browser throughout your application's lifetime.

```csharp
using PDFLib.Chromium;

// Initialize the browser
using var browser = new ChromiumBrowser();
await browser.StartAsync(new BrowserOptions());
```

## Basic HTML to PDF

To generate a PDF, you create a new page (similar to a tab in a browser), set its content, and then print it to a PDF stream.

```csharp
var html = "<html><body><h1>Hello World</h1><p>This is a PDF generated from HTML.</p></body></html>";
string outputPath = "output.pdf";

await using var outputPdf = File.Create(outputPath);
await using var page = await browser.CreatePageAsync();


// Print to PDF
await page.PrintToPdfAsync(html, outputPdf);
await outputPdf.FlushAsync();
```

### The short way (pooled pages)

`RenderPdfAsync` does the same thing in one call and reuses warm pages between renders. Opening a new
page starts a new renderer process in Chromium, which takes longer than rendering a small document, so
for many small PDFs this is roughly twice as fast as creating a page per render.

```csharp
await using var outputPdf = File.Create("output.pdf");
await browser.RenderPdfAsync(html, outputPdf);
```

Each pooled page is navigated to a fresh blank document before it is reused, so scripts and DOM state
never carry over from one render to the next. The pool is controlled by two options:

| Option | Default | Meaning |
|---|---|---|
| `PagePoolSize` | `min(ProcessorCount, 4)` | Idle pages kept warm. Each one keeps a Chromium renderer process alive, so this trades memory for speed. `0` disables pooling. |
| `MaxRendersPerPage` | `100` | Renders a page serves before it is closed and replaced. |

> If you keep a page yourself and call `SetContentAsync` on it more than once, be aware that the JavaScript
> global object survives between documents. Use `RenderPdfAsync` (or `RentPageAsync` / `ReturnPageAsync`)
> when documents must be isolated from each other.

> For PDFs with **no signature** you can stream the response back to the user! Should be able to avoid
> using `MemoryStream` - as a result you won't see memory spikes since the PDF isn't being stored in memory.
>
> For PDFs with **signature** it's unavoidable to have the PDF fully in memory. Sadly, the PDF specification
> doesn't allow for one-pass operations.
