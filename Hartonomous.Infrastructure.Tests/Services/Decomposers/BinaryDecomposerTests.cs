using FluentAssertions;
using Hartonomous.Core.Domain.Enums;
using Hartonomous.Infrastructure.Services.Decomposers;
using Microsoft.Extensions.Logging;
using Moq;

namespace Hartonomous.Infrastructure.Tests.Services.Decomposers;

public class BinaryDecomposerTests
{
    private readonly BinaryDecomposer _sut;
    private readonly Mock<ILogger<BinaryDecomposer>> _mockLogger;

    public BinaryDecomposerTests()
    {
        _mockLogger = new Mock<ILogger<BinaryDecomposer>>();
        _sut = new BinaryDecomposer(_mockLogger.Object);
    }

    [Fact]
    public void CanDecompose_AlwaysReturnsTrue()
    {
        // Binary decomposer is fallback that handles any content
        _sut.CanDecompose([], ContentType.Binary).Should().BeTrue();
        _sut.CanDecompose([0x01], ContentType.Text).Should().BeTrue();
        _sut.CanDecompose([0xFF], ContentType.Image).Should().BeTrue();
    }

    [Fact]
    public async Task DecomposeAsync_EmptyData_ThrowsArgumentException()
    {
        var act = async () => await _sut.DecomposeAsync([]);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task DecomposeAsync_NullData_ThrowsArgumentException()
    {
        var act = async () => await _sut.DecomposeAsync(null!);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task DecomposeAsync_SingleByte_ReturnsOneConstant()
    {
        byte[] data = [0x42];

        var result = await _sut.DecomposeAsync(data);

        result.Should().HaveCount(1);
        result[0].Data.Should().Equal(0x42);
        result[0].ContentType.Should().Be(ContentType.Binary);
    }

    [Fact]
    public async Task DecomposeAsync_MultipleBytes_ReturnsOneConstantPerByte()
    {
        byte[] data = [0x01, 0x02, 0x03, 0x04, 0x05];

        var result = await _sut.DecomposeAsync(data);

        result.Should().HaveCount(5);
        result[0].Data.Should().Equal(0x01);
        result[1].Data.Should().Equal(0x02);
        result[2].Data.Should().Equal(0x03);
        result[3].Data.Should().Equal(0x04);
        result[4].Data.Should().Equal(0x05);
    }

    [Fact]
    public async Task DecomposeAsync_AllByteValues_HandlesFullRange()
    {
        // Test full byte range 0-255
        byte[] data = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();

        var result = await _sut.DecomposeAsync(data);

        result.Should().HaveCount(256);
        for (int i = 0; i < 256; i++)
        {
                result[i].Data.Should().Equal(new byte[] { (byte)i }, $"Byte {i} should decompose correctly");
        }
    }

    [Fact]
    public async Task DecomposeAsync_CreatesConstantsWithHashAndCoordinate()
    {
        byte[] data = [0xAB];

        var result = await _sut.DecomposeAsync(data);

        result.Should().HaveCount(1);
        var constant = result[0];
        constant.Data.Should().Equal(0xAB);
        constant.ContentType.Should().Be(ContentType.Binary);
        constant.Hash.Should().NotBeNull("Hash should be computed");
        constant.Coordinate.Should().NotBeNull("Spatial coordinate should be computed");
    }

    [Fact]
    public async Task DecomposeAsync_WithCancellationToken_PropagatesCancellation()
    {
        byte[] data = Enumerable.Range(0, 10000).Select(i => (byte)i).ToArray();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await _sut.DecomposeAsync(data, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
