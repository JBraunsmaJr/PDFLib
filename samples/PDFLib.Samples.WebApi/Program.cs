using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using PDFLib.Chromium;
using PDFLib.Chromium.Hosting;
using PDFLib.Samples.WebApi.Contracts;
using PDFLib.Samples.WebApi.Data;
using PDFLib.Samples.WebApi.Diagnostics;
using PDFLib.Samples.WebApi.Rendering;
using PDFLib.Samples.WebApi.Reports;
using BrowserOptions = PDFLib.Chromium.BrowserOptions;

var builder = WebApplication.CreateBuilder(args);

// One Chromium instance for the whole application, started with the host and configured from the
// "Chromium" section of appsettings.json (or Chromium__* environment variables).
builder.Services.AddPdfService(builder.Configuration.GetSection("Chromium"));

builder.Services.AddSingleton<ReportRenderer>();
builder.Services.AddSingleton<RenderStats>();
builder.Services.AddSingleton<ContractStore>();
builder.Services.AddSingleton<DemoCertificates>();
builder.Services.AddSingleton<MemorySampler>();
builder.Services.AddHostedService(services => services.GetRequiredService<MemorySampler>());

/*
 * Back-pressure, in two parts.
 *
 * MaxInFlightRequests is how many PDF requests are worked on at once; the rest wait in a queue. Keep it
 * close to Chromium:MaxConcurrentRenders. Every request being worked on holds an open Chromium page, and
 * pages are what cost memory: letting 16 requests in at once instead of 4 gave the same throughput in
 * testing but more than doubled Chromium's peak memory.
 *
 * MaxQueuedRequests is how many may wait. Once the queue is full, further requests are turned away
 * immediately with 503 instead of piling up until they time out.
 */
var maxInFlight = builder.Configuration.GetValue("Sample:MaxInFlightRequests", 4);
var maxQueued = builder.Configuration.GetValue("Sample:MaxQueuedRequests", 200);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status503ServiceUnavailable;
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.RequestServices.GetRequiredService<RenderStats>().Reject();
        context.HttpContext.Response.Headers.RetryAfter = "1";
        return ValueTask.CompletedTask;
    };
    options.AddConcurrencyLimiter("pdf", limiter =>
    {
        limiter.PermitLimit = maxInFlight;
        limiter.QueueLimit = maxQueued;
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});

var app = builder.Build();
app.UseRateLimiter();

app.MapGet("/", () => Results.Json(new
{
    name = "PDFLib sample API",
    reports = new[]
    {
        "/reports/invoice/1042",
        "/reports/statement/7731?months=6",
        "/reports/sales/2026"
    },
    preview = "Add ?format=html to any report to see the HTML that is sent to Chromium",
    contracts = new[]
    {
        "POST /contracts/{id}               render the agreement and sign it for the provider",
        "GET  /contracts/{id}               download the stored PDF",
        "GET  /contracts/{id}/signatures    list signature fields and whether each is signed",
        "POST /contracts/{id}/countersign   add the customer's signature to the stored PDF"
    },
    diagnostics = new[] { "GET /stats", "GET /stats/processes", "POST /stats/reset" }
}));

// ---------------------------------------------------------------------------------------------- reports

var reports = app.MapGroup("/reports").RequireRateLimiting("pdf");

reports.MapGet("/invoice/{number:int}", (int number, HttpContext context) =>
    WriteReport<Invoice>(context, SampleData.Invoice(number), $"invoice-{number}.pdf"));

reports.MapGet("/statement/{accountId:int}", (int accountId, int? months, HttpContext context) =>
    WriteReport<AccountStatement>(context, SampleData.Statement(accountId, months ?? 6), $"statement-{accountId}.pdf"));

reports.MapGet("/sales/{year:int:range(2000,2100)}", (int year, HttpContext context) =>
    WriteReport<SalesReport>(context, SampleData.Sales(year), $"sales-{year}.pdf"));

// ---------------------------------------------------------------------------------------------- contracts

var contracts = app.MapGroup("/contracts");

/*
 * Render the agreement and sign it for the provider in one step. The customer's signature field is
 * created at the same time but left empty, so the stored PDF can be countersigned later without the
 * browser.
 */
contracts.MapPost("/{id}", async (string id, HttpContext context, ReportRenderer renderer, PdfService pdf,
    ContractStore store, DemoCertificates certificates, RenderStats stats) =>
{
    var model = SampleData.Agreement(id);
    var html = await renderer.RenderAsync<ServiceAgreement>(model);

    stats.Begin();
    try
    {
        using var buffer = new MemoryStream();
        await pdf.RenderSignedPdfAsync(html, buffer,
            // What is printed inside the provider's signature box
            new Dictionary<string, Signature>
            {
                ["signature-area-provider"] = new(model.ProviderSignatory, DateTime.UtcNow.ToString("yyyy-MM-dd"))
            },
            signer => signer.AddCertificate(certificates.Provider, "signature-area-provider"),
            context.RequestAborted);

        var bytes = buffer.ToArray();
        store.Save(id, bytes);
        stats.Complete(bytes.Length);

        context.Response.Headers.Location = $"/contracts/{Uri.EscapeDataString(id)}";
        return Results.File(bytes, "application/pdf", $"agreement-{id}.pdf");
    }
    catch
    {
        stats.Fail();
        throw;
    }
}).RequireRateLimiting("pdf");

contracts.MapGet("/{id}", (string id, ContractStore store) =>
    store.TryGet(id, out var pdf)
        ? Results.File(pdf, "application/pdf", $"agreement-{id}.pdf")
        : Results.NotFound());

contracts.MapGet("/{id}/signatures", (string id, ContractStore store) =>
    store.TryGet(id, out var pdf)
        ? Results.Ok(new PdfSigner(pdf).GetSignatureFields())
        : Results.NotFound());

// Signing later: only the stored bytes and a certificate are needed. Chromium is not involved.
contracts.MapPost("/{id}/countersign", async (string id, ContractStore store, DemoCertificates certificates) =>
{
    if (!store.TryGet(id, out var pdf)) return Results.NotFound();

    var signer = new PdfSigner(pdf);
    signer.AddCertificate(certificates.Customer, "signature-area-customer");

    try
    {
        var countersigned = await signer.SignAsync();
        store.Save(id, countersigned);
        return Results.File(countersigned, "application/pdf", $"agreement-{id}.pdf");
    }
    catch (InvalidOperationException e)
    {
        // Already countersigned
        return Results.Conflict(new { error = e.Message });
    }
});

// ---------------------------------------------------------------------------------------------- diagnostics

app.MapGet("/stats", (RenderStats stats, MemorySampler memory, IOptions<BrowserOptions> options) =>
{
    static double Mb(long bytes) => Math.Round(bytes / 1024.0 / 1024.0, 1);
    static double? MbOrNull(long? bytes) => bytes.HasValue ? Mb(bytes.Value) : null;

    var current = memory.Current;
    var peak = memory.Peak;

    return Results.Json(new
    {
        uptimeSeconds = Math.Round((DateTime.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds),
        renders = new
        {
            completed = stats.Completed,
            failed = stats.Failed,
            rejected = stats.Rejected,
            inFlight = stats.InFlight,
            peakInFlight = stats.PeakInFlight,
            megabytesWritten = Mb(stats.BytesWritten)
        },
        memoryMb = new
        {
            managedHeap = Mb(current.ManagedHeap),
            appWorkingSet = Mb(current.AppWorkingSet),
            chromium = MbOrNull(current.Chromium),
            chromiumProcesses = current.ChromiumProcesses,
            // "pss" is the accurate figure; "rss" counts memory shared between Chromium's processes repeatedly
            chromiumMeasure = current.ChromiumMeasure
        },
        peakMemoryMb = new
        {
            managedHeap = Mb(peak.ManagedHeap),
            appWorkingSet = Mb(peak.AppWorkingSet),
            chromium = MbOrNull(peak.Chromium),
            chromiumProcesses = peak.ChromiumProcesses
        },
        options = new
        {
            options.Value.MaxConcurrentRenders,
            options.Value.PagePoolSize,
            options.Value.MaxRendersPerPage,
            options.Value.CollectGarbageInterval,
            options.Value.GenerateTaggedPdf,
            maxInFlightRequests = maxInFlight,
            maxQueuedRequests = maxQueued
        }
    });
});

// Where Chromium's memory is: one row per process, largest first
app.MapGet("/stats/processes", () =>
{
    static double Mb(long bytes) => Math.Round(bytes / 1024.0 / 1024.0, 1);

    var processes = MemorySampler.ReadChildProcesses();
    if (processes == null) return Results.Json(new { error = "Per-process memory is only available on Linux." });

    return Results.Json(new
    {
        totalPssMb = processes.All(p => p.Pss.HasValue) ? Mb(processes.Sum(p => p.Pss!.Value)) : (double?)null,
        totalRssMb = Mb(processes.Sum(p => p.Rss)),
        processes = processes
            .OrderByDescending(p => p.Pss ?? p.Rss)
            .Select(p => new { p.Pid, p.Kind, pssMb = p.Pss.HasValue ? Mb(p.Pss.Value) : (double?)null, rssMb = Mb(p.Rss) })
    });
});

app.MapPost("/stats/reset", (RenderStats stats, MemorySampler memory) =>
{
    stats.Reset();
    memory.ResetPeak();
    return Results.NoContent();
});

await app.RunAsync();

/*
 * Renders a report component to HTML, prints it and streams the PDF straight into the response.
 *
 * Nothing is buffered here: the bytes go from Chromium to the client as they are produced, so memory
 * use does not grow with the size of the document. The request's cancellation token is passed all the
 * way down, so a client that disconnects stops the work.
 */
static async Task WriteReport<TReport>(HttpContext context, object model, string fileName) where TReport : IComponent
{
    var services = context.RequestServices;
    var html = await services.GetRequiredService<ReportRenderer>().RenderAsync<TReport>(model);

    if (context.Request.Query["format"] == "html")
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(html, context.RequestAborted);
        return;
    }

    var stats = services.GetRequiredService<RenderStats>();
    context.Response.ContentType = "application/pdf";
    context.Response.Headers.ContentDisposition = $"inline; filename=\"{fileName}\"";

    var output = new CountingStream(context.Response.Body);
    stats.Begin();
    try
    {
        await services.GetRequiredService<PdfService>().RenderPdfAsync(html, output, context.RequestAborted);
        stats.Complete(output.BytesWritten);
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        stats.Fail(); // the client went away; there is nobody to answer
    }
    catch
    {
        stats.Fail();
        throw;
    }
}

public partial class Program;
