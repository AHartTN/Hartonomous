#pragma once

#include <hashing/blake3_pipeline.hpp>
#include <database/bulk_copy.hpp>
#include <spatial/hilbert_curve_4d.hpp>
#include <Eigen/Core>
#include <unordered_set>

namespace Hartonomous {

using HilbertIndex = hartonomous::spatial::HilbertCurve4D::HilbertIndex;

struct PhysicalityRecord {
    BLAKE3Pipeline::Hash id;
    HilbertIndex hilbert_index;
    Eigen::Vector4d centroid;
    std::string trajectory_wkt; // New: LINESTRINGZM(...) or POINTZM(...)
};

class PhysicalityStore {
public:
    // use_temp_table=false for direct COPY (fast, requires pre-deduplication)
    explicit PhysicalityStore(PostgresConnection& db, bool use_temp_table = true);
    void store(const PhysicalityRecord& rec);
    void flush();

private:
    BulkCopy copy_;
    bool use_dedup_;
    std::unordered_set<std::string> seen_;
    std::string hash_to_uuid(const BLAKE3Pipeline::Hash& hash);
    std::string hilbert_to_hex(const HilbertIndex& h);
    std::string geom_to_hex(const Eigen::Vector4d& pt);
};

}
