# NuGet Deployment & Prerequisites

**PDFLib.Chromium** is available as a NuGet package.

## PDFLib.Chromium
This library requires **Headless Chromium** and specific system libraries to function.

### Platform Support
Currently, `PDFLib.Chromium` uses Linux-specific APIs (`pipe`, `fcntl`) for communication with Chromium and is intended 
for use on **Linux** (including Docker containers.... especially Docker containers).

### Prerequisites
To use `PDFLib.Chromium`, you must ensure that Chromium and its dependencies are installed in your environment.

If installed via nuget, the Chromium binaries will be included automatically.

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

For a complete reference on how to set up the environment, see the [Dockerfile](https://github.com/JBraunsmaJr/PDFLib/blob/main/PDFLib.Chromium.TestConsole/Dockerfile).
