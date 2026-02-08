using System;
using System.Runtime.InteropServices;

namespace Hartonomous.Marshal;

/// <summary>
/// Base class for all native engine handles.
/// Handles IDisposable pattern and ensures native resources are freed.
/// </summary>
public abstract class NativeHandle : IDisposable
{
    protected IntPtr Handle;
    private bool _disposed;

    protected NativeHandle(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Failed to create native object: {GetNativeError()}");
        }
        Handle = handle;
    }

    public IntPtr RawHandle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return Handle;
        }
    }

    protected abstract void DestroyNative(IntPtr handle);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (Handle != IntPtr.Zero)
        {
            DestroyNative(Handle);
            Handle = IntPtr.Zero;
        }

        _disposed = true;
    }

    protected static string GetNativeError()
    {
        var ptr = NativeMethods.GetLastError();
        return ptr != IntPtr.Zero
            ? System.Runtime.InteropServices.Marshal.PtrToStringAnsi(ptr) ?? "Unknown error"
            : "Unknown error";
    }

    ~NativeHandle()
    {
        Dispose(false);
    }
}
