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

public class DateTimeService : IDateTimeService
{
    public DateTime UtcNow => DateTime.UtcNow;
    public DateTime Now => DateTime.Now;
    public DateOnly Today => DateOnly.FromDateTime(DateTime.Today);
}
