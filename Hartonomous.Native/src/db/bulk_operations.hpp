#pragma once

/// BULK OPERATIONS - High-throughput data loading operations.
///
/// Provides:
/// - Binary COPY for model weights (TB-scale)
/// - Batched embedding trajectory storage
/// - Staging table workflows
///
/// Extracted from QueryStore for separation of concerns.

#include "connection.hpp"
#include "pg_result.hpp"
#include "types.hpp"
#include "../atoms/node_ref.hpp"
#include <libpq-fe.h>
#include <string>
#include <vector>
#include <tuple>
#include <unordered_set>
#include <chrono>
#include <iostream>
#include <cstring>
#include <cmath>

namespace hartonomous::db {

/// Bulk operations for high-throughput data loading.
class BulkOperations {
    PgConnection& conn_;

public:
    explicit BulkOperations(PgConnection& conn) : conn_(conn) {}

    /// Bulk store model weights - DIRECT binary COPY, no staging tables.
    /// For maximum throughput: drop indexes before, rebuild after for TB-scale loads.
    void store_model_weights(
        const std::vector<std::tuple<NodeRef, NodeRef, double>>& weights,
        NodeRef model_context,
        RelType type = REL_DEFAULT)
    {
        if (weights.empty()) return;

        auto start_time = std::chrono::high_resolution_clock::now();
        std::size_t total = weights.size();

        // Direct binary COPY to relationship table
        PGresult* res = PQexec(conn_.get(),
            "COPY relationship (from_high, from_low, to_high, to_low, "
            "weight, obs_count, rel_type, context_high, context_low) "
            "FROM STDIN WITH (FORMAT binary)");
        
        if (PQresultStatus(res) != PGRES_COPY_IN) {
            PQclear(res);
            return;
        }
        PQclear(res);

        // Build binary buffer - single allocation
        static const char COPY_HEADER[] = "PGCOPY\n\377\r\n\0";
        std::vector<char> buffer;
        buffer.reserve(total * 90 + 32);

        buffer.insert(buffer.end(), COPY_HEADER, COPY_HEADER + 11);
        buffer.push_back(0); buffer.push_back(0); buffer.push_back(0); buffer.push_back(0);
        buffer.push_back(0); buffer.push_back(0); buffer.push_back(0); buffer.push_back(0);

        auto append_int16 = [&](std::int16_t v) {
            buffer.push_back(static_cast<char>((v >> 8) & 0xFF));
            buffer.push_back(static_cast<char>(v & 0xFF));
        };

        auto append_int32 = [&](std::int32_t v) {
            buffer.push_back(static_cast<char>((v >> 24) & 0xFF));
            buffer.push_back(static_cast<char>((v >> 16) & 0xFF));
            buffer.push_back(static_cast<char>((v >> 8) & 0xFF));
            buffer.push_back(static_cast<char>(v & 0xFF));
        };

        auto append_int64 = [&](std::int64_t v) {
            buffer.push_back(static_cast<char>((v >> 56) & 0xFF));
            buffer.push_back(static_cast<char>((v >> 48) & 0xFF));
            buffer.push_back(static_cast<char>((v >> 40) & 0xFF));
            buffer.push_back(static_cast<char>((v >> 32) & 0xFF));
            buffer.push_back(static_cast<char>((v >> 24) & 0xFF));
            buffer.push_back(static_cast<char>((v >> 16) & 0xFF));
            buffer.push_back(static_cast<char>((v >> 8) & 0xFF));
            buffer.push_back(static_cast<char>(v & 0xFF));
        };

        auto append_float64 = [&](double v) {
            std::uint64_t bits;
            std::memcpy(&bits, &v, sizeof(bits));
            buffer.push_back(static_cast<char>((bits >> 56) & 0xFF));
            buffer.push_back(static_cast<char>((bits >> 48) & 0xFF));
            buffer.push_back(static_cast<char>((bits >> 40) & 0xFF));
            buffer.push_back(static_cast<char>((bits >> 32) & 0xFF));
            buffer.push_back(static_cast<char>((bits >> 24) & 0xFF));
            buffer.push_back(static_cast<char>((bits >> 16) & 0xFF));
            buffer.push_back(static_cast<char>((bits >> 8) & 0xFF));
            buffer.push_back(static_cast<char>(bits & 0xFF));
        };

        for (const auto& [from, to, weight] : weights) {
            append_int16(9);  // 9 columns
            append_int32(8); append_int64(from.id_high);
            append_int32(8); append_int64(from.id_low);
            append_int32(8); append_int64(to.id_high);
            append_int32(8); append_int64(to.id_low);
            append_int32(8); append_float64(weight);
            append_int32(4); append_int32(1);  // obs_count = 1
            append_int32(2); append_int16(type);
            append_int32(8); append_int64(model_context.id_high);
            append_int32(8); append_int64(model_context.id_low);
        }
        append_int16(-1);  // Trailer

        // Stream entire buffer
        if (PQputCopyData(conn_.get(), buffer.data(), static_cast<int>(buffer.size())) != 1) {
            PQputCopyEnd(conn_.get(), "error");
            return;
        }

        if (PQputCopyEnd(conn_.get(), nullptr) != 1) {
            return;
        }

        res = PQgetResult(conn_.get());
        PQclear(res);

        auto end_time = std::chrono::high_resolution_clock::now();
        auto total_ms = std::chrono::duration_cast<std::chrono::milliseconds>(end_time - start_time).count();
        double rate = total_ms > 0 ? (static_cast<double>(total) / total_ms * 1000.0) : 0;

        std::cerr << "store_model_weights: " << total << " rows in " << total_ms << "ms ("
                  << static_cast<std::size_t>(rate) << " rows/sec)" << std::endl;
    }

    /// Bulk store embedding trajectories - direct batched INSERT.
    void store_embedding_trajectories(
        const float* embeddings,
        std::size_t vocab_size,
        std::size_t hidden_dim,
        const std::vector<NodeRef>& token_refs,
        NodeRef model_context,
        RelType type = REL_DEFAULT)
    {
        if (vocab_size == 0 || hidden_dim == 0 || token_refs.empty()) return;

        std::size_t effective_size = std::min(vocab_size, token_refs.size());
        std::cerr << "store_embedding_trajectories: " << effective_size << " embeddings, " << hidden_dim << " dims" << std::endl;

        auto start_time = std::chrono::high_resolution_clock::now();

        // Process in batches of 100 (trajectories are large)
        constexpr std::size_t BATCH_SIZE = 100;
        std::size_t stored = 0;

        for (std::size_t batch_start = 0; batch_start < effective_size; batch_start += BATCH_SIZE) {
            std::size_t batch_end = std::min(batch_start + BATCH_SIZE, effective_size);

            std::string sql = 
                "INSERT INTO relationship (from_high, from_low, to_high, to_low, "
                "weight, obs_count, rel_type, trajectory, context_high, context_low) VALUES ";

            bool first = true;
            for (std::size_t i = batch_start; i < batch_end; ++i) {
                const float* embedding = embeddings + i * hidden_dim;
                const NodeRef& token = token_refs[i];

                // Calculate magnitude as weight
                double mag = 0.0;
                for (std::size_t d = 0; d < hidden_dim; ++d) {
                    mag += static_cast<double>(embedding[d]) * static_cast<double>(embedding[d]);
                }
                mag = std::sqrt(mag);

                // Build LineStringZM WKT
                std::string wkt = "LINESTRINGZM(";
                for (std::size_t d = 0; d < hidden_dim; ++d) {
                    if (d > 0) wkt += ",";
                    char buf[64];
                    std::snprintf(buf, sizeof(buf), "%zu 0 0 %.6g", d, static_cast<double>(embedding[d]));
                    wkt += buf;
                }
                wkt += ")";

                if (!first) sql += ",";
                first = false;

                char buf[512];
                std::snprintf(buf, sizeof(buf),
                    "(%lld,%lld,%lld,%lld,%.6g,1,%d,ST_GeomFromText('%s'),%lld,%lld)",
                    static_cast<long long>(token.id_high),
                    static_cast<long long>(token.id_low),
                    static_cast<long long>(model_context.id_high),
                    static_cast<long long>(model_context.id_low),
                    mag,
                    static_cast<int>(type),
                    wkt.c_str(),
                    static_cast<long long>(model_context.id_high),
                    static_cast<long long>(model_context.id_low));
                sql += buf;
            }

            sql += " ON CONFLICT (from_high, from_low, to_high, to_low, context_high, context_low) "
                   "DO UPDATE SET trajectory = EXCLUDED.trajectory, "
                   "weight = EXCLUDED.weight, obs_count = relationship.obs_count + 1";

            PGresult* res = PQexec(conn_.get(), sql.c_str());
            if (PQresultStatus(res) != PGRES_COMMAND_OK) {
                std::cerr << "store_embedding_trajectories batch failed: " << PQerrorMessage(conn_.get()) << std::endl;
            }
            PQclear(res);

            stored += (batch_end - batch_start);
        }

        auto end_time = std::chrono::high_resolution_clock::now();
        auto total_ms = std::chrono::duration_cast<std::chrono::milliseconds>(end_time - start_time).count();
        std::cerr << "store_embedding_trajectories: " << stored << " trajectories in " << total_ms << "ms" << std::endl;
    }

    /// Flush pending compositions to database via staging table.
    void flush_compositions(
        std::vector<std::tuple<NodeRef, NodeRef, NodeRef>>& pending_compositions,
        std::unordered_map<std::uint64_t, std::pair<NodeRef, NodeRef>>& composition_cache)
    {
        if (pending_compositions.empty()) return;

        // Deduplicate in-memory first
        std::unordered_set<std::uint64_t> seen;
        seen.reserve(pending_compositions.size());
        
        auto make_key = [](std::int64_t high, std::int64_t low) {
            return static_cast<std::uint64_t>(high) ^ 
                   (static_cast<std::uint64_t>(low) * 0x9e3779b97f4a7c15ULL);
        };
        
        auto new_end = std::remove_if(pending_compositions.begin(), pending_compositions.end(),
            [&](const auto& tuple) {
                const auto& parent = std::get<0>(tuple);
                std::uint64_t key = make_key(parent.id_high, parent.id_low);
                return !seen.insert(key).second;
            });
        pending_compositions.erase(new_end, pending_compositions.end());
        if (pending_compositions.empty()) return;

        PGresult* res;

        // Truncate staging table
        res = PQexec(conn_.get(), "TRUNCATE _comp_stage");
        PQclear(res);

        // Binary COPY to staging
        res = PQexec(conn_.get(), "COPY _comp_stage FROM STDIN WITH (FORMAT binary)");
        if (PQresultStatus(res) != PGRES_COPY_IN) { PQclear(res); pending_compositions.clear(); return; }
        PQclear(res);

        static const char HDR[] = "PGCOPY\n\377\r\n\0";
        std::vector<char> buf;
        buf.reserve(pending_compositions.size() * 60 + 32);
        buf.insert(buf.end(), HDR, HDR + 11);
        for (int i = 0; i < 8; ++i) buf.push_back(0);

        auto i16 = [&](std::int16_t v) { buf.push_back((v>>8)&0xFF); buf.push_back(v&0xFF); };
        auto i32 = [&](std::int32_t v) { for(int i=24;i>=0;i-=8) buf.push_back((v>>i)&0xFF); };
        auto i64 = [&](std::int64_t v) { for(int i=56;i>=0;i-=8) buf.push_back((v>>i)&0xFF); };

        for (const auto& [p, l, r] : pending_compositions) {
            i16(6); i32(8); i64(p.id_high); i32(8); i64(p.id_low);
            i32(8); i64(l.id_high); i32(8); i64(l.id_low);
            i32(8); i64(r.id_high); i32(8); i64(r.id_low);
            
            // Add to cache
            auto key = make_key(p.id_high, p.id_low);
            composition_cache[key] = {l, r};
        }
        i16(-1);

        PQputCopyData(conn_.get(), buf.data(), static_cast<int>(buf.size()));
        PQputCopyEnd(conn_.get(), nullptr);
        res = PQgetResult(conn_.get());
        PQclear(res);

        // INSERT with ON CONFLICT DO NOTHING
        res = PQexec(conn_.get(),
            "INSERT INTO composition (hilbert_high, hilbert_low, left_high, left_low, right_high, right_low) "
            "SELECT h, l, lh, ll, rh, rl FROM _comp_stage ON CONFLICT DO NOTHING");
        PQclear(res);

        pending_compositions.clear();
    }
};

} // namespace hartonomous::db
