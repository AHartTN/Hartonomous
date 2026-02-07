# Execution Stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy project files
COPY ["app-layer/Directory.Build.props", "app-layer/"]
COPY ["app-layer/Directory.Build.targets", "app-layer/"]
COPY ["app-layer/Hartonomous.Web/Hartonomous.Web.csproj", "app-layer/Hartonomous.Web/"]
COPY ["app-layer/Hartonomous.Shared/Hartonomous.Shared.csproj", "app-layer/Hartonomous.Shared/"]
COPY ["app-layer/Hartonomous.ServiceDefaults/Hartonomous.ServiceDefaults.csproj", "app-layer/Hartonomous.ServiceDefaults/"]

RUN dotnet restore "app-layer/Hartonomous.Web/Hartonomous.Web.csproj"

# Copy source
COPY app-layer/ app-layer/

# Copy Native Libs for Build (Required by .targets files even for Web if referencing Shared)
COPY build/linux-release-max-perf/Engine/libengine.so /src/build/linux-release-max-perf/Engine/libengine.so

WORKDIR "/src/app-layer/Hartonomous.Web"
RUN dotnet build "Hartonomous.Web.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Publish
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Hartonomous.Web.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Final Stage
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Hartonomous.Web.dll"]
