using System.Runtime.InteropServices;
using Hartonomous.Marshal;
using Microsoft.Extensions.Logging;

namespace Hartonomous.Core.Services;

/// <summary>
/// Manages the lifetime of the native C++ engine connection.
/// Registered as singleton — one connection per application lifetime.
/// </summary>
public sealed class EngineService : IDisposable
{
    private readonly IntPtr _dbHandle;
    private readonly ILogger<EngineService> _logger;
    private bool _disposed;

    public EngineService(string connectionString, ILogger<EngineService> logger)
    {
        _logger = logger;
        _dbHandle = NativeMethods.DbCreate(connectionString);
        if (_dbHandle == IntPtr.Zero)
            throw new InvalidOperationException($"Failed to connect to native engine: {GetLastError()}");
        _logger.LogInformation("Native engine connected");
    }

    public IntPtr DbHandle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _dbHandle;
        }
    }

    public bool IsConnected => !_disposed && NativeMethods.DbIsConnected(_dbHandle);

    public static string GetLastError()
    {
        var ptr = NativeMethods.GetLastError();
        return ptr != IntPtr.Zero
            ? System.Runtime.InteropServices.Marshal.PtrToStringAnsi(ptr) ?? "Unknown error"
            : "Unknown error";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        NativeMethods.DbDestroy(_dbHandle);
        _logger.LogInformation("Native engine disconnected");
    }
}
