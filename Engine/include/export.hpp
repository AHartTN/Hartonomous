#pragma once

#if defined(_WIN32)
    #if defined(HARTONOMOUS_EXPORT)
        #define HARTONOMOUS_API __declspec(dllexport)
    #else
        #define HARTONOMOUS_API __declspec(dllimport)
    #endif
#else
    #define HARTONOMOUS_API __attribute__((visibility("default")))
#endif
