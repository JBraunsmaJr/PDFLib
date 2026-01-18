#!/bin/bash
set -e

cd "$(dirname "$0")/.."

mkdir -p ./PDFLib.Chromium.TestConsole/packages

echo "[Test] Packing PDFLib.Chromium (Bundled)..."
dotnet pack ./PDFLib.Chromium/PDFLib.Chromium.csproj -c Release -p:DownloadLatestChromium=true -o ./PDFLib.Chromium.TestConsole/packages

echo "[Test] Building and running Docker container..."
docker build -t pdflib-chromium-test -f PDFLib.Chromium.TestConsole/Dockerfile .
docker run --rm pdflib-chromium-test