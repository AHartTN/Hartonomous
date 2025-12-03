namespace Hartonomous.Shared.Models;

public class AtomDto
{
    public long Id { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public byte[]? AtomicValue { get; set; }
    public string AtomType { get; set; } = string.Empty;
    public long? ParentAtomId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

public class CreateAtomRequest
{
    public byte[]? AtomicValue { get; set; }
    public string AtomType { get; set; } = string.Empty;
    public long? ParentAtomId { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}
