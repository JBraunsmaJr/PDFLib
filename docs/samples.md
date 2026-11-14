# Samples and load testing

The repository has a sample Web API and a load-test tool under
[`samples/`](https://github.com/JBraunsmaJr/PDFLib/tree/main/samples). The
[samples README](https://github.com/JBraunsmaJr/PDFLib/blob/main/samples/README.md) covers how to run
them; this page summarises what they demonstrate.

## Reports

Four reports, each a Razor component rendered to HTML and printed by `PdfService`:

| Report | Demonstrates |
|---|---|
| Invoice | Letterhead, address blocks, line items, totals. |
| Account statement | A table spanning many pages with repeating headings and grouped rows. |
| Sales performance | Landscape layout, KPI tiles and charts as inline SVG, with no JavaScript. |
| Service agreement | Signature areas, signing at render time and countersigning later. |

The templates share one layout component that inlines the stylesheet and sets up the page with CSS
`@page`, including "Page X of Y" footers.

## Handling concurrent requests

The API shows the pattern to use in a real service:

```csharp
builder.Services.AddPdfService(builder.Configuration.GetSection("Chromium"));

app.MapGet("/reports/invoice/{number:int}", async (int number, HttpContext context,
    ReportRenderer renderer, PdfService pdf) =>
{
    var html = await renderer.RenderAsync<Invoice>(SampleData.Invoice(number));

    context.Response.ContentType = "application/pdf";
    // Streams from Chromium straight into the response, and stops if the client disconnects
    await pdf.RenderPdfAsync(html, context.Response.Body, context.RequestAborted);
});
```

One browser serves every request; each request takes a warm page from the pool. A concurrency limiter
in front of the endpoints bounds how many requests are worked on at once and how many may wait, and
answers `503` beyond that.

> Bound the number of requests worked on at once, not only the number printing. Each one holds an open
> Chromium page, and pages are what cost memory. Keep that limit close to `MaxConcurrentRenders`.

## Measuring throughput and memory

```bash
samples/run-loadtest.sh -- --levels 1,2,4,8,16 --duration 20
```

The tool runs each concurrency level for a fixed time and reports requests per minute, latency
percentiles, and peak memory for both the .NET process and the Chromium process tree. Results are
written as Markdown and CSV. It exits with a non-zero code if the error rate is too high, so it can
gate a CI job.

## Sample Load

| Setting | Value |
| --- | --- |
| maxConcurrentRenders | 4 |
| pagePoolSize | 4|
| maxRendersPerPage | 100 |
| collectGarbageCollection| 4 |
| generateTaggedPdf | true |
| maxInFlightResponses | 4 |
| maxQueuedRequests| 200 |

Results:

| Clients | Requests | Req/min | Mean | p50 | p95 | p99 | Errors | 503s | App MB | Chromium MB |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | 223 | 668 | 90 ms | 60 ms | 201 ms | 253 ms | 0 | 0 | 150 | 257 |
| 2 | 321 | 958 | 125 ms | 93 ms | 238 ms | 287 ms | 0 | 0 | 166 | 272 |
| 4 | 407 | 1212 | 197 ms | 143 ms | 366 ms | 413 ms | 0 | 0 | 176 | 350 |
| 8 | 411 | 1201 | 396 ms | 390 ms | 609 ms | 692 ms | 0 | 0 | 187 | 342 |
| 16 | 403 | 1156 | 811 ms | 806 ms | 1077 ms | 1212 ms | 0 | 0 | 196 | 388 |