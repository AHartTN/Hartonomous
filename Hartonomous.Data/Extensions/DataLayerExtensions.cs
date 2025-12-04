using Hartonomous.Core.Application.Interfaces;
using Hartonomous.Data.Context;
using Hartonomous.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hartonomous.Data.Extensions;

/// <summary>
/// Data layer dependency injection extensions
/// </summary>
public static class DataLayerExtensions
{
    public static IServiceCollection AddDataLayer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register DbContext with PostgreSQL
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

            options.UseNpgsql(connectionString, npgsqlOptions =>
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

            // Enable sensitive data logging in development
            var enableSensitiveDataLogging = configuration["Logging:EnableSensitiveDataLogging"];
            if (bool.TryParse(enableSensitiveDataLogging, out var sensitiveLogging) && sensitiveLogging)
            {
                options.EnableSensitiveDataLogging();
            }

            // Enable detailed errors in development
            var enableDetailedErrors = configuration["Logging:EnableDetailedErrors"];
            if (bool.TryParse(enableDetailedErrors, out var detailedErrors) && detailedErrors)
            {
                options.EnableDetailedErrors();
            }
        });

        // Register Unit of Work
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Register generic repository
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

        return services;
    }

    public static async Task MigrateDatabase(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.MigrateAsync();
    }
}
