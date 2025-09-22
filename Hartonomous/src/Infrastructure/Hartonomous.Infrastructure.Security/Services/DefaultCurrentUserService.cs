using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace Hartonomous.Infrastructure.Security.Services;

/// <summary>
/// Default implementation of current user service using Azure Entra ID claims
/// Extracts user information from JWT tokens issued by Azure Entra ID/External ID
/// </summary>
public class DefaultCurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DefaultCurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    private ClaimsPrincipal? CurrentUser => _httpContextAccessor.HttpContext?.User;

    /// <summary>
    /// Get the current user's unique identifier from Azure Entra ID
    /// Uses 'oid' claim for Azure AD, 'sub' as fallback
    /// </summary>
    public string GetUserId()
    {
        if (CurrentUser == null || !CurrentUser.Identity?.IsAuthenticated == true)
            throw new UnauthorizedAccessException("No authenticated user found.");

        // Try Azure AD 'oid' claim first (object identifier)
        var oidClaim = CurrentUser.FindFirst("oid")?.Value;
        if (!string.IsNullOrEmpty(oidClaim))
            return oidClaim;

        // Fallback to standard 'sub' claim
        var subClaim = CurrentUser.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(subClaim))
            return subClaim;

        // Last resort - try 'sub' directly
        var directSub = CurrentUser.FindFirst("sub")?.Value;
        if (!string.IsNullOrEmpty(directSub))
            return directSub;

        throw new UnauthorizedAccessException("User ID claim not found in token. Expected 'oid', 'sub', or NameIdentifier claim.");
    }

    /// <summary>
    /// Get the current user's email address from token claims
    /// </summary>
    public string? GetUserEmail()
    {
        return CurrentUser?.FindFirst(ClaimTypes.Email)?.Value ?? 
               CurrentUser?.FindFirst("email")?.Value ??
               CurrentUser?.FindFirst("preferred_username")?.Value;
    }

    /// <summary>
    /// Get the current user's display name from token claims
    /// </summary>
    public string? GetUserName()
    {
        return CurrentUser?.FindFirst(ClaimTypes.Name)?.Value ??
               CurrentUser?.FindFirst("name")?.Value ??
               CurrentUser?.FindFirst("given_name")?.Value;
    }

    /// <summary>
    /// Check if the current user is authenticated with a valid token
    /// </summary>
    public bool IsAuthenticated()
    {
        return CurrentUser?.Identity?.IsAuthenticated == true;
    }

    /// <summary>
    /// Get all claims for the current authenticated user
    /// </summary>
    public IEnumerable<Claim> GetUserClaims()
    {
        return CurrentUser?.Claims ?? Enumerable.Empty<Claim>();
    }

    /// <summary>
    /// Get a specific claim value for the current user
    /// </summary>
    public string? GetClaimValue(string claimType)
    {
        return CurrentUser?.FindFirst(claimType)?.Value;
    }
}

/// <summary>
/// Mock implementation for development/testing when authentication is not available
/// </summary>
public class DevelopmentCurrentUserService : ICurrentUserService
{
    private const string DefaultUserId = "dev-user-12345";
    private const string DefaultUserEmail = "developer@hartonomous.local";
    private const string DefaultUserName = "Development User";

    public string GetUserId() => DefaultUserId;

    public string? GetUserEmail() => DefaultUserEmail;

    public string? GetUserName() => DefaultUserName;

    public bool IsAuthenticated() => true; // Always authenticated in dev

    public IEnumerable<Claim> GetUserClaims()
    {
        return new[]
        {
            new Claim("oid", DefaultUserId),
            new Claim(ClaimTypes.Email, DefaultUserEmail),
            new Claim(ClaimTypes.Name, DefaultUserName),
            new Claim("preferred_username", DefaultUserEmail)
        };
    }

    public string? GetClaimValue(string claimType)
    {
        return claimType switch
        {
            "oid" => DefaultUserId,
            ClaimTypes.Email or "email" => DefaultUserEmail,
            ClaimTypes.Name or "name" => DefaultUserName,
            "preferred_username" => DefaultUserEmail,
            _ => null
        };
    }
}