# ?? Hartonomous - Quick Start Guide

## Prerequisites

- ? .NET 10 SDK
- ? PostgreSQL 16+ with PostGIS extension
- ? Python 3.x (for PL/Python functions)
- ? Docker (optional, for containers)
- ? Redis (optional, for distributed caching)

## ?? Initial Setup

### 1. Clone and Restore Packages

```bash
cd Hartonomous
dotnet restore
```

### 2. Setup PostgreSQL Database

#### Install PostgreSQL with PostGIS

**Windows (with PostgreSQL installer):**
```bash
# Enable extensions during installation or after:
psql -U postgres
CREATE DATABASE hartonomous;
\c hartonomous
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS "postgis";
CREATE EXTENSION IF NOT EXISTS "plpython3u";
```

**macOS:**
```bash
brew install postgresql postgis
brew services start postgresql
createdb hartonomous
psql hartonomous -c "CREATE EXTENSION postgis;"
psql hartonomous -c "CREATE EXTENSION \"uuid-ossp\";"
```

**Docker (recommended for development):**
```bash
docker run --name hartonomous-postgres \
  -e POSTGRES_USER=postgres \
  -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_DB=hartonomous \
  -p 5432:5432 \
  -d postgis/postgis:16-3.5
```

### 3. Configure Connection String

Update `Hartonomous.API/appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=hartonomous;Username=postgres;Password=postgres"
  },
  "Logging": {
    "EnableSensitiveDataLogging": true,
    "EnableDetailedErrors": true
  }
}
```

### 4. Run Database Migrations

```bash
# Install EF Core tools globally (if not installed)
dotnet tool install --global dotnet-ef

# Create initial migration
cd Hartonomous.Data
dotnet ef migrations add InitialCreate --startup-project ../Hartonomous.API

# Apply migration
dotnet ef database update --startup-project ../Hartonomous.API
```

### 5. Run the Solution

#### Option A: Using .NET Aspire (Recommended)

```bash
cd Hartonomous.AppHost
dotnet run
```

This will start:
- ? Hartonomous.API (Web API)
- ? Hartonomous.Worker (Background Service)
- ? Hartonomous.App.Web (Blazor Web)
- ? Hartonomous.App (MAUI - optional)

Open the **Aspire Dashboard** in your browser (URL shown in console).

#### Option B: Run Individual Projects

**Terminal 1 - API:**
```bash
cd Hartonomous.API
dotnet run
```

**Terminal 2 - Worker:**
```bash
cd Hartonomous.Worker
dotnet run
```

**Terminal 3 - Blazor Web:**
```bash
cd Hartonomous.App/Hartonomous.App.Web
dotnet run
```

## ?? Verify Setup

### Check API
```bash
curl https://localhost:7001/health
# Should return: Healthy

curl https://localhost:7001/openapi/v1.json
# Should return OpenAPI spec
```

### Check Database Connection

```bash
psql -h localhost -U postgres -d hartonomous
\dt  # List tables (should show migrations)
```

## ?? Folder Structure Created

Your solution now has this enterprise-grade structure:

```
? Hartonomous.Core/
   ??? Domain/Common/              (BaseEntity, ValueObject, AggregateRoot)
   ??? Domain/Entities/
   ??? Domain/ValueObjects/
   ??? Application/Commands/
   ??? Application/Queries/
   ??? Application/Interfaces/     (IRepository, IUnitOfWork)
   ??? Application/Common/         (Result, PaginatedResult)

? Hartonomous.Data/
   ??? Context/                    (ApplicationDbContext)
   ??? Repositories/               (Repository, UnitOfWork)
   ??? Functions/PlPython/         (GPU-accelerated functions)
   ??? Spatial/                    (PostGIS support)
   ??? Extensions/                 (DataLayerExtensions)

? Hartonomous.Infrastructure/
   ??? Services/                   (CurrentUser, DateTime)
   ??? Caching/                    (CacheService)
   ??? Messaging/
   ??? Identity/
   ??? Notifications/

? Hartonomous.API/
   ??? Controllers/
   ??? Middleware/                 (Exception handling, Logging)
   ??? Extensions/
```

## ?? Next Steps

### 1. Create Your First Entity

```csharp
// Hartonomous.Core/Domain/Entities/Vehicle.cs
using Hartonomous.Core.Domain.Common;

namespace Hartonomous.Core.Domain.Entities;

public class Vehicle : BaseEntity
{
    public string Make { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Year { get; set; }
    public string VIN { get; set; } = string.Empty;
}
```

### 2. Configure Entity in Data Layer

```csharp
// Hartonomous.Data/Configurations/VehicleConfiguration.cs
using Hartonomous.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hartonomous.Data.Configurations;

public class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.HasKey(v => v.Id);
        
        builder.Property(v => v.VIN)
            .IsRequired()
            .HasMaxLength(17);
            
        builder.HasIndex(v => v.VIN)
            .IsUnique();
    }
}
```

### 3. Add DbSet to Context

```csharp
// Hartonomous.Data/Context/ApplicationDbContext.cs
public DbSet<Vehicle> Vehicles => Set<Vehicle>();
```

### 4. Create Repository Interface

```csharp
// Hartonomous.Core/Application/Interfaces/IVehicleRepository.cs
using Hartonomous.Core.Domain.Entities;

namespace Hartonomous.Core.Application.Interfaces;

public interface IVehicleRepository : IRepository<Vehicle>
{
    Task<Vehicle?> GetByVINAsync(string vin, CancellationToken cancellationToken = default);
}
```

### 5. Implement Repository

```csharp
// Hartonomous.Data/Repositories/VehicleRepository.cs
using Hartonomous.Core.Application.Interfaces;
using Hartonomous.Core.Domain.Entities;
using Hartonomous.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Hartonomous.Data.Repositories;

public class VehicleRepository : Repository<Vehicle>, IVehicleRepository
{
    public VehicleRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<Vehicle?> GetByVINAsync(string vin, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(v => v.VIN == vin, cancellationToken);
    }
}
```

### 6. Create API Controller

```csharp
// Hartonomous.API/Controllers/VehiclesController.cs
using Hartonomous.Core.Application.Interfaces;
using Hartonomous.Core.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace Hartonomous.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VehiclesController : ControllerBase
{
    private readonly IVehicleRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public VehiclesController(IVehicleRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Vehicle>>> GetAll()
    {
        var vehicles = await _repository.GetAllAsync();
        return Ok(vehicles);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Vehicle>> GetById(Guid id)
    {
        var vehicle = await _repository.GetByIdAsync(id);
        if (vehicle == null)
            return NotFound();
            
        return Ok(vehicle);
    }

    [HttpPost]
    public async Task<ActionResult<Vehicle>> Create(Vehicle vehicle)
    {
        await _repository.AddAsync(vehicle);
        await _unitOfWork.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = vehicle.Id }, vehicle);
    }
}
```

### 7. Register Repository in DI

```csharp
// Hartonomous.Data/Extensions/DataLayerExtensions.cs
// Add this line in AddDataLayer method:
services.AddScoped<IVehicleRepository, VehicleRepository>();
```

### 8. Wire Up Data Layer in API

```csharp
// Hartonomous.API/Program.cs
using Hartonomous.Data.Extensions;

// After builder.AddServiceDefaults();
builder.Services.AddDataLayer(builder.Configuration);
```

### 9. Create and Apply Migration

```bash
cd Hartonomous.Data
dotnet ef migrations add AddVehicleEntity --startup-project ../Hartonomous.API
dotnet ef database update --startup-project ../Hartonomous.API
```

### 10. Test Your API

```bash
# Create a vehicle
curl -X POST https://localhost:7001/api/vehicles \
  -H "Content-Type: application/json" \
  -d '{
    "make": "Tesla",
    "model": "Model 3",
    "year": 2024,
    "vin": "5YJ3E1EA9LF000001"
  }'

# Get all vehicles
curl https://localhost:7001/api/vehicles
```

## ?? Recommended Extensions

### Add MediatR (CQRS)
```bash
dotnet add Hartonomous.Core package MediatR
dotnet add Hartonomous.API package MediatR
```

### Add FluentValidation
```bash
dotnet add Hartonomous.Core package FluentValidation
dotnet add Hartonomous.Core package FluentValidation.DependencyInjectionExtensions
```

### Add AutoMapper
```bash
dotnet add Hartonomous.Core package AutoMapper
dotnet add Hartonomous.API package AutoMapper.Extensions.Microsoft.DependencyInjection
```

## ?? Learn More

- [ARCHITECTURE.md](./ARCHITECTURE.md) - Detailed architecture documentation
- [EF Core Documentation](https://learn.microsoft.com/en-us/ef/core/)
- [.NET Aspire Documentation](https://learn.microsoft.com/en-us/dotnet/aspire/)
- [PostGIS Documentation](https://postgis.net/documentation/)

## ?? Troubleshooting

### Connection Issues
- Ensure PostgreSQL is running
- Verify connection string
- Check firewall rules

### Migration Errors
- Ensure no pending model changes
- Drop database and recreate if needed:
  ```bash
  dotnet ef database drop --startup-project ../Hartonomous.API
  dotnet ef database update --startup-project ../Hartonomous.API
  ```

### Build Errors
- Clean solution: `dotnet clean`
- Restore packages: `dotnet restore`
- Rebuild: `dotnet build`

## ?? You're Ready!

Your enterprise-grade solution is now fully scaffolded and ready for development!
