using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.Enums;
using Hartonomous.Core.Domain.ValueObjects;

namespace Hartonomous.Core.Tests.Domain.Entities;

/// <summary>
/// Unit tests for Constant entity business logic
/// </summary>
public class ConstantTests
{
    [Fact]
    public void Create_WithValidData_ReturnsConstant()
    {
        // Arrange
        var data = "Hello, World!"u8.ToArray();
        var contentType = ContentType.Text;

        // Act
        var constant = Constant.Create(data, contentType);

        // Assert
        constant.Should().NotBeNull();
        constant.Id.Should().NotBeEmpty();
        constant.Data.Should().Equal(data);
        constant.Size.Should().Be(data.Length);
        constant.ContentType.Should().Be(contentType);
        constant.Status.Should().Be(ConstantStatus.Pending);
        constant.ReferenceCount.Should().Be(0);
        constant.Frequency.Should().Be(1);
        constant.Hash.Should().NotBeNull();
        constant.FirstSeenAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        constant.LastAccessedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Create_WithNullData_ThrowsArgumentException()
    {
        // Arrange
        byte[]? data = null;

        // Act
        Action act = () => Constant.Create(data!, ContentType.Text);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*data cannot be null or empty*");
    }

    [Fact]
    public void Create_WithEmptyData_ThrowsArgumentException()
    {
        // Arrange
        var data = Array.Empty<byte>();

        // Act
        Action act = () => Constant.Create(data, ContentType.Text);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*data cannot be null or empty*");
    }

    [Fact]
    public void Project_FromPendingStatus_SuccessfullyProjects()
    {
        // Arrange
        var data = "Test Data"u8.ToArray();
        var constant = Constant.Create(data, ContentType.Text);

        // Act
        constant.Project();

        // Assert
        constant.Status.Should().Be(ConstantStatus.Projected);
        constant.Coordinate.Should().NotBeNull();
        constant.Location.Should().NotBeNull();
        constant.ProjectedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        constant.Coordinate!.HilbertHigh.Should().BeGreaterThanOrEqualTo(0);
        constant.Coordinate!.HilbertLow.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void Project_FromNonPendingStatus_ThrowsInvalidOperationException()
    {
        // Arrange
        var data = "Test Data"u8.ToArray();
        var constant = Constant.Create(data, ContentType.Text);
        constant.Project();

        // Act
        Action act = () => constant.Project();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot project constant in status*");
    }

    [Fact]
    public void Activate_FromProjectedStatus_SuccessfullyActivates()
    {
        // Arrange
        var data = "Test Data"u8.ToArray();
        var constant = Constant.Create(data, ContentType.Text);
        constant.Project();

        // Act
        constant.Activate();

        // Assert
        constant.Status.Should().Be(ConstantStatus.Active);
        constant.ActivatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Activate_FromNonProjectedStatus_ThrowsInvalidOperationException()
    {
        // Arrange
        var data = "Test Data"u8.ToArray();
        var constant = Constant.Create(data, ContentType.Text);

        // Act
        Action act = () => constant.Activate();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot activate constant in status*");
    }

    [Fact]
    public void MarkAsDuplicate_WithValidCanonicalId_SuccessfullyMarks()
    {
        // Arrange
        var data = "Test Data"u8.ToArray();
        var constant = Constant.Create(data, ContentType.Text);
        var canonicalId = Guid.NewGuid();

        // Act
        constant.MarkAsDuplicate(canonicalId);

        // Assert
        constant.CanonicalConstantId.Should().Be(canonicalId);
        constant.IsDuplicate.Should().BeTrue();
        constant.Status.Should().Be(ConstantStatus.Deduplicated);
        constant.DeduplicatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void MarkAsDuplicate_WithEmptyCanonicalId_ThrowsArgumentException()
    {
        // Arrange
        var data = "Test Data"u8.ToArray();
        var constant = Constant.Create(data, ContentType.Text);

        // Act
        Action act = () => constant.MarkAsDuplicate(Guid.Empty);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Canonical constant ID cannot be empty*");
    }

    [Fact]
    public void MarkAsDuplicate_WithSelfId_ThrowsArgumentException()
    {
        // Arrange
        var data = "Test Data"u8.ToArray();
        var constant = Constant.Create(data, ContentType.Text);

        // Act
        Action act = () => constant.MarkAsDuplicate(constant.Id);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Cannot mark constant as duplicate of itself*");
    }

    [Fact]
    public void IncrementReferenceCount_IncreasesCount()
    {
        // Arrange
        var data = "Test Data"u8.ToArray();
        var constant = Constant.Create(data, ContentType.Text);
        var initialCount = constant.ReferenceCount;

        // Act
        constant.IncrementReferenceCount();

        // Assert
        constant.ReferenceCount.Should().Be(initialCount + 1);
    }

    [Fact]
    public void DecrementReferenceCount_DecreasesCount()
    {
        // Arrange
        var data = "Test Data"u8.ToArray();
        var constant = Constant.Create(data, ContentType.Text);
        constant.IncrementReferenceCount();
        constant.IncrementReferenceCount();
        var initialCount = constant.ReferenceCount;

        // Act
        constant.DecrementReferenceCount();

        // Assert
        constant.ReferenceCount.Should().Be(initialCount - 1);
    }

    [Fact]
    public void DecrementReferenceCount_AtZero_RemainsAtZero()
    {
        // Arrange
        var data = "Test Data"u8.ToArray();
        var constant = Constant.Create(data, ContentType.Text);

        // Act
        constant.DecrementReferenceCount();

        // Assert
        constant.ReferenceCount.Should().Be(0);
    }

    [Fact]
    public void IncrementFrequency_IncreasesFrequencyAndUpdatesLastAccessed()
    {
        // Arrange
        var data = "Test Data"u8.ToArray();
        var constant = Constant.Create(data, ContentType.Text);
        var initialFrequency = constant.Frequency;

        // Act
        constant.IncrementFrequency();

        // Assert
        constant.Frequency.Should().Be(initialFrequency + 1);
        constant.LastAccessedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void MarkAsFailed_WithErrorMessage_SetsStatusAndMessage()
    {
        // Arrange
        var data = "Test Data"u8.ToArray();
        var constant = Constant.Create(data, ContentType.Text);
        var errorMessage = "Processing failed due to invalid format";

        // Act
        constant.MarkAsFailed(errorMessage);

        // Assert
        constant.Status.Should().Be(ConstantStatus.Failed);
        constant.ErrorMessage.Should().Be(errorMessage);
    }

    [Fact]
    public void MarkAsFailed_WithNullOrEmptyMessage_ThrowsArgumentException()
    {
        // Arrange
        var data = "Test Data"u8.ToArray();
        var constant = Constant.Create(data, ContentType.Text);

        // Act
        Action act = () => constant.MarkAsFailed(string.Empty);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Error message cannot be null or empty*");
    }

    [Fact]
    public void Archive_FromActiveStatus_SuccessfullyArchives()
    {
        // Arrange
        var data = "Test Data"u8.ToArray();
        var constant = Constant.Create(data, ContentType.Text);
        constant.Project();
        constant.Activate();

        // Act
        constant.Archive();

        // Assert
        constant.Status.Should().Be(ConstantStatus.Archived);
    }

    [Fact]
    public void Archive_FromNonActiveStatus_ThrowsInvalidOperationException()
    {
        // Arrange
        var data = "Test Data"u8.ToArray();
        var constant = Constant.Create(data, ContentType.Text);

        // Act
        Action act = () => constant.Archive();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Can only archive active constants*");
    }

    [Theory]
    [InlineData(new byte[] { 0x00 })]
    [InlineData(new byte[] { 0xFF })]
    [InlineData(new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F })] // "Hello"
    public void Create_WithVariousDataPatterns_GeneratesDifferentHashes(byte[] data)
    {
        // Arrange & Act
        var constant = Constant.Create(data, ContentType.Binary);

        // Assert
        constant.Hash.Should().NotBeNull();
        constant.Hash.ToString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Create_WithSameData_GeneratesSameHash()
    {
        // Arrange
        var data = "Deterministic Test"u8.ToArray();

        // Act
        var constant1 = Constant.Create(data, ContentType.Text);
        var constant2 = Constant.Create(data, ContentType.Text);

        // Assert
        constant1.Hash.ToString().Should().Be(constant2.Hash.ToString());
    }
}
