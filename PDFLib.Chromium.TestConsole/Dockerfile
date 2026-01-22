FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base

ARG HEADLESS_CHROMIUM_DOWNLOAD_URL="https://storage.googleapis.com/chrome-for-testing-public/145.0.7572.2/linux64/chrome-headless-shell-linux64.zip"

RUN apt-get update &&  \
    apt-get install -y --no-install-recommends \
    wget \
    zlib1g \
    fontconfig \
    libfreetype6 \
    libx11-6 \
    libxext6 \
    libxrender1 \
    libssl3 \
    xfonts-75dpi \
    xfonts-base  \
    curl \
    unzip \
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
    libfontconfig1 \
    ca-certificates \
    fonts-ipafont-gothic \
    fonts-wqy-zenhei \
    fonts-thai-tlwg \
    fonts-kacst \
    fonts-freefont-ttf \
    fonts-noto-color-emoji \
    fonts-dejavu-core && \
    rm -rf /var/lib/apt/lists/*

WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release

WORKDIR /packages
COPY ["PDFLib.Chromium.TestConsole/packages/*.nupkg", "./"]

# Copy everything and restore from root
WORKDIR /src
COPY . .
RUN dotnet restore "PDFLib.Chromium.TestConsole/PDFLib.Chromium.TestConsole.csproj"
WORKDIR "/src/PDFLib.Chromium.TestConsole"
RUN dotnet build "./PDFLib.Chromium.TestConsole.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./PDFLib.Chromium.TestConsole.csproj" -c $BUILD_CONFIGURATION -r linux-x64 --self-contained false -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PDFLib.Chromium.TestConsole.dll"]
