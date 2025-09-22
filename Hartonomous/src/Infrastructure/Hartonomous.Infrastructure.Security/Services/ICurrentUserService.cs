using System.Security.Claims;

namespace Hartonomous.Infrastructure.Security.Services;

/// <summary>
/// Service for accessing current user information from Azure Entra ID tokens
/// Provides abstraction over ClaimsPrincipal for user identity operations
/// </summary>
public interface ICurrentUserService
{
    /// <summary>
    /// Get the current user's unique identifier from Azure Entra ID
    /// </summary>
    /// <returns>User ID (oid claim) from the token</returns>
    string GetUserId();

    /// <summary>
    /// Get the current user's email address
    /// </summary>
    /// <returns>Email claim from the token</returns>
    string? GetUserEmail();

    /// <summary>
    /// Get the current user's display name
    /// </summary>
    /// <returns>Name claim from the token</returns>
    string? GetUserName();

    /// <summary>
    /// Check if the current user is authenticated
    /// </summary>
    /// <returns>True if user has valid authentication token</returns>
    bool IsAuthenticated();

    /// <summary>
    /// Get all claims for the current user
    /// </summary>
    /// <returns>Collection of claims from the authentication token</returns>
    IEnumerable<Claim> GetUserClaims();

    /// <summary>
    /// Get a specific claim value for the current user
    /// </summary>
    /// <param name="claimType">The claim type to retrieve</param>
    /// <returns>The claim value if found, null otherwise</returns>
    string? GetClaimValue(string claimType);
}