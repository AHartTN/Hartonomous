using Hartonomous.Infrastructure.Security.Services;
using System.Security.Claims;

namespace Hartonomous.Core.Services;

/// <summary>
/// Development implementation of ICurrentUserService for testing and development environments.
/// This service provides mock user data when running without full Azure authentication.
/// </summary>
public class DevelopmentCurrentUserService : ICurrentUserService
{
    private const string DevelopmentUserId = "dev-user-123";
    private const string DevelopmentUserEmail = "developer@hartonomous.local";
    private const string DevelopmentUserName = "Development User";

    private readonly List<Claim> _claims;

    public DevelopmentCurrentUserService()
    {
        _claims = new List<Claim>
        {
            new("sub", DevelopmentUserId),
            new("oid", DevelopmentUserId),
            new("email", DevelopmentUserEmail),
            new("name", DevelopmentUserName),
            new("preferred_username", DevelopmentUserEmail),
            new("environment", "development"),
            new(ClaimTypes.Role, "Developer"),
            new(ClaimTypes.Role, "User")
        };
    }

    public string GetUserId()
    {
        return DevelopmentUserId;
    }

    public string? GetUserEmail()
    {
        return DevelopmentUserEmail;
    }

    public string? GetUserName()
    {
        return DevelopmentUserName;
    }

    public bool IsAuthenticated()
    {
        return true; // Always authenticated in development
    }

    public IEnumerable<Claim> GetUserClaims()
    {
        return _claims;
    }

    public string? GetClaimValue(string claimType)
    {
        return _claims.FirstOrDefault(c => c.Type == claimType)?.Value;
    }
}