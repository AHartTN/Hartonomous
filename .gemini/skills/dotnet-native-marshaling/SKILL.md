---
name: dotnet-native-marshaling
description: Manage P/Invoke marshaling between C# and C++ Engine. Use when defining NativeBindings, handling unsafe pointers, or bridging engine.so/engine.dll.
---

# .NET Native Marshaling: C#/C++ Bridge

This skill governs type-safe data transfer between C# application layer and C++ Engine for Hartonomous.

## Core Bridge Architecture

### Library Loading
- **Linux**: `libengine.so` (unified library linking engine_core + engine_io)
- **Windows**: `engine.dll`
- **Path Resolution**: Use `NativeLibrary.SetDllImportResolver` for custom search paths
- **Convention**: `CallingConvention.Cdecl` for all C++ exports

### Critical Type Mappings

| C++ Type | C# Type | Notes |
|:---|:---|:---|
| `uint8_t[16]` | `fixed byte[16]` or `Guid` | BLAKE3 hashes (128-bit) |
| `double[4]` | `fixed double[4]` | S³ coordinates (x,y,z,w) |
| `char*` | `byte*` + UTF8 | String data (unsafe pointers) |
| `void*` | `IntPtr` | Opaque handles (db connections, etc.) |
| `size_t` | `nuint` | Platform-dependent unsigned int |
| `bool` | `[MarshalAs(UnmanagedType.I1)] bool` | C bool = 1 byte |

### Struct Marshaling
All P/Invoke structs MUST use `[StructLayout(LayoutKind.Sequential)]`:

```csharp
[StructLayout(LayoutKind.Sequential)]
public unsafe struct IngestionResult
{
    public ulong atoms_processed;
    public ulong compositions_created;
    public ulong relations_created;
    [MarshalAs(UnmanagedType.I1)]
    public bool success;
}
```

## Memory Management Rules

### 1. Ownership Model
- **C++ Allocates, C++ Frees**: Never free C++-allocated memory from C#
- **C# Pins, C# Unpins**: Use `fixed` blocks for ephemeral pointer passing
- **Pattern**: C++ returns `char*`, C# copies to managed string, C# calls C++ free function

```csharp
// C++ side
extern "C" HARTONOMOUS_API char* get_error_message();
extern "C" HARTONOMOUS_API void free_error_message(char* msg);

// C# side
[DllImport(LibName)]
private static extern byte* get_error_message();

[DllImport(LibName)]
private static extern void free_error_message(byte* msg);

public static string GetLastError() 
{
    byte* msgPtr = get_error_message();
    if (msgPtr == null) return null;
    
    string result = Marshal.PtrToStringUTF8((IntPtr)msgPtr);
    free_error_message(msgPtr);
    return result;
}
```

### 2. Hash/GUID Handling
**BLAKE3 hashes are 16 bytes exactly**:

```csharp
// Pass hash to C++
Guid hash = ...;
fixed (byte* hashPtr = hash.ToByteArray()) 
{
    NativeMethods.IngestComposition(handle, hashPtr, ...);
}

// Receive hash from C++
byte[] hashBuffer = new byte[16];
fixed (byte* bufPtr = hashBuffer) 
{
    NativeMethods.GetCompositionHash(compId, bufPtr);
}
Guid hash = new Guid(hashBuffer);
```

## Error Handling Pattern

C++ Engine uses thread-local error storage:

```csharp
// C++ sets error on failure, returns false
if (!NativeMethods.ExecuteQuery(handle, query, out var results)) 
{
    string error = NativeMethods.GetLastError();
    throw new HartonomousException($"Query failed: {error}");
}
```

## Cross-Platform Testing

1. **Struct Size Verification**:
```csharp
int expected = 32; // from sizeof() in C++
int actual = Marshal.SizeOf<MyStruct>();
Assert.Equal(expected, actual);
```

2. **Symbol Resolution Test**:
```csharp
var handle = NativeLibrary.Load("engine", ...);
var funcPtr = NativeLibrary.GetExport(handle, "hartonomous_init");
Assert.NotEqual(IntPtr.Zero, funcPtr);
```

3. **Round-Trip Validation**:
- Pass known data to C++
- Retrieve via P/Invoke
- Verify bit-exact match

## Debugging Checklist

- ☑ `EntryPoint` matches C++ `extern "C"` symbol name exactly
- ☑ `CallingConvention.Cdecl` specified
- ☑ Struct layouts match (use `#pragma pack` in C++ if needed)
- ☑ Library in LD_LIBRARY_PATH (Linux) or PATH (Windows)
- ☑ C++ exports use `HARTONOMOUS_API` macro for visibility
- ☑ Memory allocated by C++ freed by C++ (ownership)
- ☑ `[MarshalAs(UnmanagedType.I1)]` for all `bool` types