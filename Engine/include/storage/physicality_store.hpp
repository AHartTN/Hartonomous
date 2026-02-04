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
    std::vector<Eigen::Vector4d> trajectory; // Raw points for WKB generation
};

class PhysicalityStore {
public:
    // use_temp_table=false for direct COPY (fast, requires pre-deduplication)
    explicit PhysicalityStore(PostgresConnection& db, bool use_temp_table = true, bool use_binary = false);
    void store(const PhysicalityRecord& rec);
    void flush();

private:
    BulkCopy copy_;
    bool use_dedup_;
    bool use_binary_;
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> seen_;
    std::string hash_to_uuid(const BLAKE3Pipeline::Hash& hash);
    std::string hilbert_to_hex(const HilbertIndex& h);
    std::string geom_to_hex(const Eigen::Vector4d& pt);
};

}
