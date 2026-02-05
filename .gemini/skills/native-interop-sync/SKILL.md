---
name: native-interop-sync
description: Keep Engine/include/interop_api.h and C# NativeBindings.cs synchronized. Use when adding new C++ functions or modifying struct definitions.
---

# Native Interop Synchronization

This skill ensures C++ Engine API stays synchronized with C# marshaling layer.

## The Synchronization Contract

**Rule**: Every `extern "C"` function in `interop_api.h` must have matching `[DllImport]` in `NativeBindings.cs`.

### C++ Side (Engine/include/interop_api.h)
```cpp
extern "C" {
    // Always use HARTONOMOUS_API for export visibility
    HARTONOMOUS_API bool hartonomous_ingest_text(
        h_db_connection_t conn,
        const char* text_utf8,
        size_t text_len,
        h_ingestion_result_t* out_result
    );
}
```

### C# Side (app-layer/Hartonomous.Marshal/NativeBindings.cs)
```csharp
public static partial class NativeMethods
{
    private const string LibName = "engine"; // engine.dll or libengine.so
    
    [DllImport(LibName, EntryPoint = "hartonomous_ingest_text", 
               CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)] // C bool -> C# bool
    public static extern unsafe bool IngestText(
        IntPtr connection,
        byte* textUtf8,
        nuint textLen,
        out IngestionResult result
    );
}
```

## Type Mapping Reference

| C++ Type (interop_api.h) | C# Type (NativeBindings.cs) | Notes |
|:---|:---|:---|
| `h_db_connection_t` | `IntPtr` | Opaque handle |
| `uint8_t[16]` | `fixed byte[16]` | BLAKE3 hashes |
| `double[4]` | `fixed double[4]` | S³ coordinates |
| `char*` | `byte*` | UTF-8 strings (unsafe) |
| `const char*` | `byte*` | UTF-8 read-only |
| `size_t` | `nuint` | Platform size |
| `bool` | `[MarshalAs(UnmanagedType.I1)] bool` | 1-byte boolean |
| `int64_t` | `long` | Signed 64-bit |
| `uint64_t` | `ulong` | Unsigned 64-bit |

## Struct Synchronization

**Critical**: Struct layouts MUST match exactly.

### C++ Definition
```cpp
// interop_api.h
typedef struct h_ingestion_result {
    uint64_t atoms_processed;
    uint64_t compositions_created;
    uint64_t relations_created;
    double elapsed_seconds;
    bool success;
    uint8_t _padding[7]; // Explicit padding for alignment
} h_ingestion_result_t;
```

### C# Definition
```csharp
// NativeBindings.cs
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct IngestionResult
{
    public ulong AtomsProcessed;
    public ulong CompositionsCreated;
    public ulong RelationsCreated;
    public double ElapsedSeconds;
    
    [MarshalAs(UnmanagedType.I1)]
    public bool Success;
    
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 7)]
    private byte[] _padding;
}
```

### Verification
```csharp
// Unit test
var expected = 32; // sizeof(h_ingestion_result_t) from C++
var actual = Marshal.SizeOf<IngestionResult>();
Assert.Equal(expected, actual);
```

## Workflow for Adding New Function

1. **Define in C++** (`Engine/include/interop_api.h`):
```cpp
extern "C" HARTONOMOUS_API bool hartonomous_new_function(
    h_db_connection_t conn,
    const uint8_t* hash,
    double* out_coordinates
);
```

2. **Implement in C++** (`Engine/src/interop/...`):
```cpp
bool hartonomous_new_function(
    h_db_connection_t conn,
    const uint8_t* hash,
    double* out_coordinates
) {
    try {
        // Implementation
        return true;
    } catch (const std::exception& e) {
        set_last_error(e.what());
        return false;
    }
}
```

3. **Declare in C#** (`NativeBindings.cs`):
```csharp
[DllImport(LibName, EntryPoint = "hartonomous_new_function",
           CallingConvention = CallingConvention.Cdecl)]
[return: MarshalAs(UnmanagedType.I1)]
public static extern unsafe bool NewFunction(
    IntPtr connection,
    byte* hash,
    double* outCoordinates
);
```

4. **Create C# Wrapper** (optional, type-safe layer):
```csharp
public static bool NewFunction(
    DatabaseConnection conn,
    Guid hash,
    out Vector4 coordinates
) {
    fixed (byte* hashPtr = hash.ToByteArray())
    fixed (double* coordPtr = stackalloc double[4])
    {
        bool success = NativeMethods.NewFunction(
            conn.Handle, hashPtr, coordPtr
        );
        
        if (success) {
            coordinates = new Vector4(
                coordPtr[0], coordPtr[1], 
                coordPtr[2], coordPtr[3]
            );
        } else {
            coordinates = default;
        }
        
        return success;
    }
}
```

5. **Test Round-Trip**:
```csharp
[Fact]
public void NewFunction_RoundTrip_Success()
{
    var hash = Guid.NewGuid();
    bool result = Interop.NewFunction(conn, hash, out var coords);
    
    Assert.True(result);
    Assert.InRange(coords.Length(), 0.99, 1.01); // S³ surface
}
```

## Error Handling Synchronization

C++ sets thread-local error, C# retrieves:

```cpp
// C++ (Engine/src/interop/error.cpp)
thread_local std::string g_last_error;

void set_last_error(const char* msg) {
    g_last_error = msg;
}

extern "C" HARTONOMOUS_API const char* hartonomous_get_last_error() {
    return g_last_error.c_str();
}
```

```csharp
// C# (NativeBindings.cs)
[DllImport(LibName, EntryPoint = "hartonomous_get_last_error")]
private static extern byte* GetLastErrorPtr();

public static string GetLastError() {
    byte* ptr = GetLastErrorPtr();
    return ptr != null ? Marshal.PtrToStringUTF8((IntPtr)ptr) : null;
}
```

## Maintenance Checklist

- ☑ Every `extern "C"` has matching `[DllImport]`
- ☑ Struct sizes match (use `Marshal.SizeOf` test)
- ☑ Calling convention is `Cdecl`
- ☑ Return type marshaling correct (especially `bool`)
- ☑ String encoding is UTF-8 (`byte*`, not `string`)
- ☑ Memory ownership clear (who allocates, who frees)
- ☑ Error handling via `GetLastError()` after failures
- ☑ `HARTONOMOUS_API` macro on all exports