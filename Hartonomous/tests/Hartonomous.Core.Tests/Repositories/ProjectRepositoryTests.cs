using Hartonomous.Core.DTOs;
using Hartonomous.Core.Repositories;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace Hartonomous.Core.Tests.Repositories;

public class ProjectRepositoryTests
{
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<IConfigurationSection> _mockConnectionStringsSection;
    private readonly ProjectRepository _repository;
    private const string TestConnectionString = "Server=localhost;Database=HartonomousTestDB;Trusted_Connection=true;";

    public ProjectRepositoryTests()
    {
        _mockConfiguration = new Mock<IConfiguration>();
        _mockConnectionStringsSection = new Mock<IConfigurationSection>();

        _mockConnectionStringsSection.Setup(x => x["DefaultConnection"])
            .Returns(TestConnectionString);

        _mockConfiguration.Setup(x => x.GetSection("ConnectionStrings"))
            .Returns(_mockConnectionStringsSection.Object);

        // Setup the direct indexer access
        _mockConfiguration.Setup(x => x["ConnectionStrings:DefaultConnection"])
            .Returns(TestConnectionString);

        _repository = new ProjectRepository(_mockConfiguration.Object);
    }

    [Fact]
    public void Constructor_WithValidConfiguration_ShouldNotThrow()
    {
        // Arrange & Act & Assert
        Assert.NotNull(_repository);
    }

    [Fact]
    public void Constructor_WithNullConnectionString_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var mockConfig = new Mock<IConfiguration>();
        mockConfig.Setup(x => x["ConnectionStrings:DefaultConnection"])
            .Returns((string?)null);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => new ProjectRepository(mockConfig.Object));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task GetProjectsByUserAsync_WithInvalidUserId_ShouldThrowArgumentException(string? userId)
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _repository.GetProjectsByUserAsync(userId!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task CreateProjectAsync_WithInvalidUserId_ShouldThrowArgumentException(string? userId)
    {
        // Arrange
        var request = new CreateProjectRequest("Test Project");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _repository.CreateProjectAsync(request, userId!));
    }

    [Fact]
    public async Task CreateProjectAsync_WithNullRequest_ShouldThrowArgumentNullException()
    {
        // Arrange
        var userId = "test-user-123";

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.CreateProjectAsync(null!, userId));
    }
}