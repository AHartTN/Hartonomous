#pragma once

/// DATABASE-BACKED CPE ENCODER WITH RELATIONSHIP EXTRACTION
///
/// Complete ingestion pipeline:
/// 1. Tokenize into words/units (UniversalTokenizer)
/// 2. Build trajectory for each token (LINESTRINGZM through semantic space)
/// 3. CPE encode tokens → compositions with trajectories
/// 4. Extract sequential relationships (token[i] → token[i+1])
/// 5. Bulk store compositions (dedup via ID check)
/// 6. Bulk store relationships (ON CONFLICT → obs_count++)

#include "node_ref.hpp"
#include "codepoint_atom_table.hpp"
#include "merkle_hash.hpp"
#include "content_chunker.hpp"
#include "../db/database_store.hpp"
#include "../db/cpe_encoder.hpp"
#include "../db/types.hpp"
#include "../db/trajectory_store.hpp"
#include "../threading/threading.hpp"
#include <vector>
#include <unordered_set>
#include <cstdint>
#include <chrono>
#include <iostream>
#include <cstring>

namespace hartonomous {

/// Database-backed encoder with relationship extraction
class DatabaseEncoder {
private:
    db::DatabaseStore& db_;
    db::CpeEncoder cpe_;
    UniversalTokenizer tokenizer_;
    
    // Stats
    std::size_t total_bytes_ = 0;
    std::size_t total_compositions_ = 0;
    std::size_t total_relationships_ = 0;

    // NodeRef hashing
    struct NodeRefHash {
        std::size_t operator()(const NodeRef& n) const noexcept {
            return static_cast<std::size_t>(n.id_high) ^ 
                   (static_cast<std::size_t>(n.id_low) * 0x9e3779b97f4a7c15ULL);
        }
    };
    struct NodeRefEqual {
        bool operator()(const NodeRef& a, const NodeRef& b) const noexcept {
            return a.id_high == b.id_high && a.id_low == b.id_low;
        }
    };

public:
    explicit DatabaseEncoder(db::DatabaseStore& db) : db_(db) {}

    /// Ingest content: compositions + relationships
    NodeRef ingest(const std::uint8_t* data, std::size_t len) {
        if (len == 0) return NodeRef{};
        total_bytes_ = len;

        // Phase 1: Tokenize into words/units
        auto tokens = tokenizer_.tokenize(data, len);
        if (tokens.empty()) return NodeRef{};

        // Phase 2: Build NodeRef + trajectory for each token
        std::vector<NodeRef> token_refs;
        std::vector<db::Trajectory> token_trajectories;
        token_refs.reserve(tokens.size());
        token_trajectories.reserve(tokens.size());

        std::vector<std::tuple<NodeRef, NodeRef, NodeRef>> all_compositions;
        all_compositions.reserve(tokens.size() * 10);

        // Helper to check if codepoints contain word characters
        // Uses Unicode general categories - any Letter or Number is a word char
        auto is_word_token = [](const std::vector<std::int32_t>& cps) {
            for (auto cp : cps) {
                // Valid codepoint range check
                if (cp < 0 || cp > 0x10FFFF) continue;
                // Exclude control characters and ASCII punctuation
                if (cp < 0x20) continue;
                if (cp >= 0x21 && cp <= 0x2F) continue;  // !"#$%&'()*+,-./
                if (cp >= 0x3A && cp <= 0x40) continue;  // :;<=>?@
                if (cp >= 0x5B && cp <= 0x60) continue;  // [\]^_`
                if (cp >= 0x7B && cp <= 0x7E) continue;  // {|}~
                // Everything else above 0x20 (including ALL Unicode letters,
                // numbers, and symbols from every language) is a word character
                if (cp >= 0x30 && cp <= 0x39) return true;  // 0-9
                if (cp >= 0x41 && cp <= 0x5A) return true;  // A-Z
                if (cp >= 0x61 && cp <= 0x7A) return true;  // a-z
                if (cp >= 0x80) return true;  // All non-ASCII: every language
            }
            return false;
        };

        // Separate word tokens for relationship building
        std::vector<NodeRef> word_refs;
        std::vector<db::Trajectory> word_trajectories;

        for (const auto& tok : tokens) {
            // Decode token to codepoints
            auto codepoints = UTF8Decoder::decode(tok.data, tok.length);
            if (codepoints.empty()) continue;

            // Build trajectory through semantic space
            db::Trajectory traj = build_trajectory(codepoints);

            // CPE encode token to get NodeRef
            NodeRef ref;
            if (codepoints.size() == 1) {
                ref = CodepointAtomTable::ref(codepoints[0]);
            } else {
                ref = cpe_.build_cpe_and_collect(codepoints, 0, codepoints.size(), all_compositions);
            }

            token_refs.push_back(ref);
            token_trajectories.push_back(traj);

            // Track word tokens separately for relationships
            if (is_word_token(codepoints)) {
                word_refs.push_back(ref);
                word_trajectories.push_back(std::move(traj));
            }
        }

        total_compositions_ = all_compositions.size();

        // Phase 3: Store compositions (dedup)
        store_compositions(all_compositions);

        // Phase 4: Build document-level composition from tokens
        NodeRef root;
        if (token_refs.size() == 1) {
            root = token_refs[0];
        } else {
            // Left-to-right composition of tokens
            root = token_refs[0];
            for (std::size_t i = 1; i < token_refs.size(); ++i) {
                NodeRef children[2] = {root, token_refs[i]};
                auto [h, l] = MerkleHash::compute(children, children + 2);
                NodeRef comp = NodeRef::comp(h, l);
                all_compositions.emplace_back(comp, root, token_refs[i]);
                root = comp;
            }
        }

        // Phase 5: Store trajectories on TOKEN compositions (the roots)
        // Each token_refs[i] gets token_trajectories[i]
        store_composition_trajectories(token_refs, token_trajectories);

        // Phase 6: Extract and store RELATIONSHIPS (word[i] → word[i+1])
        // Only word tokens, not punctuation
        store_relationships(word_refs, word_trajectories);

        return root;
    }

    NodeRef ingest(const std::string& text) {
        return ingest(reinterpret_cast<const std::uint8_t*>(text.data()), text.size());
    }

    [[nodiscard]] std::size_t bytes_processed() const { return total_bytes_; }
    [[nodiscard]] std::size_t composition_count() const { return total_compositions_; }
    [[nodiscard]] std::size_t relationship_count() const { return total_relationships_; }

private:
    /// Build trajectory from codepoints (RLE compressed)
    db::Trajectory build_trajectory(const std::vector<std::int32_t>& codepoints) {
        db::Trajectory traj;
        if (codepoints.empty()) return traj;

        db::TrajectoryPoint current{};
        bool has_current = false;

        for (std::int32_t cp : codepoints) {
            auto coord = SemanticDecompose::get_coord(cp);

            if (has_current &&
                current.page == coord.page &&
                current.type == coord.type &&
                current.base == coord.base &&
                current.variant == coord.variant) {
                current.count++;
            } else {
                if (has_current) {
                    traj.points.push_back(current);
                }
                current.page = static_cast<std::int16_t>(coord.page);
                current.type = static_cast<std::int16_t>(coord.type);
                current.base = coord.base;
                current.variant = coord.variant;
                current.count = 1;
                has_current = true;
            }
        }

        if (has_current) {
            traj.points.push_back(current);
        }

        return traj;
    }

    /// Store compositions with dedup
    void store_compositions(const std::vector<std::tuple<NodeRef, NodeRef, NodeRef>>& comps) {
        if (comps.empty()) return;

        // Local dedup
        std::unordered_set<NodeRef, NodeRefHash, NodeRefEqual> seen;
        std::vector<std::tuple<NodeRef, NodeRef, NodeRef>> unique;
        unique.reserve(comps.size());

        for (const auto& c : comps) {
            if (seen.insert(std::get<0>(c)).second) {
                unique.push_back(c);
            }
        }

        // Build COPY data
        std::string data;
        data.reserve(unique.size() * 80);
        char buf[256];

        for (const auto& [parent, left, right] : unique) {
            char* p = buf;
            p = db::DatabaseStore::write_int64(p, parent.id_high); *p++ = '\t';
            p = db::DatabaseStore::write_int64(p, parent.id_low); *p++ = '\t';
            p = db::DatabaseStore::write_int64(p, left.id_high); *p++ = '\t';
            p = db::DatabaseStore::write_int64(p, left.id_low); *p++ = '\t';
            p = db::DatabaseStore::write_int64(p, right.id_high); *p++ = '\t';
            p = db::DatabaseStore::write_int64(p, right.id_low); *p++ = '\n';
            data.append(buf, static_cast<std::size_t>(p - buf));
        }

        // Use ON CONFLICT DO UPDATE for idempotent insert
        PGresult* res = PQexec(db_.connection(), "DROP TABLE IF EXISTS _comp_stage");
        PQclear(res);
        res = PQexec(db_.connection(),
            "CREATE UNLOGGED TABLE _comp_stage ("
            "h BIGINT, l BIGINT, lh BIGINT, ll BIGINT, rh BIGINT, rl BIGINT)");
        PQclear(res);

        res = PQexec(db_.connection(), "COPY _comp_stage FROM STDIN");
        if (PQresultStatus(res) == PGRES_COPY_IN) {
            PQclear(res);
            PQputCopyData(db_.connection(), data.c_str(), static_cast<int>(data.size()));
            PQputCopyEnd(db_.connection(), nullptr);
            PQclear(PQgetResult(db_.connection()));

            res = PQexec(db_.connection(),
                "INSERT INTO composition (hilbert_high, hilbert_low, left_high, left_low, right_high, right_low, obs_count) "
                "SELECT h, l, lh, ll, rh, rl, 1 FROM _comp_stage "
                "ON CONFLICT (hilbert_high, hilbert_low) DO UPDATE SET obs_count = composition.obs_count + 1");
            PQclear(res);
        } else {
            PQclear(res);
        }

        res = PQexec(db_.connection(), "DROP TABLE IF EXISTS _comp_stage");
        PQclear(res);
    }

    /// Store trajectories on composition records.
    /// Updates existing compositions with their LineStringZM trajectory.
    void store_composition_trajectories(
        const std::vector<NodeRef>& refs,
        const std::vector<db::Trajectory>& trajectories)
    {
        if (refs.empty()) return;
        
        // Build batch UPDATE via staging table
        std::string data;
        data.reserve(refs.size() * 200);
        
        for (std::size_t i = 0; i < refs.size() && i < trajectories.size(); ++i) {
            const auto& ref = refs[i];
            const auto& traj = trajectories[i];
            
            // Skip atoms (they don't have trajectories - they ARE points)
            if (ref.is_atom) continue;
            
            // Skip empty trajectories
            if (traj.points.empty()) continue;
            
            std::string wkt = traj.to_wkt();
            if (wkt.empty() || wkt == "LINESTRINGZM EMPTY") continue;
            
            char buf[512];
            char* p = buf;
            p = db::DatabaseStore::write_int64(p, ref.id_high); *p++ = '\t';
            p = db::DatabaseStore::write_int64(p, ref.id_low); *p++ = '\t';
            // WKT needs to be escaped for COPY
            std::memcpy(p, wkt.c_str(), wkt.size());
            p += wkt.size();
            *p++ = '\n';
            data.append(buf, static_cast<std::size_t>(p - buf));
        }
        
        if (data.empty()) return;
        
        // Stage and update
        PGresult* res = PQexec(db_.connection(), "DROP TABLE IF EXISTS _traj_stage");
        PQclear(res);
        res = PQexec(db_.connection(),
            "CREATE UNLOGGED TABLE _traj_stage ("
            "h BIGINT, l BIGINT, wkt TEXT)");
        PQclear(res);
        
        res = PQexec(db_.connection(), "COPY _traj_stage FROM STDIN");
        if (PQresultStatus(res) == PGRES_COPY_IN) {
            PQclear(res);
            PQputCopyData(db_.connection(), data.c_str(), static_cast<int>(data.size()));
            PQputCopyEnd(db_.connection(), nullptr);
            PQclear(PQgetResult(db_.connection()));
            
            res = PQexec(db_.connection(),
                "UPDATE composition c "
                "SET trajectory = ST_GeomFromText(s.wkt) "
                "FROM _traj_stage s "
                "WHERE c.hilbert_high = s.h AND c.hilbert_low = s.l");
            PQclear(res);
        } else {
            PQclear(res);
        }
        
        res = PQexec(db_.connection(), "DROP TABLE IF EXISTS _traj_stage");
        PQclear(res);
    }

    /// Store sequential relationships between tokens
    void store_relationships(
        const std::vector<NodeRef>& refs,
        const std::vector<db::Trajectory>& trajectories)
    {
        if (refs.size() < 2) return;

        // Dedup relationships - same (from, to) pair may appear multiple times
        struct RelKey {
            std::int64_t from_high, from_low, to_high, to_low;
            bool operator==(const RelKey& o) const {
                return from_high == o.from_high && from_low == o.from_low &&
                       to_high == o.to_high && to_low == o.to_low;
            }
        };
        struct RelKeyHash {
            std::size_t operator()(const RelKey& k) const {
                return static_cast<std::size_t>(k.from_high) ^
                       (static_cast<std::size_t>(k.from_low) * 0x9e3779b97f4a7c15ULL) ^
                       (static_cast<std::size_t>(k.to_high) * 0x517cc1b727220a95ULL) ^
                       (static_cast<std::size_t>(k.to_low) * 0x2545f4914f6cdd1dULL);
            }
        };

        std::unordered_map<RelKey, std::size_t, RelKeyHash> seen;  // key -> count
        std::vector<std::tuple<NodeRef, NodeRef, std::size_t, std::string>> unique_rels;

        for (std::size_t i = 0; i + 1 < refs.size(); ++i) {
            const NodeRef& from = refs[i];
            const NodeRef& to = refs[i + 1];
            RelKey key{from.id_high, from.id_low, to.id_high, to.id_low};

            auto it = seen.find(key);
            if (it != seen.end()) {
                // Increment count for existing
                std::get<2>(unique_rels[it->second])++;
            } else {
                // Build trajectory for new relationship
                db::Trajectory edge_traj;
                if (i < trajectories.size() && !trajectories[i].points.empty()) {
                    edge_traj.points.push_back(trajectories[i].points.back());
                }
                if (i + 1 < trajectories.size() && !trajectories[i + 1].points.empty()) {
                    edge_traj.points.push_back(trajectories[i + 1].points.front());
                }
                std::string wkt = edge_traj.points.size() >= 2 ? edge_traj.to_wkt() : "";

                seen[key] = unique_rels.size();
                unique_rels.emplace_back(from, to, 1, wkt);
            }
        }

        total_relationships_ = unique_rels.size();

        // Build COPY data
        std::string data;
        data.reserve(unique_rels.size() * 200);

        for (const auto& [from, to, count, wkt] : unique_rels) {
            char buf[512];
            char* p = buf;
            p = db::DatabaseStore::write_int64(p, from.id_high); *p++ = '\t';
            p = db::DatabaseStore::write_int64(p, from.id_low); *p++ = '\t';
            p = db::DatabaseStore::write_int64(p, to.id_high); *p++ = '\t';
            p = db::DatabaseStore::write_int64(p, to.id_low); *p++ = '\t';
            // weight = count (aggregated)
            p = db::DatabaseStore::write_int64(p, static_cast<std::int64_t>(count)); *p++ = '\t';
            // rel_type = 0
            *p++ = '0'; *p++ = '\t';
            // context = 0,0
            *p++ = '0'; *p++ = '\t';
            *p++ = '0'; *p++ = '\t';
            // obs_count = count
            p = db::DatabaseStore::write_int64(p, static_cast<std::int64_t>(count)); *p++ = '\t';
            // trajectory
            if (wkt.empty()) {
                *p++ = '\\'; *p++ = 'N';
            } else {
                std::memcpy(p, wkt.c_str(), wkt.size());
                p += wkt.size();
            }
            *p++ = '\n';
            data.append(buf, static_cast<std::size_t>(p - buf));
        }

        // Stage and insert with ON CONFLICT
        PGresult* res = PQexec(db_.connection(), "DROP TABLE IF EXISTS _rel_stage");
        PQclear(res);
        res = PQexec(db_.connection(),
            "CREATE UNLOGGED TABLE _rel_stage ("
            "fh BIGINT, fl BIGINT, th BIGINT, tl BIGINT, "
            "w DOUBLE PRECISION, rt SMALLINT, ch BIGINT, cl BIGINT, "
            "oc INTEGER, traj TEXT)");
        PQclear(res);

        res = PQexec(db_.connection(), "COPY _rel_stage FROM STDIN");
        if (PQresultStatus(res) == PGRES_COPY_IN) {
            PQclear(res);
            (void)PQputCopyData(db_.connection(), data.c_str(), static_cast<int>(data.size()));
            (void)PQputCopyEnd(db_.connection(), nullptr);
            PGresult* copy_result = PQgetResult(db_.connection());
            
            if (PQresultStatus(copy_result) != PGRES_COMMAND_OK) {
                std::cerr << "[store_relationships] COPY failed: " 
                          << PQerrorMessage(db_.connection()) << std::endl;
            }
            PQclear(copy_result);

            res = PQexec(db_.connection(),
                "INSERT INTO relationship "
                "(from_high, from_low, to_high, to_low, weight, rel_type, context_high, context_low, obs_count, trajectory) "
                "SELECT fh, fl, th, tl, w, rt, ch, cl, oc, "
                "CASE WHEN traj IS NOT NULL AND traj != '' THEN ST_GeomFromText(traj) ELSE NULL END "
                "FROM _rel_stage "
                "ON CONFLICT (from_high, from_low, to_high, to_low, context_high, context_low) "
                "DO UPDATE SET obs_count = relationship.obs_count + 1, "
                "weight = (relationship.weight + EXCLUDED.weight) / 2");
            
            if (PQresultStatus(res) != PGRES_COMMAND_OK) {
                std::cerr << "[store_relationships] INSERT failed: " 
                          << PQerrorMessage(db_.connection()) << std::endl;
            }
            PQclear(res);
        } else {
            std::cerr << "[store_relationships] COPY start failed: " 
                      << PQerrorMessage(db_.connection()) << std::endl;
            PQclear(res);
        }

        res = PQexec(db_.connection(), "DROP TABLE IF EXISTS _rel_stage");
        PQclear(res);
    }
};

} // namespace hartonomous
