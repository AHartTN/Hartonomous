using Hartonomous.Core;
using Hartonomous.Infrastructure.Configuration;
using Hartonomous.Infrastructure.Observability;
using Hartonomous.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

// Add Azure App Configuration and Key Vault (centralized configuration)
builder.Configuration.AddHartonomousAzureConfiguration(builder.Environment);

// Add services to the container
builder.Services.AddControllers();

// Add Hartonomous services
builder.Services.AddHartonomousCore(builder.Configuration);
builder.Services.AddHartonomousAuthentication(builder.Configuration);

// Add Hartonomous data fabric (Neo4j + SQL Server VECTOR + Kafka CDC) - disabled in development
// builder.Services.AddHartonomousDataFabric(builder.Configuration);

// Add API documentation
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Hartonomous API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new()
    {
        Description = "JWT Authorization header using the Bearer scheme",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey
    });
    c.AddSecurityRequirement(new()
    {
        {
            new()
            {
                Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Initialize data fabric - disabled in development
// using (var scope = app.Services.CreateScope())
// {
//     var orchestrator = scope.ServiceProvider.GetRequiredService<DataFabricOrchestrator>();
//     await orchestrator.InitializeAsync();
// }

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Hartonomous API v1");
        c.RoutePrefix = string.Empty; // Serve Swagger UI at root
    });
}

app.UseHttpsRedirection();

// Security middleware
app.UseAuthentication();
app.UseAuthorization();

// Map controllers
app.MapControllers();

// Basic health endpoint only
app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow }));

// Basic health endpoint (backwards compatibility)
app.MapGet("/api/health", () => Results.Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow }))
    .WithName("BasicHealthCheck")
    .WithOpenApi();

app.Run();
