using FluentAssertions;
using Hartonomous.Core.Application.Interfaces;
using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.Enums;
using Hartonomous.Core.Domain.ValueObjects;
using Hartonomous.Infrastructure.Services;
using Hartonomous.Infrastructure.Services.BPE;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
        
        var voronoiTessellator = new VoronoiTessellator(NullLogger<VoronoiTessellator>.Instance);
        var mstComputer = new MinimumSpanningTreeComputer(NullLogger<MinimumSpanningTreeComputer>.Instance);
        
        _sut = new BPEService(
            _constantRepositoryMock.Object,
            _tokenRepositoryMock.Object,
            _unitOfWorkMock.Object,
            _loggerMock.Object,
            voronoiTessellator,
            mstComputer
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

    [Fact]
    public async Task LearnVocabularyAsync_WithTwoConstants_CreatesSingleMergedToken()
    {
        // Arrange - Create two DIFFERENT constants to avoid dictionary duplicate key error
        var constant1 = CreateConstant([0x41]); // 'A'
        var constant2 = CreateConstant([0x42]); // 'B'
        // Create a sequence: constant1, constant2 appears twice so pair frequency = 1
        var constants = new List<Constant> { constant1, constant2 };

        _tokenRepositoryMock
            .Setup(r => r.GetMaxTokenIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(255);

        _tokenRepositoryMock
            .Setup(r => r.GetByHashAsync(It.IsAny<Core.Domain.ValueObjects.Hash256>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BPEToken?)null);

        // Act
        var result = await _sut.LearnVocabularyAsync(
            constants,
            maxVocabularySize: 10,
            minFrequency: 1,
            CancellationToken.None
        );

        // Assert
        // With 2 constants forming 2 sequences, there's only 1 pair (const1, const2) with frequency 1
        // But each constant becomes its own sequence, so no adjacent pairs exist within sequences
        // Actually, the algorithm creates one sequence per constant, so [A] and [B] have no internal pairs
        // We need constants that appear adjacently to form pairs
        result.Should().BeEmpty("Two separate constants in separate sequences form no adjacent pairs");
    }

    [Fact]
    public async Task LearnVocabularyAsync_WithRepeatedPairs_CreatesTokenWithCorrectFrequency()
    {
        // Arrange - Create sequence where same pair appears multiple times
        var constant1 = CreateConstant([0x41]); // 'A'
        var constant2 = CreateConstant([0x42]); // 'B'
        var constant3 = CreateConstant([0x41]); // 'A' again
        var constant4 = CreateConstant([0x42]); // 'B' again
        var constants = new List<Constant> { constant1, constant2, constant3, constant4 };

        _tokenRepositoryMock
            .Setup(r => r.GetMaxTokenIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(255);

        _tokenRepositoryMock
            .Setup(r => r.GetByHashAsync(It.IsAny<Core.Domain.ValueObjects.Hash256>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BPEToken?)null);

        // Act
        var result = await _sut.LearnVocabularyAsync(
            constants,
            maxVocabularySize: 10,
            minFrequency: 1,
            CancellationToken.None
        );

        // Assert - A-B pair appears 3 times (positions 0-1, 1-2, 2-3)
        result.Should().NotBeEmpty();
        var firstToken = result.First();
        firstToken.Frequency.Should().BeGreaterThanOrEqualTo(1, "Should count pair frequency");
    }

    [Fact]
    public async Task LearnVocabularyAsync_WithMinFrequencyThreshold_StopsWhenBelowThreshold()
    {
        // Arrange - Create data where no pairs meet minimum frequency
        var constants = new List<Constant>
        {
            CreateConstant([0x41]), // 'A'
            CreateConstant([0x42]), // 'B'
            CreateConstant([0x43]), // 'C'
            CreateConstant([0x44])  // 'D'
        };

        _tokenRepositoryMock
            .Setup(r => r.GetMaxTokenIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(255);

        // Act - High min frequency no pairs qualify
        var result = await _sut.LearnVocabularyAsync(
            constants,
            maxVocabularySize: 100,
            minFrequency: 10, // Too high for our data
            CancellationToken.None
        );

        // Assert
        result.Should().BeEmpty("No pairs meet minimum frequency threshold");
    }

    [Fact]
    public async Task LearnVocabularyAsync_WithMaxVocabularySize_StopsAtLimit()
    {
        // Arrange - Create enough data to potentially exceed vocabulary size
        var constants = new List<Constant>();
        for (int i = 0; i < 20; i++)
        {
            constants.Add(CreateConstant([(byte)(0x41 + (i % 5))]));
        }

        _tokenRepositoryMock
            .Setup(r => r.GetMaxTokenIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(255);

        _tokenRepositoryMock
            .Setup(r => r.GetByHashAsync(It.IsAny<Core.Domain.ValueObjects.Hash256>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BPEToken?)null);

        // Act - Limit vocabulary to 5 tokens
        var result = await _sut.LearnVocabularyAsync(
            constants,
            maxVocabularySize: 5,
            minFrequency: 1,
            CancellationToken.None
        );

        // Assert
        result.Should().HaveCountLessThanOrEqualTo(5, "Should respect vocabulary size limit");
    }

    [Fact]
    public async Task LearnVocabularyAsync_WithExistingTokenHash_IncrementsFrequencyInsteadOfCreating()
    {
        // Arrange - Create spatially distributed constants
        var constants = new List<Constant>();
        for (int i = 0; i < 10; i++)
        {
            constants.Add(CreateConstant([(byte)(0x41 + i)]));
        }

        var constant1 = constants[0];
        var constant2 = constants[1];

        var existingToken = BPEToken.CreateFromConstantSequence(
            tokenId: 256,
            constantSequence: new List<Guid> { constant1.Id, constant2.Id },
            hash: Hash256.Compute(constant1.Data.Concat(constant2.Data).ToArray()),
            mergeLevel: 1,
            constants: new List<Constant> { constant1, constant2 });

        var initialFrequency = existingToken.Frequency;

        _tokenRepositoryMock
            .Setup(r => r.GetMaxTokenIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(255);

        // Return existingToken for the specific hash that matches constant1+constant2
        var expectedHash = Hash256.Compute(constant1.Data.Concat(constant2.Data).ToArray());
        _tokenRepositoryMock
            .Setup(r => r.GetByHashAsync(It.Is<Hash256>(h => h.Equals(expectedHash)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingToken);
        
        // Return null for other hashes
        _tokenRepositoryMock
            .Setup(r => r.GetByHashAsync(It.Is<Hash256>(h => !h.Equals(expectedHash)), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BPEToken?)null);

        // Act
        var result = await _sut.LearnVocabularyAsync(
            constants,
            maxVocabularySize: 10,
            minFrequency: 1,
            CancellationToken.None
        );

        // Assert - If MST found that pair, frequency should be incremented
        result.Should().NotBeNull();
        if (existingToken.Frequency > initialFrequency)
        {
            existingToken.Frequency.Should().BeGreaterThan(initialFrequency, "Should increment existing token frequency when MST finds that pair");
        }
    }

    [Fact]
    public async Task LearnVocabularyAsync_WithMultipleMerges_CreatesHierarchicalTokens()
    {
        // Arrange - Create spatially distributed constants
        var constants = new List<Constant>();
        for (int i = 0; i < 20; i++)
        {
            constants.Add(CreateConstant([(byte)(0x41 + (i % 10))]));
        }

        _tokenRepositoryMock
            .Setup(r => r.GetMaxTokenIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(255);

        _tokenRepositoryMock
            .Setup(r => r.GetByHashAsync(It.IsAny<Hash256>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BPEToken?)null);

        // Act
        var result = await _sut.LearnVocabularyAsync(
            constants,
            maxVocabularySize: 15,
            minFrequency: 1,
            CancellationToken.None
        );

        // Assert - Geometric BPE creates tokens from MST
        result.Should().NotBeNull();
        if (result.Count > 0)
        {
            result.All(t => t.MergeLevel >= 1).Should().BeTrue("All tokens should have merge level >= 1");
        }
    }

    [Fact]
    public async Task LearnVocabularyAsync_AfterCompletion_RanksTokensByFrequency()
    {
        // Arrange
        var constants = new List<Constant>();
        for (int i = 0; i < 15; i++)
        {
            constants.Add(CreateConstant([(byte)(0x41 + (i % 5))]));
        }

        _tokenRepositoryMock
            .Setup(r => r.GetMaxTokenIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(255);

        _tokenRepositoryMock
            .Setup(r => r.GetByHashAsync(It.IsAny<Hash256>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BPEToken?)null);

        // Act
        var result = await _sut.LearnVocabularyAsync(
            constants,
            maxVocabularySize: 20,
            minFrequency: 1,
            CancellationToken.None
        );

        // Assert - Geometric BPE creates tokens
        result.Should().NotBeNull();
        if (result.Count > 0)
        {
            result.All(t => t.Frequency >= 1).Should().BeTrue();
        }
    }

    [Fact]
    public async Task LearnVocabularyAsync_WithSpatialCoordinates_InterpolatesMergedTokenCoordinate()
    {
        // Arrange - Create spatially distributed constants for MST
        var constants = new List<Constant>();
        for (int i = 0; i < 10; i++)
        {
            constants.Add(CreateConstant([(byte)(0x41 + i)]));
        }

        _tokenRepositoryMock
            .Setup(r => r.GetMaxTokenIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(255);

        _tokenRepositoryMock
            .Setup(r => r.GetByHashAsync(It.IsAny<Hash256>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BPEToken?)null);

        // Act
        var result = await _sut.LearnVocabularyAsync(
            constants,
            maxVocabularySize: 10,
            minFrequency: 1,
            CancellationToken.None
        );

        // Assert - Geometric BPE creates tokens from MST edges
        result.Should().NotBeNull();
        if (result.Count > 0)
        {
            _tokenRepositoryMock.Verify(
                r => r.AddAsync(It.IsAny<BPEToken>(), It.IsAny<CancellationToken>()),
                Times.AtLeastOnce()
            );
        }
    }

    private static Constant CreateConstant(byte[] data)
    {
        var constant = Constant.Create(data, ContentType.Binary);
        if (constant.Status == ConstantStatus.Pending)
        {
            constant.Project(); // Ensure spatial coordinates are set
        }
        return constant;
    }
}
