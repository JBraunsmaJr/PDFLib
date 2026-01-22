# AspNetCore

The `Badger.PDFLib.Chromium.Hosting` package provides an approach for integrating
the Chromium browser into ASP.NET Core applications as a singleton service.

An example project can be found [here](https://github.com/JBraunsmaJr/PDFLib.Example).

From the example project, the notable files are:

- Dockerfile
- Program.cs
- BlazorRenderer.cs

---

### HTML

In order to create the PDF, we need the HTML representation of our Blazor/Razor application!
A few versions ago, AspNetCore provided us with an `HtmlRenderer` class. The example leverages this
for generically rendering our Blazor components.

For the Blazor/Razor components to support injecting services, they need access to the service provider.

The `BlazorRenderer` is not included in the Hosting project. There are perhaps alternative ways for converting 
components into HTML, did not want to force such a dependency. Perhaps if the community wants it, it can
be added.

```csharp
public class BlazorRenderer
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerFactory _loggerFactory;

    public BlazorRenderer(IServiceProvider serviceProvider, ILoggerFactory loggerFactory)
    {
        _serviceProvider = serviceProvider;
        _loggerFactory = loggerFactory;
    }

    public async Task<string> RenderComponent<T>(Dictionary<string, object?>? parameters = null) where T : IComponent
    {
        await using var renderer = new HtmlRenderer(_serviceProvider, _loggerFactory);

        var parameterView = parameters != null ? ParameterView.FromDictionary(parameters) : ParameterView.Empty;

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<T>(parameterView);
            return output.ToHtmlString();
        });

        return html;
    }
}
```

### Program.cs
 
To add the PDF service the following extension method is available to you.

```csharp
builder.Services.AddPdfService();
```

```csharp
builder.Services.AddPdfService(options => 
{
    MaxConcurrentRenderers = 10;
});
```

```csharp
builder.Services.AddPdfService(builder.Configuration.GetSection("Chromium"));
```

### Examples of usage

The example project contains a simple component called `Report`. To use it with minimal API's you can do the following. Take
note in the usage of `context.Response.Body`. Bytes are streamed to the user.
```csharp
app.MapGet("/print", async (HttpContext context,[FromServices] PdfService pdfService, [FromServices] BlazorRenderer renderer) =>
{
    // We can write directly to the response body
    var html = await renderer.RenderComponent<Report>();
    await pdfService.RenderPdfAsync(html, context.Response.Body);
});
```

An example of signing a PDF:

```csharp
app.MapGet("/signature", async ([FromServices] PdfService pdfService, [FromServices] BlazorRenderer renderer) =>
{
    var html = await renderer.RenderComponent<Report>();
    
    // Note: No using statement here.
    var ms = new MemoryStream();

    // Obviously, this is for example-sake. You wouldn't handle the certs in this way 
    using var rsa1 = RSA.Create(2048);
    var request1 = new CertificateRequest("cn=Test Signer 1", rsa1, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    using var cert1 = request1.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddYears(1));
    
    await pdfService.RenderSignedPdfAsync(html, ms, new()
    {
        {"signature-area-1", new Signature("Test Signer 1", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))}
    },
        signer =>
    {
        // Because the signature-area-1 on our Report page.
        signer.AddCertificate(cert1, "signature-area-1");
    });

    /*
        Since we were writing to the stream above, we have to remember
        to reset the pointer to the beginning; otherwise, you'll get a
        content length mismatch because the stream position is at the end
     */
    
    ms.Seek(0, SeekOrigin.Begin);
    
    /*
        The Results.File takes ownership of the stream and will 
        automatically close it when done.
        
        This means - DO NOT - use a 'using' statement. Otherwise the
        stream will fail due to the stream being closed.
    */
    
    return Results.File(
        fileStream: ms,
        contentType: "application/pdf",
        fileDownloadName: "SignedReport.pdf"
    );
});
```