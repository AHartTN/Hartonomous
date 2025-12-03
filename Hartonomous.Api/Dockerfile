FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 5000

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["Hartonomous.Api/Hartonomous.Api.csproj", "Hartonomous.Api/"]
COPY ["Hartonomous.Db/Hartonomous.Db.csproj", "Hartonomous.Db/"]
COPY ["Hartonomous.Shared/Hartonomous.Shared.csproj", "Hartonomous.Shared/"]
RUN dotnet restore "Hartonomous.Api/Hartonomous.Api.csproj"
COPY . .
WORKDIR "/src/Hartonomous.Api"
RUN dotnet build "Hartonomous.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Hartonomous.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Hartonomous.Api.dll"]
