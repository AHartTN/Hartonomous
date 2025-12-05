using System;
using System.Runtime.InteropServices;

namespace Hartonomous.Marshal;

// No longer needed if we pass an array/IntPtr directly
// public struct HilbertCoordinates
// {
//     public uint X;
//     public uint Y;
//     public uint Z;
//     public uint M;
// }

public static class NativeMethods
{
    private const string DllName = "Hartonomous.Native.dll";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void HilbertEncode4D(
        uint x, uint y, uint z, uint m,
        int precision,
        out ulong resultHigh, out ulong resultLow);

    // Modified to take a single pointer (IntPtr) for the result coordinates
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void HilbertDecode4D(
        ulong indexHigh, ulong indexLow,
        int precision,
        IntPtr resultCoords); // resultCoords points to a uint[4] array
}