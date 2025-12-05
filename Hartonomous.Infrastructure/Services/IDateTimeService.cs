namespace Hartonomous.Infrastructure.Services;

/// <summary>
/// Date/Time service interface for testability
/// </summary>
public interface IDateTimeService
{
    DateTime UtcNow { get; }
    DateTime Now { get; }
    DateOnly Today { get; }
}
