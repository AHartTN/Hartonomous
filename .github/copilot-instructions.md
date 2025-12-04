# Hartonomous AI Coding Instructions

## Architecture Overview

**Hartonomous** is a content-addressable storage system using Clean Architecture + DDD. The solution follows strict layered separation with dependencies flowing inward toward Core.

### Project Structure
- **Hartonomous.Core** (Domain + Application) - Business logic, entities, interfaces. Zero external dependencies.
- **Hartonomous.Data** - EF Core 10 + PostgreSQL/PostGIS data access, spatial queries, PL/Python GPU functions
- **Hartonomous.Infrastructure** - External services (caching, auth, current user context)
- **Hartonomous.API** - REST API with Zero Trust auth (Microsoft Entra ID), rate limiting, comprehensive health checks
- **Hartonomous.Worker** - Background service for async processing
- **Hartonomous.App** - MAUI cross-platform app (iOS, Android, macOS, Windows)
- **Hartonomous.App.Web** - Blazor Web (Server + WASM hybrid)
- **Hartonomous.AppHost** - .NET Aspire orchestration for local dev
- **Hartonomous.ServiceDefaults** - Shared Aspire config (OpenTelemetry, health checks, service discovery)

## Key Conventions

### Entity and Repository Pattern
All domain entities inherit from `BaseEntity` (provides `Id`, audit fields, soft delete, domain events). Use Repository + UnitOfWork pattern:
```csharp
// Inject both repository and unit of work
private readonly IVehicleRepository _repository;
private readonly IUnitOfWork _unitOfWork;

// Modify data
await _repository.AddAsync(entity);
await _unitOfWork.SaveChangesAsync(); // Single transaction
```

### Namespaces Follow Folder Structure
`Hartonomous.{ProjectName}.{FolderPath}` - e.g., `Hartonomous.Core.Domain.Entities`, `Hartonomous.Data.Repositories`

### Configuration Per Environment
Use environment-specific appsettings: `appsettings.{Environment}.json`. Environments: `Local` (Debug), `Development`, `Staging`, `Production`. Set via `Directory.Build.props` based on Configuration.

### Preprocessor Directives Available
```csharp
#if LOCAL    // Debug builds
#if DEV      // Development
#if STAGING  // Staging
#if PRODUCTION // Production/Release
```

## Build & Development Workflow

### Local Development with Aspire
```powershell
# Start all services with Aspire dashboard
dotnet run --project Hartonomous.AppHost
```
Opens dashboard at `http://localhost:15xxx` showing all services, logs, traces, metrics.

### Database Migrations
```powershell
# From Hartonomous.Data directory
dotnet ef migrations add <MigrationName> --startup-project ../Hartonomous.API
dotnet ef database update --startup-project ../Hartonomous.API
```
**Required PostgreSQL extensions**: `uuid-ossp`, `postgis`, `plpython3u`

### Individual Project Runs
```powershell
# API (port 7001 HTTPS, 5001 HTTP)
dotnet run --project Hartonomous.API

# Worker
dotnet run --project Hartonomous.Worker

# Blazor Web
dotnet run --project Hartonomous.App/Hartonomous.App.Web
```

### Build Artifacts
Centralized in `artifacts/` directory (not individual project `bin/obj`). See `Directory.Build.props` for configuration.

## Security & Authentication

### Zero Trust by Default
All API endpoints require authentication by default (`RequireAuthorization()` on controller mapping). Use `[AllowAnonymous]` to opt out.

### Authentication Uses Microsoft Entra ID
JWT Bearer tokens validated against Entra ID (formerly Azure AD). Configuration in `appsettings.json`:
```json
{
  "Authentication": {
    "TenantId": "...",
    "ClientId": "...",
    "Audience": "...",
    "Authority": "https://login.microsoftonline.com/{TenantId}"
  }
}
```

### Authorization Policies
Defined in `AuthenticationConfiguration.cs`:
- `AdminPolicy` - requires Admin/Administrator role + `api.admin` scope
- `UserPolicy` - requires User/Reader role + `api.read` scope  
- `WritePolicy` - requires `api.write` scope
- `ApiScopePolicy` - requires `api.access` scope

Use: `[Authorize(Policy = "AdminPolicy")]`

### Rate Limiting
Configured per-endpoint via `RateLimitingConfiguration.cs`. Uses token bucket algorithm.

### Security Headers
Automatically applied in `Program.cs`: `X-Content-Type-Options`, `X-Frame-Options`, `X-XSS-Protection`, `Strict-Transport-Security`, etc.

## Data Access Patterns

### Spatial Queries with PostGIS
Entities can inherit from `SpatialEntity` for geometry/geography support:
```csharp
// Use NetTopologySuite types
public Point Location { get; set; } // POINT(x y)
```
Use `SpatialQueryExtensions.cs` for k-NN, distance, containment queries.

### PL/Python GPU Functions
Located in `Hartonomous.Data/Functions/PlPython/`. Define SQL functions in C# that map to PL/Python implementations for GPU-accelerated operations (CuPy, TensorFlow, PyTorch).

### Soft Delete
All `BaseEntity` entities use soft delete. Global query filter in `ApplicationDbContext` excludes `IsDeleted == true` records automatically.

## Deployment

### Azure Pipelines CI/CD
`azure-pipelines.yml` orchestrates:
1. **Build** - Compiles Core → Data → Infrastructure → ServiceDefaults → API/Worker
2. **Package** - Creates NuGet packages for main branch/tags
3. **Deploy** - Uses Azure Arc Run Command to deploy to on-premises servers

### Deployment Targets
- `hart-server` (Linux/Windows) - Production/Staging
- `hart-desktop` (Linux/Windows) - Development
- Azure-hosted VMs

### Infrastructure Setup
**Linux**: systemd services + nginx reverse proxy  
**Windows**: IIS app pools + sites

Scripts in `deploy/` directory handle infrastructure provisioning and app deployment per environment.

### Health Checks
- `/health` - All checks
- `/health/live` - Liveness (is process running?)
- `/health/ready` - Readiness (can accept traffic?)

## Testing

### Run Tests
```powershell
dotnet test --configuration Release
```
Tests should exist in `**/*Tests.csproj` projects (not yet created in structure).

## Common Tasks

### Add New Entity
1. Create entity in `Hartonomous.Core/Domain/Entities/` inheriting `BaseEntity`
2. Create EF configuration in `Hartonomous.Data/Configurations/`
3. Add `DbSet<T>` to `ApplicationDbContext`
4. Create repository interface in `Hartonomous.Core/Application/Interfaces/`
5. Implement repository in `Hartonomous.Data/Repositories/`
6. Register in DI: `Hartonomous.Data/Extensions/DataLayerExtensions.cs`
7. Create migration: `dotnet ef migrations add Add{Entity}`
8. Create controller in `Hartonomous.API/Controllers/`

### Add Background Job
Implement `BackgroundService` in `Hartonomous.Worker/Jobs/` and register in `Program.cs`.

### Add Cache Layer
Use `ICacheService` from Infrastructure. Backed by Redis in production.

## Important Files

- `Directory.Build.props` - Global MSBuild properties (versions, environment, artifacts path)
- `Hartonomous.slnx` - Solution file (XML format)
- `Hartonomous.API/Program.cs` - API startup with middleware pipeline
- `Hartonomous.Data/Context/ApplicationDbContext.cs` - EF Core DbContext
- `Hartonomous.Data/Extensions/DataLayerExtensions.cs` - Data layer DI registration

## Target Framework

All projects target **net10.0** (.NET 10).
