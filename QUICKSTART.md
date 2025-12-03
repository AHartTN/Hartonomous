# Quick Start Guide

Get up and running with Hartonomous Database in 5 minutes.

## Prerequisites Check

```powershell
# Check .NET version (need 8.0+)
dotnet --version

# Check Docker
docker --version

# Install EF Core tools if needed
dotnet tool install --global dotnet-ef
```

## 1. Deploy All Environments

```powershell
# From project root
.\scripts\deploy-all.ps1
```

This starts all 4 database environments and applies migrations.

## 2. Verify Deployment

```powershell
# Check running containers
docker ps | Select-String hartonomous

# You should see 5 containers:
# - hartonomous-db-localhost (port 5432)
# - hartonomous-db-dev (port 5433)
# - hartonomous-db-staging (port 5434)
# - hartonomous-db-production (port 5435)
# - hartonomous-pgadmin (port 5050)
```

## 3. Connect to Databases

### Via PgAdmin (GUI)

1. Open http://localhost:5050
2. Login with:
   - Email: `admin@hartonomous.local`
   - Password: `admin`
3. All 4 databases are pre-configured

### Via Connection String (Code)

**Localhost:**
```
Host=localhost;Port=5432;Database=hartonomous_localhost;Username=postgres;Password=postgres_local
```

**Dev:**
```
Host=localhost;Port=5433;Database=hartonomous_dev;Username=postgres;Password=postgres_dev
```

## 4. Run Tests

```powershell
dotnet test
```

Tests use Testcontainers and will spin up temporary PostgreSQL instances automatically.

## 5. Create Your First Migration

```powershell
cd Hartonomous.Db

# Set environment
$env:ASPNETCORE_ENVIRONMENT = "localhost"

# Add entity to Entities/ folder (create your class)
# Update HartonomousDbContext.cs with your new entity

# Create migration
dotnet ef migrations add AddYourEntity

# Apply migration
dotnet ef database update
```

## Common Commands

```powershell
# Deploy single environment
.\scripts\deploy-environment.ps1 -Environment localhost

# Stop and remove environment
.\scripts\teardown-environment.ps1 -Environment localhost

# View logs
docker logs hartonomous-db-localhost

# Restart environment
docker-compose restart postgres-localhost
```

## Ports Reference

| Environment | Port | Database |
|------------|------|----------|
| Localhost  | 5432 | hartonomous_localhost |
| Dev        | 5433 | hartonomous_dev |
| Staging    | 5434 | hartonomous_staging |
| Production | 5435 | hartonomous_production |
| PgAdmin    | 5050 | Web UI |

## Next Steps

- Read [README.md](README.md) for detailed documentation
- Review entity models in `Hartonomous.Db/Entities/`
- Check out test examples in `Hartonomous.Db.Tests/`
- Customize appsettings for your needs

## Troubleshooting

**"Port already in use"**
```powershell
# Find process using port
netstat -ano | findstr :5432

# Kill process (replace PID)
taskkill /PID <PID> /F
```

**"Docker not running"**
```powershell
# Start Docker Desktop
# Wait for it to fully start, then retry
```

**"Migration failed"**
```powershell
# Check which migrations are applied
dotnet ef migrations list

# Drop and recreate (DEVELOPMENT ONLY)
dotnet ef database drop -f
dotnet ef database update
```

## Quick Test

```powershell
# Create a test user via psql
docker exec -it hartonomous-db-localhost psql -U postgres -d hartonomous_localhost

# In psql shell:
INSERT INTO users (email, first_name, last_name, is_active)
VALUES ('test@example.com', 'Test', 'User', true);

SELECT * FROM users;

\q
```

Done! You now have a fully functional enterprise database setup.
