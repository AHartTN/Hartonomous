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
#include "../atoms/composition_store.hpp"
#include <libpq-fe.h>
#include <vector>
#include <string>
#include <stdexcept>
#include <cstring>
#include <chrono>

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
    /// Seed ONLY byte atoms (0-255) - FAST for tests
    void seed_byte_atoms() {
        CopyBuilder data;
        data.reserve(256 * 100);
        
        for (uint32_t byte = 0; byte < 256; ++byte) {
            AtomId id = SemanticDecompose::get_atom_id(byte);
            auto coord = SemanticDecompose::get_coord(byte);
            
            data.field(id.high)
                .field(id.low)
                .field(static_cast<std::int64_t>(byte))
                .field(static_cast<std::int64_t>(0))
                .point_zm_int(coord.page, coord.type, coord.base, coord.variant)
                .end_row();
        }
        
        PgResult res(PQexec(conn_.get(), Schema::COPY_ATOMS));
        if (res.status() != PGRES_COPY_IN) {
            throw std::runtime_error("Failed to start COPY for byte atoms");
        }
        
        std::string str = data.str();
        if (PQputCopyData(conn_.get(), str.c_str(), static_cast<int>(str.size())) != 1) {
            throw std::runtime_error("Failed to put COPY data for byte atoms");
        }
        
        if (PQputCopyEnd(conn_.get(), nullptr) != 1) {
            throw std::runtime_error("Failed to end COPY for byte atoms");
        }
        
        PgResult end_res(PQgetResult(conn_.get()));
    }

    /// Seed Unicode atoms - IDEMPOTENT, PARALLEL
    /// Fast path: Multi-threaded generation + Multi-connection COPY
    /// Slow path: Staging + ON CONFLICT when partial
    /// Returns: (total_atoms, newly_inserted)
    std::pair<size_t, size_t> seed_unicode_atoms_idempotent() {
        auto start = std::chrono::steady_clock::now();

        constexpr size_t TOTAL_ATOMS = 1112064;  // Unicode - surrogates
        constexpr uint32_t MAX_CP = 0x10FFFF + 1;

        // Quick check: if all atoms exist, skip entirely
        size_t existing = get_atom_count();
        if (existing >= TOTAL_ATOMS) {
            if (!quiet_) hartonomous::log().info("All ", TOTAL_ATOMS, " atoms already exist. Skipping.");
            return {TOTAL_ATOMS, 0};
        }

        if (existing == 0) {
            // FAST PATH: Parallel generation (all cores) + Parallel COPY (limited connections)
            if (!quiet_) {
                hartonomous::log().info("Empty table - ", gen_threads_, " gen threads, ", copy_threads_, " COPY connections");
            }

            auto gen_start = std::chrono::steady_clock::now();

            // Generate with ALL available threads (CPU-bound)
            uint32_t gen_chunk_size = MAX_CP / static_cast<uint32_t>(gen_threads_);
            std::vector<std::string> gen_results(gen_threads_);

            Threading::parallel_for(gen_threads_, [&](size_t i) {
                uint32_t cp_start = static_cast<uint32_t>(i * gen_chunk_size);
                uint32_t cp_end = (i == gen_threads_ - 1) ? MAX_CP : static_cast<uint32_t>((i + 1) * gen_chunk_size);
                gen_results[i] = generate_chunk(cp_start, cp_end);
            });

            // Merge gen_results into copy_chunks (fewer, larger chunks for COPY)
            std::vector<std::string> copy_chunks(copy_threads_);
            size_t chunks_per_copy = (gen_threads_ + copy_threads_ - 1) / copy_threads_;
            for (size_t i = 0; i < gen_threads_; ++i) {
                size_t copy_idx = i / chunks_per_copy;
                if (copy_idx >= copy_threads_) copy_idx = copy_threads_ - 1;
                copy_chunks[copy_idx] += std::move(gen_results[i]);
            }

            auto gen_end = std::chrono::steady_clock::now();
            if (!quiet_) {
                hartonomous::log().info("Generated in ", std::chrono::duration<double>(gen_end - gen_start).count(), "s");
            }

            // Parallel COPY with limited connections (IO-bound)
            auto copy_start = std::chrono::steady_clock::now();
            std::vector<size_t> copy_results(copy_threads_, 0);

            // Filter to non-empty chunks, then parallel COPY
            std::vector<size_t> non_empty_indices;
            for (size_t i = 0; i < copy_chunks.size(); ++i) {
                if (!copy_chunks[i].empty()) {
                    non_empty_indices.push_back(i);
                }
            }

            Threading::parallel_for(non_empty_indices.size(), [&](size_t j) {
                size_t i = non_empty_indices[j];
                copy_results[i] = copy_chunk(connstr_, copy_chunks[i]);
            });

            size_t success_count = 0;
            for (size_t r : copy_results) success_count += r;

            auto copy_end = std::chrono::steady_clock::now();
            double copy_elapsed = std::chrono::duration<double>(copy_end - copy_start).count();

            if (!quiet_) {
                hartonomous::log().info("Parallel COPY in ", copy_elapsed, "s (", (TOTAL_ATOMS / copy_elapsed / 1000), "K atoms/s)");
            }

            if (success_count != non_empty_indices.size()) {
                throw std::runtime_error("Some COPY operations failed");
            }

            auto end = std::chrono::steady_clock::now();
            if (!quiet_) {
                hartonomous::log().info("Inserted ", TOTAL_ATOMS, " atoms. Total: ", std::chrono::duration<double>(end - start).count(), "s");
            }

            return {TOTAL_ATOMS, TOTAL_ATOMS};
        } else {
            // SLOW PATH: Partial data exists, use staging + ON CONFLICT
            if (!quiet_) {
                hartonomous::log().info("Found ", existing, "/", TOTAL_ATOMS, " atoms. Using staging table...");
            }

            // Generate all data (parallel with all cores)
            auto gen_start = std::chrono::steady_clock::now();
            uint32_t chunk_size = MAX_CP / static_cast<uint32_t>(gen_threads_);
            std::vector<std::string> gen_results(gen_threads_);

            Threading::parallel_for(gen_threads_, [&](size_t i) {
                uint32_t cp_start = static_cast<uint32_t>(i * chunk_size);
                uint32_t cp_end = (i == gen_threads_ - 1) ? MAX_CP : static_cast<uint32_t>((i + 1) * chunk_size);
                gen_results[i] = generate_chunk(cp_start, cp_end);
            });

            auto gen_end = std::chrono::steady_clock::now();
            if (!quiet_) {
                hartonomous::log().info("Generated in ", std::chrono::duration<double>(gen_end - gen_start).count(), "s");
            }

            // Create unlogged staging table
            exec("DROP TABLE IF EXISTS atoms_staging");
            exec(Schema::CREATE_ATOMS_STAGING);

            // COPY to staging - stream each chunk directly (Issue 11 fix: no concatenation)
            auto copy_start = std::chrono::steady_clock::now();
            start_copy(Schema::COPY_ATOMS_STAGING);

            for (auto& chunk : gen_results) {
                if (!chunk.empty()) {
                    if (PQputCopyData(conn_.get(), chunk.c_str(), static_cast<int>(chunk.size())) != 1) {
                        throw std::runtime_error("COPY data failed: " + std::string(PQerrorMessage(conn_.get())));
                    }
                }
                chunk.clear();  // Release memory immediately
                chunk.shrink_to_fit();
            }
            if (PQputCopyEnd(conn_.get(), nullptr) != 1) {
                throw std::runtime_error("COPY end failed: " + std::string(PQerrorMessage(conn_.get())));
            }

            PgResult res(PQgetResult(conn_.get()));
            if (res.status() != PGRES_COMMAND_OK) {
                throw std::runtime_error("COPY staging failed: " + std::string(PQerrorMessage(conn_.get())));
            }

            auto copy_end = std::chrono::steady_clock::now();
            if (!quiet_) {
                hartonomous::log().info("COPY to staging in ", std::chrono::duration<double>(copy_end - copy_start).count(), "s");
            }

            // INSERT from staging with ON CONFLICT DO NOTHING
            auto insert_start = std::chrono::steady_clock::now();
            PgResult insert_res(PQexec(conn_.get(), Schema::INSERT_ATOMS_FROM_STAGING));
            insert_res.expect_ok("INSERT_ATOMS_FROM_STAGING");
            size_t inserted = insert_res.affected_rows();

            // Cleanup staging
            exec("DROP TABLE atoms_staging");

            auto end = std::chrono::steady_clock::now();
            if (!quiet_) {
                hartonomous::log().info("INSERT ON CONFLICT in ", std::chrono::duration<double>(end - insert_start).count(), "s");
                hartonomous::log().info("Inserted ", inserted, " new atoms. Total: ", std::chrono::duration<double>(end - start).count(), "s");
            }

            return {TOTAL_ATOMS, inserted};
        }
    }

    /// Seed compositions from a CompositionStore - IDEMPOTENT
    size_t seed_compositions(const CompositionStore& store) {
        auto start = std::chrono::steady_clock::now();

        const auto& all_comps = store.all_compositions();
        if (all_comps.empty()) return 0;

        // Create temp tables
        exec("DROP TABLE IF EXISTS comps_staging");
        exec("DROP TABLE IF EXISTS struct_staging");
        exec(Schema::CREATE_COMPS_STAGING);
        exec(Schema::CREATE_STRUCT_STAGING);

        // COPY compositions to staging
        {
            CopyBuilder data;
            for (const auto& [pair, ref] : all_comps) {
                data.field(ref.id_high)
                    .field(ref.id_low)
                    .null()
                    .field(2)
                    .null()
                    .end_row();
            }

            start_copy(Schema::COPY_COMPS_STAGING);
            std::string str = data.str();
            if (PQputCopyData(conn_.get(), str.c_str(), static_cast<int>(str.size())) != 1) {
                throw std::runtime_error("COPY comps failed");
            }
            if (PQputCopyEnd(conn_.get(), nullptr) != 1) {
                throw std::runtime_error("COPY end failed");
            }
            PgResult res(PQgetResult(conn_.get()));
            if (res.status() != PGRES_COMMAND_OK) {
                throw std::runtime_error("COPY comps staging failed");
            }
        }

        // COPY composition_relation to staging
        {
            CopyBuilder data;
            for (const auto& [pair, ref] : all_comps) {
                data.field(ref.id_high)
                    .field(ref.id_low)
                    .field(0)
                    .field(pair.first.id_high)
                    .field(pair.first.id_low)
                    .field(1)
                    .end_row();

                data.field(ref.id_high)
                    .field(ref.id_low)
                    .field(1)
                    .field(pair.second.id_high)
                    .field(pair.second.id_low)
                    .field(1)
                    .end_row();
            }

            start_copy(Schema::COPY_STRUCT_STAGING);
            std::string str = data.str();
            if (PQputCopyData(conn_.get(), str.c_str(), static_cast<int>(str.size())) != 1) {
                throw std::runtime_error("COPY composition_relation failed");
            }
            if (PQputCopyEnd(conn_.get(), nullptr) != 1) {
                throw std::runtime_error("COPY end failed");
            }
            PgResult res(PQgetResult(conn_.get()));
            if (res.status() != PGRES_COMMAND_OK) {
                throw std::runtime_error("COPY composition_relation staging failed");
            }
        }

        // INSERT with ON CONFLICT DO NOTHING
        PgResult res(PQexec(conn_.get(), Schema::INSERT_COMPS_FROM_STAGING));
        res.expect_ok("INSERT_COMPS_FROM_STAGING");
        size_t inserted = res.affected_rows();

        PgResult struct_res(PQexec(conn_.get(), Schema::INSERT_STRUCT_FROM_STAGING));
        struct_res.expect_ok("INSERT_STRUCT_FROM_STAGING");

        // Cleanup
        exec("DROP TABLE IF EXISTS comps_staging");
        exec("DROP TABLE IF EXISTS struct_staging");

        auto end = std::chrono::steady_clock::now();
        double elapsed = std::chrono::duration<double>(end - start).count();

        if (!quiet_) {
            hartonomous::log().info("Inserted ", inserted, " new compositions in ", elapsed, "s");
        }

        return inserted;
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
