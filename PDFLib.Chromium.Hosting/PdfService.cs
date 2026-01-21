using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PDFLib.Chromium.Hosting;

/// <summary>
/// A hosted service that manages the lifecycle of the <see cref="ChromiumBrowser"/>.
/// </summary>
public class PdfService : IHostedService
{
    private readonly ChromiumBrowser _browser;
    private readonly BrowserOptions _options;
    private readonly ILogger<PdfService> _logger;
    public PdfService(ChromiumBrowser browser, IOptions<BrowserOptions> options, ILogger<PdfService> logger)
    {
        _browser = browser;
        _logger = logger;
        _options = options.Value;
    }

    /// <summary>
    /// Renders HTML to a PDF and writes it to the destination stream.
    /// </summary>
    /// <param name="html">The HTML content to render.</param>
    /// <param name="destinationStream">The stream where the PDF will be written.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async Task RenderPdfAsync(string html, Stream destinationStream, CancellationToken cancellationToken = default)
    {
        await using var page = await _browser.CreatePageAsync();
        await page.PrintToPdfAsync(html, destinationStream, null, cancellationToken);
    }

    /// <summary>
    /// Renders HTML to a PDF, signs it using the provided certificates, and writes it to the destination stream.
    /// </summary>
    /// <param name="html">The HTML content to render.</param>
    /// <param name="destinationStream">The stream where the signed PDF will be written.</param>
    /// <param name="setupSigner">An action to configure the <see cref="PdfSigner"/> with certificates.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async Task RenderSignedPdfAsync(string html, Stream destinationStream, Action<PdfSigner> setupSigner, CancellationToken cancellationToken = default)
    {
        await using var page = await _browser.CreatePageAsync();
        using var ms = new MemoryStream();
        
        var zones = await page.PrintToPdfAsync(html, ms, null, cancellationToken);
        
        var signer = new PdfSigner(ms.ToArray(), zones);
        setupSigner(signer);
        
        var signedBytes = await signer.SignAsync();
        
        await destinationStream.WriteAsync(signedBytes, cancellationToken);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting ChromiumBrowser Service...");
        await _browser.StartAsync(_options);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping ChromiumBrowser Service...");
        _browser.Dispose();
        return Task.CompletedTask;
    }
}
