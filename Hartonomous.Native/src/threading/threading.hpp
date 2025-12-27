#pragma once

#include <thread>
#include <vector>
#include <atomic>
#include <functional>
#include <future>
#include <cstddef>
#include <algorithm>

#ifdef _WIN32
#include <windows.h>
#endif

namespace hartonomous {

/// Threading utilities optimized for modern hybrid CPUs.
struct Threading {
    /// Get physical core count (excludes hyperthreading).
    [[nodiscard]] static std::size_t physical_cores() noexcept {
#ifdef _WIN32
        DWORD len = 0;
        GetLogicalProcessorInformationEx(RelationProcessorCore, nullptr, &len);
        std::vector<char> buffer(len);
        if (GetLogicalProcessorInformationEx(RelationProcessorCore,
                reinterpret_cast<PSYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX>(buffer.data()), &len)) {
            std::size_t count = 0;
            char* ptr = buffer.data();
            while (ptr < buffer.data() + len) {
                auto info = reinterpret_cast<PSYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX>(ptr);
                if (info->Relationship == RelationProcessorCore) ++count;
                ptr += info->Size;
            }
            if (count > 0) return count;
        }
#endif
        auto hw = std::thread::hardware_concurrency();
        return hw > 1 ? hw / 2 : 1;
    }

    /// All logical processors for CPU-bound work.
    [[nodiscard]] static std::size_t cpu_threads() noexcept {
        auto hw = std::thread::hardware_concurrency();
        return hw > 0 ? static_cast<std::size_t>(hw) : 8;
    }

    /// Physical cores only for memory-bound work.
    [[nodiscard]] static std::size_t memory_threads() noexcept {
        return physical_cores();
    }

    /// PostgreSQL optimal: (2 * cores) + 1 for NVMe/SSD.
    [[nodiscard]] static std::size_t pg_connections() noexcept {
        return 2 * physical_cores() + 1;
    }

    /// IO-bound thread count.
    [[nodiscard]] static std::size_t io_threads() noexcept {
        return std::min(pg_connections(), cpu_threads());
    }

    [[nodiscard]] static std::size_t default_thread_count() noexcept {
        return cpu_threads();
    }

    [[nodiscard]] static std::size_t io_thread_count(std::size_t max = 0) noexcept {
        std::size_t optimal = io_threads();
        return max > 0 ? std::min(max, optimal) : optimal;
    }

    [[nodiscard]] static std::pair<std::size_t, std::size_t>
    detect_thread_split(std::size_t max_io = 0) noexcept {
        std::size_t gen = cpu_threads();
        std::size_t io = max_io > 0 ? std::min(max_io, io_threads()) : io_threads();
        return {gen, io};
    }

    /// Work-stealing parallel for.
    template<typename Func>
    static void parallel_for(std::size_t count, Func&& work, std::size_t num_threads = 0) {
        if (count == 0) return;

        if (num_threads == 0) num_threads = cpu_threads();
        num_threads = std::min(num_threads, count);

        if (num_threads == 1) {
            for (std::size_t i = 0; i < count; ++i) work(i);
            return;
        }

        std::atomic<std::size_t> next{0};
        std::vector<std::thread> threads;
        threads.reserve(num_threads);

        for (std::size_t t = 0; t < num_threads; ++t) {
            threads.emplace_back([&work, &next, count]() {
                while (true) {
                    std::size_t idx = next.fetch_add(1, std::memory_order_relaxed);
                    if (idx >= count) break;
                    work(idx);
                }
            });
        }

        for (auto& t : threads) t.join();
    }

    /// Chunked parallel execution returning results.
    template<typename Result, typename Func>
    static std::vector<Result> parallel_chunk(std::size_t count, Func&& work, std::size_t num_threads = 0) {
        if (count == 0) return {};

        if (num_threads == 0) num_threads = cpu_threads();
        num_threads = std::min(num_threads, count);

        std::size_t chunk = count / num_threads;
        std::size_t rem = count % num_threads;

        std::vector<Result> results(num_threads);
        std::vector<std::thread> threads;
        threads.reserve(num_threads);

        std::size_t start = 0;
        for (std::size_t t = 0; t < num_threads; ++t) {
            std::size_t end = start + chunk + (t < rem ? 1 : 0);
            threads.emplace_back([&work, &results, t, start, end]() {
                results[t] = work(start, end);
            });
            start = end;
        }

        for (auto& t : threads) t.join();
        return results;
    }

    /// Fire-and-forget async execution.
    template<typename Func>
    static std::future<void> async(Func&& work) {
        return std::async(std::launch::async, std::forward<Func>(work));
    }

    /// Async with result.
    template<typename Func>
    static auto async_result(Func&& work) -> std::future<decltype(work())> {
        return std::async(std::launch::async, std::forward<Func>(work));
    }
};

} // namespace hartonomous
