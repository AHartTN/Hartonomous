using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "Hartonomous Code Atomizer API",
        Version = "v0.1.0",
        Description = "High-performance code AST atomization using Roslyn and Tree-sitter",
        Contact = new()
        {
            Name = "Anthony Hart",
            Email = "aharttn@gmail.com",
            Url = new Uri("https://github.com/AHartTN/Hartonomous")
        }
    });
});

// CORS for development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Metrics
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddPrometheusExporter();
        metrics.AddMeter("Hartonomous.CodeAtomizer");
    });

var app = builder.Build();

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseCors();
app.UseAuthorization();
app.MapControllers();
app.MapPrometheusScrapingEndpoint();

Log.Information("?? Hartonomous Code Atomizer API starting...");
Log.Information("   Roslyn: Microsoft.CodeAnalysis.CSharp 4.12.0");
Log.Information("   Tree-sitter: Coming soon");

app.Run();
