# Sample API and load test in one image. Build from the repository root:
#
#   docker compose build pdflib.samples.api
#   docker compose up pdflib.samples.api            # API on http://localhost:5080
#   docker compose run --rm pdflib.samples.loadtest # API + load test inside one container
#
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS base

ARG HEADLESS_CHROMIUM_DOWNLOAD_URL="https://storage.googleapis.com/chrome-for-testing-public/157.0.8103.50/linux64/chrome-headless-shell-linux64.zip"

# Libraries Chromium needs, plus fonts so reports render with real typefaces
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
    curl \
    unzip \
    ca-certificates \
    fontconfig \
    libfreetype6 \
    libfontconfig1 \
    libnss3 \
    libatk1.0-0t64 \
    libatk-bridge2.0-0 \
    libcups2 \
    libdrm2 \
    libxcomposite1 \
    libxdamage1 \
    libxfixes3 \
    libxrandr2 \
    libgbm1 \
    libasound2t64 \
    libxkbcommon0 \
    libpango-1.0-0 \
    libpangocairo-1.0-0 \
    libxshmfence1 \
    fonts-liberation \
    fonts-dejavu-core \
    fonts-noto-color-emoji && \
    rm -rf /var/lib/apt/lists/*

# The library looks for "chrome-shell" on the PATH when no binary is bundled or configured
RUN curl -SL "$HEADLESS_CHROMIUM_DOWNLOAD_URL" -o /tmp/chromium.zip && \
    unzip /tmp/chromium.zip -d /opt/chromium && \
    rm /tmp/chromium.zip && \
    ln -s /opt/chromium/chrome-headless-shell-linux64/chrome-headless-shell /usr/local/bin/chrome-shell && \
    chrome-shell --version

WORKDIR /src
COPY . .
RUN mkdir -p PDFLib.Chromium.TestConsole/packages /out && \
    dotnet build -c Release samples/PDFLib.Samples.WebApi && \
    dotnet build -c Release samples/PDFLib.Samples.LoadTest

ENV ASPNETCORE_URLS=http://0.0.0.0:5080
EXPOSE 5080

ENTRYPOINT [ "dotnet", "run", "-c", "Release", "--no-build", "--project", "samples/PDFLib.Samples.WebApi" ]
