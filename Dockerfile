# Hartonomous Multi-Stage Build
# Enterprise-grade containerized build for reproducible native + .NET artifacts
#
# Build stages:
#   1. native-build: Compile C++ native library
#   2. dotnet-build: Compile .NET application with native deps
#   3. runtime: Minimal production image
#
# Usage:
#   docker build -t hartonomous .
#   docker build --target native-build -t hartonomous-native .
#   docker build --target dotnet-build -t hartonomous-sdk .

# ==============================================================================
# Stage 1: Native C++ Build
# ==============================================================================
FROM mcr.microsoft.com/devcontainers/cpp:1-debian-12 AS native-build

# Install build dependencies
RUN apt-get update && apt-get install -y --no-install-recommends \
    cmake \
    ninja-build \
    libpq-dev \
    postgresql-server-dev-all \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /src

# Copy only CMake files first for layer caching
COPY Hartonomous.Native/CMakeLists.txt Hartonomous.Native/CMakePresets.json ./Hartonomous.Native/
COPY Hartonomous.Native/vcpkg.json ./Hartonomous.Native/
COPY CMakeLists.txt CMakePresets.json ./

# Copy source files
COPY Hartonomous.Native/src ./Hartonomous.Native/src
COPY Hartonomous.Native/tests ./Hartonomous.Native/tests

# Copy test data for tests
COPY test-data ./test-data

# Configure and build
WORKDIR /src/Hartonomous.Native
RUN cmake --preset linux-gcc-release \
    -DCMAKE_INSTALL_PREFIX=/opt/hartonomous \
    -DBUILD_TESTING=ON

RUN cmake --build --preset linux-gcc-release --parallel $(nproc)

# Run tests
RUN ctest --preset linux-gcc-release --output-on-failure

# Install
RUN cmake --install out/build/linux-gcc-release

# ==============================================================================
# Stage 2: .NET SDK Build
# ==============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS dotnet-build

# Install build tools for NativeAOT
RUN apt-get update && apt-get install -y --no-install-recommends \
    clang \
    zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*

# Copy native libraries from previous stage
COPY --from=native-build /opt/hartonomous/lib /app/native/linux-x64

WORKDIR /src

# Copy solution and project files first for layer caching
COPY Docker.slnx ./
COPY Directory.*.props ./
COPY Hartonomous.Core/*.csproj ./Hartonomous.Core/
COPY Hartonomous.Data/*.csproj ./Hartonomous.Data/
COPY Hartonomous.Infrastructure/*.csproj ./Hartonomous.Infrastructure/
COPY Hartonomous.Worker/*.csproj ./Hartonomous.Worker/
COPY Hartonomous.CLI/*.csproj ./Hartonomous.CLI/
COPY Hartonomous.Terminal/*.csproj ./Hartonomous.Terminal/
COPY Hartonomous.ServiceDefaults/*.csproj ./Hartonomous.ServiceDefaults/

# Restore dependencies
RUN dotnet restore Docker.slnx -r linux-x64

# Copy source code
COPY Hartonomous.Core ./Hartonomous.Core
COPY Hartonomous.Data ./Hartonomous.Data
COPY Hartonomous.Infrastructure ./Hartonomous.Infrastructure
COPY Hartonomous.Worker ./Hartonomous.Worker
COPY Hartonomous.CLI ./Hartonomous.CLI
COPY Hartonomous.Terminal ./Hartonomous.Terminal
COPY Hartonomous.ServiceDefaults ./Hartonomous.ServiceDefaults

# Copy native library to runtime location
RUN mkdir -p Hartonomous.Core/runtimes/linux-x64/native && \
    cp /app/native/linux-x64/* Hartonomous.Core/runtimes/linux-x64/native/ || true

# Build
ARG VERSION=0.1.0
RUN dotnet build Docker.slnx \
    --configuration Release \
    --no-restore \
    -p:Version=${VERSION}

# Publish Worker
RUN dotnet publish Hartonomous.Worker/Hartonomous.Worker.csproj \
    --configuration Release \
    -r linux-x64 \
    --self-contained false \
    --output /app/worker

# Publish CLI
RUN dotnet publish Hartonomous.CLI/Hartonomous.CLI.csproj \
    --configuration Release \
    -r linux-x64 \
    --self-contained false \
    --output /app/cli

# ==============================================================================
# Stage 3: Runtime - Worker Service
# ==============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime-worker

# Install PostgreSQL client library
RUN apt-get update && apt-get install -y --no-install-recommends \
    libpq5 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Copy native library
COPY --from=native-build /opt/hartonomous/lib/*.so* /usr/local/lib/
RUN ldconfig

# Copy .NET application
COPY --from=dotnet-build /app/worker ./

# Create non-root user
RUN useradd -r -s /bin/false hartonomous
USER hartonomous

ENV DOTNET_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

ENTRYPOINT ["dotnet", "Hartonomous.Worker.dll"]

# ==============================================================================
# Stage 4: Runtime - CLI Tool
# ==============================================================================
FROM mcr.microsoft.com/dotnet/runtime:10.0-noble AS runtime-cli

# Install PostgreSQL client library
RUN apt-get update && apt-get install -y --no-install-recommends \
    libpq5 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Copy native library
COPY --from=native-build /opt/hartonomous/lib/*.so* /usr/local/lib/
COPY --from=native-build /opt/hartonomous/bin/* /usr/local/bin/
RUN ldconfig

# Copy .NET application
COPY --from=dotnet-build /app/cli ./

# Create non-root user
RUN useradd -r -s /bin/false hartonomous
USER hartonomous

ENTRYPOINT ["dotnet", "Hartonomous.CLI.dll"]

# ==============================================================================
# Default target is worker
# ==============================================================================
FROM runtime-worker
