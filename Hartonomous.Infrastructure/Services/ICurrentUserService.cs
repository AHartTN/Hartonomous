namespace Hartonomous.Infrastructure.Services;

/// <summary>
/// Base service interface for current user context
/// </summary>
public interface ICurrentUserService
{
    string? UserId { get; }
    string? UserName { get; }
    string? Email { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
    IEnumerable<string> GetRoles();
}
