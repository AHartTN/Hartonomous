using Hartonomous.Infrastructure.Configuration;
using Hartonomous.Infrastructure.Observability;
using Hartonomous.Infrastructure.Security;
using Hartonomous.MCP;
using Hartonomous.MCP.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Add Azure Key Vault configuration
builder.Configuration.AddHartonomousKeyVault(builder.Environment);

// Add services to the container
builder.Services.AddControllers();

// Add Hartonomous services
builder.Services.AddHartonomousMcp();
builder.Services.AddHartonomousAuthentication(builder.Configuration);
builder.Services.AddHartonomousObservability("Hartonomous.MCP");

// Add CORS for SignalR
builder.Services.AddCors(options =>
{
    options.AddPolicy("MCP", policy =>
    {
        policy.WithOrigins("http://localhost:3000", "https://localhost:3001") // Add your frontend URLs
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// Add API documentation
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Hartonomous MCP API", Version = "v1" });
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

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Hartonomous MCP API v1");
        c.RoutePrefix = string.Empty; // Serve Swagger UI at root
    });
}

app.UseHttpsRedirection();

// CORS must come before authentication
app.UseCors("MCP");

// Security middleware
app.UseAuthentication();
app.UseAuthorization();

// Map controllers
app.MapControllers();

// Map SignalR hub
app.MapHub<McpHub>("/mcp-hub");

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow, Service = "MCP" }))
    .WithName("HealthCheck");

// MCP-specific endpoints
app.MapGet("/mcp/info", () => Results.Ok(new
{
    Service = "Hartonomous Multi-Agent Context Protocol",
    Version = "1.0.0",
    Capabilities = new[]
    {
        "agent-registration",
        "message-routing",
        "workflow-orchestration",
        "task-assignment",
        "real-time-communication"
    },
    Endpoints = new
    {
        Hub = "/mcp-hub",
        Agents = "/api/agents",
        Workflows = "/api/workflows"
    }
}))
.WithName("McpInfo");

app.Run();