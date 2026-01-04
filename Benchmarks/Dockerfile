FROM mcr.microsoft.com/dotnet/sdk:10.0 AS base

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
    wget https://github.com/wkhtmltopdf/packaging/releases/download/0.12.6.1-2/wkhtmltox_0.12.6.1-2.jammy_amd64.deb && \
    apt-get install -y ./wkhtmltox_0.12.6.1-2.jammy_amd64.deb && \
    rm wkhtmltox_0.12.6.1-2.jammy_amd64.deb && \
    rm -rf /var/lib/apt/lists/*

# Download the headless shell of chromium
RUN curl -SL "$HEADLESS_CHROMIUM_DOWNLOAD_URL" -o /tmp/chromium.zip && \
    unzip /tmp/chromium.zip -d /opt/chromium && \
    rm /tmp/chromium.zip && \
    ln -s /opt/chromium/chrome-headless-shell-linux64/chrome-headless-shell /usr/local/bin/chrome-shell 

RUN chmod +x /usr/local/bin/chrome-shell
RUN chrome-shell --version     

RUN mkdir -p /out && chmod 777 -R /out
USER 0

WORKDIR /src
COPY . .
RUN dotnet restore "Benchmarks/Benchmarks.csproj"
WORKDIR "/src/Benchmarks"
RUN dotnet build "./Benchmarks.csproj" -c "release"

ENTRYPOINT [ "dotnet", "run", "-c", "release" ]