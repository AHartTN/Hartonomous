using System.Security.Claims;

namespace Hartonomous.Api.Services;

public interface ITenantService
{
    string? GetCurrentTenantId();
}

public class TenantService : ITenantService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TenantService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? GetCurrentTenantId()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null) return null;

        // Try to get tenant ID from claims
        var tenantIdClaim = user.FindFirst("tid") ??
                           user.FindFirst(ClaimTypes.NameIdentifier) ??
                           user.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid");

        return tenantIdClaim?.Value;
    }
}
