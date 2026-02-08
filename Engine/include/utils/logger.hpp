#pragma once

#include <iostream>
#include <string>
#include <iomanip>
#include <mutex>

namespace Hartonomous {

/**
 * @brief Thread-safe logging utility for the C++ engine.
 */
class Logger {
public:
    enum class Level {
        Info,
        Step,
        Success,
        Warning,
        Error,
        Bulk
    };

    static void log(Level level, const std::string& message) {
        static std::mutex mutex;
        std::lock_guard<std::mutex> lock(mutex);

        const char* color = "";
        const char* prefix = "";

        switch (level) {
            case Level::Info:    color = "\033[0;36m"; prefix = "=== "; break; // Cyan
            case Level::Step:    color = "\033[1;33m"; prefix = ">>> "; break; // Yellow
            case Level::Success: color = "\033[0;32m"; prefix = "✓ ";   break; // Green
            case Level::Warning: color = "\033[1;33m"; prefix = "⚠ ";   break; // Yellow
            case Level::Error:   color = "\033[0;31m"; prefix = "✗ ";   break; // Red
            case Level::Bulk:    color = "\033[0;35m"; prefix = "[BULK] "; break; // Magenta
        }

        std::cout << color << prefix << message << "\033[0m" << std::endl;
    }

    static void info(const std::string& msg)    { log(Level::Info, msg); }
    static void step(const std::string& msg)    { log(Level::Step, msg); }
    static void success(const std::string& msg) { log(Level::Success, msg); }
    static void warn(const std::string& msg)    { log(Level::Warning, msg); }
    static void error(const std::string& msg)   { log(Level::Error, msg); }
    static void bulk(const std::string& msg)    { log(Level::Bulk, msg); }
};

} // namespace Hartonomous
