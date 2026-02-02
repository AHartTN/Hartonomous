#include <storage/physicality_store.hpp>
#include <iomanip>
#include <sstream>
#include <endian.h>
#include <cstring>
#include <iostream>

namespace Hartonomous {

PhysicalityStore::PhysicalityStore(PostgresConnection& db) : copy_(db) {
    copy_.begin_table("hartonomous.physicality", {"id", "hilbert", "centroid"});
}

std::string PhysicalityStore::hash_to_uuid(const BLAKE3Pipeline::Hash& hash) {
    std::ostringstream ss;
    ss << std::hex << std::setfill('0');
    for (int i = 0; i < 16; ++i) {
        if (i == 4 || i == 6 || i == 8 || i == 10) ss << '-';
        ss << std::setw(2) << (static_cast<unsigned>(hash[i]) & 0xFF);
    }
    return ss.str();
}

std::string PhysicalityStore::geom_to_hex(const Eigen::Vector4d& pt) {
    // Standard WKB for POINT ZM (37 bytes)
    // Endian(1) + Type(4) + X(8) + Y(8) + Z(8) + M(8)
    // Type = POINT(1) | HasZ(0x80000000) | HasM(0x40000000) = 0xC0000001
    
    uint8_t buf[37];
    buf[0] = 0x01; // Little Endian

    uint32_t type_le = htole32(0xC0000001u);
    std::memcpy(buf + 1, &type_le, 4);

    double coords[4] = { pt[0], pt[1], pt[2], pt[3] };
    for (int i = 0; i < 4; ++i) {
        uint64_t u;
        std::memcpy(&u, &coords[i], 8);
        uint64_t u_le = htole64(u);
        std::memcpy(buf + 5 + i * 8, &u_le, 8);
    }

    std::ostringstream ss;
    ss << std::hex << std::setfill('0');
    for (int i = 0; i < 37; ++i) {
        ss << std::setw(2) << (static_cast<unsigned>(buf[i]) & 0xFF);
    }

    return ss.str(); 
}

void PhysicalityStore::store(const PhysicalityRecord& rec) {
    std::string uuid = hash_to_uuid(rec.id);
    if (seen_.count(uuid)) return;

    copy_.add_row({
        uuid,
        rec.hilbert_index.to_string(),
        geom_to_hex(rec.centroid)
    });
    seen_.insert(uuid);
}

void PhysicalityStore::flush() {
    copy_.flush();
}

}
