using FluentAssertions;
using Hartonomous.Core.Application.Interfaces;
using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.Enums;
using Hartonomous.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Hartonomous.Infrastructure.Tests.Services;

/// <summary>
/// Tests for BPEService vocabulary learning and tokenization
/// </summary>
public class BPEServiceTests
{
    private readonly Mock<IConstantRepository> _constantRepositoryMock;
    private readonly Mock<IBPETokenRepository> _tokenRepositoryMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<ILogger<BPEService>> _loggerMock;
    private readonly BPEService _sut;

    public BPEServiceTests()
    {
        _constantRepositoryMock = new Mock<IConstantRepository>();
        _tokenRepositoryMock = new Mock<IBPETokenRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _loggerMock = new Mock<ILogger<BPEService>>();
        
        _sut = new BPEService(
            _constantRepositoryMock.Object,
            _tokenRepositoryMock.Object,
            _unitOfWorkMock.Object,
            _loggerMock.Object
        );
    }

    [Fact]
    public async Task LearnVocabularyAsync_WithEmptyInput_ReturnsEmpty()
    {
        // Arrange
        var constants = new List<Constant>();

        // Act
        var result = await _sut.LearnVocabularyAsync(
            constants,
            maxVocabularySize: 10,
            minFrequency: 1,
            CancellationToken.None
        );

        // Assert
        result.Should().BeEmpty("Should return empty list for empty input");
        _tokenRepositoryMock.Verify(
            r => r.AddAsync(It.IsAny<BPEToken>(), It.IsAny<CancellationToken>()),
            Times.Never()
        );
    }

    [Fact]
    public async Task LearnVocabularyAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var constants = new List<Constant> 
        { 
            CreateConstant([0x41]), 
            CreateConstant([0x42]) 
        };

        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _sut.LearnVocabularyAsync(
                constants,
                maxVocabularySize: 10,
                minFrequency: 1,
                cts.Token
            )
        );
    }

    [Fact]
    public async Task LearnVocabularyAsync_WithSingleConstant_ReturnsEmpty()
    {
        // Arrange - Need at least 2 constants to form pairs
        var constants = new List<Constant> { CreateConstant([0x41]) };

        // Act
        var result = await _sut.LearnVocabularyAsync(
            constants,
            maxVocabularySize: 10,
            minFrequency: 1,
            CancellationToken.None
        );

        // Assert
        result.Should().BeEmpty("Cannot create pairs from single constant");
    }

    private static Constant CreateConstant(byte[] data)
    {
        var constant = Constant.Create(data, ContentType.Binary);
        constant.Project(); // Ensure spatial coordinates are set
        return constant;
    }
}
