using Hartonomous.Core.Domain.ValueObjects;

namespace Hartonomous.Core.Tests.Domain.ValueObjects;

/// <summary>
/// Unit tests for Hash256 value object
/// </summary>
public class Hash256Tests
{
    [Fact]
    public void Compute_WithValidData_ReturnsHash()
    {
        // Arrange
        var data = "Hello, World!"u8.ToArray();

        // Act
        var hash = Hash256.Compute(data);

        // Assert
        hash.Should().NotBeNull();
        hash.ToString().Should().NotBeNullOrEmpty();
        hash.ToString().Length.Should().Be(64); // SHA-256 produces 64 hex characters
    }

    [Fact]
    public void Compute_WithSameData_ProducesSameHash()
    {
        // Arrange
        var data = "Deterministic test data"u8.ToArray();

        // Act
        var hash1 = Hash256.Compute(data);
        var hash2 = Hash256.Compute(data);

        // Assert
        hash1.ToString().Should().Be(hash2.ToString());
    }

    [Fact]
    public void Compute_WithDifferentData_ProducesDifferentHashes()
    {
        // Arrange
        var data1 = "First dataset"u8.ToArray();
        var data2 = "Second dataset"u8.ToArray();

        // Act
        var hash1 = Hash256.Compute(data1);
        var hash2 = Hash256.Compute(data2);

        // Assert
        hash1.ToString().Should().NotBe(hash2.ToString());
    }

    [Fact]
    public void Compute_WithEmptyData_ReturnsValidHash()
    {
        // Arrange
        var data = Array.Empty<byte>();

        // Act
        var hash = Hash256.Compute(data);

        // Assert
        hash.Should().NotBeNull();
        hash.ToString().Length.Should().Be(64);
    }

    [Theory]
    [InlineData(new byte[] { 0x00 })]
    [InlineData(new byte[] { 0xFF })]
    [InlineData(new byte[] { 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0 })]
    public void Compute_WithVariousInputs_ProducesValidHashes(byte[] data)
    {
        // Act
        var hash = Hash256.Compute(data);

        // Assert
        hash.Should().NotBeNull();
        hash.ToString().Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void FromHex_WithValidHexString_CreatesHash()
    {
        // Arrange
        var hexString = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"; // SHA-256 of empty string

        // Act
        var hash = Hash256.FromHex(hexString);

        // Assert
        hash.Should().NotBeNull();
        hash.ToString().Should().Be(hexString);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("0123456789abcdef")] // Too short
    public void FromHex_WithInvalidHexString_ThrowsArgumentException(string invalidHex)
    {
        // Act
        Action act = () => Hash256.FromHex(invalidHex);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FromHex_WithInvalidHexCharacter_ThrowsFormatException()
    {
        // Arrange
        var invalidHex = "g3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        // Act
        Action act = () => Hash256.FromHex(invalidHex);

        // Assert
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void ToString_ReturnsLowercaseHex()
    {
        // Arrange
        var data = "Test"u8.ToArray();
        var hash = Hash256.Compute(data);

        // Act
        var hexString = hash.ToString();

        // Assert
        hexString.Should().MatchRegex("^[0-9a-f]{64}$");
        hexString.Should().NotContain("A");
        hexString.Should().NotContain("B");
        hexString.Should().NotContain("C");
        hexString.Should().NotContain("D");
        hexString.Should().NotContain("E");
        hexString.Should().NotContain("F");
    }

    [Fact]
    public void Equals_WithSameHash_ReturnsTrue()
    {
        // Arrange
        var data = "Same data"u8.ToArray();
        var hash1 = Hash256.Compute(data);
        var hash2 = Hash256.Compute(data);

        // Act & Assert
        hash1.Equals(hash2).Should().BeTrue();
        (hash1 == hash2).Should().BeTrue();
    }

    [Fact]
    public void Equals_WithDifferentHash_ReturnsFalse()
    {
        // Arrange
        var data1 = "Data one"u8.ToArray();
        var data2 = "Data two"u8.ToArray();
        var hash1 = Hash256.Compute(data1);
        var hash2 = Hash256.Compute(data2);

        // Act & Assert
        hash1.Equals(hash2).Should().BeFalse();
        (hash1 != hash2).Should().BeTrue();
    }

    [Fact]
    public void GetHashCode_WithSameHash_ReturnsSameCode()
    {
        // Arrange
        var data = "Consistent data"u8.ToArray();
        var hash1 = Hash256.Compute(data);
        var hash2 = Hash256.Compute(data);

        // Act
        var hashCode1 = hash1.GetHashCode();
        var hashCode2 = hash2.GetHashCode();

        // Assert
        hashCode1.Should().Be(hashCode2);
    }

    [Fact]
    public void RoundTrip_ComputeAndFromHex_PreservesHash()
    {
        // Arrange
        var data = "Round trip test"u8.ToArray();

        // Act
        var originalHash = Hash256.Compute(data);
        var hexString = originalHash.ToString();
        var restoredHash = Hash256.FromHex(hexString);

        // Assert
        restoredHash.ToString().Should().Be(originalHash.ToString());
        restoredHash.Should().Be(originalHash);
    }

    [Fact]
    public void Compute_WithLargeData_CompletesSuccessfully()
    {
        // Arrange
        var data = new byte[1024 * 1024]; // 1 MB
        Random.Shared.NextBytes(data);

        // Act
        var hash = Hash256.Compute(data);

        // Assert
        hash.Should().NotBeNull();
        hash.ToString().Length.Should().Be(64);
    }
}
