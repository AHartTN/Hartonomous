using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Hartonomous.Marshal;

/// <summary>
/// P/Invoke declarations for Hartonomous native library using LibraryImport source generation.
/// </summary>
/// <remarks>
/// Uses LibraryImport (.NET 7+) instead of DllImport for:
/// - Compile-time source generation (no runtime IL stubs)
/// - NativeAOT compatibility
/// - Debuggable marshalling code
/// - Better performance
/// Reference: https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke-source-generation
/// </remarks>
public static partial class NativeMethods
{
    private const string LibraryName = "Hartonomous.Native.dll";

    /// <summary>
    /// Encodes 4D coordinates (X, Y, Z, M) into a 128-bit Hilbert index.
    /// </summary>
    /// <param name="x">X coordinate (21-bit quantized)</param>
    /// <param name="y">Y coordinate (Shannon entropy, 21-bit)</param>
    /// <param name="z">Z coordinate (Compressibility, 21-bit)</param>
    /// <param name="m">M coordinate (Connectivity, 21-bit)</param>
    /// <param name="precision">Bit precision per dimension (default 21)</param>
    /// <param name="resultHigh">High 64 bits of 128-bit Hilbert index</param>
    /// <param name="resultLow">Low 64 bits of 128-bit Hilbert index</param>
    [LibraryImport(LibraryName, EntryPoint = "HilbertEncode4D")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial void HilbertEncode4D(
        uint x, uint y, uint z, uint m,
        int precision,
        out ulong resultHigh,
        out ulong resultLow);

    /// <summary>
    /// Decodes a 128-bit Hilbert index back into 4D coordinates (X, Y, Z, M).
    /// </summary>
    /// <param name="indexHigh">High 64 bits of 128-bit Hilbert index</param>
    /// <param name="indexLow">Low 64 bits of 128-bit Hilbert index</param>
    /// <param name="precision">Bit precision per dimension (default 21)</param>
    /// <param name="resultCoords">Pointer to uint[4] array to receive decoded coordinates</param>
    [LibraryImport(LibraryName, EntryPoint = "HilbertDecode4D")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial void HilbertDecode4D(
        ulong indexHigh,
        ulong indexLow,
        int precision,
        IntPtr resultCoords);
}