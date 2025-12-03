# Hartonomous Database Project

Enterprise-grade PostgreSQL database project with multi-environment support for localhost, development, staging, and production environments.

## Architecture

```
Hartonomous-001/
├── Hartonomous.Db/              # Core database project with EF Core
│   ├── Entities/                # Database entities
│   ├── Configuration/           # Database configuration
│   ├── HartonomousDbContext.cs  # EF Core DbContext
│   └── Migrations/              # EF Core migrations (generated)
├── Hartonomous.Db.Tests/        # Unit and integration tests
├── scripts/                     # Deployment and utility scripts
├── docker-compose.yml           # Main Docker configuration
└── azure-pipelines.yml          # Azure Pipelines CI/CD

```

## Prerequisites

- .NET 8.0 SDK or later
- Docker Desktop
- PowerShell 7+ (for deployment scripts)
- Entity Framework Core CLI tools

Install EF Core tools:
```powershell
dotnet tool install --global dotnet-ef
```

## Environment Configuration

### Database Ports

Each environment runs on a different port on localhost:

| Environment | Port | Database Name |
|------------|------|---------------|
| Localhost  | 5432 | hartonomous_localhost |
| Dev        | 5433 | hartonomous_dev |
| Staging    | 5434 | hartonomous_staging |
| Production | 5435 | hartonomous_production |

### PgAdmin

PgAdmin is available at `http://localhost:5050`
- Email: `admin@hartonomous.local`
- Password: `admin`

All four databases are pre-configured in PgAdmin.

## Quick Start

### Deploy All Environments

```powershell
.\scripts\deploy-all.ps1
```

This will:
1. Start Docker containers for all environments
2. Wait for databases to be ready
3. Apply EF Core migrations to each database

### Deploy Single Environment

```powershell
.\scripts\deploy-environment.ps1 -Environment localhost
.\scripts\deploy-environment.ps1 -Environment dev
.\scripts\deploy-environment.ps1 -Environment staging
.\scripts\deploy-environment.ps1 -Environment production
```

### Manual Docker Deployment

Start all environments:
```bash
docker-compose up -d
```

Start specific environment:
```bash
docker-compose up -d postgres-localhost
docker-compose up -d postgres-dev
docker-compose up -d postgres-staging
docker-compose up -d postgres-production
```

## Database Migrations

### Create a New Migration

```powershell
cd Hartonomous.Db

# Set environment
$env:ASPNETCORE_ENVIRONMENT = "localhost"

# Create migration
dotnet ef migrations add YourMigrationName
```

### Apply Migrations

```powershell
# Apply to localhost
$env:ASPNETCORE_ENVIRONMENT = "localhost"
dotnet ef database update

# Apply to dev
$env:ASPNETCORE_ENVIRONMENT = "dev"
dotnet ef database update

# Apply to staging
$env:ASPNETCORE_ENVIRONMENT = "staging"
dotnet ef database update

# Apply to production
$env:ASPNETCORE_ENVIRONMENT = "production"
dotnet ef database update
```

### Rollback Migration

```powershell
# Rollback to specific migration
dotnet ef database update PreviousMigrationName

# Rollback all migrations
dotnet ef database update 0
```

### Remove Last Migration

```powershell
dotnet ef migrations remove
```

## Running Tests

```powershell
# Run all tests
dotnet test

# Run tests with detailed output
dotnet test --verbosity detailed

# Run tests with code coverage
dotnet test --collect:"XPlat Code Coverage"
```

The test project uses Testcontainers to spin up PostgreSQL containers automatically for integration tests.

## Connection Strings

### Localhost
```
Host=localhost;Port=5432;Database=hartonomous_localhost;Username=postgres;Password=postgres_local
```

### Dev
```
Host=localhost;Port=5433;Database=hartonomous_dev;Username=postgres;Password=postgres_dev
```

### Staging
```
Host=localhost;Port=5434;Database=hartonomous_staging;Username=postgres;Password=postgres_staging
```

### Production
```
Host=localhost;Port=5435;Database=hartonomous_production;Username=postgres;Password=postgres_prod
```

## Environment Variables

Environment-specific variables are stored in `.env.{environment}` files:
- `.env.localhost`
- `.env.dev`
- `.env.staging`
- `.env.production`

See `.env.example` for all available configuration options.

## Azure Pipelines

The project includes a complete Azure Pipelines configuration (`azure-pipelines.yml`) that:

1. **Build Stage**: Builds the solution and runs tests
2. **Deploy Localhost**: Deploys to localhost (on main branch)
3. **Deploy Dev**: Deploys to dev (on develop branch)
4. **Deploy Staging**: Deploys to staging (after dev)
5. **Deploy Production**: Deploys to production (after staging)

### Setting Up Azure Pipelines

1. Create a new pipeline in Azure DevOps
2. Point it to `azure-pipelines.yml`
3. Configure environments in Azure DevOps:
   - localhost
   - dev
   - staging
   - production

## Teardown / Cleanup

### Stop and Remove Single Environment

```powershell
.\scripts\teardown-environment.ps1 -Environment localhost
```

You'll be prompted whether to delete the data volume.

### Stop and Remove All Environments

```powershell
.\scripts\teardown-environment.ps1 -Environment all
```

This removes all containers and volumes (deletes all data).

### Manual Docker Cleanup

```bash
# Stop all containers
docker-compose down

# Stop and remove volumes (deletes data)
docker-compose down -v

# Remove specific container
docker-compose rm -f postgres-localhost
```

## Database Schema

### Users Table

| Column | Type | Description |
|--------|------|-------------|
| id | bigint | Primary key |
| email | varchar(255) | Unique email address |
| first_name | varchar(100) | User's first name |
| last_name | varchar(100) | User's last name |
| created_at | timestamp | Creation timestamp |
| updated_at | timestamp | Last update timestamp |
| is_active | boolean | Active status |

### Audit Logs Table

| Column | Type | Description |
|--------|------|-------------|
| id | bigint | Primary key |
| user_id | bigint | Foreign key to users |
| action | varchar(100) | Action performed |
| entity_type | varchar(100) | Type of entity |
| entity_id | bigint | ID of entity |
| timestamp | timestamp | When action occurred |
| details | jsonb | JSON details |

## Best Practices

### Development Workflow

1. Make schema changes in `HartonomousDbContext.cs`
2. Create migration: `dotnet ef migrations add MigrationName`
3. Review generated migration in `Migrations/` folder
4. Test on localhost: `dotnet ef database update`
5. Run tests: `dotnet test`
6. Commit migration files
7. Deploy through pipeline

### Production Deployments

1. Always test migrations on localhost and dev first
2. Backup production database before applying migrations
3. Review migration SQL: `dotnet ef migrations script`
4. Apply to staging, verify thoroughly
5. Apply to production during maintenance window

### Security Notes

- Change default passwords in production
- Use Azure Key Vault or similar for production secrets
- Enable SSL for production databases
- Implement proper backup strategies
- Use read replicas for reporting queries

## Troubleshooting

### Database Won't Start

```powershell
# Check container logs
docker logs hartonomous-db-localhost

# Restart container
docker-compose restart postgres-localhost
```

### Migration Fails

```powershell
# Check current migration status
dotnet ef migrations list

# Drop and recreate database (DEVELOPMENT ONLY)
dotnet ef database drop
dotnet ef database update
```

### Connection Refused

Ensure the correct port is being used for your environment and that the Docker container is running:

```bash
docker ps | grep hartonomous
```

### Test Database Issues

Tests use Testcontainers which require Docker. Ensure Docker Desktop is running.

## Performance Tuning

### Connection Pooling

The project uses Npgsql's built-in connection pooling. Configure in `DatabaseConfiguration.cs`:

```csharp
MaxPoolSize=100;MinPoolSize=10
```

### Indexes

Add indexes for frequently queried columns in `HartonomousDbContext.cs`:

```csharp
entity.HasIndex(e => e.Email);
```

### Query Optimization

Use EF Core's query filters and projections to limit data:

```csharp
var users = await context.Users
    .Where(u => u.IsActive)
    .Select(u => new { u.Id, u.Email })
    .ToListAsync();
```

## Contributing

1. Create feature branch from `develop`
2. Make changes and add tests
3. Ensure all tests pass
4. Create pull request to `develop`
5. After approval, merge to `develop`
6. Release from `develop` to `main`

## License

Copyright © 2025 Hartonomous. All rights reserved.

## Support

For issues, questions, or contributions, please contact the development team.
