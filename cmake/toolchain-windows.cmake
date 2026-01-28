set(CMAKE_SYSTEM_NAME Windows)

set(CMAKE_C_COMPILER   cl.exe)
set(CMAKE_CXX_COMPILER cl.exe)

set(CMAKE_C_STANDARD 17)
set(CMAKE_CXX_STANDARD 23)

set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

add_compile_options(
    /O2
    /arch:AVX2
    /fp:fast
    /EHsc
)

set(CMAKE_EXPORT_COMPILE_COMMANDS ON)
