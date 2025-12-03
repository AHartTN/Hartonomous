using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Hartonomous.Api.Configuration;
using Hartonomous.Api.Services;
using Hartonomous.Db;
using Hartonomous.Db.Configuration;
using Hartonomous.Shared.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Npgsql;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/hartonomous-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// Azure configuration
var azureConfig = builder.Configuration.GetSection("Azure").Get<AzureConfiguration>()
    ?? new AzureConfiguration();

DefaultAzureCredential credential = new(new DefaultAzureCredentialOptions
{
    ManagedIdentityClientId = builder.Configuration["AZURE_CLIENT_ID"],
    ExcludeVisualStudioCredential = true,
    ExcludeVisualStudioCodeCredential = true
});

// Load secrets from Key Vault if configured
if (!string.IsNullOrEmpty(azureConfig.KeyVaultUri))
{
    var secretClient = new SecretClient(new Uri(azureConfig.KeyVaultUri), credential);
    builder.Configuration.AddAzureKeyVault(new Uri(azureConfig.KeyVaultUri), credential);
}

// Configure Azure AD authentication
var azureAdConfig = builder.Configuration.GetSection("AzureAd").Get<AzureAdConfiguration>();
if (azureAdConfig != null && !string.IsNullOrEmpty(azureAdConfig.TenantId))
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("Reader", policy => policy.RequireRole("Hartonomous.Reader"));
        options.AddPolicy("Writer", policy => policy.RequireRole("Hartonomous.Writer"));
        options.AddPolicy("Admin", policy => policy.RequireRole("Hartonomous.Admin"));
    });
}

var dbConfig = DatabaseConfiguration.Load(builder.Environment.EnvironmentName);

// Configure PostgreSQL connection with managed identity
builder.Services.AddDbContext<HartonomousDbContext>((sp, options) =>
{
    var tenantService = sp.GetService<ITenantService>();
    var connectionString = dbConfig.ConnectionString;

    // Use managed identity for Azure AD auth if configured
    if (azureConfig.UseManagedIdentity && connectionString.Contains("localhost") == false)
    {
        var connBuilder = new NpgsqlConnectionStringBuilder(connectionString);

        // Remove password, use access token provider
        connBuilder.Password = null;
        connBuilder.IntegratedSecurity = false;

        var dataSource = new NpgsqlDataSourceBuilder(connBuilder.ConnectionString)
            .UsePeriodicPasswordProvider(async (_, ct) =>
            {
                // Get Azure AD token for PostgreSQL
                var tokenRequestContext = new Azure.Core.TokenRequestContext(
                    new[] { "https://ossrdbms-aad.database.windows.net/.default" });
                var token = await credential.GetTokenAsync(tokenRequestContext, ct);
                return token.Token;
            }, TimeSpan.FromHours(1), TimeSpan.FromSeconds(10))
            .Build();

        options.UseNpgsql(dataSource, npgsqlOptions =>
        {
            npgsqlOptions.EnableRetryOnFailure(
                maxRetryCount: dbConfig.MaxRetryCount,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorCodesToAdd: null);
            npgsqlOptions.CommandTimeout(dbConfig.CommandTimeout);
            npgsqlOptions.UseNetTopologySuite();
            npgsqlOptions.UseVector();
        });
    }
    else
    {
        options.UseNpgsql(connectionString, npgsqlOptions =>
        {
            npgsqlOptions.EnableRetryOnFailure(
                maxRetryCount: dbConfig.MaxRetryCount,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorCodesToAdd: null);
            npgsqlOptions.CommandTimeout(dbConfig.CommandTimeout);
            npgsqlOptions.UseNetTopologySuite();
            npgsqlOptions.UseVector();
        });
    }

    if (dbConfig.EnableSensitiveDataLogging)
        options.EnableSensitiveDataLogging();

    if (dbConfig.EnableDetailedErrors)
        options.EnableDetailedErrors();
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantService, TenantService>();
builder.Services.AddScoped<IAtomService, AtomService>();
builder.Services.AddScoped<ITensorService, TensorService>();
builder.Services.AddScoped<IIngestionService, IngestionService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowMaui",
        policy => policy
            .WithOrigins("https://localhost:7000", "http://localhost:5000")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowMaui");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
