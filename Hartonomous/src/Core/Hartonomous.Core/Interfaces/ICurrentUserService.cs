namespace Hartonomous.Core.Interfaces;

/// <summary>
/// Service for managing current user context
/// </summary>
public interface ICurrentUserService
{
    /// <summary>
    /// Gets the current user ID
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Current user ID or null if not authenticated</returns>
    Task<string?> GetCurrentUserIdAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current user's display name
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Current user display name or null if not available</returns>
    Task<string?> GetCurrentUserNameAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the current user is authenticated
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if authenticated</returns>
    Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the current user has the specified role
    /// </summary>
    /// <param name="role">Role name to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if user has the role</returns>
    Task<bool> IsInRoleAsync(string role, CancellationToken cancellationToken = default);
}