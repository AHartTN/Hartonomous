using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;
using Serilog;

namespace Hartonomous.IntegrationTests.Infrastructure;

/// <summary>
/// Main test fixture for integration tests that provides database, web application, and authentication setup
/// </summary>
public class TestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sqlContainer;
    private readonly IConfiguration _testConfiguration;

    public string ConnectionString { get; private set; } = string.Empty;
    public string TestUserId { get; private set; } = "test-user-123";
    public HttpClient HttpClient { get; private set; } = null!;

    public TestFixture()
    {
        // Configure Serilog for test logging
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .CreateLogger();

        // Load test configuration
        _testConfiguration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.IntegrationTests.json")
            .AddEnvironmentVariables()
            .Build();

        // Configure SQL Server container
        _sqlContainer = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .WithPassword("YourStrong!Passw0rd")
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("MSSQL_PID", "Developer")
            .WithPortBinding(0, 1433)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(1433))
            .Build();
    }

    public async Task InitializeAsync()
    {
        // Start SQL Server container if configured to use TestContainers
        if (_testConfiguration.GetValue<bool>("IntegrationTests:UseTestContainers"))
        {
            await _sqlContainer.StartAsync();
            var port = _sqlContainer.GetMappedPublicPort(1433);
            ConnectionString = $"Server=localhost,{port};Database=HartonomousDB_Tests;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=true;";
        }
        else
        {
            ConnectionString = _testConfiguration.GetConnectionString("DefaultConnection")!;
        }

        // Initialize database schema
        await InitializeDatabaseAsync();

        // Create HTTP client with authentication
        HttpClient = CreateAuthenticatedClient();
    }

    public async Task DisposeAsync()
    {
        HttpClient?.Dispose();
        if (_testConfiguration.GetValue<bool>("IntegrationTests:UseTestContainers"))
        {
            await _sqlContainer.DisposeAsync();
        }
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddConfiguration(_testConfiguration);
            config.AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("ConnectionStrings:DefaultConnection", ConnectionString)
            });
        });

        builder.ConfigureServices(services =>
        {
            // Configure test-specific services
            services.AddSingleton<ILoggerFactory>(provider =>
                LoggerFactory.Create(builder => builder.AddSerilog()));

            // Add integration test specific configurations
            services.Configure<IntegrationTestOptions>(
                _testConfiguration.GetSection("IntegrationTests"));
        });

        builder.UseEnvironment("IntegrationTests");
    }

    private async Task InitializeDatabaseAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        // Create database if it doesn't exist
        var createDbCommand = connection.CreateCommand();
        createDbCommand.CommandText = @"
            IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'HartonomousDB_Tests')
                CREATE DATABASE HartonomousDB_Tests;";
        await createDbCommand.ExecuteNonQueryAsync();

        // Switch to the test database
        await connection.ChangeDatabaseAsync("HartonomousDB_Tests");

        // Execute schema creation scripts
        await ExecuteSchemaScriptAsync(connection);
    }

    private async Task ExecuteSchemaScriptAsync(SqlConnection connection)
    {
        var schemaScript = @"
            -- Create Projects table
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Projects')
            CREATE TABLE dbo.Projects (
                ProjectId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                UserId NVARCHAR(128) NOT NULL,
                ProjectName NVARCHAR(256) NOT NULL,
                CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
            );

            -- Create ModelMetadata table
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ModelMetadata')
            CREATE TABLE dbo.ModelMetadata (
                ModelId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                ProjectId UNIQUEIDENTIFIER NOT NULL,
                ModelName NVARCHAR(256) NOT NULL,
                Version NVARCHAR(50) NOT NULL,
                License NVARCHAR(100) NOT NULL,
                MetadataJson NVARCHAR(MAX),
                CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(ProjectId)
            );

            -- Create ModelComponents table
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ModelComponents')
            CREATE TABLE dbo.ModelComponents (
                ComponentId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                ModelId UNIQUEIDENTIFIER NOT NULL,
                ComponentName NVARCHAR(512) NOT NULL,
                ComponentType NVARCHAR(128) NOT NULL,
                CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                FOREIGN KEY (ModelId) REFERENCES dbo.ModelMetadata(ModelId)
            );

            -- Create OutboxEvents table
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'OutboxEvents')
            CREATE TABLE dbo.OutboxEvents (
                EventId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                EventType NVARCHAR(255) NOT NULL,
                Payload NVARCHAR(MAX) NOT NULL,
                CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                ProcessedAt DATETIME2 NULL
            );";

        var command = connection.CreateCommand();
        command.CommandText = schemaScript;
        await command.ExecuteNonQueryAsync();
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();

        // Generate JWT token for test user
        var token = GenerateJwtToken(TestUserId, "Integration Test User", "test@hartonomous.com");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private string GenerateJwtToken(string userId, string name, string email)
    {
        var jwtConfig = _testConfiguration.GetSection("Authentication:Jwt");
        var key = Encoding.ASCII.GetBytes(jwtConfig["Key"]!);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Name, name),
                new Claim(ClaimTypes.Email, email),
                new Claim("sub", userId)
            }),
            Expires = DateTime.UtcNow.AddHours(1),
            Issuer = jwtConfig["Issuer"],
            Audience = jwtConfig["Audience"],
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    public async Task CleanDatabaseAsync()
    {
        if (!_testConfiguration.GetValue<bool>("IntegrationTests:CleanupBetweenTests"))
            return;

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ChangeDatabaseAsync("HartonomousDB_Tests");

        var cleanupScript = @"
            DELETE FROM dbo.ModelComponents;
            DELETE FROM dbo.ModelMetadata;
            DELETE FROM dbo.Projects;
            DELETE FROM dbo.OutboxEvents;";

        var command = connection.CreateCommand();
        command.CommandText = cleanupScript;
        await command.ExecuteNonQueryAsync();
    }
}

public class IntegrationTestOptions
{
    public bool UseTestContainers { get; set; } = true;
    public bool UseRealServices { get; set; } = false;
    public bool CleanupBetweenTests { get; set; } = true;
    public TimeSpan DatabaseTimeout { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromMinutes(1);
    public bool ParallelExecution { get; set; } = false;
}