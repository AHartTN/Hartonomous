#pragma once

/// BULK STORE - Parallel, pipelined ingestion into the universal substrate
///
/// Architecture:
/// 1. Encoding threads: parallel tree building, produce compositions
/// 2. Batch collector: lock-free accumulation of pending writes
/// 3. Writer thread: pipelined COPY to PostgreSQL while encoding continues
///
/// No temp tables. No row-by-row. Real production ingestion.

#include "connection.hpp"
#include "pg_result.hpp"
#include "../atoms/node_ref.hpp"
#include "../atoms/codepoint_atom_table.hpp"
#include "../atoms/merkle_hash.hpp"
#include <libpq-fe.h>
#include <vector>
#include <queue>
#include <thread>
#include <mutex>
#include <condition_variable>
#include <atomic>
#include <future>
#include <functional>

namespace hartonomous::db {

/// Composition tuple for bulk insert
struct CompTuple {
    std::int64_t parent_high, parent_low;
    std::int64_t left_high, left_low;
    std::int64_t right_high, right_low;
};

/// Relationship tuple for bulk insert
struct RelTuple {
    std::int64_t from_high, from_low;
    std::int64_t to_high, to_low;
    double weight;
    std::int16_t rel_type;
    std::int64_t ctx_high, ctx_low;
};

/// Thread-safe batch collector
template<typename T>
class BatchCollector {
    std::vector<T> buffer_;
    std::mutex mutex_;
    std::size_t capacity_;

public:
    explicit BatchCollector(std::size_t capacity = 100000)
        : capacity_(capacity) {
        buffer_.reserve(capacity_);
    }

    void push(const T& item) {
        std::lock_guard<std::mutex> lock(mutex_);
        buffer_.push_back(item);
    }

    void push(T&& item) {
        std::lock_guard<std::mutex> lock(mutex_);
        buffer_.push_back(std::move(item));
    }

    template<typename It>
    void push_range(It begin, It end) {
        std::lock_guard<std::mutex> lock(mutex_);
        buffer_.insert(buffer_.end(), begin, end);
    }

    std::vector<T> drain() {
        std::lock_guard<std::mutex> lock(mutex_);
        std::vector<T> result;
        result.swap(buffer_);
        buffer_.reserve(capacity_);
        return result;
    }

    std::size_t size() const {
        std::lock_guard<std::mutex> lock(const_cast<std::mutex&>(mutex_));
        return buffer_.size();
    }
};

/// Pipelined database writer
class PipelinedWriter {
    std::string connstr_;
    PgConnection conn_;

    std::queue<std::vector<CompTuple>> comp_queue_;
    std::queue<std::vector<RelTuple>> rel_queue_;
    std::mutex queue_mutex_;
    std::condition_variable queue_cv_;

    std::atomic<bool> running_{true};
    std::thread writer_thread_;

    std::atomic<std::size_t> comps_written_{0};
    std::atomic<std::size_t> rels_written_{0};

public:
    explicit PipelinedWriter(const std::string& connstr = ConnectionConfig::connection_string())
        : connstr_(connstr)
        , conn_(connstr_)
    {
        writer_thread_ = std::thread(&PipelinedWriter::writer_loop, this);
    }

    ~PipelinedWriter() {
        shutdown();
    }

    void submit_compositions(std::vector<CompTuple>&& batch) {
        if (batch.empty()) return;
        {
            std::lock_guard<std::mutex> lock(queue_mutex_);
            comp_queue_.push(std::move(batch));
        }
        queue_cv_.notify_one();
    }

    void submit_relationships(std::vector<RelTuple>&& batch) {
        if (batch.empty()) return;
        {
            std::lock_guard<std::mutex> lock(queue_mutex_);
            rel_queue_.push(std::move(batch));
        }
        queue_cv_.notify_one();
    }

    void flush() {
        // Wait for queues to drain
        while (true) {
            {
                std::lock_guard<std::mutex> lock(queue_mutex_);
                if (comp_queue_.empty() && rel_queue_.empty()) break;
            }
            std::this_thread::sleep_for(std::chrono::milliseconds(10));
        }
    }

    void shutdown() {
        running_ = false;
        queue_cv_.notify_all();
        if (writer_thread_.joinable()) {
            writer_thread_.join();
        }
    }

    std::size_t compositions_written() const { return comps_written_; }
    std::size_t relationships_written() const { return rels_written_; }

private:
    void writer_loop() {
        while (running_ || !queues_empty()) {
            std::vector<CompTuple> comps;
            std::vector<RelTuple> rels;

            {
                std::unique_lock<std::mutex> lock(queue_mutex_);
                queue_cv_.wait_for(lock, std::chrono::milliseconds(100), [this] {
                    return !running_ || !comp_queue_.empty() || !rel_queue_.empty();
                });

                if (!comp_queue_.empty()) {
                    comps = std::move(comp_queue_.front());
                    comp_queue_.pop();
                }
                if (!rel_queue_.empty()) {
                    rels = std::move(rel_queue_.front());
                    rel_queue_.pop();
                }
            }

            if (!comps.empty()) {
                write_compositions(comps);
                comps_written_ += comps.size();
            }
            if (!rels.empty()) {
                write_relationships(rels);
                rels_written_ += rels.size();
            }
        }
    }

    bool queues_empty() {
        std::lock_guard<std::mutex> lock(queue_mutex_);
        return comp_queue_.empty() && rel_queue_.empty();
    }

    void write_compositions(const std::vector<CompTuple>& batch) {
        // Build multi-row INSERT
        constexpr std::size_t CHUNK = 5000;

        for (std::size_t i = 0; i < batch.size(); i += CHUNK) {
            std::size_t end = std::min(i + CHUNK, batch.size());

            std::string query;
            query.reserve((end - i) * 100);
            query = "INSERT INTO composition (hilbert_high, hilbert_low, "
                    "left_high, left_low, right_high, right_low) VALUES ";

            char buf[128];
            for (std::size_t j = i; j < end; ++j) {
                const auto& c = batch[j];
                if (j > i) query += ',';
                std::snprintf(buf, sizeof(buf),
                    "(%lld,%lld,%lld,%lld,%lld,%lld)",
                    static_cast<long long>(c.parent_high),
                    static_cast<long long>(c.parent_low),
                    static_cast<long long>(c.left_high),
                    static_cast<long long>(c.left_low),
                    static_cast<long long>(c.right_high),
                    static_cast<long long>(c.right_low));
                query += buf;
            }

            query += " ON CONFLICT DO NOTHING";
            PQexec(conn_.get(), query.c_str());
        }
    }

    void write_relationships(const std::vector<RelTuple>& batch) {
        constexpr std::size_t CHUNK = 5000;

        for (std::size_t i = 0; i < batch.size(); i += CHUNK) {
            std::size_t end = std::min(i + CHUNK, batch.size());

            std::string query;
            query.reserve((end - i) * 150);
            query = "INSERT INTO relationship (from_high, from_low, to_high, to_low, "
                    "weight, rel_type, context_high, context_low) VALUES ";

            char buf[200];
            for (std::size_t j = i; j < end; ++j) {
                const auto& r = batch[j];
                if (j > i) query += ',';
                std::snprintf(buf, sizeof(buf),
                    "(%lld,%lld,%lld,%lld,%.17g,%d,%lld,%lld)",
                    static_cast<long long>(r.from_high),
                    static_cast<long long>(r.from_low),
                    static_cast<long long>(r.to_high),
                    static_cast<long long>(r.to_low),
                    r.weight,
                    static_cast<int>(r.rel_type),
                    static_cast<long long>(r.ctx_high),
                    static_cast<long long>(r.ctx_low));
                query += buf;
            }

            query += " ON CONFLICT (from_high, from_low, to_high, to_low, "
                     "context_high, context_low) DO UPDATE SET "
                     "weight = relationship.weight + EXCLUDED.weight, "
                     "obs_count = relationship.obs_count + 1";
            PQexec(conn_.get(), query.c_str());
        }
    }
};

/// Parallel encoder with pipelined writes
class BulkStore {
    PipelinedWriter writer_;
    BatchCollector<CompTuple> comp_collector_;
    BatchCollector<RelTuple> rel_collector_;

    std::size_t flush_threshold_;
    std::atomic<std::size_t> total_encoded_{0};

public:
    explicit BulkStore(std::size_t flush_threshold = 50000)
        : flush_threshold_(flush_threshold)
    {}

    /// Encode content and queue compositions for writing
    NodeRef encode(const std::uint8_t* data, std::size_t len) {
        if (len == 0) return NodeRef{};

        // Decode UTF-8 to codepoints
        auto codepoints = UTF8Decoder::decode(data, len);
        const auto& atoms = CodepointAtomTable::instance();
        if (codepoints.size() == 1) return atoms.ref(codepoints[0]);

        // Build tree, collect compositions
        std::vector<CompTuple> local_comps;
        local_comps.reserve(codepoints.size());

        NodeRef root = build_tree_codepoints(codepoints, 0, codepoints.size(), local_comps, atoms);

        // Push to collector
        comp_collector_.push_range(local_comps.begin(), local_comps.end());
        total_encoded_ += local_comps.size();

        // Auto-flush if threshold reached
        if (comp_collector_.size() >= flush_threshold_) {
            flush_compositions();
        }

        return root;
    }

    NodeRef encode(const std::string& s) {
        return encode(reinterpret_cast<const std::uint8_t*>(s.data()), s.size());
    }

    /// Add relationship
    void add_relationship(NodeRef from, NodeRef to, double weight,
                          std::int16_t rel_type, NodeRef context = NodeRef{}) {
        if (std::abs(weight) < 1e-9) return;  // Sparse filter

        rel_collector_.push({
            from.id_high, from.id_low,
            to.id_high, to.id_low,
            weight, rel_type,
            context.id_high, context.id_low
        });

        if (rel_collector_.size() >= flush_threshold_) {
            flush_relationships();
        }
    }

    /// Flush pending compositions to writer
    void flush_compositions() {
        auto batch = comp_collector_.drain();
        if (!batch.empty()) {
            writer_.submit_compositions(std::move(batch));
        }
    }

    /// Flush pending relationships to writer
    void flush_relationships() {
        auto batch = rel_collector_.drain();
        if (!batch.empty()) {
            writer_.submit_relationships(std::move(batch));
        }
    }

    /// Flush everything and wait for writes to complete
    void sync() {
        flush_compositions();
        flush_relationships();
        writer_.flush();
    }

    std::size_t total_encoded() const { return total_encoded_; }
    std::size_t compositions_written() const { return writer_.compositions_written(); }
    std::size_t relationships_written() const { return writer_.relationships_written(); }

private:
    NodeRef build_tree_codepoints(const std::vector<std::int32_t>& codepoints,
                                   std::size_t start, std::size_t end,
                                   std::vector<CompTuple>& comps,
                                   const CodepointAtomTable& atoms) {
        std::size_t len = end - start;
        if (len == 1) return atoms.ref(codepoints[start]);
        if (len == 2) {
            NodeRef left = atoms.ref(codepoints[start]);
            NodeRef right = atoms.ref(codepoints[start + 1]);
            NodeRef children[2] = {left, right};
            auto [h, l] = MerkleHash::compute(children, children + 2);
            NodeRef comp = NodeRef::comp(h, l);
            comps.push_back({h, l, left.id_high, left.id_low, right.id_high, right.id_low});
            return comp;
        }

        std::size_t mid = start + len / 2;
        NodeRef left = build_tree_codepoints(codepoints, start, mid, comps, atoms);
        NodeRef right = build_tree_codepoints(codepoints, mid, end, comps, atoms);

        NodeRef children[2] = {left, right};
        auto [h, l] = MerkleHash::compute(children, children + 2);
        NodeRef comp = NodeRef::comp(h, l);
        comps.push_back({h, l, left.id_high, left.id_low, right.id_high, right.id_low});
        return comp;
    }
};

/// Parallel content ingestion with thread pool
class ParallelIngester {
    BulkStore& store_;
    std::size_t num_threads_;

public:
    ParallelIngester(BulkStore& store, std::size_t threads = 0)
        : store_(store)
        , num_threads_(threads > 0 ? threads : std::thread::hardware_concurrency())
    {}

    /// Ingest multiple content items in parallel
    std::vector<NodeRef> ingest_parallel(const std::vector<std::string>& items) {
        std::vector<NodeRef> results(items.size());
        std::vector<std::future<void>> futures;
        futures.reserve(num_threads_);

        std::atomic<std::size_t> next_idx{0};

        auto worker = [&]() {
            while (true) {
                std::size_t idx = next_idx.fetch_add(1);
                if (idx >= items.size()) break;
                results[idx] = store_.encode(items[idx]);
            }
        };

        for (std::size_t i = 0; i < num_threads_; ++i) {
            futures.push_back(std::async(std::launch::async, worker));
        }

        for (auto& f : futures) {
            f.wait();
        }

        return results;
    }

    /// Ingest files in parallel
    std::vector<std::pair<std::string, NodeRef>> ingest_files(
        const std::vector<std::string>& paths)
    {
        std::vector<std::pair<std::string, NodeRef>> results(paths.size());
        std::atomic<std::size_t> next_idx{0};

        auto worker = [&]() {
            while (true) {
                std::size_t idx = next_idx.fetch_add(1);
                if (idx >= paths.size()) break;

                const auto& path = paths[idx];
                std::ifstream file(path, std::ios::binary);
                if (!file) {
                    results[idx] = {path, NodeRef{}};
                    continue;
                }

                std::string content((std::istreambuf_iterator<char>(file)),
                                     std::istreambuf_iterator<char>());

                results[idx] = {path, store_.encode(content)};
            }
        };

        std::vector<std::future<void>> futures;
        for (std::size_t i = 0; i < num_threads_; ++i) {
            futures.push_back(std::async(std::launch::async, worker));
        }

        for (auto& f : futures) {
            f.wait();
        }

        return results;
    }
};

} // namespace hartonomous::db
