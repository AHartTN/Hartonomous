#pragma once

#include <storage/substrate_store.hpp>
#include <spatial/hilbert_curve_4d.hpp>
#include <Eigen/Core>

namespace Hartonomous {

using HilbertIndex = hartonomous::spatial::HilbertCurve4D::HilbertIndex;

struct PhysicalityRecord {
    BLAKE3Pipeline::Hash id;
    HilbertIndex hilbert_index;
    Eigen::Vector4d centroid;
    std::vector<Eigen::Vector4d> trajectory; 
};

class PhysicalityStore : public SubstrateStore<PhysicalityRecord> {
public:
    explicit PhysicalityStore(PostgresConnection& db, bool use_temp_table = true, bool use_binary = false);
    void store(const PhysicalityRecord& rec) override;

private:
    std::string geom_to_hex(const Eigen::Vector4d& pt);
};

}
