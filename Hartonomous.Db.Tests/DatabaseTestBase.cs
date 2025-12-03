using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Hartonomous.Db.Tests;

public abstract class DatabaseTestBase : IAsyncLifetime
{
    private PostgreSqlContainer? _postgresContainer;
    protected HartonomousDbContext? DbContext;

    public async Task InitializeAsync()
    {
        _postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("hartonomous_test")
            .WithUsername("postgres")
            .WithPassword("postgres_test")
            .Build();

        await _postgresContainer.StartAsync();

        var options = new DbContextOptionsBuilder<HartonomousDbContext>()
            .UseNpgsql(_postgresContainer.GetConnectionString())
            .EnableSensitiveDataLogging()
            .EnableDetailedErrors()
            .Options;

        DbContext = new HartonomousDbContext(options);
        await DbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (DbContext != null)
        {
            await DbContext.DisposeAsync();
        }

        if (_postgresContainer != null)
        {
            await _postgresContainer.DisposeAsync();
        }
    }
}
