# PDF Lib

Honestly, this was a product of me being bored and wondering what it would take
to generate a PDF without using external libraries. It... is... interesting to say the least.

As I progressed through the stages of wonder... I realized that there's some potential
in it.

This project is still in its early stages and is not yet ready for production use.

----

# Overview

Currently, you can generate a PDF programmatically via the API, or alternatively use
Razor syntax (which I think is preferrable).

By using the Razor API you can use familiar markup to generate your PDFs. All without external
dependencies on a web-browser.

Example the PDF produced via the PdfTest project: [PDF Example](./razor-test.pdf)


# Research / Path

## The Renderer / Headless Chromium

[Headless Chromium](https://github.com/chromium/chromium/blob/main/headless/README.md) is a package
google made available which contains the Blink and V8 engines, 200MB than full browser.

## Interop

IronPDF, based on some snooping, appears to have some form of C++ shared libraries they created.

Google suggests using Puppeteer, or websockets. However, I suspect using a named pipe (linux only) will be
sufficient.

The `--remote-debugging-pipe` flag leverages file descriptors 3 (Standard in/out), and 4.

```bash
mkfifo /tmp/namedPipe
```



