# Stage 1: Build the application
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy the project file and restore dependencies
COPY ["src/Hartonomous.CodeAtomizer.Api/Hartonomous.CodeAtomizer.Api.csproj", "Hartonomous.CodeAtomizer.Api/"]
RUN dotnet restore "Hartonomous.CodeAtomizer.Api/Hartonomous.CodeAtomizer.Api.csproj"

# Copy the rest of the source code
COPY . .

# Publish the application
WORKDIR "/src/src/Hartonomous.CodeAtomizer.Api"
RUN dotnet publish "Hartonomous.CodeAtomizer.Api.csproj" -c Release -o /app/publish

# Stage 2: Create the final runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Expose the port the app runs on (must match what's in the C# code)
# We assume 8080 as per the docker-compose setup.
EXPOSE 8080

# The official ASP.NET Core images use this environment variable to set the URL
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "Hartonomous.CodeAtomizer.Api.dll"]
