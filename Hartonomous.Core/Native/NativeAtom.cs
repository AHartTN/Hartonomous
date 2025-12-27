using System.Runtime.InteropServices;

namespace Hartonomous.Core.Native;

/// <summary>
/// Native atom structure matching the C++ HartonomousAtom.
/// CENTER-ORIGIN GEOMETRY: coordinates are SIGNED, origin at (0,0,0,0).
/// Surface faces are at ±int.MaxValue.
/// Uses explicit layout to match C++ struct exactly.
/// Must be kept in sync with exports.h.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 40)]
internal struct NativeAtom
{
    [FieldOffset(0)]
    public long HilbertHigh;      // 8 bytes (offset 0-7)
    
    [FieldOffset(8)]
    public long HilbertLow;       // 8 bytes (offset 8-15)
    
    [FieldOffset(16)]
    public int Codepoint;         // 4 bytes (offset 16-19)
    
    [FieldOffset(20)]
    public int X;                 // 4 bytes (offset 20-23)
    
    [FieldOffset(24)]
    public int Y;                 // 4 bytes (offset 24-27)
    
    [FieldOffset(28)]
    public int Z;                 // 4 bytes (offset 28-31)
    
    [FieldOffset(32)]
    public int W;                 // 4 bytes (offset 32-35)
    
    [FieldOffset(36)]
    public byte Face;             // 1 byte (offset 36)
    // 3 bytes padding (37-39) to reach 40 bytes total
}
