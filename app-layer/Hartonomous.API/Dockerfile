# Execution Stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Install runtime dependencies
RUN apt-get update && apt-get install -y --no-install-recommends \
    libpq5 \
    libgomp1 \
    && rm -rf /var/lib/apt/lists/*

# Copy Host Libraries (MKL, divsufsort) directly to /usr/lib
# These were gathered into docker-libs/ on the host
COPY docker-libs/ /usr/lib/
# Run ldconfig to update cache
RUN ldconfig

# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy project files
COPY ["app-layer/Directory.Build.props", "app-layer/"]
COPY ["app-layer/Directory.Build.targets", "app-layer/"]
COPY ["app-layer/Hartonomous.API/Hartonomous.API.csproj", "app-layer/Hartonomous.API/"]
COPY ["app-layer/Hartonomous.Core/Hartonomous.Core.csproj", "app-layer/Hartonomous.Core/"]
COPY ["app-layer/Hartonomous.Data/Hartonomous.Data.csproj", "app-layer/Hartonomous.Data/"]
COPY ["app-layer/Hartonomous.Shared/Hartonomous.Shared.csproj", "app-layer/Hartonomous.Shared/"]
COPY ["app-layer/Hartonomous.Common/Hartonomous.Common.csproj", "app-layer/Hartonomous.Common/"]
COPY ["app-layer/Hartonomous.Marshal/Hartonomous.Marshal.csproj", "app-layer/Hartonomous.Marshal/"]
COPY ["app-layer/Hartonomous.ServiceDefaults/Hartonomous.ServiceDefaults.csproj", "app-layer/Hartonomous.ServiceDefaults/"]

RUN dotnet restore "app-layer/Hartonomous.API/Hartonomous.API.csproj"

# Copy source code
COPY app-layer/ app-layer/

# Copy Native Libs for Build (Crucial for Hartonomous.Marshal.csproj Content item)
# Destination path must match relative path in csproj: ../../build/linux-release-max-perf/Engine/libengine.so
# Since we are in /src, and csproj is in /src/app-layer/Hartonomous.Marshal, ../../ is /src.
# So we need /src/build/linux-release-max-perf/Engine/libengine.so
COPY build/linux-release-max-perf/Engine/libengine.so /src/build/linux-release-max-perf/Engine/libengine.so

WORKDIR "/src/app-layer/Hartonomous.API"
RUN dotnet build "Hartonomous.API.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Publish
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Hartonomous.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Final Stage
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Copy Pre-built Native Engine to system path for runtime
COPY build/linux-release-max-perf/Engine/libengine.so /usr/lib/libengine.so
# Also ensure libs are there (already in base, but just to be sure)
# COPY --from=base /usr/lib/libmkl* /usr/lib/

ENTRYPOINT ["dotnet", "Hartonomous.API.dll"]
