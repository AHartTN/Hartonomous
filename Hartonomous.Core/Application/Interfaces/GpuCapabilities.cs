namespace Hartonomous.Core.Application.Interfaces;

/// <summary>
/// GPU capability information.
/// </summary>
public sealed record GpuCapabilities
{
    public bool HasCuPy { get; init; }
    public bool HasCuMl { get; init; }
    public int GpuCount { get; init; }
    public long GpuMemoryMb { get; init; }
    public string? ErrorMessage { get; init; }
    
    public bool IsAvailable => HasCuPy && GpuCount > 0;
}
