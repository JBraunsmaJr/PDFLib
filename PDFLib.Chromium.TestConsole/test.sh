#!/bin/bash
set -e

cd "$(dirname "$0")/.."

mkdir -p PDFLib.Chromium.TestConsole/packages

dotnet restore PDFLib.Chromium/PDFLib.Chromium.csproj
dotnet restore PDFLib.Razor/PDFLib.Razor.csproj

echo "[Test] Packing PDFLib.Chromium (Bundled)..."
rm -rf PDFLib.Chromium.TestConsole/packages/*
dotnet build ./PDFLib.Chromium/PDFLib.Chromium.csproj -c Release -p:DownloadLatestChromium=true -p:Version=1.0.999
dotnet pack ./PDFLib.Chromium/PDFLib.Chromium.csproj -c Release -p:DownloadLatestChromium=true -o ./PDFLib.Chromium.TestConsole/packages --no-build -p:Version=1.0.999

echo "[Test] Building and running Docker container..."
docker build -t pdflib-chromium-test -f PDFLib.Chromium.TestConsole/Dockerfile .
docker run --rm pdflib-chromium-test