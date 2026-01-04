using PuppeteerSharp;

namespace PDFApi.Services;

public class PuppeteerBrowserService : IBrowsingService, IAsyncDisposable
{
    private IBrowser _browser;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private async Task<IBrowser> GetBrowserAsync()
    {
        if (_browser is not null && _browser.IsConnected) return _browser;

        await _lock.WaitAsync();

        try
        {
            if (_browser is not null && _browser.IsConnected) return _browser;

            var launchOptions = new LaunchOptions
            {
                Headless = true,
                Args = new[]
                {
                    "--no-sandbox",
                    "--disable-setuid-sandbox",
                    "--disable-dev-shm-usage", // Docker/Linux OOM
                    "--disable-gpu"
                }
            };

            _browser = await Puppeteer.LaunchAsync(launchOptions);
            return _browser;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null) await _browser.DisposeAsync();
    }

    public async Task<Stream> RenderHtmlToPdfStreamAsync(string htmlContent)
    {
        var browser = await GetBrowserAsync();

        var context = await browser.CreateBrowserContextAsync();
        await using var page = await context.NewPageAsync();

        // Intercepts requests to prevent reading local files (SSRF protection)
        await page.SetRequestInterceptionAsync(true);

        page.Request += (_, e) =>
        {
            if (e.Request.ResourceType == ResourceType.Image || e.Request.Url.StartsWith("file://"))
            {
                if (e.Request.Url.StartsWith("file://"))
                    e.Request.AbortAsync();
                else
                    e.Request.ContinueAsync();
            }
            else
                e.Request.ContinueAsync();
        };

        await page.SetContentAsync(htmlContent, new NavigationOptions
        {
            WaitUntil = [WaitUntilNavigation.Networkidle0]
        });

        return await page.PdfStreamAsync();
    }
}