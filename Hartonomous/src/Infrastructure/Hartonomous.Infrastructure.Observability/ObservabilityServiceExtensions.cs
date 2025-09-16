using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
using Hartonomous.Infrastructure.Observability.Interfaces;
using Hartonomous.Infrastructure.Observability.Services;

namespace Hartonomous.Infrastructure.Observability;

public static class ObservabilityServiceExtensions
{
    public static IServiceCollection AddHartonomousObservability(this IServiceCollection services, string serviceName)
    {
        var resourceBuilder = ResourceBuilder.CreateDefault().AddService(serviceName);

        services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .SetResourceBuilder(resourceBuilder)
                .AddAspNetCoreInstrumentation()
                .AddConsoleExporter())
            .WithTracing(tracing => tracing
                .SetResourceBuilder(resourceBuilder)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSqlClientInstrumentation()
                .AddConsoleExporter());

        // Register metrics collector
        services.AddSingleton<IMetricsCollector, NoOpMetricsCollector>();

        return services;
    }
}