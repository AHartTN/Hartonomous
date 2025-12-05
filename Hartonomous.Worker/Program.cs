using Hartonomous.Data.Extensions;
using Hartonomous.Infrastructure.Extensions;
using Hartonomous.ServiceDefaults;
using Hartonomous.Worker.Configuration;
using Hartonomous.Worker.Jobs;
using Hartonomous.Worker.Services;

namespace Hartonomous.Worker;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        
        // Add Aspire service defaults (telemetry, health checks, service discovery)
        builder.AddServiceDefaults();

        // Configure worker settings
        builder.Services.Configure<WorkerSettings>(
            builder.Configuration.GetSection(WorkerSettings.SectionName));

        // Add data layer (EF Core, repositories, unit of work)
        builder.Services.AddDataLayer(builder.Configuration);

        // Add infrastructure services (caching, current user, etc.)
        builder.Services.AddInfrastructureServices(builder.Configuration);

        // Add MediatR for CQRS
        builder.Services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(Hartonomous.Core.Application.Commands.ContentIngestion.IngestContentCommand).Assembly);
        });

        // Register background workers
        builder.Services.AddHostedService<ContentProcessingWorker>();
        builder.Services.AddHostedService<BPELearningScheduler>();
        builder.Services.AddHostedService<ConstantIndexingWorker>();
        builder.Services.AddHostedService<LandmarkDetectionWorker>();
        builder.Services.AddHostedService<MaterializedViewRefreshJob>();

        // Add health checks
        builder.Services.AddHealthChecks()
            .AddCheck<WorkerHealthCheck>("workers", tags: new[] { "ready" });

        var host = builder.Build();
        host.Run();
    }
}
