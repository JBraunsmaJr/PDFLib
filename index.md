---
_layout: landing
---

# PDF Lib

An open source PDF library using .NET which focuses on rendering PDFs from HTML/CSS, and optionally signing them with digital
signatures.

| File     | Page Count |
|:---------|:----------:|
| sample   |     2      |
| medium   |    140     |
| x2-large |    280     |
| x3-large |    420     |

![Metrics Overview](assets/overview.png)

## Features

- Can reuse your frontend code to [generate PDFs](docs/introduction.md)!
- Can sign PDFs with [digital signatures](docs/digital-signatures.md)
- Different [wait strategies](docs/wait-strategies.md) to use to indicate a page is ready for printing

## The Journey:

This project started off as me being bored, a descent into madness. Was curious what it would take to create a PDF in C#. Quite the journey that
was!

Initially I created a translation layer which converted Razor syntax into PDF syntax. However, it became obvious that styling and layouts
were painful to do. As a developer, I felt I should be able to reuse the same frontend code to generate PDFs.

HTML/CSS to PDF became the focus. Manually creating the layout/renderer didn't sound reasonable when existing engines such as Blink (Chrome) and
WebKit (Safari) already exist. Blink is pixel-perfect, what you see in the browser is what you get in the PDF. So I went with Blink!

Decided to leverage Chromium's headless shell, which requires using the Chromium Developer Protocol (CDP). Unfortunately, it uses
base64 encoding on top of using JSON for communication. Thus, a 33% increase in data size off the rip.