namespace Hartonomous.Core.Domain.Exceptions;

/// <summary>
/// Exception thrown when BPE token operations fail
/// </summary>
public sealed class BPETokenException : DomainException
{
    public int? TokenId { get; }
    
    public BPETokenException(string message, int? tokenId = null)
        : base(message)
    {
        TokenId = tokenId;
    }
    
    public BPETokenException(string message, Exception innerException, int? tokenId = null)
        : base(message, innerException)
    {
        TokenId = tokenId;
    }
}
