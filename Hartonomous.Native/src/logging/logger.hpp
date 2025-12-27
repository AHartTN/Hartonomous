#pragma once

#include <iostream>
#include <sstream>
#include <chrono>
#include <iomanip>
#include <mutex>

namespace hartonomous {

/// Log severity levels
enum class LogLevel : int {
    Debug = 0,
    Info = 1,
    Warn = 2,
    Error = 3,
    Silent = 99
};

/// Minimal centralized logger.
/// Thread-safe, supports quiet mode and log levels.
class Logger {
    LogLevel level_ = LogLevel::Info;
    std::ostream* out_ = &std::cerr;
    bool timestamps_ = false;
    mutable std::mutex mutex_;
    
public:
    Logger() = default;
    
    /// Set minimum log level
    void set_level(LogLevel level) noexcept { level_ = level; }
    [[nodiscard]] LogLevel level() const noexcept { return level_; }
    
    /// Enable quiet mode (same as LogLevel::Silent)
    void set_quiet(bool quiet = true) noexcept {
        level_ = quiet ? LogLevel::Silent : LogLevel::Info;
    }
    [[nodiscard]] bool quiet() const noexcept { return level_ == LogLevel::Silent; }
    
    /// Enable/disable timestamps
    void set_timestamps(bool enabled) noexcept { timestamps_ = enabled; }
    
    /// Set output stream (default: stderr)
    void set_output(std::ostream& out) noexcept { out_ = &out; }
    
    /// Check if level would be logged
    [[nodiscard]] bool would_log(LogLevel level) const noexcept {
        return static_cast<int>(level) >= static_cast<int>(level_);
    }
    
    /// Log a debug message
    template<typename... Args>
    void debug(Args&&... args) const {
        log(LogLevel::Debug, std::forward<Args>(args)...);
    }
    
    /// Log an info message
    template<typename... Args>
    void info(Args&&... args) const {
        log(LogLevel::Info, std::forward<Args>(args)...);
    }
    
    /// Log a warning message
    template<typename... Args>
    void warn(Args&&... args) const {
        log(LogLevel::Warn, std::forward<Args>(args)...);
    }
    
    /// Log an error message
    template<typename... Args>
    void error(Args&&... args) const {
        log(LogLevel::Error, std::forward<Args>(args)...);
    }
    
    /// Generic log with explicit level
    template<typename... Args>
    void log(LogLevel level, Args&&... args) const {
        if (!would_log(level)) return;
        
        std::ostringstream ss;
        if (timestamps_) {
            auto now = std::chrono::system_clock::now();
            auto time = std::chrono::system_clock::to_time_t(now);
            ss << std::put_time(std::localtime(&time), "[%H:%M:%S] ");
        }
        
        // Level prefix
        switch (level) {
            case LogLevel::Debug: ss << "[DEBUG] "; break;
            case LogLevel::Warn:  ss << "[WARN] "; break;
            case LogLevel::Error: ss << "[ERROR] "; break;
            default: break;
        }
        
        // Fold expression to output all arguments
        ((ss << args), ...);
        ss << '\n';
        
        std::lock_guard lock(mutex_);
        (*out_) << ss.str();
        out_->flush();
    }
};

/// Global logger instance
inline Logger& log() {
    static Logger instance;
    return instance;
}

/// RAII timer that logs elapsed time on destruction
class ScopedTimer {
    std::string name_;
    std::chrono::steady_clock::time_point start_;
    
public:
    explicit ScopedTimer(std::string name) 
        : name_(std::move(name))
        , start_(std::chrono::steady_clock::now()) {}
    
    ~ScopedTimer() {
        auto end = std::chrono::steady_clock::now();
        double elapsed = std::chrono::duration<double>(end - start_).count();
        log().info(name_, " in ", std::fixed, std::setprecision(3), elapsed, "s");
    }
    
    /// Get elapsed time so far
    [[nodiscard]] double elapsed() const {
        auto now = std::chrono::steady_clock::now();
        return std::chrono::duration<double>(now - start_).count();
    }
};

/// RAII progress reporter for batch operations
class Progress {
    std::string name_;
    std::size_t total_;
    std::size_t current_ = 0;
    std::size_t report_interval_;
    std::chrono::steady_clock::time_point start_;
    
public:
    Progress(std::string name, std::size_t total, std::size_t interval = 0)
        : name_(std::move(name))
        , total_(total)
        , report_interval_(interval > 0 ? interval : std::max<std::size_t>(total / 10, 1))
        , start_(std::chrono::steady_clock::now()) {}
    
    void advance(std::size_t n = 1) {
        current_ += n;
        if (current_ % report_interval_ == 0 || current_ == total_) {
            report();
        }
    }
    
    void report() const {
        if (log().quiet()) return;
        
        double elapsed = std::chrono::duration<double>(
            std::chrono::steady_clock::now() - start_).count();
        double rate = current_ / elapsed;
        double pct = 100.0 * current_ / total_;
        
        log().info(name_, ": ", current_, "/", total_, 
                   " (", std::fixed, std::setprecision(1), pct, "%) ",
                   std::setprecision(0), rate, "/s");
    }
};

} // namespace hartonomous
