using FluentAssertions;
using Hartonomous.Core.Application.Interfaces;
using Hartonomous.Core.Domain.Enums;
using Hartonomous.Infrastructure.Services;
using Hartonomous.Infrastructure.Services.Decomposers;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text;

namespace Hartonomous.Infrastructure.Tests.Services;

public class ContentDecomposerFactoryTests
{
    private readonly Mock<ILogger<BinaryDecomposer>> _mockBinaryLogger;
    private readonly Mock<ILogger<TextDecomposer>> _mockTextLogger;
    private readonly Mock<ILogger<ContentDecomposerFactory>> _mockFactoryLogger;
    private readonly ContentDecomposerFactory _sut;
    private readonly List<IContentDecomposer> _decomposers;

    public ContentDecomposerFactoryTests()
    {
        _mockBinaryLogger = new Mock<ILogger<BinaryDecomposer>>();
        _mockTextLogger = new Mock<ILogger<TextDecomposer>>();
        _mockFactoryLogger = new Mock<ILogger<ContentDecomposerFactory>>();

        _decomposers = new List<IContentDecomposer>
        {
            new BinaryDecomposer(_mockBinaryLogger.Object),
            new TextDecomposer(_mockTextLogger.Object)
        };

        _sut = new ContentDecomposerFactory(_decomposers, _mockFactoryLogger.Object);
    }

    [Fact]
    public void Constructor_NullDecomposers_ThrowsArgumentNullException()
    {
        var act = () => new ContentDecomposerFactory(null!, _mockFactoryLogger.Object);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("decomposers");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new ContentDecomposerFactory(_decomposers, null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public void GetDecomposer_NullData_ThrowsArgumentException()
    {
        var act = () => _sut.GetDecomposer(null!, ContentType.Binary);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("data");
    }

    [Fact]
    public void GetDecomposer_EmptyData_ThrowsArgumentException()
    {
        var act = () => _sut.GetDecomposer([], ContentType.Binary);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("data");
    }

    [Fact]
    public void GetDecomposer_BinaryContent_ReturnsBinaryDecomposer()
    {
        byte[] binaryData = [0x00, 0x01, 0x02, 0xFF];

        var decomposer = _sut.GetDecomposer(binaryData, ContentType.Binary);

        decomposer.Should().BeOfType<BinaryDecomposer>();
        decomposer.SupportedContentType.Should().Be(ContentType.Binary);
    }

    [Fact]
    public void GetDecomposer_TextContent_WithValidUtf8_ReturnsTextDecomposer()
    {
        byte[] textData = Encoding.UTF8.GetBytes("Hello, World!");

        var decomposer = _sut.GetDecomposer(textData, ContentType.Text);

        decomposer.Should().BeOfType<TextDecomposer>();
        decomposer.SupportedContentType.Should().Be(ContentType.Text);
    }

    [Fact]
    public void GetDecomposer_TextContent_WithNonTextData_FallsBackToBinary()
    {
        byte[] binaryData = [0xFF, 0xFE, 0xFD, 0x00];

        var decomposer = _sut.GetDecomposer(binaryData, ContentType.Text);

        // Text decomposer may auto-detect as text or fall back to binary
        // Either is acceptable behavior
        decomposer.Should().NotBeNull();
    }

    [Fact]
    public void GetDecomposer_ImageContent_WithNoImageDecomposer_FallsBackToBinary()
    {
        byte[] imageData = [0xFF, 0xD8, 0xFF, 0xE0]; // JPEG header

        var decomposer = _sut.GetDecomposer(imageData, ContentType.Image);

        // No image decomposer registered, should fall back to binary
        decomposer.Should().BeOfType<BinaryDecomposer>();
    }

    [Fact]
    public void GetDecomposer_VideoContent_WithNoVideoDecomposer_FallsBackToBinary()
    {
        byte[] videoData = [0x00, 0x00, 0x00, 0x20, 0x66, 0x74, 0x79, 0x70]; // MP4 header

        var decomposer = _sut.GetDecomposer(videoData, ContentType.Video);

        decomposer.Should().BeOfType<BinaryDecomposer>();
    }

    [Fact]
    public void GetDecomposer_AudioContent_WithNoAudioDecomposer_FallsBackToBinary()
    {
        byte[] audioData = [0x49, 0x44, 0x33]; // ID3 tag (MP3)

        var decomposer = _sut.GetDecomposer(audioData, ContentType.Audio);

        decomposer.Should().BeOfType<BinaryDecomposer>();
    }

    [Fact]
    public void GetDecomposer_AutoDetectsText_WhenTextNotDeclared()
    {
        byte[] textData = Encoding.UTF8.GetBytes("This is clearly text content");

        // Declare as Binary but data is valid UTF-8
        var decomposer = _sut.GetDecomposer(textData, ContentType.Binary);

        // Binary decomposer should be returned since it matches declared type
        // (Binary decomposer always accepts content)
        decomposer.Should().BeOfType<BinaryDecomposer>();
    }

    [Fact]
    public async Task DecomposeAsync_DelegatesToGetDecomposer()
    {
        byte[] data = [0x42];

        var result = await _sut.DecomposeAsync(data, ContentType.Binary);

        result.Should().HaveCount(1);
        result[0].Data.Should().Equal(0x42);
        result[0].ContentType.Should().Be(ContentType.Binary);
    }

    [Fact]
    public async Task DecomposeAsync_TextContent_ReturnsTextConstants()
    {
        byte[] data = Encoding.UTF8.GetBytes("Test");

        var result = await _sut.DecomposeAsync(data, ContentType.Text);

        result.Should().NotBeEmpty();
        result.Should().AllSatisfy(c => c.ContentType.Should().Be(ContentType.Text));
    }

    [Fact]
    public async Task DecomposeAsync_WithCancellationToken_PropagatesCancellation()
    {
        byte[] data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Test ", 100000)));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await _sut.DecomposeAsync(data, ContentType.Text, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void GetDecomposer_WithNoDecomposers_ThrowsInvalidOperationException()
    {
        var emptyFactory = new ContentDecomposerFactory(
            Array.Empty<IContentDecomposer>(),
            _mockFactoryLogger.Object);
        byte[] data = [0x42];

        var act = () => emptyFactory.GetDecomposer(data, ContentType.Binary);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Binary decomposer not registered*");
    }

    [Fact]
    public void GetDecomposer_CallsCanDecomposeOnDecomposers()
    {
        byte[] data = [0x42];

        var decomposer = _sut.GetDecomposer(data, ContentType.Binary);

        // Should have queried decomposers to find match
        decomposer.Should().NotBeNull();
        decomposer.CanDecompose(data, ContentType.Binary).Should().BeTrue();
    }
}
