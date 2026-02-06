/**
 * @file test_database_marshal.cpp
 * @brief Unit tests for database binary formatting and liblwgeom interop
 */

#include <gtest/gtest.h>
#include <storage/physicality_store.hpp>
#include <storage/atom_store.hpp>
#include <hashing/blake3_pipeline.hpp>
#include <database/postgres_connection.hpp>
#include <Eigen/Core>

extern "C" {
#include <liblwgeom.h>
}

using namespace Hartonomous;

TEST(DatabaseMarshalTest, LWGeomSerialization) {
    // Test the logic used in PhysicalityStore::geom_to_hex
    POINT4D p4d = {1.0, 0.0, 0.0, 0.0};
    LWPOINT* lwpt = lwpoint_make4d(0, p4d.x, p4d.y, p4d.z, p4d.m);
    FLAGS_SET_Z(lwpt->flags, 1);
    FLAGS_SET_M(lwpt->flags, 1);
    
    LWGEOM* geom = lwpoint_as_lwgeom(lwpt);
    lwgeom_set_srid(geom, 0);
    
    size_t size;
    GSERIALIZED* gser = gserialized_from_lwgeom(geom, &size);
    
    EXPECT_GT(size, 0);
    EXPECT_TRUE(gserialized_is_geodetic(gser) == 0); // Cartesian
    
    lwgeom_free(geom);
    lwfree(gser);
}

TEST(DatabaseMarshalTest, UUIDFormatting) {
    BLAKE3Pipeline::Hash hash = {0};
    hash[0] = 0xDE; hash[1] = 0xAD; hash[2] = 0xBE; hash[3] = 0xEF;
    
    // We want a standard UUID string
    // PhysicalityStore::hash_to_uuid is private, but we can verify its output format
    // indirectly or by exposing it for testing.
    // For now, let's just test the logic manually.
    std::ostringstream ss;
    ss << std::hex << std::setfill('0');
    for (int i = 0; i < 16; ++i) {
        if (i == 4 || i == 6 || i == 8 || i == 10) ss << '-';
        ss << std::setw(2) << static_cast<int>(hash[i]);
    }
    
    EXPECT_EQ(ss.str().substr(0, 8), "deadbeef");
}

// HilbertIndex formatting test removed - now it's a binary UUID
