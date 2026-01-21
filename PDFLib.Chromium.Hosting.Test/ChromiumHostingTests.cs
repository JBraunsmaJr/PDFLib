using System.Net;

namespace PDFLib.Chromium.Hosting.Test;

[CollectionDefinition("Integration Tests")]
public class IntegrationTestCollection : ICollectionFixture<TestWebApplicationFactory>
{
}

[Collection("Integration Tests")]
public class ChromiumHostingTests : IAsyncLifetime
{
    private readonly HttpClient _client;
    private readonly IServiceProvider _services;
    private readonly TestWebApplicationFactory _factory;
    public ChromiumHostingTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _services = factory.Services;
    }
    
    [Fact]
    public void AddChromiumBrowser_RegistersServices()
    {
        // Assert
        var browser = _services.GetService<ChromiumBrowser>();
        var hostedService = _services.GetServices<IHostedService>().OfType<PdfService>().FirstOrDefault();

        Assert.NotNull(browser);
        Assert.NotNull(hostedService);
        Assert.Same(ChromiumBrowser.Instance, browser);
    }

    [Fact]
    public async Task CanPrintPdf()
    {
        var response = await _client.GetAsync("/print");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsByteArrayAsync();
        Assert.True(content.Length > 0);
    }

    [Fact]
    public async Task CanSignPdf()
    {
        var response = await _client.GetAsync("/sign");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsByteArrayAsync();
        Assert.True(content.Length > 0);
    }

    public Task InitializeAsync()
    {
        // Ensure the server is started
        _ = _factory.Server;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
    }
}
