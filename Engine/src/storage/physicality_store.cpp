#include <storage/physicality_store.hpp>
#include <storage/format_utils.hpp>
#include <endian.h>
#include <cstring>
#include <iostream>
#include <iomanip>
#include <cstdio>

namespace Hartonomous {

PhysicalityStore::PhysicalityStore(PostgresConnection& db, bool use_temp_table, bool use_binary)
    : SubstrateStore(db, "hartonomous.physicality", {"id", "hilbert", "centroid", "trajectory"}, use_temp_table, use_binary) {}

void PhysicalityStore::store(const PhysicalityRecord& rec) {
    if (is_duplicate(rec.id)) return;

    if (use_binary_) {
        BulkCopy::BinaryRow row;
        row.add_uuid(rec.id);
        row.add_uuid(rec.hilbert_index);
        
        // POINTZM Centroid
        uint8_t wkb[37];
        wkb[0] = 0x01;
        uint32_t type_le = htole32(0xC0000001u);
        std::memcpy(wkb + 1, &type_le, 4);
        for (int i = 0; i < 4; ++i) {
            double coord = rec.centroid[i];
            uint64_t u; std::memcpy(&u, &coord, 8);
            u = htole64(u); std::memcpy(wkb + 5 + i * 8, &u, 8);
        }
        row.add_bytes(wkb, 37);
        
        // Trajectory
        if (rec.trajectory.empty()) {
            row.add_null();
        } else if (rec.trajectory.size() == 1) {
            row.add_bytes(wkb, 37); // POINTZM
        } else {
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
                    u = htole64(u); std::memcpy(ptr, &u, 8); ptr += 8;
                }
            }
            row.add_bytes(traj_wkb.data(), size);
        }
        copy_.add_row(row);
    } else {
        char centroid_wkt[128];
        snprintf(centroid_wkt, sizeof(centroid_wkt), "POINTZM(%.10f %.10f %.10f %.10f)",
            rec.centroid[0], rec.centroid[1], rec.centroid[2], rec.centroid[3]);
        
        std::string traj_wkt;
        if (rec.trajectory.size() > 1) {
            std::ostringstream ss; ss << "LINESTRINGZM(";
            for (size_t i = 0; i < rec.trajectory.size(); ++i) {
                if (i > 0) ss << ",";
                const auto& p = rec.trajectory[i];
                ss << std::fixed << std::setprecision(10) << p[0] << " " << p[1] << " " << p[2] << " " << p[3];
            }
            ss << ")"; traj_wkt = ss.str();
        }

        copy_.add_row({
            hash_to_uuid(rec.id),
            hash_to_uuid(rec.hilbert_index),
            centroid_wkt,
            traj_wkt.empty() ? (rec.trajectory.size() == 1 ? centroid_wkt : "\\N") : traj_wkt
        });
    }
}

} // namespace Hartonomous