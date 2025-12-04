using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Hartonomous.API.Configuration;

/// <summary>
/// Configures OpenAPI documentation generation using .NET 10's built-in OpenAPI support.
/// </summary>
public static class SwaggerConfiguration
{
    public static IServiceCollection AddSwaggerConfiguration(this IServiceCollection services)
    {
        // Add built-in OpenAPI document generation (replaces Swashbuckle generator)
        services.AddOpenApi("v1", options =>
        {
            options.OpenApiVersion = Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_1;

            // Add document transformer to set API metadata
            options.AddDocumentTransformer<ApiInfoDocumentTransformer>();

            // Add document transformer to configure JWT Bearer authentication
            options.AddDocumentTransformer<BearerSecurityDocumentTransformer>();
        });

        return services;
    }

    public static IApplicationBuilder UseSwaggerConfiguration(this WebApplication app)
    {
        // Serve OpenAPI JSON document at /openapi/v1.json (allow anonymous for Swagger UI)
        app.MapOpenApi().AllowAnonymous();

        // Use Swashbuckle's Swagger UI to visualize the OpenAPI document
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/openapi/v1.json", "Hartonomous API v1");
            options.RoutePrefix = "api-docs";
            options.DocumentTitle = "Hartonomous API Documentation";
            options.DisplayRequestDuration();
            options.EnableDeepLinking();
            options.EnableFilter();
            options.ShowExtensions();
            options.EnableValidator();
        });

        return app;
    }
}

/// <summary>
/// Document transformer to set API metadata (title, description, contact, license).
/// </summary>
internal sealed class ApiInfoDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Version = "v1",
            Title = "Hartonomous API",
            Description = "Content-addressable storage system with spatial indexing and GPU acceleration",
            Contact = new OpenApiContact
            {
                Name = "Hartonomous Team",
                Email = "info@hartonomous.com"
            },
            License = new OpenApiLicense
            {
                Name = "MIT License",
                Url = new Uri("https://opensource.org/licenses/MIT")
            }
        };

        return Task.CompletedTask;
    }
}

/// <summary>
/// Document transformer to add JWT Bearer authentication security scheme to all operations.
/// </summary>
internal sealed class BearerSecurityDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        // Add Bearer security scheme to components
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token.",
            In = ParameterLocation.Header
        };

        // Apply Bearer requirement to all operations
        if (document.Paths != null)
        {
            foreach (var pathItem in document.Paths.Values)
            {
                if (pathItem.Operations != null)
                {
                    foreach (var operation in pathItem.Operations.Values)
                    {
                        operation.Security ??= [];
                        operation.Security.Add(new OpenApiSecurityRequirement
                        {
                            [new OpenApiSecuritySchemeReference("Bearer", document)] = []
                        });
                    }
                }
            }
        }

        return Task.CompletedTask;
    }
}
