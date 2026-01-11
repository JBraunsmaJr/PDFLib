# PDF Lib

Join me in the descent into madness, as I explore the possibilities of PDF generation.

Honestly, this was a product of me being bored and wondering what it would take
to generate a PDF without using external libraries. It... is... interesting to say the least.

As I progressed through the stages of wonder... I realized that there's some potential
in it.

This project is still in its early stages and is not yet ready for production use.

----

# Overview

The C# Project **PDFLib** is a library which manually crafts PDFs, without using third-party libraries. Went on to explore
the possibility of using Razor syntax to generate PDFs as well, instead of pursuing a fluent-api. After playing with the idea,
I realized the difficulty in pursuing styling. Developers would also require a "PDF" version of their webpages, which is not ideal.

To leverage existing HTML/CSS, one requires a renderer. WebKit and Blink are the two most popular choices. For the time being,
I've opted to use Chrome's headless Chromium since it's said to be pixel-perfect when creating PDFs.

**PDFLib.Console** is where Chromium is being explored. Interestingly enough, for small workloads it outperforms DinkToPdf,
but that quickly changes as the workload size increases. 

# Research / Path

## The Renderer / Headless Chromium

[Headless Chromium](https://github.com/chromium/chromium/blob/main/headless/README.md) is a package
google made available which contains the Blink and V8 engines, 200MB than full browser.

## Interop

IronPDF, based on some snooping, appears to have some form of C++ shared libraries they created.

Google suggests using Puppeteer, or websockets. However, I suspect using a named pipe (linux only) will be
enough.

The `--remote-debugging-pipe` flag leverages file descriptors 3 (Standard in/out), and 4. .NET only supports 0, 1, and 2.
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

Would appear as the file increases in size, PdfLib decreases in performance. Requires further investigation, quite possible it's the 
CDP overhead / allocations.

Old:

| Method | FileName             | Mean        | Error     | StdDev    | Median      | Gen0      | Allocated   |
|------- |--------------------- |------------:|----------:|----------:|------------:|----------:|------------:|
| Dink   | large-sample.html    | 1,018.79 ms | 13.280 ms | 12.422 ms | 1,015.22 ms |         - |   556.66 KB |                                                                                                                                                                                                                                                                                                                   
| PdfLib | large-sample.html    |   835.67 ms |  5.601 ms |  4.677 ms |   836.23 ms |         - |  8490.33 KB |
| Dink   | sample.html          |   250.94 ms |  4.925 ms |  8.496 ms |   252.28 ms |         - |   119.41 KB |
| PdfLib | sample.html          |    52.92 ms |  3.180 ms |  9.020 ms |    49.30 ms |         - |    86.47 KB |
| Dink   | x2-large-sample.html | 1,839.50 ms |  8.079 ms |  7.162 ms | 1,838.82 ms |         - |  1006.61 KB |
| PdfLib | x2-large-sample.html | 1,689.92 ms |  8.135 ms |  7.610 ms | 1,689.54 ms | 1000.0000 | 17478.55 KB |
| Dink   | x3-large-sample.html | 2,593.96 ms | 10.492 ms |  9.814 ms | 2,592.97 ms |         - |  1457.91 KB |
| PdfLib | x3-large-sample.html | 2,789.28 ms | 15.657 ms | 14.646 ms | 2,784.81 ms | 1000.0000 | 18444.96 KB |

New:

| Method | FileName             | Mean        | Error     | StdDev     | Median      | Gen0      | Allocated   |
|------- |--------------------- |------------:|----------:|-----------:|------------:|----------:|------------:|
| Dink   | large-sample.html    | 1,030.67 ms |  7.634 ms |   6.375 ms | 1,028.72 ms |         - |   556.66 KB |
| PdfLib | large-sample.html    |   829.91 ms |  6.681 ms |   6.250 ms |   828.23 ms |         - |  8490.34 KB |
| Dink   | sample.html          |   250.64 ms |  4.932 ms |   8.240 ms |   251.87 ms |         - |   119.41 KB |
| PdfLib | sample.html          |    80.39 ms | 12.496 ms |  36.845 ms |    60.40 ms |         - |    86.33 KB |
| Dink   | x2-large-sample.html | 1,812.86 ms | 12.315 ms |  10.917 ms | 1,810.01 ms |         - |  1006.61 KB |
| PdfLib | x2-large-sample.html | 1,904.25 ms | 48.289 ms | 141.625 ms | 1,867.41 ms | 1000.0000 |  21574.8 KB |
| Dink   | x3-large-sample.html | 2,732.06 ms | 53.705 ms |  92.639 ms | 2,701.66 ms |         - |  1457.91 KB |
| PdfLib | x3-large-sample.html | 2,855.67 ms | 52.383 ms |  98.389 ms | 2,810.33 ms | 1000.0000 | 18444.57 KB |