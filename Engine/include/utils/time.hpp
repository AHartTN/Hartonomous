#pragma once

#include <chrono>

namespace Hartonomous {

/**
 * @brief High-resolution timer and high-level timing utilities.
 */
class Timer {
public:
    using Clock = std::chrono::steady_clock;
    using TimePoint = std::chrono::time_point<Clock>;

    Timer() : start_(Clock::now()) {}

    /**
     * @brief Reset the timer to the current time.
     */
    void reset() {
        start_ = Clock::now();
    }

    /**
     * @brief Get elapsed milliseconds since last reset or construction.
     */
    double elapsed_ms() const {
        return std::chrono::duration_cast<std::chrono::milliseconds>(Clock::now() - start_).count();
    }

    /**
     * @brief Get elapsed seconds since last reset or construction.
     */
    double elapsed_sec() const {
        return elapsed_ms() / 1000.0;
    }

    /**
     * @brief Static helper to get milliseconds since a given time point.
     */
    static double ms_since(TimePoint start) {
        return std::chrono::duration_cast<std::chrono::milliseconds>(Clock::now() - start).count();
    }

private:
    TimePoint start_;
};

} // namespace Hartonomous