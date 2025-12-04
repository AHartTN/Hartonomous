namespace Hartonomous.Core.Domain.Exceptions;

/// <summary>
/// Exception thrown when a duplicate constant is detected
/// </summary>
public sealed class DuplicateConstantException : DomainException
{
    public string Hash { get; }
    public Guid? ExistingConstantId { get; }
    
    public DuplicateConstantException(string hash, Guid? existingConstantId = null)
        : base($"Constant with hash {hash} already exists")
    {
        Hash = hash;
        ExistingConstantId = existingConstantId;
    }
}
