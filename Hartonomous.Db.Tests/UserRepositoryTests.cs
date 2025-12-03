using FluentAssertions;
using Hartonomous.Db.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Hartonomous.Db.Tests;

public class UserRepositoryTests : DatabaseTestBase
{
    [Fact]
    public async Task CreateUser_ShouldAddUserToDatabase()
    {
        var user = new User
        {
            Email = "test@example.com",
            FirstName = "Test",
            LastName = "User",
            IsActive = true
        };

        DbContext!.Users.Add(user);
        await DbContext.SaveChangesAsync();

        user.Id.Should().BeGreaterThan(0);

        var savedUser = await DbContext.Users.FirstOrDefaultAsync(u => u.Email == "test@example.com");
        savedUser.Should().NotBeNull();
        savedUser!.FirstName.Should().Be("Test");
        savedUser.LastName.Should().Be("User");
        savedUser.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetUserByEmail_ShouldReturnUser()
    {
        var user = new User
        {
            Email = "findme@example.com",
            FirstName = "Find",
            LastName = "Me",
            IsActive = true
        };

        DbContext!.Users.Add(user);
        await DbContext.SaveChangesAsync();

        var foundUser = await DbContext.Users
            .FirstOrDefaultAsync(u => u.Email == "findme@example.com");

        foundUser.Should().NotBeNull();
        foundUser!.Email.Should().Be("findme@example.com");
    }

    [Fact]
    public async Task UpdateUser_ShouldModifyUserData()
    {
        var user = new User
        {
            Email = "update@example.com",
            FirstName = "Original",
            LastName = "Name",
            IsActive = true
        };

        DbContext!.Users.Add(user);
        await DbContext.SaveChangesAsync();

        user.FirstName = "Updated";
        user.LastName = "NewName";
        await DbContext.SaveChangesAsync();

        var updatedUser = await DbContext.Users.FindAsync(user.Id);
        updatedUser.Should().NotBeNull();
        updatedUser!.FirstName.Should().Be("Updated");
        updatedUser.LastName.Should().Be("NewName");
    }

    [Fact]
    public async Task DeleteUser_ShouldRemoveUserFromDatabase()
    {
        var user = new User
        {
            Email = "delete@example.com",
            FirstName = "Delete",
            LastName = "Me",
            IsActive = true
        };

        DbContext!.Users.Add(user);
        await DbContext.SaveChangesAsync();
        var userId = user.Id;

        DbContext.Users.Remove(user);
        await DbContext.SaveChangesAsync();

        var deletedUser = await DbContext.Users.FindAsync(userId);
        deletedUser.Should().BeNull();
    }

    [Fact]
    public async Task CreateUser_WithDuplicateEmail_ShouldThrowException()
    {
        var user1 = new User
        {
            Email = "duplicate@example.com",
            FirstName = "First",
            LastName = "User",
            IsActive = true
        };

        DbContext!.Users.Add(user1);
        await DbContext.SaveChangesAsync();

        var user2 = new User
        {
            Email = "duplicate@example.com",
            FirstName = "Second",
            LastName = "User",
            IsActive = true
        };

        DbContext.Users.Add(user2);

        var act = async () => await DbContext.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
