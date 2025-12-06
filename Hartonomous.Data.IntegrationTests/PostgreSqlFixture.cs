using Hartonomous.Core.Application.Interfaces;
using Hartonomous.Data.Context;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Hartonomous.Data.IntegrationTests;

/// <summary>
/// xUnit fixture for PostgreSQL + PostGIS integration tests.
/// Manages container lifecycle, applies migrations, and provides seeded DbContext.
/// </summary>
public class PostgreSqlFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    
    public ApplicationDbContext? DbContext { get; private set; }
    public IUnitOfWork? UnitOfWork => DbContext as IUnitOfWork;
    
    public async Task InitializeAsync()
    {
        // Create PostgreSQL container with PostGIS extension
        _container = new PostgreSqlBuilder()
            .WithImage("postgis/postgis:17-3.5")  // PostgreSQL 17 + PostGIS 3.5
            .WithDatabase("hartonomous_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithCleanUp(true)
            .Build();
        
        // Start container
        await _container.StartAsync();
        
        // Get connection string
        var connectionString = _container.GetConnectionString();
        
        // Create DbContext
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseNpgsql(
            connectionString,
            npgsqlOptions => npgsqlOptions.UseNetTopologySuite())
            .ConfigureWarnings(warnings => 
                warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        
        DbContext = new ApplicationDbContext(optionsBuilder.Options);
        
        // Apply migrations (creates schema + functions + indexes)
        await DbContext.Database.MigrateAsync();
        
        // Seed test data
        await SeedDatabaseAsync();
    }
    
    private async Task SeedDatabaseAsync()
    {
        if (DbContext == null) return;
        
        // Use the DatabaseSeeder to populate test data
        var seeder = new DatabaseSeeder(DbContext);
        await seeder.SeedAsync();
        await DbContext.SaveChangesAsync();
    }
    
    public async Task DisposeAsync()
    {
        if (DbContext != null)
        {
            await DbContext.DisposeAsync();
        }
        
        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }
}
