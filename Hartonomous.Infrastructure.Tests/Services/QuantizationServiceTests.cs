using Hartonomous.Infrastructure.Services;
using Xunit;

namespace Hartonomous.Infrastructure.Tests.Services;

public class QuantizationServiceTests
{
    private readonly QuantizationService _service;
    private const int MaxQuantizedValue = 2_097_151; // 2^21 - 1

    public QuantizationServiceTests()
    {
        _service = new QuantizationService();
    }

    [Fact]
    public void CalculateShannonEntropy_ZeroEntropyData_ReturnsZero()
    {
        // Arrange: All same byte
        var data = new byte[1000];
        Array.Fill(data, (byte)0x42);

        // Act
        var result = _service.CalculateShannonEntropy(data);

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public void CalculateShannonEntropy_MaximumEntropyData_ReturnsMaxValue()
    {
        // Arrange: Uniform distribution (all bytes appear equally)
        var data = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            data[i] = (byte)i;
        }

        // Act
        var result = _service.CalculateShannonEntropy(data);

        // Assert: Should be close to max (8 bits entropy)
        Assert.True(result > MaxQuantizedValue * 0.99, $"Expected near max value, got {result}");
    }

    [Fact]
    public void CalculateShannonEntropy_EmptyData_ReturnsZero()
    {
        // Arrange
        var data = Array.Empty<byte>();

        // Act
        var result = _service.CalculateShannonEntropy(data);

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public void CalculateShannonEntropy_ReturnsValueInValidRange()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5, 1, 2, 3, 4, 5 };

        // Act
        var result = _service.CalculateShannonEntropy(data);

        // Assert
        Assert.InRange(result, 0, MaxQuantizedValue);
    }

    [Fact]
    public void CalculateKolmogorovComplexity_HighlyCompressibleData_ReturnsLowValue()
    {
        // Arrange: Repeated pattern
        var data = new byte[1000];
        Array.Fill(data, (byte)0x42);

        // Act
        var result = _service.CalculateKolmogorovComplexity(data);

        // Assert: Should compress well (low complexity)
        Assert.True(result < MaxQuantizedValue * 0.5, $"Expected low complexity, got {result}");
    }

    [Fact]
    public void CalculateKolmogorovComplexity_IncompressibleData_ReturnsHighValue()
    {
        // Arrange: Random-ish data (hard to compress)
        var data = new byte[1000];
        var random = new Random(42);
        random.NextBytes(data);

        // Act
        var result = _service.CalculateKolmogorovComplexity(data);

        // Assert: Should not compress well (high complexity)
        Assert.True(result > MaxQuantizedValue * 0.5, $"Expected high complexity, got {result}");
    }

    [Fact]
    public void CalculateKolmogorovComplexity_EmptyData_ReturnsZero()
    {
        // Arrange
        var data = Array.Empty<byte>();

        // Act
        var result = _service.CalculateKolmogorovComplexity(data);

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public void CalculateKolmogorovComplexity_ReturnsValueInValidRange()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

        // Act
        var result = _service.CalculateKolmogorovComplexity(data);

        // Assert
        Assert.InRange(result, 0, MaxQuantizedValue);
    }

    [Fact]
    public void CalculateGraphConnectivity_FromReferenceCount_ReturnsLogarithmicValue()
    {
        // Arrange
        long lowRefs = 10;
        long medRefs = 1000;
        long highRefs = 1_000_000;

        // Act
        var lowVal = _service.CalculateGraphConnectivity(lowRefs);
        var medVal = _service.CalculateGraphConnectivity(medRefs);
        var highVal = _service.CalculateGraphConnectivity(highRefs);

        // Assert: Should be increasing
        Assert.True(lowVal < medVal);
        Assert.True(medVal < highVal);
        Assert.InRange(highVal, 0, MaxQuantizedValue);
    }

    [Fact]
    public void CalculateGraphConnectivity_EmptyData_ReturnsZero()
    {
        // Arrange
        var data = Array.Empty<byte>();

        // Act
        var result = _service.CalculateGraphConnectivity(data);

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public void CalculateGraphConnectivity_SingleByte_ReturnsZero()
    {
        // Arrange
        var data = new byte[] { 0x42 };

        // Act
        var result = _service.CalculateGraphConnectivity(data);

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public void CalculateGraphConnectivity_ReturnsValueInValidRange()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 2, 1, 2, 3, 4, 5 };

        // Act
        var result = _service.CalculateGraphConnectivity(data);

        // Assert
        Assert.InRange(result, 0, MaxQuantizedValue);
    }

    [Fact]
    public void Quantize_ReturnsAllThreeDimensions()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

        // Act
        var (y, z, m) = _service.Quantize(data);

        // Assert
        Assert.InRange(y, 0, MaxQuantizedValue);
        Assert.InRange(z, 0, MaxQuantizedValue);
        Assert.InRange(m, 0, MaxQuantizedValue);
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 42 })]
    [InlineData(new byte[] { 1, 2, 3 })]
    public void Quantize_DifferentDataSizes_ReturnsValidValues(byte[] data)
    {
        // Act
        var (y, z, m) = _service.Quantize(data);

        // Assert
        Assert.InRange(y, 0, MaxQuantizedValue);
        Assert.InRange(z, 0, MaxQuantizedValue);
        Assert.InRange(m, 0, MaxQuantizedValue);
    }
}
