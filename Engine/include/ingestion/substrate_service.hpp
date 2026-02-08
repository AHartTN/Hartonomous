#pragma once

#include <hashing/blake3_pipeline.hpp>
#include <spatial/hilbert_curve_4d.hpp>
#include <storage/atom_lookup.hpp>
#include <ingestion/substrate_batch.hpp>
#include <utils/unicode.hpp>
#include <Eigen/Core>
#include <vector>
#include <string>
#include <cstring>
#include <algorithm>

namespace Hartonomous {

/**
 * @brief Thread-safe service for computing substrate identities and geometries.
 */
class SubstrateService {
public:
    struct CachedComp {
        BLAKE3Pipeline::Hash comp_id;
        BLAKE3Pipeline::Hash phys_id;
        Eigen::Vector4d centroid;
        bool valid = false;
    };

    struct ComputedComp {
        CompositionRecord comp;
        std::vector<CompositionSequenceRecord> seq;
        PhysicalityRecord phys;
        CachedComp cache_entry;
        bool valid = false;
    };

    struct ComputedRelation {
        RelationRecord rel;
        PhysicalityRecord phys;
        std::vector<RelationSequenceRecord> seq;
        RelationRatingRecord rating;
        RelationEvidenceRecord evidence;
        bool valid = false;
    };

    /**
     * @brief Compute composition identity and S3 geometry from text.
     */
    static ComputedComp compute_comp(const std::string& text, AtomLookup& lookup) {
        if (text.empty()) return {};
        std::u32string utf32 = utf8_to_utf32(text);
        
        std::vector<BLAKE3Pipeline::Hash> atom_ids;
        std::vector<Eigen::Vector4d> positions;
        atom_ids.reserve(utf32.size());
        positions.reserve(utf32.size());

        for (char32_t cp : utf32) {
            auto info = lookup.lookup(cp);
            if (info) {
                atom_ids.push_back(info->id);
                positions.push_back(info->position);
            }
        }
        if (atom_ids.empty()) return {};

        // 1. Composition ID: BLAKE3(0x43 + atom_ids)
        size_t c_len = 1 + atom_ids.size() * 16;
        std::vector<uint8_t> c_data(c_len);
        c_data[0] = 0x43;
        for (size_t k = 0; k < atom_ids.size(); ++k)
            std::memcpy(c_data.data() + 1 + k * 16, atom_ids[k].data(), 16);
        auto cid = BLAKE3Pipeline::hash(c_data.data(), c_len);

        // 2. Centroid (S3 projection)
        Eigen::Vector4d centroid = Eigen::Vector4d::Zero();
        for (const auto& p : positions) centroid += p;
        centroid /= static_cast<double>(positions.size());
        double norm = centroid.norm();
        if (norm > 1e-10) centroid /= norm; else centroid = Eigen::Vector4d(1, 0, 0, 0);

        // 3. Physicality ID: BLAKE3(0x50 + centroid + trajectory)
        size_t p_len = 1 + sizeof(double) * 4 + positions.size() * sizeof(double) * 4;
        std::vector<uint8_t> p_data(p_len);
        p_data[0] = 0x50;
        std::memcpy(p_data.data() + 1, centroid.data(), sizeof(double) * 4);
        for (size_t k = 0; k < positions.size(); ++k)
            std::memcpy(p_data.data() + 1 + sizeof(double) * 4 + k * sizeof(double) * 4, positions[k].data(), sizeof(double) * 4);
        auto pid = BLAKE3Pipeline::hash(p_data.data(), p_len);

        Eigen::Vector4d hc;
        for (int k = 0; k < 4; ++k) hc[k] = (centroid[k] + 1.0) / 2.0;

        ComputedComp res;
        res.comp = {cid, pid};
        res.phys = {pid, hartonomous::spatial::HilbertCurve4D::encode(hc, hartonomous::spatial::HilbertCurve4D::EntityType::Composition), centroid, decimate_trajectory(positions)};
        res.cache_entry = {cid, pid, centroid, true};
        res.valid = true;

        for (size_t i = 0; i < atom_ids.size(); ) {
            uint32_t ord = static_cast<uint32_t>(i);
            uint32_t occ = 1;
            while (i + occ < atom_ids.size() && atom_ids[i + occ] == atom_ids[i]) ++occ;
            
            uint8_t sdata[37];
            sdata[0] = 0x53;
            std::memcpy(sdata + 1, cid.data(), 16);
            std::memcpy(sdata + 17, atom_ids[i].data(), 16);
            std::memcpy(sdata + 33, &ord, 4);
            res.seq.push_back({BLAKE3Pipeline::hash(sdata, 37), cid, atom_ids[i], ord, occ});
            i += occ;
        }
        return res;
    }

    /**
     * @brief Compute relation identity and geometry between two compositions.
     */
    static ComputedRelation compute_relation(const CachedComp& a, const CachedComp& b, 
                                            const BLAKE3Pipeline::Hash& content_id, double base_rating = 1500.0) {
        if (!a.valid || !b.valid || a.comp_id == b.comp_id) return {};

        bool a_first = std::memcmp(a.comp_id.data(), b.comp_id.data(), 16) < 0;
        uint8_t r_in[33];
        r_in[0] = 0x52;
        if (a_first) {
            std::memcpy(r_in + 1, a.comp_id.data(), 16);
            std::memcpy(r_in + 17, b.comp_id.data(), 16);
        } else {
            std::memcpy(r_in + 1, b.comp_id.data(), 16);
            std::memcpy(r_in + 17, a.comp_id.data(), 16);
        }
        auto rid = BLAKE3Pipeline::hash(r_in, 33);

        Eigen::Vector4d r_centroid = (a.centroid + b.centroid) * 0.5;
        double norm = r_centroid.norm();
        if (norm > 1e-10) r_centroid /= norm; else r_centroid = Eigen::Vector4d(1, 0, 0, 0);

        std::vector<Eigen::Vector4d> r_traj = {a.centroid, b.centroid};
        size_t p_len = 1 + sizeof(double) * 4 + r_traj.size() * sizeof(double) * 4;
        std::vector<uint8_t> p_data(p_len);
        p_data[0] = 0x50;
        std::memcpy(p_data.data() + 1, r_centroid.data(), sizeof(double) * 4);
        for (size_t k = 0; k < r_traj.size(); ++k)
            std::memcpy(p_data.data() + 1 + sizeof(double) * 4 + k * sizeof(double) * 4, r_traj[k].data(), sizeof(double) * 4);
        auto pid = BLAKE3Pipeline::hash(p_data.data(), p_len);

        Eigen::Vector4d hc;
        for (int k = 0; k < 4; ++k) hc[k] = (r_centroid[k] + 1.0) / 2.0;

        ComputedRelation res;
        res.rel = {rid, pid};
        res.phys = {pid, hartonomous::spatial::HilbertCurve4D::encode(hc, hartonomous::spatial::HilbertCurve4D::EntityType::Relation), r_centroid, r_traj};
        res.rating = {rid, 1, base_rating, 32.0};
        res.valid = true;

        for (uint32_t k = 0; k < 2; ++k) {
            const auto& cid = (k == 0) ? (a_first ? a.comp_id : b.comp_id) : (a_first ? b.comp_id : a.comp_id);
            uint8_t rs_data[37];
            rs_data[0] = 0x54;
            std::memcpy(rs_data + 1, rid.data(), 16);
            std::memcpy(rs_data + 17, cid.data(), 16);
            std::memcpy(rs_data + 33, &k, 4);
            res.seq.push_back({BLAKE3Pipeline::hash(rs_data, 37), rid, cid, k, 1});
        }

        uint8_t ev_data[32];
        std::memcpy(ev_data, content_id.data(), 16);
        std::memcpy(ev_data + 16, rid.data(), 16);
        res.evidence = {BLAKE3Pipeline::hash(ev_data, 32), content_id, rid, true, base_rating, 1.0};
        return res;
    }

    /**
     * @brief Decimate long trajectories to keep storage and GIST index costs constant.
     */
    static std::vector<Eigen::Vector4d> decimate_trajectory(const std::vector<Eigen::Vector4d>& pts) {
        static constexpr size_t MAX_PTS = 16;
        if (pts.size() <= MAX_PTS) return pts;
        std::vector<Eigen::Vector4d> res;
        res.reserve(MAX_PTS);
        for (size_t i = 0; i < MAX_PTS; ++i) {
            size_t idx = (i * (pts.size() - 1)) / (MAX_PTS - 1);
            res.push_back(pts[idx]);
        }
        return res;
    }
};

} // namespace Hartonomous