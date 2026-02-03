#include <storage/physicality_store.hpp>
#include <iomanip>
#include <sstream>
#include <endian.h>
#include <cstring>
#include <iostream>
#include <cstdio>

namespace Hartonomous {

// Fast hex lookup table
static const char* hex_lut = "0123456789abcdef";

PhysicalityStore::PhysicalityStore(PostgresConnection& db, bool use_temp_table)
    : copy_(db, use_temp_table), use_dedup_(use_temp_table) {
    copy_.begin_table("hartonomous.physicality", {"id", "hilbert", "centroid", "trajectory"});
}

std::string PhysicalityStore::hash_to_uuid(const BLAKE3Pipeline::Hash& hash) {
    // Fast UUID conversion without ostringstream
    char buf[37];
    char* p = buf;
    for (int i = 0; i < 16; ++i) {
        if (i == 4 || i == 6 || i == 8 || i == 10) *p++ = '-';
        *p++ = hex_lut[(hash[i] >> 4) & 0xF];
        *p++ = hex_lut[hash[i] & 0xF];
    }
    *p = '\0';
    return std::string(buf, 36);
}

std::string PhysicalityStore::geom_to_hex(const Eigen::Vector4d& pt) {
    // Standard WKB for POINT ZM (37 bytes)
    // Fast hex encoding without ostringstream
    uint8_t wkb[37];
    wkb[0] = 0x01; // Little Endian

    uint32_t type_le = htole32(0xC0000001u);
    std::memcpy(wkb + 1, &type_le, 4);

    for (int i = 0; i < 4; ++i) {
        double coord = pt[i];
        uint64_t u;
        std::memcpy(&u, &coord, 8);
        uint64_t u_le = htole64(u);
        std::memcpy(wkb + 5 + i * 8, &u_le, 8);
    }

    // Fast hex encoding
    char hex[75]; // 37 * 2 + 1
    for (int i = 0; i < 37; ++i) {
        hex[i * 2] = hex_lut[(wkb[i] >> 4) & 0xF];
        hex[i * 2 + 1] = hex_lut[wkb[i] & 0xF];
    }
    hex[74] = '\0';
    return std::string(hex, 74);
}

void PhysicalityStore::store(const PhysicalityRecord& rec) {
    std::string uuid = hash_to_uuid(rec.id);

    // Skip dedup in direct mode (caller guarantees uniqueness)
    if (use_dedup_) {
        if (seen_.count(uuid)) return;
        seen_.insert(uuid);
    }

    // Fast WKT without ostringstream
    char wkt[128];
    int len = snprintf(wkt, sizeof(wkt), "POINTZM(%.10f %.10f %.10f %.10f)",
                       rec.centroid[0], rec.centroid[1], rec.centroid[2], rec.centroid[3]);
    std::string trajectory = rec.trajectory_wkt.empty() ? std::string(wkt, len) : rec.trajectory_wkt;

    copy_.add_row({
        uuid,
        rec.hilbert_index.to_string(),
        geom_to_hex(rec.centroid),
        trajectory
    });
}

void PhysicalityStore::flush() {
    copy_.flush();
}

}
