using System;
using System.Runtime.InteropServices;

namespace Hartonomous.Marshal;

/// <summary>
/// Generic base class for C# services that wrap a native C++ handle.
/// Provides a consistent pattern for lifecycle management and error handling.
/// </summary>
public abstract class NativeService : NativeHandle
{
    protected NativeService(IntPtr handle) : base(handle)
    {
    }

    /// <summary>
    /// Executes a native operation and throws if it fails.
    /// </summary>
    protected void InvokeNative(Func<IntPtr, bool> action, string errorMessage)
    {
        if (!action(RawHandle))
        {
            throw new InvalidOperationException($"{errorMessage}: {GetNativeError()}");
        }
    }

    /// <summary>
    /// Executes a native operation that returns a result, and throws if it fails.
    /// </summary>
    protected TResult InvokeNative<TResult>(NativeFunc<TResult> action, string errorMessage)
    {
        if (!action(RawHandle, out var result))
        {
            throw new InvalidOperationException($"{errorMessage}: {GetNativeError()}");
        }
        return result;
    }

    protected delegate bool NativeFunc<TResult>(IntPtr handle, out TResult result);
}