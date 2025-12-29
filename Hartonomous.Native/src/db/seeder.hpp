#pragma once

#include "connection.hpp"
#include "schema.hpp"
#include "schema_manager.hpp"
#include "pg_result.hpp"
#include "copy_builder.hpp"
#include "../threading/threading.hpp"
#include "../logging/logger.hpp"
#include "../atoms/semantic_decompose.hpp"
#include "../atoms/atom_id.hpp"
#include <libpq-fe.h>
#include <vector>
#include <string>
#include <stdexcept>
#include <cstring>
#include <chrono>
#include <atomic>

namespace hartonomous::db {

using hartonomous::Threading;

/// High-performance IDEMPOTENT database seeder.
/// - Uses SchemaManager for proper validation/repair (no suppression)
/// - Pre-computes all IDs (deterministic)
/// - Parallel COPY with multiple connections (auto-detected thread count)
/// - Single query to find existing IDs
/// - Only inserts what's missing
class Seeder {
    std::string connstr_;  // Cached connection string (Issue 12 fix)
    PgConnection conn_;
    bool quiet_ = false;
    size_t gen_threads_;   // For CPU-bound generation (use all cores)
    size_t copy_threads_;  // For IO-bound COPY (limited by Postgres)
    SchemaStatus last_schema_status_;  // Last schema validation result

public:
    explicit Seeder(bool quiet = false)
        : connstr_(ConnectionConfig::connection_string())
        , conn_(connstr_)
        , quiet_(quiet)
    {
        auto [gen, copy] = Threading::detect_thread_split();
        gen_threads_ = gen;
        copy_threads_ = copy;
    }

    ~Seeder() = default;

    Seeder(const Seeder&) = delete;
    Seeder& operator=(const Seeder&) = delete;

    /// Ensure schema AND atoms exist (truly idempotent - validates and repairs)
    /// This is the ONLY method you need to call - schema + 1.1M atoms guaranteed.
    /// Returns the schema status for inspection.
    SchemaStatus ensure_schema() {
        // Use SchemaManager for proper validation and repair
        SchemaManager mgr(connstr_);
        last_schema_status_ = mgr.ensure_schema();
        
        if (last_schema_status_.has_errors()) {
            std::string err = "Schema validation failed: ";
            for (const auto& e : last_schema_status_.errors) {
                err += e + "; ";
            }
            throw std::runtime_error(err);
        }
        
        if (!quiet_) {
            hartonomous::log().info("Schema: ", last_schema_status_.summary());
            for (const auto& action : last_schema_status_.actions_taken) {
                hartonomous::log().info("  - ", action);
            }
        }

        // Always ensure atoms exist - idempotent, automatic
        auto [total, inserted] = seed_unicode_atoms_idempotent();

        // ANALYZE for optimal query plans (only if we inserted new atoms)
        if (inserted > 0) {
            exec("ANALYZE atom");
            if (!quiet_) hartonomous::log().info("ANALYZE complete.");
        }
        
        return last_schema_status_;
    }
    
    /// Get the last schema status (for inspection after ensure_schema)
    [[nodiscard]] const SchemaStatus& schema_status() const { return last_schema_status_; }

    /// Schema-only for special cases (prefer ensure_schema())
    SchemaStatus ensure_schema_only() {
        SchemaManager mgr(connstr_);
        last_schema_status_ = mgr.ensure_schema();
        
        if (!quiet_) {
            hartonomous::log().info("Schema: ", last_schema_status_.summary());
        }
        
        return last_schema_status_;
    }

    /// Get count of existing atoms
    size_t get_atom_count() {
        PgResult res(PQexec(conn_.get(), "SELECT COUNT(*) FROM atom WHERE codepoint IS NOT NULL"));
        if (res.status() != PGRES_TUPLES_OK) {
            return 0;
        }
        return std::stoull(res.get_value(0, 0));
    }

private:
    /// Generate atoms for a codepoint range into a string buffer
    static std::string generate_chunk(uint32_t start_cp, uint32_t end_cp) {
        CopyBuilder data;
        data.reserve(100 * (end_cp - start_cp));

        for (uint32_t cp = start_cp; cp < end_cp; ++cp) {
            if (cp >= 0xD800 && cp <= 0xDFFF) continue;

            AtomId id = SemanticDecompose::get_atom_id(cp);
            auto coord = SemanticDecompose::get_coord(cp);

            data.field(id.high)
                .field(id.low)
                .field(static_cast<std::int64_t>(cp))
                .field(static_cast<std::int64_t>(0))
                .point_zm_int(coord.page, coord.type, coord.base, coord.variant)
                .end_row();
        }
        return data.str();
    }

    /// COPY a chunk using its own connection
    static size_t copy_chunk(const std::string& connstr, const std::string& data) {
        try {
            PgConnection conn(connstr);

            PgResult res(PQexec(conn.get(), Schema::COPY_ATOMS));
            if (res.status() != PGRES_COPY_IN) {
                return 0;
            }

            if (PQputCopyData(conn.get(), data.c_str(), static_cast<int>(data.size())) != 1) {
                return 0;
            }
            if (PQputCopyEnd(conn.get(), nullptr) != 1) {
                return 0;
            }

            PgResult end_res(PQgetResult(conn.get()));
            return (end_res.status() == PGRES_COMMAND_OK) ? 1 : 0;
        } catch (const PgError&) {
            return 0;
        }
    }

public:
    /// Seed Unicode atoms - DETERMINISTIC, PARALLEL, INSTANT
    /// All 1,112,064 atoms have deterministic IDs from Hilbert curve encoding.
    /// No staging tables, no slow paths - just parallel generation and parallel COPY.
    /// Returns: (total_atoms, newly_inserted)
    std::pair<size_t, size_t> seed_unicode_atoms_idempotent() {
        constexpr size_t TOTAL_ATOMS = 1112064;  // Unicode - surrogates
        constexpr uint32_t MAX_CP = 0x10FFFF + 1;

        // Check if already seeded (instant - single COUNT query)
        size_t existing = get_atom_count();
        if (existing >= TOTAL_ATOMS) {
            return {TOTAL_ATOMS, 0};
        }

        auto start = std::chrono::steady_clock::now();

        // PARALLEL GENERATION - use all cores, deterministic output
        uint32_t gen_chunk_size = MAX_CP / static_cast<uint32_t>(gen_threads_);
        std::vector<std::string> gen_results(gen_threads_);

        Threading::parallel_for(gen_threads_, [&](size_t i) {
            uint32_t cp_start = static_cast<uint32_t>(i * gen_chunk_size);
            uint32_t cp_end = (i == gen_threads_ - 1) ? MAX_CP : static_cast<uint32_t>((i + 1) * gen_chunk_size);
            gen_results[i] = generate_chunk(cp_start, cp_end);
        });

        // Merge into COPY chunks (fewer connections than gen threads)
        std::vector<std::string> copy_chunks(copy_threads_);
        size_t chunks_per_copy = (gen_threads_ + copy_threads_ - 1) / copy_threads_;
        for (size_t i = 0; i < gen_threads_; ++i) {
            size_t copy_idx = std::min(i / chunks_per_copy, copy_threads_ - 1);
            copy_chunks[copy_idx] += std::move(gen_results[i]);
        }

        // PARALLEL COPY - multiple connections, ON CONFLICT handled by PK
        std::atomic<size_t> total_success{0};
        Threading::parallel_for(copy_threads_, [&](size_t i) {
            if (copy_chunks[i].empty()) return;
            total_success += copy_chunk(connstr_, copy_chunks[i]);
        });

        auto end = std::chrono::steady_clock::now();
        double elapsed = std::chrono::duration<double>(end - start).count();

        if (!quiet_) {
            size_t inserted = TOTAL_ATOMS - existing;
            hartonomous::log().info("Seeded ", inserted, " atoms in ", elapsed, "s (",
                static_cast<size_t>(TOTAL_ATOMS / elapsed / 1000), "K/s)");
        }

        return {TOTAL_ATOMS, TOTAL_ATOMS - existing};
    }

    void begin() { exec("BEGIN"); }
    void commit() { exec("COMMIT"); }
    void rollback() { exec("ROLLBACK"); }

private:
    void exec(const char* sql) {
        PgResult res(PQexec(conn_.get(), sql));
        ExecStatusType status = res.status();
        if (status != PGRES_COMMAND_OK && status != PGRES_TUPLES_OK) {
            throw std::runtime_error("SQL error: " + std::string(PQerrorMessage(conn_.get())));
        }
    }

    void start_copy(const char* sql) {
        PgResult res(PQexec(conn_.get(), sql));
        if (res.status() != PGRES_COPY_IN) {
            throw std::runtime_error("COPY start failed: " + std::string(PQerrorMessage(conn_.get())));
        }
    }
};

} // namespace hartonomous::db
