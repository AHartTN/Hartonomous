using Hartonomous.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hartonomous.Data;

/// <summary>
/// Design-time factory for ApplicationDbContext
/// Used by EF Core migrations tooling
/// </summary>
public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        
        // Use a connection string for design-time migrations
        // In production, this is overridden by configuration
        var connectionString = "Host=localhost;Database=hartonomous_dev;Username=postgres;Password=postgres";
        
        optionsBuilder.UseNpgsql(connectionString, npgsqlOptions =>
        {
            npgsqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorCodesToAdd: null);
            
            // Enable PostGIS for spatial data
            npgsqlOptions.UseNetTopologySuite();
            
            // Set command timeout
            npgsqlOptions.CommandTimeout(60);
            
            // Migrations assembly
            npgsqlOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
        });
        
        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
