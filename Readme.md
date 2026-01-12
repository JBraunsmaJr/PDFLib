# PDF Lib

Join me in the descent into madness, as I explore the possibilities of PDF generation.

Honestly, this was a product of me being bored and wondering what it would take
to generate a PDF without using external libraries. It... is... interesting to say the least.

As I progressed through the stages of wonder... I realized that there's some potential
in it.

This project is still in its early stages and is not yet ready for production use.


![comparison chart](./assets/benchmarks.png)

----

Implemented "Targeted Parsing", by using Utf8JsonReader to find the result/error property first, then only parse that specific subtree instead of
the entire message.

Traded small allocations (High GC pressure) for larger upfront allocations (1MB buffer).

A more predicatable working set with 1 MB scratch buffer.

Learned that the u8 literals are specially handled by the JIT compiler. It embeds UTF-8 bytes in the assembly data section, 
avoiding heap allocations. By using the predefined literals in variables we lost the fragmentation handling `ValueTextEquals` provides.

Zero Allocation Routing:
Changed `CdpDispatcher.ProcessMessage`, we use `GetCachedStringZeroAlloc` with `ValueTextEquals` to match event names directly
against raw UTF-8 bytes. We previously allocated a string to check `_eventHandlers` dictionary keys. Now we maintain a small cache 
list of known event names and use `reader.ValueTextEquals` to match raw bytes without allocation. This handles both contiguous and fragmented 
data (from `PipeReader`) and correctly processes JSON escapes. For unknown message ID/metadata we `Skip()` entirely without allocating.

----

# Overview

The C# Project **PDFLib.Razor** is a library which manually crafts PDFs, without using third-party libraries. Went on to explore
the possibility of using Razor syntax to generate PDFs as well, instead of pursuing a fluent-api. After playing with the idea,
I realized the difficulty in pursuing styling. Developers would also require a "PDF" version of their webpages, which is
not ideal.

To leverage existing HTML/CSS, one requires a renderer. WebKit and Blink are the two most popular choices. For the time being,
I've opted to use Chrome's headless Chromium since it's said to be pixel-perfect when creating PDFs.

**PDFLib.Chromium** is where Chromium is being explored. The difficulty lies in extracting as much performance out of it as possible. There is an inherit overhead by using a browser (albeit headless)
as it has to render using Blink engine, and runs the V8 engine for javascript.

# Research / Path

## The Renderer / Headless Chromium

[Headless Chromium](https://github.com/chromium/chromium/blob/main/headless/README.md) is a package
google made available which contains the Blink and V8 engines, 200MB than full browser.

## Interop

IronPDF, based on some snooping, appears to have some form of C++ shared libraries they created.

Google suggests using Puppeteer, or websockets. However, I suspect using a named pipe (linux only) will be
enough.

The `--remote-debugging-pipe` flag leverages file descriptors 3 (Standard in/out), and 4. .NET only supports 0, 1, and
2.
So we have to redirect things.

## Base64

Chromium's CDP sends data using Base64 inside JSON, so there is no way around this in the current DevTools
Protocol.

Base64 is ~33% larger than raw binary. However, by reading 1MB, the 33% overhead yields about 1.33MB of string data.
Although not ideal, it's fairly negligible considering the state of the modern internet.

## Known things

Apparently the following errors are "normal" and do not impact PDF generation

```
[0104/155522.469691:ERROR:dbus/bus.cc:406] Failed to connect to the bus: Failed to connect to socket /run/dbus/system_bus_socket: No such file or directory                                               
[0104/155522.470946:ERROR:dbus/bus.cc:406] Failed to connect to the bus: Failed to connect to socket /run/dbus/system_bus_socket: No such file or directory
[0104/155522.471026:ERROR:dbus/bus.cc:406] Failed to connect to the bus: Failed to connect to socket /run/dbus/system_bus_socket: No such file or directory
[0104/155522.567381:WARNING:device/bluetooth/dbus/bluez_dbus_manager.cc:209] Floss manager service not available, cannot set Floss enable/disable.
```

## Benchmarks currently

| File                 | Page Count |
|----------------------|------------|
| sample.html          | 2          |
| large-sample.html    | 140        |
| x2-large-sample.html | 280        |
| x3-large-sample.html | 420        |

At first, the library out performed DinkToPdf for smaller workloads. That changed with larger workloads. Memory allocations ballooned and speed took a hit.

| Method | FileName             |       Mean |     Error |    StdDev |     Median |      Gen0 |      Gen1 |   Allocated |
|--------|----------------------|-----------:|----------:|----------:|-----------:|----------:|----------:|------------:|
| Dink   | large-sample.html    | 1,463.7 ms |  88.62 ms | 261.30 ms | 1,477.9 ms |         - |         - |   556.66 KB |                                                                                 
| PdfLib | large-sample.html    | 1,412.9 ms |  83.36 ms | 245.79 ms | 1,521.8 ms | 1000.0000 |         - |  9023.87 KB |
| Dink   | sample.html          |   226.9 ms |   4.53 ms |   6.04 ms |   224.9 ms |         - |         - |   119.41 KB |
| PdfLib | sample.html          |   114.3 ms |   7.00 ms |  20.31 ms |   114.7 ms |         - |         - |    87.59 KB |
| Dink   | x2-large-sample.html | 2,067.1 ms | 112.95 ms | 318.59 ms | 1,887.9 ms |         - |         - |  1006.61 KB |
| PdfLib | x2-large-sample.html | 1,732.4 ms |  11.41 ms |  10.67 ms | 1,733.7 ms | 1000.0000 |         - | 19073.21 KB |
| Dink   | x3-large-sample.html | 2,611.6 ms |   9.80 ms |   9.17 ms | 2,611.5 ms |         - |         - |  1457.91 KB |
| PdfLib | x3-large-sample.html | 3,104.5 ms |  66.57 ms | 183.36 ms | 3,062.2 ms | 3000.0000 | 2000.0000 | 21212.35 KB |

Perf branch (benchmarked on a separate machine):

| Method | FileName             |        Mean |      Error |     StdDev |      Median |   Allocated |
|--------|----------------------|------------:|-----------:|-----------:|------------:|------------:|
| Dink   | large-sample.html    | 1,257.07 ms |  24.921 ms |  38.057 ms | 1,260.68 ms |   556.66 KB |                                                                                                                               
| PdfLib | large-sample.html    | 1,015.48 ms |  20.241 ms |  29.029 ms | 1,008.90 ms |  7318.37 KB |
| Dink   | sample.html          |   230.42 ms |   2.169 ms |   2.029 ms |   230.75 ms |   119.41 KB |
| PdfLib | sample.html          |    97.86 ms |   2.052 ms |   5.986 ms |    96.94 ms |    87.57 KB |
| Dink   | x2-large-sample.html | 2,232.20 ms |  33.663 ms |  38.767 ms | 2,219.86 ms |  1006.61 KB |
| PdfLib | x2-large-sample.html | 2,106.81 ms |  57.644 ms | 158.769 ms | 2,044.80 ms | 15645.16 KB |
| Dink   | x3-large-sample.html | 3,564.62 ms | 177.177 ms | 499.731 ms | 3,355.50 ms |  1457.91 KB |
| PdfLib | x3-large-sample.html | 3,451.96 ms | 134.999 ms | 398.049 ms | 3,256.12 ms |  15780.7 KB |

We finally reached a somewhat margin of error here where the library out performs DinkToHtml for smaller workloads, and is about the same
for larger ones. For reference, x3-large-sample.html equates to 420 pages. 

| Method | FileName             | Mean        | Error     | StdDev    | Median      | Allocated   |
|------- |--------------------- |------------:|----------:|----------:|------------:|------------:|
| Dink   | large-sample.html    | 1,006.55 ms |  9.404 ms |  8.337 ms | 1,005.49 ms |   636.86 KB |                                                                                                       
| PdfLib | large-sample.html    |   834.61 ms |  7.265 ms |  6.796 ms |   834.52 ms |  9098.34 KB |
| Dink   | sample.html          |   247.00 ms |  4.933 ms | 10.512 ms |   249.18 ms |   119.42 KB |
| PdfLib | sample.html          |    55.03 ms |  3.413 ms |  9.848 ms |    51.38 ms |    86.29 KB |
| Dink   | x2-large-sample.html | 1,805.79 ms |  9.329 ms |  8.726 ms | 1,805.88 ms |  1166.98 KB |
| PdfLib | x2-large-sample.html | 1,716.48 ms | 11.120 ms |  9.857 ms | 1,717.10 ms | 16389.27 KB |
| Dink   | x3-large-sample.html | 2,599.59 ms |  9.782 ms |  9.150 ms | 2,601.43 ms |  1698.45 KB |
| PdfLib | x3-large-sample.html | 2,833.06 ms | 11.097 ms |  9.267 ms | 2,833.30 ms | 16514.05 KB |

For about 840 pages (simply doubled the x3-large.sample.html file), we achieve the following:

| Method | FileName             | Mean    | Error    | StdDev   | Allocated |
|------- |--------------------- |--------:|---------:|---------:|----------:|
| Dink   | x6-large-sample.html | 4.922 s | 0.0335 s | 0.0280 s |   3.21 MB |                                                                                                                             
| PdfLib | x6-large-sample.html | 8.734 s | 0.0422 s | 0.0353 s |   33.5 MB |

DinkToPdf builds on top of wkhtmltopdf library which was the defacto standard for server-based PDF generation. However, it was built on top of QT engine's implementation which is no longer maintained or supported. 
It's not recommended to use wkhtmltopdf due to security vulnerabilities. Also does not support the latest JavaScript, HTML, or CSS features.

Since open-source libraries have not replaced it, industry has shifted towards using chromium's tooling. 
