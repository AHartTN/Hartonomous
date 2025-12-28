namespace Hartonomous.Core.Models;

/// <summary>
/// RLE-compressed point in semantic trajectory.
/// </summary>
public readonly record struct TrajectoryPoint(
    short Page,      // X: Unicode page
    short Type,      // Y: Character type
    int Base,        // Z: Base character
    byte Variant,    // M: Variant (case/diacritical)
    uint Count);     // RLE: repetition count
