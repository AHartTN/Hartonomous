using FluentAssertions;
using Hartonomous.Db.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Hartonomous.Db.Tests;

public class AuditLogTests : DatabaseTestBase
{
    [Fact]
    public async Task CreateAuditLog_ShouldAddLogToDatabase()
    {
        var auditLog = new AuditLog
        {
            UserId = 1,
            Action = "CREATE",
            EntityType = "User",
            EntityId = 123,
            Details = "{\"field\":\"value\"}"
        };

        DbContext!.AuditLogs.Add(auditLog);
        await DbContext.SaveChangesAsync();

        auditLog.Id.Should().BeGreaterThan(0);

        var savedLog = await DbContext.AuditLogs.FirstOrDefaultAsync(a => a.Id == auditLog.Id);
        savedLog.Should().NotBeNull();
        savedLog!.Action.Should().Be("CREATE");
        savedLog.EntityType.Should().Be("User");
    }

    [Fact]
    public async Task GetAuditLogsByUserId_ShouldReturnUserLogs()
    {
        var userId = 42L;
        var logs = new[]
        {
            new AuditLog { UserId = userId, Action = "LOGIN", EntityType = "Auth" },
            new AuditLog { UserId = userId, Action = "UPDATE", EntityType = "Profile" },
            new AuditLog { UserId = 99L, Action = "DELETE", EntityType = "Post" }
        };

        DbContext!.AuditLogs.AddRange(logs);
        await DbContext.SaveChangesAsync();

        var userLogs = await DbContext.AuditLogs
            .Where(a => a.UserId == userId)
            .ToListAsync();

        userLogs.Should().HaveCount(2);
        userLogs.Should().OnlyContain(a => a.UserId == userId);
    }

    [Fact]
    public async Task GetAuditLogsByTimeRange_ShouldReturnLogsInRange()
    {
        var now = DateTime.UtcNow;
        var log1 = new AuditLog { Action = "ACTION1", Timestamp = now.AddHours(-2) };
        var log2 = new AuditLog { Action = "ACTION2", Timestamp = now.AddHours(-1) };
        var log3 = new AuditLog { Action = "ACTION3", Timestamp = now };

        DbContext!.AuditLogs.AddRange(log1, log2, log3);
        await DbContext.SaveChangesAsync();

        var startTime = now.AddHours(-1.5);
        var endTime = now.AddMinutes(-30);

        var logsInRange = await DbContext.AuditLogs
            .Where(a => a.Timestamp >= startTime && a.Timestamp <= endTime)
            .ToListAsync();

        logsInRange.Should().HaveCount(1);
        logsInRange.First().Action.Should().Be("ACTION2");
    }
}
