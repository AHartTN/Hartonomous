using Hartonomous.Core.Interfaces;

namespace Hartonomous.Core.Services;

/// <summary>
/// Default implementation of current user service
/// </summary>
public class DefaultCurrentUserService : ICurrentUserService
{
    /// <inheritdoc />
    public Task<string?> GetCurrentUserIdAsync(CancellationToken cancellationToken = default)
    {
        // In a real implementation, this would get the user from the HTTP context, JWT token, etc.
        // For now, return a default user ID
        return Task.FromResult<string?>("default-user");
    }

    /// <inheritdoc />
    public Task<string?> GetCurrentUserNameAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>("Default User");
    }

    /// <inheritdoc />
    public Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> IsInRoleAsync(string role, CancellationToken cancellationToken = default)
    {
        // Simple role check - in production this would check against actual user roles
        return Task.FromResult(role == "User" || role == "Admin");
    }
}