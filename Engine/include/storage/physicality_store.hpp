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
};

class PhysicalityStore {
public:
    explicit PhysicalityStore(PostgresConnection& db);
    void store(const PhysicalityRecord& rec);
    void flush();

private:
    BulkCopy copy_;
    std::unordered_set<std::string> seen_;
    std::string hash_to_uuid(const BLAKE3Pipeline::Hash& hash);
    std::string hilbert_to_hex(const HilbertIndex& h);
    std::string geom_to_hex(const Eigen::Vector4d& pt);
};

}
