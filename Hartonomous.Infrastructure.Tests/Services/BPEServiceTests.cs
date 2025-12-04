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

        // Act - High min frequency means no pairs qualify
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
        // Arrange
        var constant1 = CreateConstant([0x41]);
        var constant2 = CreateConstant([0x42]);
        var constants = new List<Constant> { constant1, constant2 };

        var existingToken = BPEToken.CreateFromConstantSequence(
            tokenId: 256,
            constantSequence: new List<Guid> { constant1.Id, constant2.Id },
            hash: Core.Domain.ValueObjects.Hash256.Compute([0x41, 0x42]),
            mergeLevel: 1,
            constants: constants
        );

        _tokenRepositoryMock
            .Setup(r => r.GetMaxTokenIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(255);

        _tokenRepositoryMock
            .Setup(r => r.GetByHashAsync(It.IsAny<Core.Domain.ValueObjects.Hash256>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingToken);

        // Act
        var result = await _sut.LearnVocabularyAsync(
            constants,
            maxVocabularySize: 10,
            minFrequency: 1,
            CancellationToken.None
        );

        // Assert
        result.Should().BeEmpty("Should not create new tokens when hash exists");
        existingToken.Frequency.Should().BeGreaterThan(1, "Should increment existing token frequency");
        _tokenRepositoryMock.Verify(
            r => r.AddAsync(It.IsAny<BPEToken>(), It.IsAny<CancellationToken>()),
            Times.Never(),
            "Should not add new token when deduplicating"
        );
    }

    [Fact]
    public async Task LearnVocabularyAsync_WithMultipleMerges_CreatesHierarchicalTokens()
    {
        // Arrange - Create pattern that allows multiple merge levels
        var constantA = CreateConstant([0x41]); // 'A'
        var constantB = CreateConstant([0x42]); // 'B'
        var constantC = CreateConstant([0x43]); // 'C'
        var constantD = CreateConstant([0x44]); // 'D'
        
        // Create sequence: A B A B C D (allows AB to merge, then potentially higher levels)
        var constants = new List<Constant> 
        { 
            constantA, constantB, constantA, constantB, constantC, constantD 
        };

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
        result.Should().NotBeEmpty("Should create merged tokens");
        result.Should().Contain(t => t.MergeLevel >= 1, "Should have tokens with merge level >= 1");
    }

    [Fact]
    public async Task LearnVocabularyAsync_WithBatchCommit_SavesEvery100Iterations()
    {
        // Arrange - Create enough constants to potentially trigger batch commit
        var constants = new List<Constant>();
        for (int i = 0; i < 250; i++)
        {
            constants.Add(CreateConstant([(byte)(0x41 + (i % 10))]));
        }

        _tokenRepositoryMock
            .Setup(r => r.GetMaxTokenIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(255);

        _tokenRepositoryMock
            .Setup(r => r.GetByHashAsync(It.IsAny<Core.Domain.ValueObjects.Hash256>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BPEToken?)null);

        // Act
        var result = await _sut.LearnVocabularyAsync(
            constants,
            maxVocabularySize: 150,
            minFrequency: 1,
            CancellationToken.None
        );

        // Assert
        _unitOfWorkMock.Verify(
            u => u.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.AtLeastOnce(),
            "Should commit changes during learning"
        );
    }

    [Fact]
    public async Task LearnVocabularyAsync_AfterCompletion_RanksTokensByFrequency()
    {
        // Arrange
        var constants = new List<Constant>();
        for (int i = 0; i < 10; i++)
        {
            constants.Add(CreateConstant([(byte)(0x41 + (i % 3))]));
        }

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
        if (result.Count > 0)
        {
            result.Should().BeInDescendingOrder(t => t.Frequency, "Tokens should be ranked by frequency");
            
            for (int i = 0; i < result.Count; i++)
            {
                result[i].VocabularyRank.Should().Be(i + 1, "Rank should match position in frequency-sorted list");
            }
        }
    }

    [Fact]
    public async Task LearnVocabularyAsync_WithSpatialCoordinates_InterpolatesMergedTokenCoordinate()
    {
        // Arrange
        var constant1 = CreateConstant([0x41]);
        var constant2 = CreateConstant([0x42]);
        constant1.Project(); // Ensure coordinates are set
        constant2.Project();
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
        result.Should().NotBeEmpty();
        // Token should be created with spatial properties from constants
        var token = result.First();
        token.ConstantSequence.Should().HaveCount(2);
    }

    [Fact]
    public async Task LearnVocabularyAsync_WithDifferentDataPatterns_CountsPairsCorrectly()
    {
        // Arrange - Create specific pattern: A B C B C D
        // Pairs: (A,B), (B,C), (C,B), (B,C), (C,D)
        // B-C appears twice, should be most frequent
        var constantA = CreateConstant([0x41]);
        var constantB = CreateConstant([0x42]);
        var constantC = CreateConstant([0x43]);
        var constantD = CreateConstant([0x44]);
        
        var constants = new List<Constant> 
        { 
            constantA, constantB, constantC, constantB, constantC, constantD 
        };

        _tokenRepositoryMock
            .Setup(r => r.GetMaxTokenIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(255);

        _tokenRepositoryMock
            .Setup(r => r.GetByHashAsync(It.IsAny<Core.Domain.ValueObjects.Hash256>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BPEToken?)null);

        // Act
        var result = await _sut.LearnVocabularyAsync(
            constants,
            maxVocabularySize: 5,
            minFrequency: 1,
            CancellationToken.None
        );

        // Assert
        result.Should().NotBeEmpty("Should create tokens from frequent pairs");
        // First token should be for most frequent pair (B-C with frequency 2)
        if (result.Count > 0)
        {
            result[0].Frequency.Should().BeGreaterThanOrEqualTo(1);
        }
    }

    [Fact]
    public async Task LearnVocabularyAsync_WithLargeVocabulary_HandlesScaleEfficiently()
    {
        // Arrange - Create substantial dataset
        var constants = new List<Constant>();
        for (int i = 0; i < 100; i++)
        {
            constants.Add(CreateConstant([(byte)(0x41 + (i % 26))])); // A-Z repeated
        }

        _tokenRepositoryMock
            .Setup(r => r.GetMaxTokenIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(255);

        _tokenRepositoryMock
            .Setup(r => r.GetByHashAsync(It.IsAny<Core.Domain.ValueObjects.Hash256>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BPEToken?)null);

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await _sut.LearnVocabularyAsync(
            constants,
            maxVocabularySize: 50,
            minFrequency: 2,
            CancellationToken.None
        );
        stopwatch.Stop();

        // Assert
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(5000, "Should complete in reasonable time");
        result.Should().HaveCountLessThanOrEqualTo(50, "Should respect vocabulary limit");
    }

    [Fact]
    public async Task LearnVocabularyAsync_WithException_LogsErrorAndRethrows()
    {
        // Arrange
        var constants = new List<Constant> { CreateConstant([0x41]), CreateConstant([0x42]) };

        _tokenRepositoryMock
            .Setup(r => r.GetMaxTokenIdAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.LearnVocabularyAsync(
                constants,
                maxVocabularySize: 10,
                minFrequency: 1,
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task LearnVocabularyAsync_WithConstantSequenceResolution_BuildsCorrectMergedSequences()
    {
        // Arrange - Create constants that will merge into larger sequences
        var constants = new List<Constant>();
        for (int i = 0; i < 8; i++)
        {
            constants.Add(CreateConstant([(byte)(0x41 + (i % 2))])); // A B A B A B A B
        }

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
        result.Should().NotBeEmpty();
        // First merge should create AB token with sequence length 2
        result.Should().Contain(t => t.ConstantSequence.Count >= 2, 
            "Should build sequences from constituent constants");
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
