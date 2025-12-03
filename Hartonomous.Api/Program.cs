using Hartonomous.Api.Services;
using Hartonomous.Db;
using Hartonomous.Db.Configuration;
using Hartonomous.Shared.Interfaces;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/hartonomous-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

var dbConfig = DatabaseConfiguration.Load(builder.Environment.EnvironmentName);

builder.Services.AddDbContext<HartonomousDbContext>(options =>
{
    options.UseNpgsql(dbConfig.ConnectionString, npgsqlOptions =>
    {
        npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: dbConfig.MaxRetryCount,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorCodesToAdd: null);
        npgsqlOptions.CommandTimeout(dbConfig.CommandTimeout);
        npgsqlOptions.UseNetTopologySuite();
        npgsqlOptions.UseVector();
    });

    if (dbConfig.EnableSensitiveDataLogging)
        options.EnableSensitiveDataLogging();

    if (dbConfig.EnableDetailedErrors)
        options.EnableDetailedErrors();
});

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
app.UseAuthorization();
app.MapControllers();

app.Run();
