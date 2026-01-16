---
_layout: landing
---

# PDF Lib

An open source PDF library using .NET which focuses on rendering PDFs from HTML/CSS, and optionally signing them with digital
signatures.

![Metrics Overview](assets/overview.png)

## The Journey:

This project started off as me being bored, a descent into madness. Was curious what it would take to create a PDF in C#. Quite the journey that
was!

Initially I created a translation layer which converted Razor syntax into PDF syntax. However, it became obvious that styling and layouts
were painful to do. As a developer, I felt I should be able to reuse the same frontend code to generate PDFs.

HTML/CSS to PDF became the focus. Manually creating the layout/renderer didn't sound reasonable when existing engines such as Blink (Chrome) and
WebKit (Safari) already exist. Blink is pixel-perfect, what you see in the browser is what you get in the PDF. So I decided to use Blink!

Decided to leverage Chromium's headless shell, which requires using the Chromium Developer Protocol (CDP). Unfortunately, it uses
base64 encoding on top of using JSON for communication. Thus, a 33% increase in data size off the rip.

## Features

- Can reuse your frontend code to generate PDFs!
- Can sign PDFs with digital signatures
- Different wait strategies to use to indicate a page is ready for printing
