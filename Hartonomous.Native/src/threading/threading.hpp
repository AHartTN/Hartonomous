#pragma once

#include <thread>
#include <vector>
#include <atomic>
#include <functional>
#include <cstddef>

namespace hartonomous {

/// Threading utilities for parallel work distribution.
/// Provides consistent thread count detection and work-stealing parallel loops.
struct Threading {
    /// Get optimal thread count for CPU-bound work.
    /// Returns hardware_concurrency if available, otherwise 4.
    [[nodiscard]] static std::size_t default_thread_count() noexcept {
        auto hw = std::thread::hardware_concurrency();
        return hw > 0 ? static_cast<std::size_t>(hw) : 4;
    }
    
    /// Get optimal thread count for IO-bound work (typically fewer than CPU count).
    /// @param max_connections Maximum concurrent connections to allow
    [[nodiscard]] static std::size_t io_thread_count(std::size_t max_connections = 8) noexcept {
        return std::min(max_connections, default_thread_count());
    }
    
    /// Execute work items in parallel using work-stealing pattern.
    /// Each worker claims items atomically until all are processed.
    /// 
    /// @tparam Func Callable with signature void(std::size_t index)
    /// @param count Total number of work items
    /// @param work Function to call for each item index
    /// @param num_threads Thread count (0 = auto)
    template<typename Func>
    static void parallel_for(std::size_t count, Func&& work, std::size_t num_threads = 0) {
        if (count == 0) return;
        
        if (num_threads == 0) {
            num_threads = default_thread_count();
        }
        num_threads = std::min(num_threads, count);
        
        if (num_threads == 1) {
            // Single-threaded fast path
            for (std::size_t i = 0; i < count; ++i) {
                work(i);
            }
            return;
        }
        
        std::atomic<std::size_t> next_item{0};
        std::vector<std::thread> threads;
        threads.reserve(num_threads);
        
        for (std::size_t t = 0; t < num_threads; ++t) {
            threads.emplace_back([&work, &next_item, count]() {
                while (true) {
                    std::size_t idx = next_item.fetch_add(1, std::memory_order_relaxed);
                    if (idx >= count) break;
                    work(idx);
                }
            });
        }
        
        for (auto& t : threads) {
            t.join();
        }
    }
    
    /// Execute work items with thread-local results that are collected at the end.
    /// 
    /// @tparam Result Type that supports move construction
    /// @tparam Func Callable with signature Result(std::size_t start, std::size_t end)
    /// @param count Total number of work items  
    /// @param work Function that processes a range and returns result
    /// @param num_threads Thread count (0 = auto)
    /// @return Vector of results, one per thread
    template<typename Result, typename Func>
    static std::vector<Result> parallel_chunk(std::size_t count, Func&& work, std::size_t num_threads = 0) {
        if (count == 0) return {};
        
        if (num_threads == 0) {
            num_threads = default_thread_count();
        }
        num_threads = std::min(num_threads, count);
        
        std::size_t chunk_size = count / num_threads;
        std::size_t remainder = count % num_threads;
        
        std::vector<Result> results(num_threads);
        std::vector<std::thread> threads;
        threads.reserve(num_threads);
        
        std::size_t start = 0;
        for (std::size_t t = 0; t < num_threads; ++t) {
            std::size_t this_chunk = chunk_size + (t < remainder ? 1 : 0);
            std::size_t end = start + this_chunk;
            
            threads.emplace_back([&work, &results, t, start, end]() {
                results[t] = work(start, end);
            });
            
            start = end;
        }
        
        for (auto& t : threads) {
            t.join();
        }
        
        return results;
    }
    
    /// Detect the optimal thread split for generation vs IO.
    /// Returns (gen_threads, io_threads) based on hardware.
    /// 
    /// @param max_io_threads Maximum IO threads (e.g., DB connections)
    [[nodiscard]] static std::pair<std::size_t, std::size_t> 
    detect_thread_split(std::size_t max_io_threads = 8) noexcept {
        std::size_t total = default_thread_count();
        std::size_t io = std::min(max_io_threads, total);
        return {total, io};
    }
};

} // namespace hartonomous
