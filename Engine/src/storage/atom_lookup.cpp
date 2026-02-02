#include <storage/atom_lookup.hpp>
#include <sstream>
#include <iomanip>
#include <stdexcept>
#include <iostream>

namespace Hartonomous {

AtomLookup::AtomLookup(PostgresConnection& db) : db_(db) {}

AtomLookup::Hash AtomLookup::uuid_to_hash(const std::string& uuid) {
    Hash hash{};
    size_t j = 0;
    for (size_t i = 0; i < uuid.size() && j < 16; ++i) {
        if (uuid[i] == '-') continue;
        uint8_t nibble1 = (uuid[i] >= 'a') ? (uuid[i] - 'a' + 10) :
                          (uuid[i] >= 'A') ? (uuid[i] - 'A' + 10) :
                          (uuid[i] - '0');
        ++i;
        if (i >= uuid.size()) break;
        uint8_t nibble2 = (uuid[i] >= 'a') ? (uuid[i] - 'a' + 10) :
                          (uuid[i] >= 'A') ? (uuid[i] - 'A' + 10) :
                          (uuid[i] - '0');
        hash[j++] = (nibble1 << 4) | nibble2;
    }
    return hash;
}

AtomLookup::Vec4 AtomLookup::parse_geometry(const std::string& geom_hex) {
    Vec4 pos{0, 0, 0, 0};

    // Parse PostGIS POINT(x y z m) text format or use ST_X/Y/Z/M in query
    // For now, we query with ST_X, ST_Y, ST_Z, ST_M
    // This function is called with comma-separated x,y,z,m values
    std::istringstream ss(geom_hex);
    char delim;
    ss >> pos[0] >> delim >> pos[1] >> delim >> pos[2] >> delim >> pos[3];

    return pos;
}

AtomLookup::HilbertIndex AtomLookup::parse_hilbert(const std::string& hilbert_str) {
    HilbertIndex idx{};
    // Parse as decimal string into high/low parts
    // For simplicity, parse as two 64-bit halves if needed
    // The format depends on how it's stored - check the schema

    // Assuming it's stored as a decimal string representation
    // We need to parse it back to high/low uint64 parts
    if (hilbert_str.empty()) return idx;

    // Simple approach: if it fits in uint64, use that
    // For full 128-bit, would need proper parsing
    try {
        unsigned long long val = std::stoull(hilbert_str);
        idx.lo = val;
        idx.hi = 0;
    } catch (...) {
        // If it's too large, we'd need proper 128-bit parsing
        // For now, set to zero
    }

    return idx;
}

std::optional<AtomLookup::AtomInfo> AtomLookup::lookup(uint32_t codepoint) {
    // Check cache first
    auto it = cache_.find(codepoint);
    if (it != cache_.end()) {
        return it->second;
    }

    // Query database
    std::string sql = R"(
        SELECT a.id, a.codepoint, p.id as phys_id,
               ST_X(p.centroid) as x, ST_Y(p.centroid) as y,
               ST_Z(p.centroid) as z, ST_M(p.centroid) as m,
               p.hilbert::text
        FROM hartonomous.atom a
        JOIN hartonomous.physicality p ON a.physicalityid = p.id
        WHERE a.codepoint = $1
    )";

    std::optional<AtomInfo> result;

    db_.query(sql, {std::to_string(codepoint)}, [&](const std::vector<std::string>& row) {
        if (row.size() >= 8) {
            AtomInfo info;
            info.id = uuid_to_hash(row[0]);
            info.codepoint = static_cast<uint32_t>(std::stoul(row[1]));
            info.physicality_id = uuid_to_hash(row[2]);
            info.position[0] = std::stod(row[3]);
            info.position[1] = std::stod(row[4]);
            info.position[2] = std::stod(row[5]);
            info.position[3] = std::stod(row[6]);
            info.hilbert_index = parse_hilbert(row[7]);

            cache_[codepoint] = info;
            result = info;
        }
    });

    return result;
}

std::unordered_map<uint32_t, AtomLookup::AtomInfo> AtomLookup::lookup_batch(
    const std::vector<uint32_t>& codepoints) {

    std::unordered_map<uint32_t, AtomInfo> results;
    std::vector<uint32_t> missing;

    // Check cache first
    for (uint32_t cp : codepoints) {
        auto it = cache_.find(cp);
        if (it != cache_.end()) {
            results[cp] = it->second;
        } else {
            missing.push_back(cp);
        }
    }

    if (missing.empty()) {
        return results;
    }

    // Build IN clause for missing codepoints
    std::ostringstream in_clause;
    in_clause << "(";
    for (size_t i = 0; i < missing.size(); ++i) {
        if (i > 0) in_clause << ",";
        in_clause << missing[i];
    }
    in_clause << ")";

    std::string sql = R"(
        SELECT a.id, a.codepoint, p.id as phys_id,
               ST_X(p.centroid) as x, ST_Y(p.centroid) as y,
               ST_Z(p.centroid) as z, ST_M(p.centroid) as m,
               p.hilbert::text
        FROM hartonomous.atom a
        JOIN hartonomous.physicality p ON a.physicalityid = p.id
        WHERE a.codepoint IN )" + in_clause.str();

    std::cout << "DEBUG: AtomLookup SQL: " << sql.substr(0, 200) << "..." << std::endl;

    db_.query(sql, [&](const std::vector<std::string>& row) {
        if (row.size() >= 8) {
            AtomInfo info;
            info.id = uuid_to_hash(row[0]);
            info.codepoint = static_cast<uint32_t>(std::stoul(row[1]));
            info.physicality_id = uuid_to_hash(row[2]);
            info.position[0] = std::stod(row[3]);
            info.position[1] = std::stod(row[4]);
            info.position[2] = std::stod(row[5]);
            info.position[3] = std::stod(row[6]);
            info.hilbert_index = parse_hilbert(row[7]);

            cache_[info.codepoint] = info;
            results[info.codepoint] = info;
        }
    });

    return results;
}

void AtomLookup::preload_all() {
    if (preloaded_) return;

    cache_.clear();
    cache_.reserve(1114112);  // Full Unicode codespace

    std::string sql = R"(
        SELECT a.id, a.codepoint, p.id as phys_id,
               ST_X(p.centroid) as x, ST_Y(p.centroid) as y,
               ST_Z(p.centroid) as z, ST_M(p.centroid) as m,
               p.hilbert::text
        FROM hartonomous.atom a
        JOIN hartonomous.physicality p ON a.physicalityid = p.id
    )";

    db_.query(sql, [&](const std::vector<std::string>& row) {
        if (row.size() >= 8) {
            AtomInfo info;
            info.id = uuid_to_hash(row[0]);
            info.codepoint = static_cast<uint32_t>(std::stoul(row[1]));
            info.physicality_id = uuid_to_hash(row[2]);
            info.position[0] = std::stod(row[3]);
            info.position[1] = std::stod(row[4]);
            info.position[2] = std::stod(row[5]);
            info.position[3] = std::stod(row[6]);
            info.hilbert_index = parse_hilbert(row[7]);

            cache_[info.codepoint] = info;
        }
    });

    preloaded_ = true;
}

} // namespace Hartonomous
