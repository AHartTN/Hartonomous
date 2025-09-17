using Microsoft.Extensions.Diagnostics.HealthChecks;
using Hartonomous.Infrastructure.Neo4j;
using Hartonomous.Infrastructure.Milvus;

namespace Hartonomous.Infrastructure.EventStreaming.HealthChecks;

/// <summary>
/// Health check for the complete Hartonomous data fabric
/// </summary>
public class DataFabricHealthCheck : IHealthCheck
{
    private readonly DataFabricOrchestrator _orchestrator;

    public DataFabricHealthCheck(DataFabricOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var health = await _orchestrator.CheckHealthAsync();

            var isHealthy = health.OverallStatus == "Healthy";
            var status = isHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy;

            var data = new Dictionary<string, object>
            {
                ["neo4j_status"] = health.Neo4jStatus,
                ["neo4j_details"] = health.Neo4jDetails,
                ["milvus_status"] = health.MilvusStatus,
                ["milvus_details"] = health.MilvusDetails,
                ["overall_status"] = health.OverallStatus,
                ["check_time"] = health.CheckTime
            };

            return new HealthCheckResult(status,
                $"Data fabric is {health.OverallStatus.ToLower()}",
                data: data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Data fabric health check failed",
                ex);
        }
    }
}