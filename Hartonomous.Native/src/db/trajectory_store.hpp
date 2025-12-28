#pragma once

/// TRAJECTORY STORE - Operations for trajectory storage and retrieval.
///
/// Trajectories are RLE-compressed paths through 4D semantic space.
/// "Hello" → H(1), e(1), l(2), o(1) - NOT 5 separate records.
///
/// Extracted from query_store.hpp for separation of concerns.

#include "connection.hpp"
#include "pg_result.hpp"
#include "types.hpp"
#include "../atoms/semantic_decompose.hpp"
#include <libpq-fe.h>
#include <optional>
#include <string>
#include <cstdio>
#include <cstring>

namespace hartonomous::db {

/// Trajectory storage and retrieval operations.
/// Uses PostGIS LineStringZM for geometric storage.
class TrajectoryStore {
    PgConnection& conn_;

public:
    explicit TrajectoryStore(PgConnection& conn) : conn_(conn) {}

    /// Build RLE-compressed trajectory from text.
    /// "Hello" → H(1), e(1), l(2), o(1) - NOT 5 separate records
    [[nodiscard]] static Trajectory build_trajectory(const std::string& text) {
        Trajectory traj;
        if (text.empty()) return traj;

        TrajectoryPoint current{};
        bool has_current = false;

        for (unsigned char c : text) {
            auto coord = SemanticDecompose::get_coord(static_cast<std::int32_t>(c));

            if (has_current &&
                current.page == coord.page &&
                current.type == coord.type &&
                current.base == coord.base &&
                current.variant == coord.variant) {
                // Same point - increment RLE count
                current.count++;
            } else {
                // New point - save current if exists
                if (has_current) {
                    traj.points.push_back(current);
                }
                current.page = coord.page;
                current.type = coord.type;
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

    /// Store trajectory with weight (sparse - only call for salient relationships).
    /// This stores the ENTIRE path as ONE LineStringZM, not N separate records.
    void store(NodeRef from, NodeRef to, const Trajectory& traj,
               RelType type = REL_DEFAULT, NodeRef context = NodeRef{}) {
        // Don't store empty trajectories or zero weights
        if (traj.points.empty()) return;

        std::string wkt = traj.to_wkt();

        char query[2048];
        std::snprintf(query, sizeof(query),
            "INSERT INTO relationship (from_high, from_low, to_high, to_low, "
            "weight, obs_count, trajectory, rel_type, context_high, context_low) "
            "VALUES (%lld, %lld, %lld, %lld, %f, 1, ST_GeomFromText('%s'), %d, %lld, %lld) "
            "ON CONFLICT (from_high, from_low, to_high, to_low, context_high, context_low) "
            "DO UPDATE SET weight = relationship.weight + EXCLUDED.weight, "
            "obs_count = relationship.obs_count + 1, trajectory = EXCLUDED.trajectory",
            static_cast<long long>(from.id_high),
            static_cast<long long>(from.id_low),
            static_cast<long long>(to.id_high),
            static_cast<long long>(to.id_low),
            traj.weight,
            wkt.c_str(),
            static_cast<int>(type),
            static_cast<long long>(context.id_high),
            static_cast<long long>(context.id_low));

        PQexec(conn_.get(), query);
    }

    /// Retrieve trajectory from database and decode back to RLE form.
    [[nodiscard]] std::optional<Trajectory> get(NodeRef from, NodeRef to,
                                                 NodeRef context = NodeRef{}) {
        char query[512];
        std::snprintf(query, sizeof(query),
            "SELECT weight, ST_AsText(trajectory) FROM relationship "
            "WHERE from_high = %lld AND from_low = %lld "
            "  AND to_high = %lld AND to_low = %lld "
            "  AND context_high = %lld AND context_low = %lld",
            static_cast<long long>(from.id_high),
            static_cast<long long>(from.id_low),
            static_cast<long long>(to.id_high),
            static_cast<long long>(to.id_low),
            static_cast<long long>(context.id_high),
            static_cast<long long>(context.id_low));

        PgResult res(PQexec(conn_.get(), query));
        if (res.status() != PGRES_TUPLES_OK || res.row_count() == 0) {
            return std::nullopt;
        }

        Trajectory traj;
        traj.weight = std::stod(res.get_value(0, 0));

        const char* wkt = res.get_value(0, 1);
        if (wkt && wkt[0] != '\0') {
            traj = parse_wkt(wkt, traj.weight);
        }

        return traj;
    }

    /// Export trajectory to text (inverse of build_trajectory).
    /// Expands RLE and converts semantic coords back to codepoints.
    [[nodiscard]] static std::string to_text(const Trajectory& traj) {
        std::string result;
        result.reserve(traj.expanded_length());

        for (const auto& pt : traj.points) {
            SemanticCoord coord{
                static_cast<std::uint8_t>(pt.page),
                static_cast<std::uint8_t>(pt.type),
                pt.base,
                pt.variant
            };
            std::int32_t cp = SemanticDecompose::to_codepoint(coord);

            // Expand RLE
            for (std::uint32_t i = 0; i < pt.count; ++i) {
                if (cp >= 0 && cp <= 127) {
                    result.push_back(static_cast<char>(cp));
                } else {
                    // UTF-8 encode for non-ASCII
                    encode_utf8(cp, result);
                }
            }
        }

        return result;
    }

    /// Export trajectory to RLE string representation: "H(1)e(1)l(2)o(1)"
    [[nodiscard]] static std::string to_rle_string(const Trajectory& traj) {
        std::string result;

        for (const auto& pt : traj.points) {
            SemanticCoord coord{
                static_cast<std::uint8_t>(pt.page),
                static_cast<std::uint8_t>(pt.type),
                pt.base,
                pt.variant
            };
            std::int32_t cp = SemanticDecompose::to_codepoint(coord);

            if (cp >= 32 && cp <= 126) {
                result.push_back(static_cast<char>(cp));
            } else {
                char buf[16];
                std::snprintf(buf, sizeof(buf), "\\u%04X", cp);
                result += buf;
            }

            if (pt.count > 1) {
                char buf[16];
                std::snprintf(buf, sizeof(buf), "(x%u)", pt.count);
                result += buf;
            }
        }

        return result;
    }

private:
    /// Parse WKT LineStringZM back to Trajectory with RLE compression.
    [[nodiscard]] static Trajectory parse_wkt(const char* wkt, double weight) {
        Trajectory traj;
        traj.weight = weight;

        // Skip "LINESTRINGZM(" prefix
        const char* p = wkt;
        while (*p && *p != '(') ++p;
        if (*p == '(') ++p;

        TrajectoryPoint current{};
        bool has_current = false;

        while (*p && *p != ')') {
            // Skip whitespace and commas
            while (*p == ' ' || *p == ',') ++p;
            if (*p == ')' || *p == '\0') break;

            // Parse four numbers: X Y Z M
            int page = 0, type = 0, base = 0, variant = 0;
            if (std::sscanf(p, "%d %d %d %d", &page, &type, &base, &variant) == 4) {
                // RLE compress: if same as current, increment count
                if (has_current &&
                    current.page == page &&
                    current.type == type &&
                    current.base == base &&
                    current.variant == static_cast<std::uint8_t>(variant)) {
                    current.count++;
                } else {
                    if (has_current) {
                        traj.points.push_back(current);
                    }
                    current.page = static_cast<std::int16_t>(page);
                    current.type = static_cast<std::int16_t>(type);
                    current.base = base;
                    current.variant = static_cast<std::uint8_t>(variant);
                    current.count = 1;
                    has_current = true;
                }
            }

            // Skip to next comma or end
            while (*p && *p != ',' && *p != ')') ++p;
        }

        if (has_current) {
            traj.points.push_back(current);
        }

        return traj;
    }

    /// UTF-8 encode a codepoint and append to string.
    static void encode_utf8(std::int32_t cp, std::string& out) {
        if (cp < 0x80) {
            out.push_back(static_cast<char>(cp));
        } else if (cp < 0x800) {
            out.push_back(static_cast<char>(0xC0 | (cp >> 6)));
            out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
        } else if (cp < 0x10000) {
            out.push_back(static_cast<char>(0xE0 | (cp >> 12)));
            out.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
        } else {
            out.push_back(static_cast<char>(0xF0 | (cp >> 18)));
            out.push_back(static_cast<char>(0x80 | ((cp >> 12) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
        }
    }
};

} // namespace hartonomous::db
