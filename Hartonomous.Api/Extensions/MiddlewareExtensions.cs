namespace Hartonomous.API.Extensions;

/// <summary>
/// Middleware extension methods
/// </summary>
public static class MiddlewareExtensions
{
    public static IApplicationBuilder UseCustomExceptionHandler(this IApplicationBuilder app)
    {
        return app.UseMiddleware<Middleware.ExceptionHandlingMiddleware>();
    }

    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app)
    {
        return app.UseMiddleware<Middleware.RequestLoggingMiddleware>();
    }
}
