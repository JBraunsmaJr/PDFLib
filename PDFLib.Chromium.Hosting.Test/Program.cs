using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Mvc;
using PDFLib.Chromium.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddPdfService();

var app = builder.Build();

app.MapGet("/", () => "OK");

var sampleHtml = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sample.html"));
var signSample = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sign.html"));

app.MapGet("/print", async (HttpContext request, [FromServices] PdfService service) =>
{
    request.Response.ContentType = "application/pdf";
    await service.RenderPdfAsync(sampleHtml, request.Response.Body);
});

app.MapGet("/sign", async (HttpContext request, [FromServices] PdfService service) =>
{
    request.Response.ContentType = "application/pdf";
    using var rsa1 = RSA.Create(2048);
    var request1 = new CertificateRequest("cn=Test Signer 1", rsa1, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    using var cert1 = request1.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddYears(1));

    await service.RenderSignedPdfAsync(signSample, request.Response.Body, (signer) =>
    {
        signer.AddCertificate(cert1, "signature-area-1");
    });
});

await app.RunAsync();

namespace PDFLib.Chromium.Hosting.Test
{
    public partial class Program { }
}
