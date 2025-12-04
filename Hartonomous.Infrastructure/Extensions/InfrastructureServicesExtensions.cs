using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Hartonomous.Core.Application.Interfaces;
using Hartonomous.Infrastructure.Caching;
using Hartonomous.Infrastructure.Messaging;
using Hartonomous.Infrastructure.Services;
using Hartonomous.Infrastructure.Services.Decomposers;
using Hartonomous.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hartonomous.Infrastructure.Extensions;

/// <summary>
/// Dependency injection extensions for Infrastructure layer services.
/// </summary>
public static class InfrastructureServicesExtensions
{
    /// <summary>
    /// Add infrastructure services to the service collection.
    /// </summary>
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Add GPU service with connection string
        services.AddSingleton<IGpuService>(sp =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("DefaultConnection string not found");
            var logger = sp.GetRequiredService<ILogger<GpuService>>();
            return new GpuService(connectionString, logger);
        });

        // Register Redis distributed cache
        var redisConnection = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrEmpty(redisConnection))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnection;
                options.InstanceName = configuration["Redis:InstanceName"] ?? "Hartonomous_";
            });
        }
        else
        {
            // Fallback to in-memory cache for development
            services.AddDistributedMemoryCache();
        }

        services.AddSingleton<ICacheService, CacheService>();

        // Register Blob Storage
        var blobStorageConnection = configuration.GetConnectionString("BlobStorage");
        if (!string.IsNullOrEmpty(blobStorageConnection) && 
            !blobStorageConnection.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            // Azure Blob Storage
            services.AddSingleton(sp => new BlobServiceClient(blobStorageConnection));
            services.AddSingleton<IBlobStorageService, AzureBlobStorageService>();
        }
        else
        {
            // Local file storage
            var localStoragePath = blobStorageConnection?.Replace("file://", "") 
                ?? Path.Combine(Directory.GetCurrentDirectory(), "local_storage");
            
            services.AddSingleton<IBlobStorageService>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<LocalFileStorageService>>();
                return new LocalFileStorageService(localStoragePath, logger);
            });
        }

        // Register Message Queue
        var queueConnection = configuration.GetConnectionString("MessageQueue");
        if (!string.IsNullOrEmpty(queueConnection) && 
            queueConnection != "InMemory")
        {
            // Azure Storage Queue
            services.AddSingleton(sp => new QueueServiceClient(queueConnection));
            services.AddSingleton<IMessageQueueService, AzureQueueService>();
        }
        else
        {
            // In-memory queue for development
            services.AddSingleton<IMessageQueueService, InMemoryQueueService>();
        }

        // Add other infrastructure services here
        // services.AddScoped<ICurrentUserService, CurrentUserService>();

        // Register BPE service
        services.AddScoped<IBPEService, BPEService>();

        // Register content decomposers
        services.AddScoped<IContentDecomposer, BinaryDecomposer>();
        services.AddScoped<IContentDecomposer, TextDecomposer>();
        services.AddScoped<IContentDecomposerFactory, ContentDecomposerFactory>();

        return services;
    }
}

