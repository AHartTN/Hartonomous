namespace Hartonomous.Infrastructure.Services.BPE;

/// <summary>
/// Represents a gap in the Hilbert-sorted sequence of constants
/// Used for compression opportunity detection
/// </summary>
public record HilbertGap
{
    /// <summary>Index of the constant before the gap</summary>
    public int StartIndex { get; init; }
    
    /// <summary>Index of the constant after the gap</summary>
    public int EndIndex { get; init; }
    
    /// <summary>Hilbert index of the constant before the gap</summary>
    public ulong StartHilbert { get; init; }
    
    /// <summary>Hilbert index of the constant after the gap</summary>
    public ulong EndHilbert { get; init; }
    
    /// <summary>Size of the gap in Hilbert space</summary>
    public ulong GapSize { get; init; }
    
    /// <summary>Whether this is a very sparse gap (10x threshold)</summary>
    public bool IsSparse { get; init; }
}
