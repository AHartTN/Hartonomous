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
#include <iostream>

namespace Hartonomous {

/**
 * @brief High-performance background thread for flushing SubstrateBatches to the database.
 * 
 * Each flusher maintains its own private database connection to prevent collisions.
 */
class AsyncFlusher {
public:
    AsyncFlusher() {
        thread_ = std::thread(&AsyncFlusher::worker, this);
    }

    ~AsyncFlusher() {
        {
            std::lock_guard<std::mutex> lock(mutex_);
            stop_ = true;
        }
        cv_.notify_one();
        if (thread_.joinable()) thread_.join();
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
        cv_.notify_one();
    }

    /**
     * @brief Block until all enqueued batches are fully flushed.
     */
    void wait_all() {
        std::unique_lock<std::mutex> lock(mutex_);
        cv_.wait(lock, [this] { return queue_.empty() && !worker_busy_; });
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
                worker_busy_ = true;
            }
            cv_.notify_one();

            if (batch && !batch->empty()) {
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
                } catch (const std::exception& e) {
                    std::cerr << "\n[ERROR] Async flush failed: " << e.what() << std::endl;
                }
            }

            {
                std::lock_guard<std::mutex> lock(mutex_);
                worker_busy_ = false;
            }
            cv_.notify_one();
        }
    }

    std::queue<std::unique_ptr<SubstrateBatch>> queue_;
    std::mutex mutex_;
    std::condition_variable cv_;
    std::thread thread_;
    std::atomic<bool> stop_{false};
    std::atomic<bool> worker_busy_{false};
};

} // namespace Hartonomous
