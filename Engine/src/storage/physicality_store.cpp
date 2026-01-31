#include <storage/physicality_store.hpp>
#include <iomanip>
#include <sstream>
#include <endian.h>

extern "C" {
#include <liblwgeom.h>
}

namespace Hartonomous {

PhysicalityStore::PhysicalityStore(PostgresConnection& db) : copy_(db) {
    copy_.begin_table("Physicality", {"Id", "Hilbert", "Centroid"});
}

std::string PhysicalityStore::hash_to_uuid(const BLAKE3Pipeline::Hash& hash) {
    std::ostringstream ss;
    ss << std::hex << std::setfill('0');
    for (int i = 0; i < 16; ++i) {
        if (i == 4 || i == 6 || i == 8 || i == 10) ss << '-';
        ss << std::setw(2) << static_cast<int>(hash[i]);
    }
    return ss.str();
}

std::string PhysicalityStore::hilbert_to_hex(const HilbertIndex& h) {
    uint64_t hi = htobe64(h.hi);
    uint64_t lo = htobe64(h.lo);

    std::ostringstream ss;
    ss << "\\\\x" << std::hex << std::setfill('0')
       << std::setw(16) << hi << std::setw(16) << lo;
    return ss.str();
}

std::string PhysicalityStore::geom_to_hex(const Eigen::Vector4d& pt) {
    POINT4D p4d;
    p4d.x = pt[0];
    p4d.y = pt[1];
    p4d.z = pt[2];
    p4d.m = pt[3];

    LWPOINT* lwpt = lwpoint_make4d(0, p4d.x, p4d.y, p4d.z, p4d.m);
    FLAGS_SET_Z(lwpt->flags, 1);
    FLAGS_SET_M(lwpt->flags, 1);

    LWGEOM* geom = lwpoint_as_lwgeom(lwpt);
    lwgeom_set_srid(geom, 0);

    size_t size;
    GSERIALIZED* gser = gserialized_from_lwgeom(geom, &size);
    uint8_t* wkb = (uint8_t*)gser;

    std::ostringstream ss;
    ss << "\\\\x" << std::hex << std::setfill('0');
    for (size_t i = 0; i < size; ++i) {
        ss << std::setw(2) << static_cast<int>(wkb[i]);
    }

    lwgeom_free(geom);
    lwfree(gser);

    return ss.str();
}

void PhysicalityStore::store(const PhysicalityRecord& rec) {
    std::string uuid = hash_to_uuid(rec.id);
    if (seen_.count(uuid)) return;

    copy_.add_row({
        uuid,
        hilbert_to_hex(rec.hilbert_index),
        geom_to_hex(rec.centroid)
    });
    seen_.insert(uuid);
}

void PhysicalityStore::flush() {
    copy_.flush();
}

}
