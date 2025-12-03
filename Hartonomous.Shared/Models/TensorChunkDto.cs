namespace Hartonomous.Shared.Models;

public class TensorChunkDto
{
    public long Id { get; set; }
    public long AtomId { get; set; }
    public string TensorName { get; set; } = string.Empty;
    public int[] Shape { get; set; } = Array.Empty<int>();
    public int[] ChunkStart { get; set; } = Array.Empty<int>();
    public int[] ChunkEnd { get; set; } = Array.Empty<int>();
    public string ContentHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class CreateTensorChunkRequest
{
    public long AtomId { get; set; }
    public string TensorName { get; set; } = string.Empty;
    public int[] Shape { get; set; } = Array.Empty<int>();
    public int[] ChunkStart { get; set; } = Array.Empty<int>();
    public int[] ChunkEnd { get; set; } = Array.Empty<int>();
    public byte[] BinaryPayload { get; set; } = Array.Empty<byte>();
    public double[]? Embedding { get; set; }
}

public class TensorSearchRequest
{
    public double[] QueryVector { get; set; } = Array.Empty<double>();
    public int Limit { get; set; } = 10;
}
