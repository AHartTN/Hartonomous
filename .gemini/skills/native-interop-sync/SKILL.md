---
name: native-interop-sync
description: Synchronize C++ Interop API definitions with C# P/Invoke bindings. Use when you modify 'Engine/include/interop_api.h' or need to update 'app-layer/Hartonomous.Marshal/NativeBindings.cs'.
---

# Native Interop Sync

This skill ensures that the high-performance C++ Engine and the C# Marshalling layer stay in perfect bit-level synchronization.

## Mapping Standards

| C++ (interop_api.h) | C# (NativeBindings.cs) | Note |
| :--- | :--- | :--- |
| `h_db_connection_t` | `IntPtr` | Opaque opaque handle |
| `uint8_t[16]` | `fixed byte[16]` | For 128-bit hashes/IDs |
| `double[4]` | `fixed double[4]` | For 4D S³ coordinates |
| `size_t` | `nuint` | Native-sized unsigned integer |
| `char*` | `byte*` | Unsafe pointer for string data |

## Struct Alignment
All interop structs must use `[StructLayout(LayoutKind.Sequential)]` to match C++ packing.
- **Example**: `WalkState`, `IngestionStats`, `ResearchPlan`.

## Workflow
1.  **Header Audit**: Read `Engine/include/interop_api.h` for modified `extern "C"` functions.
2.  **Binding Update**: Transform new symbols into `[DllImport]` signatures in `app-layer/Hartonomous.Marshal/NativeBindings.cs`.
3.  **Hash Verification**: Ensure 128-bit hashes are passed as `byte*` to avoid `Guid` marshalling overhead in high-frequency loops.
4.  **Parity Check**: Confirm `LibName` constant resolves correctly on both Linux (`libengine.so`) and Windows (`engine.dll`).

## Error Handling
The Engine uses thread-local error storage. Always check `hartonomous_get_last_error()` if a function returns `false`.
```csharp
if (!NativeMethods.IngestText(handle, text, out var stats)) {
    var errorPtr = NativeMethods.GetLastError();
    // Handle error...
}
```