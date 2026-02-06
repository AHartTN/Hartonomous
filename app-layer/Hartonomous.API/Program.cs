using Hartonomous.Core.Services;

namespace Hartonomous.API;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();

        // Native engine — singleton (one DB connection for the app lifetime)
        var connString = builder.Configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");

        builder.Services.AddSingleton(sp =>
            new EngineService(connString, sp.GetRequiredService<ILogger<EngineService>>()));

        // Walk, Query, Ingestion services — singleton (they hold native handles)
        builder.Services.AddSingleton(sp =>
            new WalkService(sp.GetRequiredService<EngineService>(), sp.GetRequiredService<ILogger<WalkService>>()));
        builder.Services.AddSingleton(sp =>
            new QueryService(sp.GetRequiredService<EngineService>(), sp.GetRequiredService<ILogger<QueryService>>()));
        builder.Services.AddSingleton(sp =>
            new IngestionService(sp.GetRequiredService<EngineService>(), sp.GetRequiredService<ILogger<IngestionService>>()));

        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        var app = builder.Build();

        app.MapDefaultEndpoints();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.MapControllers();
        app.Run();
    }
}

