# Hartonomous - Enterprise Architecture

## ??? Solution Structure

This solution follows **Clean Architecture** and **Domain-Driven Design (DDD)** principles with clear separation of concerns.

### Projects Overview

```
Hartonomous/
??? Hartonomous.Core/              # Domain & Application Layer (Business Logic)
??? Hartonomous.Data/              # Data Access Layer (EF Core, PostgreSQL/PostGIS)
??? Hartonomous.Infrastructure/    # Infrastructure Services
??? Hartonomous.API/               # REST API (ASP.NET Core Web API)
??? Hartonomous.Worker/            # Background Services (Worker Service)
??? Hartonomous.App/               # MAUI Multi-platform App
??? Hartonomous.App.Web/           # Blazor Web App (Server + WASM)
??? Hartonomous.App.Shared/        # Shared Blazor Components
??? Hartonomous.App.Web.Client/    # Blazor WebAssembly Client
??? Hartonomous.AppHost/           # .NET Aspire Orchestration
??? Hartonomous.ServiceDefaults/   # Shared Aspire Defaults
```

---

## ?? Hartonomous.Core

**Purpose**: Contains all business logic, domain models, and application use cases.  
**Dependencies**: None (no external dependencies - pure .NET)

### Folder Structure

```
Core/
??? Domain/
?   ??? Common/                    # Base classes (BaseEntity, AggregateRoot, ValueObject)
?   ??? Entities/                  # Domain entities
?   ??? ValueObjects/              # Immutable value objects
?   ??? Aggregates/                # Aggregate roots
?   ??? Events/                    # Domain events
?   ??? Exceptions/                # Domain exceptions
?   ??? Enums/                     # Domain enumerations
??? Application/
    ??? Commands/                  # CQRS Commands
    ??? Queries/                   # CQRS Queries
    ??? DTOs/                      # Data Transfer Objects
    ??? Interfaces/                # Repository & Service interfaces
    ??? Validators/                # FluentValidation validators
    ??? Mappings/                  # AutoMapper profiles
    ??? Behaviors/                 # MediatR pipeline behaviors
    ??? Common/                    # Result patterns, base classes
    ??? Specifications/            # Specification pattern
```

### Key Patterns
- ? Domain-Driven Design (DDD)
- ? CQRS (Command Query Responsibility Segregation)
- ? Repository Pattern
- ? Unit of Work Pattern
- ? Result Pattern
- ? Specification Pattern

---

## ??? Hartonomous.Data

**Purpose**: Data persistence layer using **EF Core 10** with **PostgreSQL/PostGIS**.  
**Dependencies**: Hartonomous.Core

### Folder Structure

```
Data/
??? Context/
?   ??? ApplicationDbContext.cs    # Main DbContext with PostGIS extensions
??? Configurations/                # EF Core entity configurations
??? Repositories/                  # Repository implementations
?   ??? Repository.cs             # Generic repository
?   ??? UnitOfWork.cs             # Unit of Work implementation
??? Migrations/                    # EF Core migrations
??? Seeders/                       # Data seeders
??? Interceptors/                  # EF Core interceptors
??? Extensions/
?   ??? DataLayerExtensions.cs    # DI registration
??? Functions/
?   ??? PlPython/                 # PL/Python functions for GPU access
?       ??? PlPythonFunctions.cs  # SQL function definitions
??? Spatial/
    ??? SpatialEntity.cs          # Base class for spatial entities
    ??? SpatialQueryExtensions.cs # PostGIS query helpers
```

### Technologies
- ? **EF Core 10** - ORM
- ? **PostgreSQL** - Primary database
- ? **PostGIS** - Spatial data support
- ? **NetTopologySuite** - .NET spatial library
- ? **PL/Python** - GPU-accelerated database functions

### Database Features
- ? Soft delete (global query filter)
- ? Audit fields (CreatedAt, UpdatedAt, etc.)
- ? Spatial data support (Point, Polygon, LineString)
- ? GPU-accelerated computations via PL/Python
- ? Transaction management
- ? Connection resiliency

---

## ?? Hartonomous.Infrastructure

**Purpose**: External service integrations and cross-cutting concerns.  
**Dependencies**: Hartonomous.Core

### Folder Structure

```
Infrastructure/
??? Services/
?   ??? ICurrentUserService.cs    # User context
?   ??? DateTimeService.cs        # Testable date/time
??? Caching/
?   ??? CacheService.cs           # Distributed caching (Redis)
??? Messaging/                     # Message brokers (RabbitMQ, Azure Service Bus)
??? Storage/                       # Blob storage (Azure, AWS S3)
??? Identity/                      # Authentication & Authorization
??? Notifications/                 # Push notifications
??? ExternalServices/              # Third-party API integrations
??? BackgroundJobs/                # Hangfire, Quartz.NET jobs
??? Logging/                       # Structured logging
??? Email/                         # Email service
??? SMS/                           # SMS service
```

### Key Services
- ? Distributed caching (Redis)
- ? Current user context
- ? Date/time abstraction (for testing)

---

## ?? Hartonomous.API

**Purpose**: RESTful API built with **ASP.NET Core Web API**.  
**Dependencies**: Core, Data, Infrastructure, ServiceDefaults

### Folder Structure

```
API/
??? Controllers/                   # API endpoints
?   ??? WeatherForecastController.cs
??? Middleware/
?   ??? ExceptionHandlingMiddleware.cs    # Global error handling
?   ??? RequestLoggingMiddleware.cs       # Request/response logging
??? Filters/                       # Action filters
??? Extensions/
?   ??? MiddlewareExtensions.cs   # Middleware registration
??? Configuration/                 # API-specific config
??? Program.cs                    # Application entry point
```

### Features
- ? JWT Authentication (Microsoft Identity Web)
- ? OpenAPI/Swagger
- ? Global exception handling
- ? Request/response logging
- ? Aspire service defaults
- ? Health checks
- ? OpenTelemetry

---

## ?? Hartonomous.Worker

**Purpose**: Background processing with **Worker Service**.  
**Dependencies**: Core, Data, Infrastructure, ServiceDefaults

### Folder Structure

```
Worker/
??? Jobs/                         # Background job implementations
??? Services/                     # Worker-specific services
??? Configuration/                # Worker configuration
??? Worker.cs                     # Main background service
??? Program.cs                   # Application entry point
```

### Use Cases
- ? Scheduled tasks
- ? Message queue processing
- ? Data synchronization
- ? Report generation

---

## ?? Hartonomous.App (MAUI)

**Purpose**: Cross-platform mobile and desktop application.  
**Target Platforms**: iOS, Android, macOS, Windows

### Structure
```
App/
??? Hartonomous.App/              # MAUI project
??? Hartonomous.App.Shared/       # Shared Blazor components
??? Hartonomous.App.Web/          # Blazor Web (Server + WASM)
??? Hartonomous.App.Web.Client/   # Blazor WebAssembly
```

---

## ?? Hartonomous.AppHost

**Purpose**: **.NET Aspire** orchestration for local development.  
**Dependencies**: API, Worker, App, App.Web

### Features
- ? Service orchestration
- ? Service discovery
- ? Distributed tracing
- ? Centralized configuration

---

## ??? Hartonomous.ServiceDefaults

**Purpose**: Shared Aspire defaults for all services.

### Provides
- ? OpenTelemetry (metrics, traces, logs)
- ? Service discovery
- ? Health checks
- ? HTTP resilience
- ? Standardized logging

---

## ??? Database Configuration

### PostgreSQL Extensions Required
```sql
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";     -- UUID generation
CREATE EXTENSION IF NOT EXISTS "postgis";       -- Spatial data
CREATE EXTENSION IF NOT EXISTS "plpython3u";    -- Python functions (GPU access)
```

### Connection String Example
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=hartonomous;Username=postgres;Password=yourpassword;Include Error Detail=true"
  }
}
```

### GPU-Accelerated Functions
PL/Python functions can leverage GPU through libraries like:
- **CuPy** - GPU-accelerated NumPy
- **TensorFlow** - Machine learning
- **PyTorch** - Deep learning
- **RAPIDS cuSpatial** - GPU-accelerated spatial analytics

---

## ?? Design Principles

### 1. **Separation of Concerns**
- Each layer has a single, well-defined responsibility
- Core layer is independent of external frameworks

### 2. **Dependency Inversion**
- Dependencies point inward toward Core
- Core defines interfaces, outer layers implement them

### 3. **Clean Architecture**
```
???????????????????????????????????????
?        API / Worker / App           ?  Presentation
???????????????????????????????????????
?       Infrastructure & Data         ?  Infrastructure
???????????????????????????????????????
?      Application (Use Cases)        ?  Application
???????????????????????????????????????
?      Domain (Business Logic)        ?  Domain
???????????????????????????????????????
```

### 4. **Testability**
- Interfaces for all external dependencies
- Mockable services (IDateTimeService, ICurrentUserService)
- Repository pattern for data access

### 5. **Performance**
- Distributed caching (Redis)
- EF Core query optimization
- GPU-accelerated database functions
- Spatial indexing with PostGIS

---

## ?? Getting Started

### Prerequisites
- .NET 10 SDK
- PostgreSQL 16+ with PostGIS
- Python 3.x (for PL/Python)
- Docker (optional)

### Setup
```bash
# Restore packages
dotnet restore

# Run migrations
dotnet ef database update --project Hartonomous.Data --startup-project Hartonomous.API

# Run with Aspire
dotnet run --project Hartonomous.AppHost
```

---

## ?? Next Steps

### Implement
1. Add MediatR for CQRS
2. Add FluentValidation for validation
3. Add AutoMapper for mappings
4. Configure authentication/authorization
5. Create domain entities
6. Implement repositories
7. Create API endpoints
8. Add unit & integration tests

### Enhance
- Add Serilog for structured logging
- Integrate Application Insights
- Add API versioning
- Implement rate limiting
- Add API documentation
- Configure CI/CD pipelines

---

## ?? Best Practices

? Use Result pattern instead of exceptions for business logic  
? Keep domain logic in Core layer  
? Use value objects for complex properties  
? Implement domain events for side effects  
? Use specifications for complex queries  
? Follow SOLID principles  
? Write tests for all business logic  
? Use async/await consistently  
? Implement proper error handling  
? Use strongly-typed IDs (Guid)

---

**Version**: 1.0  
**Last Updated**: 2025  
**Target Framework**: .NET 10
