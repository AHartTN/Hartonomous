using Hartonomous.CodeAtomizer.Core.Services;
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

// .NET 10 built-in OpenAPI support (replaces Swashbuckle)
builder.Services.AddOpenApi();

// Register Language Profile Loader as singleton
builder.Services.AddSingleton(sp =>
{
    var profilePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "config", "language-profiles");
    var loader = new LanguageProfileLoader(profilePath);
    loader.LoadProfilesAsync().GetAwaiter().GetResult(); // Load profiles at startup
    return loader;
});

// Register Atom Memory Service as singleton
builder.Services.AddSingleton<AtomMemoryService>();

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

var app = builder.Build();

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // .NET 10 native OpenAPI endpoint
}

app.UseSerilogRequestLogging();
app.UseCors();
app.UseAuthorization();
app.MapControllers();

Log.Information("?? Hartonomous Code Atomizer API starting...");
Log.Information("   Roslyn: C# semantic analysis");
Log.Information("   Tree-sitter: 18+ languages (Python, JS, Go, Rust, Java, etc.)");
Log.Information("   Hilbert curves: 3D?1D spatial indexing");
Log.Information("   OpenAPI: http://localhost:8001/openapi/v1.json");

app.Run();
