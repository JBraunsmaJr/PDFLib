# PDF Lib

Join me in the descent into madness, as I explore the possibilities of PDF generation.

Honestly, this was a product of me being bored and wondering what it would take
to generate a PDF without using external libraries. It... is... interesting to say the least.

As I progressed through the stages of wonder... I realized that there's some potential
in it.

This project is still in its early stages and is not yet ready for production use.

Preface this with... it took until now for me to realize the benchmarks for PDF Lib was including the memory stream we were writing to (to simulate a user receiving their PDF). 
That's why you'll see the significant reduction in memory. 

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

The C# Project **PDFLib.Razor** is a library that manually crafts PDFs without using third-party libraries. Went on to explore
the possibility of using Razor syntax to generate PDFs as well, instead of pursuing a fluent-api. After playing with the idea,
I realized the difficulty in pursuing styling. Developers would also require a "PDF" version of their webpages, which is
not ideal.

To leverage existing HTML/CSS, one requires a renderer. WebKit and Blink are the two most popular choices. For the time being,
I've opted to use Chrome's headless Chromium since it's said to be pixel-perfect when creating PDFs.

**PDFLib.Chromium** is where Chromium is being explored. The difficulty lies in extracting as much performance out of it as possible. There is an inherited overhead by using a browser (albeit headless)
as it has to render using Blink engine and runs the V8 engine for JavaScript.

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

| Method | FileName             | Mean        | Error     | StdDev    | Median      | Allocated  |
|------- |--------------------- |------------:|----------:|----------:|------------:|-----------:|
| Dink   | large-sample.html    | 1,010.58 ms |  9.862 ms |  8.235 ms | 1,007.34 ms |  636.86 KB |                                                                                                        
| PdfLib | large-sample.html    |   845.99 ms |  8.489 ms |  7.089 ms |   845.25 ms |  138.13 KB |
| Dink   | sample.html          |   250.86 ms |  4.952 ms |  8.543 ms |   253.14 ms |  119.42 KB |
| PdfLib | sample.html          |    52.36 ms |  2.575 ms |  7.591 ms |    49.46 ms |   19.48 KB |
| Dink   | x2-large-sample.html | 1,794.39 ms | 11.802 ms |  9.855 ms | 1,793.57 ms | 1166.98 KB |
| PdfLib | x2-large-sample.html | 1,712.90 ms | 15.502 ms | 12.945 ms | 1,708.03 ms |  261.83 KB |
| Dink   | x3-large-sample.html | 2,572.94 ms |  7.158 ms |  6.346 ms | 2,573.16 ms | 1698.45 KB |
| PdfLib | x3-large-sample.html | 2,806.93 ms | 23.164 ms | 21.668 ms | 2,801.68 ms |  385.69 KB |


DinkToPdf builds on top of a wkhtmltopdf library which was the defacto standard for server-based PDF generation. However, it was built on top of QT engine's implementation, which is no longer maintained or supported. 
It's not recommended to use wkhtmltopdf due to security vulnerabilities. Also does not support the latest JavaScript, HTML, or CSS features.

# NuGet Deployment & Prerequisites

**PDFLib.Chromium** is available as a NuGet package.

## PDFLib.Chromium
This library requires **Headless Chromium** and specific system libraries to function.

### Platform Support
Currently, `PDFLib.Chromium` uses Linux-specific APIs (`pipe`, `fcntl`) for communication with Chromium and is intended for use on **Linux** (including Docker containers.... especially Docker containers).

### Prerequisites
To use `PDFLib.Chromium`, you must ensure that Chromium and its dependencies are installed in your environment.

#### 1. Chromium Headless Shell
The library expects `chrome-headless-shell` to be available in your PATH or configured via `BrowserOptions.BinaryPath`.

#### 2. Linux Dependencies
On Debian-based systems (like the official .NET images), you can install the necessary dependencies using:

```bash
apt-get update && apt-get install -y --no-install-recommends \
    wget zlib1g fontconfig libfreetype6 libx11-6 libxext6 libxrender1 \
    libssl3 xfonts-75dpi xfonts-base curl unzip libnss3 libatk1.0-0t64 \
    libatk-bridge2.0-0 libcups2 libdrm2 libxcomposite1 libxdamage1 \
    libxfixes3 libxrandr2 libgbm1 libasound2t64 libxkbcommon0 \
    libpango-1.0-0 libpangocairo-1.0-0 libxshmfence1 fonts-liberation \
    libfontconfig1 ca-certificates fonts-ipafont-gothic fonts-wqy-zenhei \
    fonts-thai-tlwg fonts-kacst fonts-freefont-ttf fonts-noto-color-emoji \
    fonts-dejavu-core
```

For a complete reference on how to set up the environment, see the [Dockerfile](./PDFLib.Chromium.TestConsole/Dockerfile).

#### 3. Setup Script (Optional)
If you are deploying to a custom Linux environment, you can use the following snippet to download the recommended version of the Headless Chromium shell:

```bash
export HEADLESS_CHROMIUM_DOWNLOAD_URL="https://storage.googleapis.com/chrome-for-testing-public/145.0.7572.2/linux64/chrome-headless-shell-linux64.zip"
curl -SL "$HEADLESS_CHROMIUM_DOWNLOAD_URL" -o /tmp/chromium.zip
unzip /tmp/chromium.zip -d /opt/chromium
ln -s /opt/chromium/chrome-headless-shell-linux64/chrome-headless-shell /usr/local/bin/chrome-shell
chmod +x /usr/local/bin/chrome-shell
```
