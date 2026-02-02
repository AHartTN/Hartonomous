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
    // Manual EWKB construction for POINT ZM with SRID=0
    // Layout: 1 byte endian + 4 bytes type + 4 bytes srid + 4*8 bytes coords = 41 bytes
    uint8_t buf[41];
    buf[0] = 0x01; // little-endian marker

    // Type with Z, M, SRID bits set
    // POINT (1) | HasZ (0x80000000) | HasM (0x40000000) | HasSRID (0x20000000) = 0xE0000001
    uint32_t type_le = htole32(0xE0000001u);
    std::memcpy(buf + 1, &type_le, sizeof(type_le));

    uint32_t srid_le = htole32(0u);
    std::memcpy(buf + 5, &srid_le, sizeof(srid_le));

    // Write doubles as little-endian uint64_t
    double coords[4] = { pt[0], pt[1], pt[2], pt[3] };
    for (int i = 0; i < 4; ++i) {
        uint64_t u;
        static_assert(sizeof(double) == sizeof(uint64_t));
        std::memcpy(&u, &coords[i], sizeof(u));
        u = htole64(u);
        std::memcpy(buf + 9 + i * 8, &u, sizeof(u));
    }

    std::ostringstream ss;
    ss << std::hex << std::setfill('0');
    for (int i = 0; i < 41; ++i) {
        ss << std::setw(2) << (static_cast<unsigned>(buf[i]) & 0xFF);
    }
    
    // DEBUG
    // std::cout << "EWKB Hex: " << ss.str() << std::endl;

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
