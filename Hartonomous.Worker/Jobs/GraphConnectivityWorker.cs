using Hartonomous.Core.Application.Interfaces;
using Hartonomous.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hartonomous.Worker.Jobs;

/// <summary>
/// Background worker that periodically recalculates the graph connectivity (M-dimension)
/// for Constants based on their reference counts in the composition graph.
/// This ensures the M-dimension reflects the true emergent importance/centrality of the atom.
/// </summary>
public class GraphConnectivityWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<GraphConnectivityWorker> _logger;
    private readonly IQuantizationService _quantizationService;
    
    // Run every 15 minutes to keep centrality relatively fresh without hammering DB
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    public GraphConnectivityWorker(
        IServiceProvider serviceProvider,
        ILogger<GraphConnectivityWorker> logger,
        IQuantizationService quantizationService)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _quantizationService = quantizationService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GraphConnectivityWorker started. Schedule: Every {Interval}", Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await UpdateConnectivityMetricsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing GraphConnectivityWorker");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task UpdateConnectivityMetricsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IUnitOfWork>(); // Assuming UoW exposes Context or similar, or we get Repo
        // Accessing DbContext directly for bulk update efficiency would be better, but let's stick to Repo pattern if possible.
        // However, for a "Graph Worker", we likely need direct DB access for efficient batching.
        // Let's assume we can get the ConstantRepository.
        var constantRepo = scope.ServiceProvider.GetRequiredService<IConstantRepository>();
        
        _logger.LogInformation("Starting graph connectivity update...");

        // 1. Identify constants whose reference count has changed significantly since last projection
        // For this MVP, we'll just process active constants in batches.
        // In a production system, we'd use an event queue or "dirty" flag.
        
        var page = 0;
        var pageSize = 1000;
        var totalUpdated = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            // Fetch batch of constants
            // We ideally want those where Projected M != Calculated M
            // But strictly speaking, we can just iterate all active ones for now to "fix" the placeholder values.
            var constants = await constantRepo.GetPagedAsync(page, pageSize, cancellationToken);
            
            if (!constants.Any()) break;

            foreach (var constant in constants)
            {
                if (constant.Coordinate == null) continue;

                // Calculate TRUE M-dimension based on current reference count
                // Note: ReferenceCount property on Constant should be kept up-to-date by app logic
                // or we might need to COUNT() directly if we don't trust the counter.
                // Assuming ReferenceCount is reliable for now.
                int newM = _quantizationService.CalculateGraphConnectivity(constant.ReferenceCount);

                // If M has changed significantly, re-project
                if (Math.Abs(constant.Coordinate.QuantizedConnectivity - newM) > 100) // Threshold to avoid jitter
                {
                    // We must re-project the constant to move it in Hilbert space
                    // This preserves X, Y, Z but updates M
                    constant.ProjectWithQuantization(
                        constant.Coordinate.QuantizedEntropy,
                        constant.Coordinate.QuantizedCompressibility,
                        newM);
                    
                    totalUpdated++;
                }
            }

            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);
            
            _logger.LogInformation("Processed batch {Page}. Updated {Count} constants.", page, totalUpdated);
            page++;
        }
        
        _logger.LogInformation("Graph connectivity update completed. Total constants moved in M-dimension: {Total}", totalUpdated);
    }
}
