#include <storage/atom_lookup.hpp>
#include <sstream>
#include <iomanip>
#include <stdexcept>
#include <iostream>

namespace Hartonomous {

AtomLookup::AtomLookup(PostgresConnection& db) : db_(db) {}

std::optional<AtomLookup::AtomInfo> AtomLookup::lookup(uint32_t codepoint) {
    if (auto it = cache_.find(codepoint); it != cache_.end()) return it->second;

    std::string sql = R"(
        SELECT a.id, a.codepoint, p.id as phys_id,
               ST_X(p.centroid), ST_Y(p.centroid), ST_Z(p.centroid), ST_M(p.centroid),
               p.hilbert
        FROM hartonomous.atom a
        JOIN hartonomous.physicality p ON a.physicalityid = p.id
        WHERE a.codepoint = $1
    )";

    std::optional<AtomInfo> result;
    db_.query(sql, {std::to_string(codepoint)}, [&](const std::vector<std::string>& row) {
        if (row.size() >= 8) {
            AtomInfo info;
            info.id = BLAKE3Pipeline::from_hex(row[0]);
            info.codepoint = static_cast<uint32_t>(std::stoul(row[1]));
            info.physicality_id = BLAKE3Pipeline::from_hex(row[2]);
            for (int i=0; i<4; ++i) info.position[i] = std::stod(row[3+i]);
            info.hilbert_index = BLAKE3Pipeline::from_hex(row[7]);
            cache_[codepoint] = info;
            result = info;
        }
    });
    return result;
}

std::unordered_map<uint32_t, AtomLookup::AtomInfo> AtomLookup::lookup_batch(const std::vector<uint32_t>& codepoints) {
    std::unordered_map<uint32_t, AtomInfo> results;
    std::vector<uint32_t> missing;
    for (uint32_t cp : codepoints) {
        if (auto it = cache_.find(cp); it != cache_.end()) results[cp] = it->second;
        else missing.push_back(cp);
    }
    if (missing.empty()) return results;

    std::ostringstream in_clause; in_clause << "(";
    for (size_t i = 0; i < missing.size(); ++i) { if (i > 0) in_clause << ","; in_clause << missing[i]; }
    in_clause << ")";

    std::string sql = R"(
        SELECT a.id, a.codepoint, p.id as phys_id,
               ST_X(p.centroid), ST_Y(p.centroid), ST_Z(p.centroid), ST_M(p.centroid),
               p.hilbert
        FROM hartonomous.atom a
        JOIN hartonomous.physicality p ON a.physicalityid = p.id
        WHERE a.codepoint IN )" + in_clause.str();

    db_.query(sql, [&](const std::vector<std::string>& row) {
        if (row.size() >= 8) {
            AtomInfo info;
            info.id = BLAKE3Pipeline::from_hex(row[0]);
            info.codepoint = static_cast<uint32_t>(std::stoul(row[1]));
            info.physicality_id = BLAKE3Pipeline::from_hex(row[2]);
            for (int i=0; i<4; ++i) info.position[i] = std::stod(row[3+i]);
            info.hilbert_index = BLAKE3Pipeline::from_hex(row[7]);
            cache_[info.codepoint] = info;
            results[info.codepoint] = info;
        }
    });
    return results;
}

void AtomLookup::preload_all() {
    if (preloaded_) return;
    cache_.clear();
    cache_.reserve(1114112);

    std::string sql = R"(
        SELECT a.id, a.codepoint, p.id as phys_id,
               ST_X(p.centroid), ST_Y(p.centroid), ST_Z(p.centroid), ST_M(p.centroid),
               p.hilbert
        FROM hartonomous.atom a
        JOIN hartonomous.physicality p ON a.physicalityid = p.id
    )";

    db_.stream_query(sql, [&](const std::vector<std::string>& row) {
        if (row.size() >= 8) {
            AtomInfo info;
            info.id = BLAKE3Pipeline::from_hex(row[0]);
            info.codepoint = static_cast<uint32_t>(std::stoul(row[1]));
            info.physicality_id = BLAKE3Pipeline::from_hex(row[2]);
            for (int i=0; i<4; ++i) info.position[i] = std::stod(row[3+i]);
            info.hilbert_index = BLAKE3Pipeline::from_hex(row[7]);
            cache_[info.codepoint] = info;
        }
    });
    preloaded_ = true;
}

} // namespace Hartonomous
