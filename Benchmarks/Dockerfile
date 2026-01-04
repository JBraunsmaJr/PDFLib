FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

RUN apt-get update && \
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
        xfonts-base && \
    wget https://github.com/wkhtmltopdf/packaging/releases/download/0.12.6.1-2/wkhtmltox_0.12.6.1-2.jammy_amd64.deb && \
    apt-get install -y ./wkhtmltox_0.12.6.1-2.jammy_amd64.deb && \
    rm wkhtmltox_0.12.6.1-2.jammy_amd64.deb && \
    rm -rf /var/lib/apt/lists/*

ARG BUILD_CONFIGURATION=Release
USER 0

WORKDIR /src
COPY ["Benchmarks/Benchmarks.csproj", "Benchmarks/"]
RUN dotnet restore "Benchmarks/Benchmarks.csproj"
COPY . .
WORKDIR "/src/Benchmarks"
RUN dotnet build "./Benchmarks.csproj" -c $BUILD_CONFIGURATION

ENTRYPOINT [ "dotnet", "run", "-c", "release" ]
