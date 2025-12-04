using FluentValidation;
using Hartonomous.Core.Application.Common;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Hartonomous.Core.Application.Extensions;

public static class ApplicationLayerExtensions
{
    /// <summary>
    /// Registers application layer services including MediatR and FluentValidation
    /// </summary>
    public static IServiceCollection AddApplicationLayer(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        // Register MediatR with all handlers from this assembly
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            
            // Add validation pipeline behavior
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        // Register all FluentValidation validators from this assembly
        services.AddValidatorsFromAssembly(assembly);

        return services;
    }
}
