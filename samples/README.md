# Samples

Two projects that show the library doing realistic work:

| Project | What it is |
|---|---|
| `PDFLib.Samples.WebApi` | An ASP.NET Core API that renders four kinds of report to PDF, handles concurrent requests, and signs documents now and later. |
| `PDFLib.Samples.LoadTest` | A console tool that drives the API with concurrent clients and reports requests per minute, latency and memory. |

## Running the API

The API needs a Chromium headless shell. Any one of these works:

- **Docker** (nothing to install): `docker compose up pdflib.samples.api`, from the repository root.
- **A binary you already have:** set `Chromium__BinaryPath=/path/to/chrome-headless-shell`.
- **`chrome-shell` on the `PATH`:** the library falls back to it when nothing is bundled or configured.

```bash
dotnet run -c Release --project samples/PDFLib.Samples.WebApi --urls http://localhost:5080
```

Then open any report in a browser, or save it with `curl`:

```bash
curl -o invoice.pdf   http://localhost:5080/reports/invoice/1042
curl -o statement.pdf "http://localhost:5080/reports/statement/7731?months=6"
curl -o sales.pdf     http://localhost:5080/reports/sales/2026
```

Add `?format=html` to a report URL to see the exact HTML that is sent to Chromium. That is the quickest
way to work on a template: edit, refresh, and print to PDF only when it looks right.

## The reports

Each report is a Razor component under `Reports/`, rendered to HTML by `ReportRenderer` and printed by
`PdfService`. Data comes from `SampleData`, which derives everything from the id in the URL: the same
id always gives the same document, and a different id gives a different one.

| Report | Endpoint | Shows |
|---|---|---|
| Invoice | `GET /reports/invoice/{number}` | Letterhead, address blocks, line items with discounts, totals, payment details. One or two pages. |
| Account statement | `GET /reports/statement/{account}?months=N` | A table that runs over many pages: headings repeat on each page, rows never split, months are grouped. About two pages per month. |
| Sales performance | `GET /reports/sales/{year}` | Landscape. A page of KPI tiles and four charts drawn as inline SVG, then a page of tables. No JavaScript and no charting library. |
| Service agreement | `POST /contracts/{id}` | Numbered clauses, a fee schedule and two signature areas. Signed digitally; see below. |

Things worth copying from the templates:

- **`ReportLayout.razor` wraps every report.** `HtmlRenderer` ignores `@layout`, so the report brings its own
  `<head>` and inlines its stylesheet. Chromium gets one self-contained document with nothing to fetch.
- **Page setup is CSS.** `@page` sets the paper size and margins, and its margin boxes print
  "Page 3 of 12" and a footer on every page. Landscape is one parameter on the layout.
- **Charts are SVG written by the template.** They need no script to run before printing and stay
  vector-sharp at any zoom.
- **`Fmt` formats with the invariant culture**, so output is identical on a developer machine and in a
  container running in globalization-invariant mode.
- **`<text>` is reserved in Razor.** SVG text goes through the small `SvgText` component instead.

## Signing now and later

The agreement has two elements whose ids start with `signature-area-`. The library turns each into a
signature field in the PDF.

```bash
# Render the agreement and sign it for the provider. The customer's field is created but left empty.
curl -X POST -o agreement.pdf http://localhost:5080/contracts/SA-2026-0117

# Which fields exist, and which are signed?
curl http://localhost:5080/contracts/SA-2026-0117/signatures
# [{"name":"signature-area-provider","isSigned":true},{"name":"signature-area-customer","isSigned":false}]

# Later: add the customer's signature to the stored PDF. Chromium is not involved.
curl -X POST -o agreement.pdf http://localhost:5080/contracts/SA-2026-0117/countersign
```

Both signatures validate, and the first is untouched by the second. Countersigning twice returns `409`.

The certificates are self-signed and created at start-up (`DemoCertificates`), so a PDF reader will show
the signatures as valid but from an unknown issuer. The store is in memory and keeps the last 200
documents. Both are stand-ins: a real service uses issued certificates and durable storage, and nothing
else about the flow changes.

## Concurrency

`Program.cs` is written the way a production endpoint would be:

- **One browser for the whole application.** `AddPdfService` starts Chromium with the host and stops it on
  shutdown. Requests share it; each gets its own page from a warm pool.
- **Streaming.** The PDF goes from Chromium into the response as it is produced. Nothing is buffered, so
  memory does not grow with document size.
- **Cancellation.** `HttpContext.RequestAborted` is passed down, so a client that disconnects stops the work.
- **Back-pressure.** A concurrency limiter lets a fixed number of PDF requests run, queues the next ones,
  and answers `503` with `Retry-After` once the queue is full, instead of letting requests pile up.

The settings, in `appsettings.json` or as environment variables (`Chromium__PagePoolSize=8`):

| Setting | Default | Meaning |
|---|---|---|
| `Chromium:MaxConcurrentRenders` | 4 | Documents printed at the same time. |
| `Chromium:PagePoolSize` | 4 | Warm pages kept between requests. |
| `Chromium:CollectGarbageInterval` | 0 | Collect garbage in a pooled page after every N renders. `4` cut Chromium's peak memory by about 47% in testing, with no measurable loss of throughput. |
| `Chromium:GenerateTaggedPdf` | true | Accessibility tags. Turning them off is faster and gives much smaller files. |
| `Sample:MaxInFlightRequests` | 4 | PDF requests worked on at once. Keep it close to `MaxConcurrentRenders`. |
| `Sample:MaxQueuedRequests` | 200 | Requests allowed to wait before the API answers `503`. |

`GET /stats` reports what the API has done and what it is using:

```json
{
  "renders": { "completed": 284, "failed": 0, "rejected": 0, "inFlight": 2, "peakInFlight": 4 },
  "memoryMb": { "managedHeap": 21.4, "appWorkingSet": 149.0, "chromium": 471.3, "chromiumProcesses": 9 },
  "peakMemoryMb": { "managedHeap": 38.2, "appWorkingSet": 153.1, "chromium": 483.0, "chromiumProcesses": 9 }
}
```

The Chromium figure is the whole Chromium process tree, measured on Linux from `/proc`. It is usually
the larger of the two, so a memory number that leaves it out is misleading.

`chromiumMeasure` says how that figure was obtained. `pss` is the accurate one: memory shared between
Chromium's processes is counted once. If the kernel refuses that reading the sampler falls back to
`rss`, which counts shared memory once per process and overstates the total two to three times.
`GET /stats/processes` breaks the figure down by process (browser, GPU, each renderer) with both
measures side by side, which shows where the memory is.

## Load test

One command builds both projects, starts the API, runs the test and stops the API:

```bash
samples/run-loadtest.sh
samples/run-loadtest.sh -- --levels 1,4,8,16,32 --duration 30 --scenario invoice
Chromium__MaxConcurrentRenders=8 Sample__MaxInFlightRequests=8 samples/run-loadtest.sh
```

Or in Docker, with results written to `samples/results/`:

```bash
docker compose run --rm pdflib.samples.loadtest
```

To test an API that is already running: `dotnet run -c Release --project samples/PDFLib.Samples.LoadTest -- --url http://host:5080`.

| Option | Default | Meaning |
|---|---|---|
| `--levels` | `1,2,4,8,16` | Concurrent clients; one run per level. |
| `--duration` | `20` | Seconds per level. |
| `--warmup` | `5` | Seconds of warm-up before the first level. |
| `--scenario` | `mix` | `mix`, `invoice`, `statement`, `sales` or `contract`. |
| `--out` | `loadtest-results` | Results go to `<out>.md` and `<out>.csv`. |
| `--max-error-rate` | `0.01` | The tool exits with code 1 above this, so it can gate a CI job. |

Each client sends a request, reads the entire PDF, checks that it is one, and sends the next. Ids are
random, so every request renders a different document. The `mix` scenario is 50% invoices, 20%
statements, 20% sales reports and 10% signed agreements.

### Example output

```
Clients  Requests   Req/min     Mean      p50      p95      p99  Errors   503s   App MB  Chromium MB
      1       204       815     74ms     61ms    145ms    174ms       0      0      149          458
      2       284      1132    106ms     89ms    190ms    306ms       0      0      153          483
      4       298      1183    202ms    176ms    396ms    563ms       0      0      161          779
```

These numbers are from a **2-core** development container and are here to show the format, not as a
benchmark. Throughput levels off at the number of cores, because printing is CPU work inside Chromium;
run it on your own hardware for figures that mean something.

### What the numbers tell you

- **Requests per minute stop rising once clients exceed cores.** More clients then only add waiting time.
- **Memory follows the number of requests being worked on, not the number of clients.** With 16 clients,
  letting 16 requests in at once peaked at about 1.5 GB of Chromium memory; letting 4 in and queueing the
  rest peaked at about 0.65 GB with the same throughput. That is why `MaxInFlightRequests` defaults to 4.
- **Reused pages hold on to memory.** A page that has printed a few dozen documents held about three
  times as much as a fresh one, because Chromium has no reason to collect garbage in an idle page.
  `Chromium__CollectGarbageInterval=4` cut Chromium's peak memory by about 47% with no measurable loss of
  throughput; `1` cut it by about 55% for up to 8% less.
- **Tagged PDFs cost throughput.** Setting `Chromium__GenerateTaggedPdf=false` gave roughly 10 to 20% more
  requests per minute on the same machine. Leave tagging on if your PDFs must be accessible.
