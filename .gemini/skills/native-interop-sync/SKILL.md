---
name: native-interop-sync
description: Keep Engine/include/interop_api.h and C# NativeBindings.cs synchronized. Use when adding new C++ functions or modifying struct definitions.
---

# Native Interop Synchronization

**Rule**: Every `extern "C"` in `interop_api.h` MUST have matching `[DllImport]` in `NativeBindings.cs`.

## Adding a New Function

1. **C++ header** (`Engine/include/interop_api.h`):
```cpp
extern "C" HARTONOMOUS_API bool hartonomous_new_function(
    h_db_connection_t conn, const uint8_t* hash, double* out_coords);
```

2. **C++ implementation** (`Engine/src/interop_api.cpp`):
```cpp
bool hartonomous_new_function(...) {
    try { /* impl */ return true; }
    catch (const std::exception& e) { set_last_error(e.what()); return false; }
}
```

3. **C# declaration** (`app-layer/Hartonomous.Marshal/NativeBindings.cs`):
```csharp
[DllImport(LibName, EntryPoint = "hartonomous_new_function",
           CallingConvention = CallingConvention.Cdecl)]
[return: MarshalAs(UnmanagedType.I1)]
public static extern unsafe bool NewFunction(IntPtr conn, byte* hash, double* outCoords);
```

4. **Verify**: `Marshal.SizeOf<Struct>()` matches C++ `sizeof()`

## Checklist
- ☑ `HARTONOMOUS_API` macro on all exports
- ☑ `CallingConvention.Cdecl` specified
- ☑ `[MarshalAs(UnmanagedType.I1)]` on all `bool`
- ☑ Strings as `byte*` + UTF-8, never `string`
- ☑ C++ allocates → C++ frees (ownership)
- ☑ Error: `hartonomous_get_last_error()` after failures