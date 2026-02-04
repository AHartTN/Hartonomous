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

PhysicalityStore::PhysicalityStore(PostgresConnection& db, bool use_temp_table, bool use_binary)
    : copy_(db, use_temp_table), use_dedup_(use_temp_table), use_binary_(use_binary) {
    copy_.set_binary(use_binary);
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
    if (use_binary_) {
        // Skip dedup in binary mode (caller usually handles uniqueness or we rely on DB)
        // Actually, if use_dedup_ is true, we must check.
        if (use_dedup_) {
            if (seen_.count(rec.id)) return;
            seen_.insert(rec.id);
        }

        BulkCopy::BinaryRow row;
        row.add_uuid(rec.id);
        
        // 1. Hilbert Index as 16 bytes (bytea)
        uint64_t h_bytes[2];
        h_bytes[0] = htobe64(rec.hilbert_index.hi);
        h_bytes[1] = htobe64(rec.hilbert_index.lo);
        row.add_bytes(h_bytes, 16);
        
        // 2. Centroid as WKB (POINTZM)
        uint8_t wkb[37];
        wkb[0] = 0x01; // Little Endian
        uint32_t type_le = htole32(0xC0000001u); // POINTZM
        std::memcpy(wkb + 1, &type_le, 4);
        for (int i = 0; i < 4; ++i) {
            double coord = rec.centroid[i];
            uint64_t u;
            std::memcpy(&u, &coord, 8);
            uint64_t u_le = htole64(u);
            std::memcpy(wkb + 5 + i * 8, &u_le, 8);
        }
        row.add_bytes(wkb, 37);
        
        // 3. Trajectory
        if (rec.trajectory.empty()) {
            row.add_null();
        } else if (rec.trajectory.size() == 1) {
            // Single point trajectory -> POINTZM (Same as centroid serialization essentially)
            // Re-use logic for efficiency
            uint8_t pt_wkb[37];
            pt_wkb[0] = 0x01;
            uint32_t t = htole32(0xC0000001u);
            std::memcpy(pt_wkb + 1, &t, 4);
            for (int i = 0; i < 4; ++i) {
                double v = rec.trajectory[0][i];
                uint64_t u; std::memcpy(&u, &v, 8);
                uint64_t u_le = htole64(u);
                std::memcpy(pt_wkb + 5 + i*8, &u_le, 8);
            }
            row.add_bytes(pt_wkb, 37);
        } else {
            // >1 points -> LINESTRINGZM
            // Size: 1 (endian) + 4 (type) + 4 (num_points) + 32*N (points)
            size_t size = 9 + (32 * rec.trajectory.size());
            std::vector<uint8_t> traj_wkb(size);
            traj_wkb[0] = 0x01;
            uint32_t type = htole32(0xC0000002u); // LINESTRINGZM
            std::memcpy(traj_wkb.data() + 1, &type, 4);
            uint32_t num = htole32(static_cast<uint32_t>(rec.trajectory.size()));
            std::memcpy(traj_wkb.data() + 5, &num, 4);
            
            uint8_t* ptr = traj_wkb.data() + 9;
            for (const auto& pt : rec.trajectory) {
                for (int i=0; i<4; ++i) {
                    double v = pt[i];
                    uint64_t u; std::memcpy(&u, &v, 8);
                    u = htole64(u);
                    std::memcpy(ptr, &u, 8);
                    ptr += 8;
                }
            }
            row.add_bytes(traj_wkb.data(), size);
        }
        
        copy_.add_row(row);
    } else {
        std::string uuid = hash_to_uuid(rec.id);

        if (use_dedup_) {
            if (seen_.count(rec.id)) return;
            seen_.insert(rec.id);
        }

        // Fast WKT without ostringstream
        std::string traj_wkt;
        if (rec.trajectory.empty()) {
            // NULL
        } else if (rec.trajectory.size() == 1) {
            char buf[128];
            snprintf(buf, sizeof(buf), "POINTZM(%.10f %.10f %.10f %.10f)",
                rec.trajectory[0][0], rec.trajectory[0][1], rec.trajectory[0][2], rec.trajectory[0][3]);
            traj_wkt = buf;
        } else {
            std::ostringstream ss;
            ss << "LINESTRINGZM(";
            for (size_t i = 0; i < rec.trajectory.size(); ++i) {
                if (i > 0) ss << ",";
                const auto& p = rec.trajectory[i];
                ss << std::fixed << std::setprecision(10) << p[0] << " " << p[1] << " " << p[2] << " " << p[3];
            }
            ss << ")";
            traj_wkt = ss.str();
        }

        copy_.add_row(std::vector<std::string>{
            uuid,
            rec.hilbert_index.to_string(),
            geom_to_hex(rec.centroid),
            traj_wkt.empty() ? "\\N" : traj_wkt
        });
    }
}

void PhysicalityStore::flush() {
    copy_.flush();
}

}
