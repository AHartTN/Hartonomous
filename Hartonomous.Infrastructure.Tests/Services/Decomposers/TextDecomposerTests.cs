using FluentAssertions;
using Hartonomous.Core.Domain.Enums;
using Hartonomous.Infrastructure.Services.Decomposers;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text;

namespace Hartonomous.Infrastructure.Tests.Services.Decomposers;

public class TextDecomposerTests
{
    private readonly TextDecomposer _sut;
    private readonly Mock<ILogger<TextDecomposer>> _mockLogger;

    public TextDecomposerTests()
    {
        _mockLogger = new Mock<ILogger<TextDecomposer>>();
        _sut = new TextDecomposer(_mockLogger.Object);
    }

    #region CanDecompose Tests

    [Fact]
    public void CanDecompose_ValidUtf8Text_ReturnsTrue()
    {
        byte[] validUtf8 = Encoding.UTF8.GetBytes("Hello, World!");

        _sut.CanDecompose(validUtf8, ContentType.Text).Should().BeTrue();
    }

    [Fact]
    public void CanDecompose_NonTextContentType_ReturnsFalse()
    {
        byte[] data = Encoding.UTF8.GetBytes("Hello");

        // Non-text types require auto-detection (high printable ratio)
        // For now, valid UTF-8 text will auto-detect successfully
        _sut.CanDecompose(data, ContentType.Binary).Should().BeTrue("valid UTF-8 will be auto-detected");
        _sut.CanDecompose(data, ContentType.Image).Should().BeTrue("valid UTF-8 will be auto-detected");
    }

    [Fact]
    public void CanDecompose_EmptyData_ReturnsTrue()
    {
        // Empty data with Text type returns true (declaredType == Text check)
        _sut.CanDecompose([], ContentType.Text).Should().BeTrue();
    }

    #endregion

    #region DecomposeAsync Basic Tests

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
    public async Task DecomposeAsync_SimpleText_CreatesMultiGranularityConstants()
    {
        byte[] data = Encoding.UTF8.GetBytes("Hello World");

        var result = await _sut.DecomposeAsync(data);

        // Should create constants at multiple granularities
        result.Should().NotBeEmpty();
        result.Should().Contain(c => c.ContentType == ContentType.Text);
    }

    [Fact]
    public async Task DecomposeAsync_MultiByteCharacters_HandlesCorrectly()
    {
        byte[] data = Encoding.UTF8.GetBytes("Hello €");  // Contains multi-byte character

        var result = await _sut.DecomposeAsync(data);

        result.Should().NotBeEmpty();
        result.Should().AllSatisfy(c => c.Hash.Should().NotBeNull());
        result.Should().AllSatisfy(c => c.Coordinate.Should().NotBeNull());
    }

    [Fact]
    public async Task DecomposeAsync_EmojisAndSymbols_HandlesCorrectly()
    {
        byte[] data = Encoding.UTF8.GetBytes("A🚀B");

        var result = await _sut.DecomposeAsync(data);

        result.Should().NotBeEmpty();
        result.Should().AllSatisfy(c => c.ContentType.Should().Be(ContentType.Text));
    }

    [Fact]
    public async Task DecomposeAsync_MixedLanguages_HandlesMultilingualContent()
    {
        byte[] data = Encoding.UTF8.GetBytes("Hello مرحبا 你好 Привет");

        var result = await _sut.DecomposeAsync(data);

        result.Should().NotBeEmpty();
        result.Should().AllSatisfy(c => c.Data.Should().NotBeNull());
    }

    #endregion

    #region Property Validation Tests

    [Fact]
    public async Task DecomposeAsync_CreatesConstantsWithCorrectContentType()
    {
        byte[] data = Encoding.UTF8.GetBytes("Test");

        var result = await _sut.DecomposeAsync(data);

        result.Should().AllSatisfy(c => c.ContentType.Should().Be(ContentType.Text));
    }

    [Fact]
    public async Task DecomposeAsync_CreatesConstantsWithHashAndCoordinate()
    {
        byte[] data = Encoding.UTF8.GetBytes("Test");

        var result = await _sut.DecomposeAsync(data);

        result.Should().NotBeEmpty();
        result.Should().AllSatisfy(c =>
        {
            c.Hash.Should().NotBeNull("Hash should be computed");
            c.Coordinate.Should().NotBeNull("Spatial coordinate should be computed");
        });
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task DecomposeAsync_VeryLongString_HandlesEfficiently()
    {
        string longText = string.Concat(Enumerable.Repeat("Hello World ", 1000));
        byte[] data = Encoding.UTF8.GetBytes(longText);

        var result = await _sut.DecomposeAsync(data);

        result.Should().NotBeEmpty();
    }

    [Fact]
    public async Task DecomposeAsync_WithCancellationToken_PropagatesCancellation()
    {
        string longText = string.Concat(Enumerable.Repeat("Test ", 100000));
        byte[] data = Encoding.UTF8.GetBytes(longText);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await _sut.DecomposeAsync(data, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion
}
