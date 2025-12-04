using FluentAssertions;
using Hartonomous.Core.Application.Commands.ContentIngestion;
using Hartonomous.Core.Application.Interfaces;
using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.Enums;
using Hartonomous.Core.Domain.ValueObjects;
using Moq;

namespace Hartonomous.Core.Tests.Application.Commands.ContentIngestion;

public class IngestContentCommandHandlerTests
{
    private readonly Mock<IConstantRepository> _constantRepositoryMock;
    private readonly Mock<IContentIngestionRepository> _ingestionRepositoryMock;
    private readonly Mock<IContentDecomposerFactory> _decomposerFactoryMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly IngestContentCommandHandler _sut;

    public IngestContentCommandHandlerTests()
    {
        _constantRepositoryMock = new Mock<IConstantRepository>();
        _ingestionRepositoryMock = new Mock<IContentIngestionRepository>();
        _decomposerFactoryMock = new Mock<IContentDecomposerFactory>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();

        _sut = new IngestContentCommandHandler(
            _constantRepositoryMock.Object,
            _ingestionRepositoryMock.Object,
            _decomposerFactoryMock.Object,
            _unitOfWorkMock.Object);
    }

    [Fact]
    public async Task Handle_WithNewContent_CreatesConstantsAndIngestion()
    {
        // Arrange
        byte[] content = [0x48, 0x65, 0x6C, 0x6C, 0x6F]; // "Hello"
        var request = new IngestContentCommand
        {
            ContentData = content,
            ContentType = ContentType.Text,
            SourceUri = "test://source"
        };

        var decomposedConstants = new List<Constant>
        {
            Constant.Create([0x48], ContentType.Binary),
            Constant.Create([0x65], ContentType.Binary),
            Constant.Create([0x6C], ContentType.Binary),
            Constant.Create([0x6C], ContentType.Binary),
            Constant.Create([0x6F], ContentType.Binary)
        };

        _ingestionRepositoryMock.Setup(r => r.GetByContentHashAsync(It.IsAny<Hash256>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Hartonomous.Core.Domain.Entities.ContentIngestion?)null);

        _decomposerFactoryMock.Setup(f => f.DecomposeAsync(content, ContentType.Text, It.IsAny<CancellationToken>()))
            .ReturnsAsync(decomposedConstants);

        _constantRepositoryMock.Setup(r => r.GetByHashAsync(It.IsAny<Hash256>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Constant?)null);

        _constantRepositoryMock.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<Constant>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _ingestionRepositoryMock.Setup(r => r.AddAsync(It.IsAny<Hartonomous.Core.Domain.Entities.ContentIngestion>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Hartonomous.Core.Domain.Entities.ContentIngestion entity, CancellationToken _) => entity);

        _unitOfWorkMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        // Act
        var result = await _sut.Handle(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.TotalConstantsCreated.Should().Be(5);
        result.Value.UniqueConstantsCreated.Should().Be(4); // "l" appears twice, so 4 unique
        
        _constantRepositoryMock.Verify(r => r.AddRangeAsync(
            It.Is<IEnumerable<Constant>>(c => c.Count() == 4), 
            It.IsAny<CancellationToken>()), 
            Times.Once);
        
        _ingestionRepositoryMock.Verify(r => r.AddAsync(
            It.Is<Hartonomous.Core.Domain.Entities.ContentIngestion>(i => i.IsSuccessful), 
            It.IsAny<CancellationToken>()), 
            Times.Once);
        
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithDuplicateContent_ReturnsCachedResult()
    {
        // Arrange
        byte[] content = [0x48, 0x65, 0x6C, 0x6C, 0x6F];
        var request = new IngestContentCommand
        {
            ContentData = content,
            ContentType = ContentType.Text,
            SourceUri = "test://source"
        };
        var contentHash = Hash256.Compute(content);

        var existingIngestion = Hartonomous.Core.Domain.Entities.ContentIngestion.Create(content, ContentType.Text, "test://source", null);
        existingIngestion.RecordConstants([Guid.NewGuid()], 1);
        existingIngestion.Complete(100);

        _ingestionRepositoryMock.Setup(r => r.GetByContentHashAsync(contentHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingIngestion);

        // Act
        var result = await _sut.Handle(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.IngestionId.Should().Be(existingIngestion.Id);
        result.Value.TotalConstantsCreated.Should().Be(existingIngestion.ConstantCount);
        
        _decomposerFactoryMock.Verify(f => f.DecomposeAsync(It.IsAny<byte[]>(), It.IsAny<ContentType>(), It.IsAny<CancellationToken>()), Times.Never);
        _constantRepositoryMock.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<Constant>>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithExistingConstants_DoesNotCreateDuplicates()
    {
        // Arrange
        byte[] content = [0x41, 0x42]; // Two bytes
        var request = new IngestContentCommand
        {
            ContentData = content,
            ContentType = ContentType.Binary,
            SourceUri = "test://source"
        };

        var constantA = Constant.Create([0x41], ContentType.Binary);
        var constantB = Constant.Create([0x42], ContentType.Binary);

        var decomposedConstants = new List<Constant> { constantA, constantB };

        _ingestionRepositoryMock.Setup(r => r.GetByContentHashAsync(It.IsAny<Hash256>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Hartonomous.Core.Domain.Entities.ContentIngestion?)null);

        _decomposerFactoryMock.Setup(f => f.DecomposeAsync(content, ContentType.Binary, It.IsAny<CancellationToken>()))
            .ReturnsAsync(decomposedConstants);

        var existingConstantA = Constant.Create([0x41], ContentType.Binary);
        
        _constantRepositoryMock.Setup(r => r.GetByHashAsync(constantA.Hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingConstantA);
        
        _constantRepositoryMock.Setup(r => r.GetByHashAsync(constantB.Hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Constant?)null);

        _constantRepositoryMock.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<Constant>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _ingestionRepositoryMock.Setup(r => r.AddAsync(It.IsAny<Hartonomous.Core.Domain.Entities.ContentIngestion>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Hartonomous.Core.Domain.Entities.ContentIngestion entity, CancellationToken _) => entity);

        _unitOfWorkMock.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _sut.Handle(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalConstantsCreated.Should().Be(2);
        result.Value.UniqueConstantsCreated.Should().Be(1); // Only constantB is new
        
        _constantRepositoryMock.Verify(r => r.AddRangeAsync(
            It.Is<IEnumerable<Constant>>(c => c.Count() == 1), // Only constantB is new
            It.IsAny<CancellationToken>()), 
            Times.Once);
    }

    [Fact]
    public async Task Handle_CalculatesDeduplicationRatio()
    {
        // Arrange
        byte[] content = [0x41, 0x41, 0x41, 0x41]; // Four identical bytes
        var request = new IngestContentCommand
        {
            ContentData = content,
            ContentType = ContentType.Binary,
            SourceUri = "test://source"
        };

        var constantA = Constant.Create([0x41], ContentType.Binary);
        var decomposedConstants = new List<Constant> 
        { 
            constantA, 
            Constant.Create([0x41], ContentType.Binary),
            Constant.Create([0x41], ContentType.Binary),
            Constant.Create([0x41], ContentType.Binary)
        };

        _ingestionRepositoryMock.Setup(r => r.GetByContentHashAsync(It.IsAny<Hash256>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Hartonomous.Core.Domain.Entities.ContentIngestion?)null);

        _decomposerFactoryMock.Setup(f => f.DecomposeAsync(content, ContentType.Binary, It.IsAny<CancellationToken>()))
            .ReturnsAsync(decomposedConstants);

        _constantRepositoryMock.Setup(r => r.GetByHashAsync(It.IsAny<Hash256>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Constant?)null);

        _constantRepositoryMock.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<Constant>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _ingestionRepositoryMock.Setup(r => r.AddAsync(It.IsAny<Hartonomous.Core.Domain.Entities.ContentIngestion>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Hartonomous.Core.Domain.Entities.ContentIngestion entity, CancellationToken _) => entity);

        _unitOfWorkMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _sut.Handle(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalConstantsCreated.Should().Be(4);
        result.Value.UniqueConstantsCreated.Should().Be(1);
        result.Value.DeduplicationRatio.Should().Be(0.25); // 1 unique / 4 total = 0.25
    }

    [Fact]
    public async Task Handle_WithDecompositionError_ReturnsFailure()
    {
        // Arrange
        byte[] content = [0x48, 0x65, 0x6C, 0x6C, 0x6F];
        var request = new IngestContentCommand
        {
            ContentData = content,
            ContentType = ContentType.Text,
            SourceUri = "test://source"
        };

        _ingestionRepositoryMock.Setup(r => r.GetByContentHashAsync(It.IsAny<Hash256>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Hartonomous.Core.Domain.Entities.ContentIngestion?)null);

        _decomposerFactoryMock.Setup(f => f.DecomposeAsync(content, ContentType.Text, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Decomposition failed"));

        // Act
        var result = await _sut.Handle(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Failed to ingest content");
        result.Error.Should().Contain("Decomposition failed");
    }

    [Fact]
    public async Task Handle_ReturnsCorrectStatistics()
    {
        // Arrange
        byte[] content = [0x41];
        var request = new IngestContentCommand
        {
            ContentData = content,
            ContentType = ContentType.Binary,
            SourceUri = "test://source"
        };

        var decomposedConstants = new List<Constant> { Constant.Create([0x41], ContentType.Binary) };

        _ingestionRepositoryMock.Setup(r => r.GetByContentHashAsync(It.IsAny<Hash256>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Hartonomous.Core.Domain.Entities.ContentIngestion?)null);

        _decomposerFactoryMock.Setup(f => f.DecomposeAsync(content, ContentType.Binary, It.IsAny<CancellationToken>()))
            .ReturnsAsync(decomposedConstants);

        _constantRepositoryMock.Setup(r => r.GetByHashAsync(It.IsAny<Hash256>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Constant?)null);

        _constantRepositoryMock.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<Constant>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _ingestionRepositoryMock.Setup(r => r.AddAsync(It.IsAny<Hartonomous.Core.Domain.Entities.ContentIngestion>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Hartonomous.Core.Domain.Entities.ContentIngestion entity, CancellationToken _) => entity);

        _unitOfWorkMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _sut.Handle(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.ProcessingTimeMs.Should().BeGreaterOrEqualTo(0);
        result.Value.ContentHash.Should().NotBeNullOrEmpty();
        result.Value.IngestionId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_BatchesConstantLookups()
    {
        // Arrange
        byte[] content = new byte[250]; // 250 bytes to trigger batching (batch size = 100)
        for (int i = 0; i < 250; i++)
        {
            content[i] = (byte)(i % 256);
        }
        var request = new IngestContentCommand
        {
            ContentData = content,
            ContentType = ContentType.Binary,
            SourceUri = "test://source"
        };

        var decomposedConstants = content.Select(b => Constant.Create([b], ContentType.Binary)).ToList();

        _ingestionRepositoryMock.Setup(r => r.GetByContentHashAsync(It.IsAny<Hash256>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Hartonomous.Core.Domain.Entities.ContentIngestion?)null);

        _decomposerFactoryMock.Setup(f => f.DecomposeAsync(content, ContentType.Binary, It.IsAny<CancellationToken>()))
            .ReturnsAsync(decomposedConstants);

        _constantRepositoryMock.Setup(r => r.GetByHashAsync(It.IsAny<Hash256>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Constant?)null);

        _constantRepositoryMock.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<Constant>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _ingestionRepositoryMock.Setup(r => r.AddAsync(It.IsAny<Hartonomous.Core.Domain.Entities.ContentIngestion>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Hartonomous.Core.Domain.Entities.ContentIngestion entity, CancellationToken _) => entity);

        _unitOfWorkMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _sut.Handle(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalConstantsCreated.Should().Be(250);
        
        // Should call AddRangeAsync once (not 250 times)
        _constantRepositoryMock.Verify(r => r.AddRangeAsync(
            It.Is<IEnumerable<Constant>>(c => c.Count() == 250), 
            It.IsAny<CancellationToken>()), 
            Times.Once);
    }
}
