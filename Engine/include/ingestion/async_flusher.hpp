#pragma once

#include <database/postgres_connection.hpp>
#include <ingestion/substrate_batch.hpp>
#include <storage/physicality_store.hpp>
#include <storage/composition_store.hpp>
#include <storage/relation_store.hpp>
#include <storage/relation_evidence_store.hpp>
#include <thread>
#include <mutex>
#include <condition_variable>
#include <queue>
#include <memory>
#include <atomic>
#include <vector>
#include <iostream>

namespace Hartonomous {

/**
 * @brief Multi-worker background flusher for SubstrateBatches.
 *
 * Each worker maintains its own private database connection.
 * Multiple workers drain from a shared queue for parallel DB writes.
 * FK checks disabled via session_replication_role='replica' so batch
 * ordering across workers is safe.
 */
class AsyncFlusher {
public:
    explicit AsyncFlusher(size_t num_workers = 3) {
        workers_.reserve(num_workers);
        for (size_t i = 0; i < num_workers; ++i)
            workers_.emplace_back(&AsyncFlusher::worker, this);
    }

    ~AsyncFlusher() {
        {
            std::lock_guard<std::mutex> lock(mutex_);
            stop_ = true;
        }
        cv_.notify_all();
        for (auto& t : workers_)
            if (t.joinable()) t.join();
    }

    /**
     * @brief Enqueue a batch for background flushing.
     * Blocks if the queue is full (backpressure).
     */
    void enqueue(std::unique_ptr<SubstrateBatch> batch) {
        {
            std::unique_lock<std::mutex> lock(mutex_);
            cv_.wait(lock, [this] { return queue_.size() < 16 || stop_; });
            if (stop_) return;
            queue_.push(std::move(batch));
        }
        cv_.notify_all();
    }

    /**
     * @brief Block until all enqueued batches are fully flushed.
     */
    void wait_all() {
        std::unique_lock<std::mutex> lock(mutex_);
        cv_.wait(lock, [this] { return queue_.empty() && workers_busy_ == 0; });
    }

private:
    void worker() {
        PostgresConnection db;
        db.execute("SET synchronous_commit = off");
        db.execute("SET session_replication_role = 'replica'");

        while (true) {
            std::unique_ptr<SubstrateBatch> batch;
            {
                std::unique_lock<std::mutex> lock(mutex_);
                cv_.wait(lock, [this] { return !queue_.empty() || stop_; });
                if (stop_ && queue_.empty()) break;
                batch = std::move(queue_.front());
                queue_.pop();
                workers_busy_++;
            }
            cv_.notify_all();

            if (batch && !batch->empty()) {
                // Retry loop: RelationRatingStore ON CONFLICT upserts can deadlock
                // when multiple workers update the same relation_id simultaneously.
                // PostgreSQL aborts one side — we just retry with backoff.
                for (int attempt = 0; attempt < 4; ++attempt) {
                    try {
                        PostgresConnection::Transaction txn(db);
                        { PhysicalityStore s(db, false, true); for (auto& r : batch->phys) s.store(r); s.flush(); }
                        { CompositionStore s(db, false, true); for (auto& r : batch->comp) s.store(r); s.flush(); }
                        { CompositionSequenceStore s(db, false, true); for (auto& r : batch->seq) s.store(r); s.flush(); }
                        { RelationStore s(db, false, true); for (auto& r : batch->rel) s.store(r); s.flush(); }
                        { RelationSequenceStore s(db, false, true); for (auto& r : batch->rel_seq) s.store(r); s.flush(); }
                        { RelationRatingStore s(db, true); for (auto& r : batch->rating) s.store(r); s.flush(); }
                        { RelationEvidenceStore s(db, false, true); for (auto& r : batch->evidence) s.store(r); s.flush(); }
                        txn.commit();
                        break;  // Success
                    } catch (const std::exception& e) {
                        std::string err = e.what();
                        if (err.find("deadlock") != std::string::npos && attempt < 3) {
                            // Backoff: 20-70ms, 40-140ms, 80-280ms
                            int base_ms = 20 * (1 << attempt);
                            std::this_thread::sleep_for(std::chrono::milliseconds(
                                base_ms + (std::hash<std::thread::id>{}(std::this_thread::get_id()) % (base_ms * 2))));
                        } else {
                            std::cerr << "\n[ERROR] Async flush failed: " << err << std::endl;
                            break;
                        }
                    }
                }
            }

            {
                std::lock_guard<std::mutex> lock(mutex_);
                workers_busy_--;
            }
            cv_.notify_all();
        }
    }

    std::queue<std::unique_ptr<SubstrateBatch>> queue_;
    std::mutex mutex_;
    std::condition_variable cv_;
    std::vector<std::thread> workers_;
    std::atomic<bool> stop_{false};
    std::atomic<int> workers_busy_{0};
};

} // namespace Hartonomous
