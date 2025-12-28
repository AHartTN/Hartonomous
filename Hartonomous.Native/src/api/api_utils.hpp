#pragma once

/// API UTILITIES - Common patterns for C FFI exports
///
/// Provides:
/// - SAFE_API_CALL macro for consistent error handling
/// - Buffer copy utilities with size validation
/// - Type conversion helpers (C++ → C structs)
///
/// Eliminates duplicated boilerplate across export functions.

#include "exports.h"
#include "../db/types.hpp"
#include "../atoms/node_ref.hpp"
#include <cstring>
#include <string>
#include <vector>
#include <algorithm>

namespace hartonomous::api {

// =============================================================================
// ERROR CODES
// =============================================================================

/// Standard API error codes
enum class ApiError : int {
    Success = 0,
    NullPointer = -1,
    InvalidArgument = -2,
    BufferTooSmall = -3,
    NotFound = -4,
    DatabaseError = -5,
    InternalError = -6,
    NotInitialized = -7
};

// =============================================================================
// SAFE API CALL MACRO
// =============================================================================

/// Wrap C++ code in try/catch with standard error code returns.
/// Usage:
///   SAFE_API_CALL({
///       auto& store = get_store();
///       // ... do work ...
///       return 0;
///   })
#define SAFE_API_CALL(code) \
    try { \
        code \
    } catch (const std::bad_alloc&) { \
        return static_cast<int>(hartonomous::api::ApiError::InternalError); \
    } catch (const std::exception&) { \
        return static_cast<int>(hartonomous::api::ApiError::DatabaseError); \
    } catch (...) { \
        return static_cast<int>(hartonomous::api::ApiError::InternalError); \
    }

/// Validate pointer is not null
#define VALIDATE_PTR(ptr) \
    if (!(ptr)) return static_cast<int>(hartonomous::api::ApiError::NullPointer)

/// Validate capacity is positive
#define VALIDATE_CAPACITY(cap) \
    if ((cap) <= 0) return static_cast<int>(hartonomous::api::ApiError::InvalidArgument)

// =============================================================================
// BUFFER UTILITIES
// =============================================================================

/// Copy string to buffer with size validation.
/// @param src Source string
/// @param buffer Target buffer
/// @param capacity Buffer capacity
/// @param out_len Pointer to receive actual length
/// @return 0 on success, -3 if buffer too small
inline int copy_string_to_buffer(
    const std::string& src,
    char* buffer,
    std::int32_t capacity,
    std::int32_t* out_len)
{
    *out_len = static_cast<std::int32_t>(src.size());
    
    if (static_cast<std::int32_t>(src.size()) > capacity) {
        return static_cast<int>(ApiError::BufferTooSmall);
    }
    
    std::memcpy(buffer, src.data(), src.size());
    return 0;
}

/// Copy vector to C array with capacity check.
/// @param src Source vector
/// @param dest Destination array
/// @param capacity Array capacity
/// @param out_count Pointer to receive actual count
/// @param converter Function to convert each element
template<typename TSrc, typename TDest, typename Converter>
void copy_to_array(
    const std::vector<TSrc>& src,
    TDest* dest,
    std::int32_t capacity,
    std::int32_t* out_count,
    Converter converter)
{
    std::int32_t count = static_cast<std::int32_t>(
        std::min(src.size(), static_cast<std::size_t>(capacity)));
    
    for (std::int32_t i = 0; i < count; ++i) {
        converter(src[i], dest[i]);
    }
    
    *out_count = count;
}

// =============================================================================
// TYPE CONVERTERS
// =============================================================================

/// Convert SpatialMatch to HartonomousSpatialMatch
inline void convert_spatial_match(
    const db::SpatialMatch& src,
    HartonomousSpatialMatch& dest)
{
    dest.hilbert_high = src.hilbert_high;
    dest.hilbert_low = src.hilbert_low;
    dest.codepoint = src.codepoint;
    dest.distance = src.distance;
}

/// Convert Relationship to HartonomousRelationship
inline void convert_relationship(
    const db::Relationship& src,
    HartonomousRelationship& dest)
{
    dest.from_high = src.from.id_high;
    dest.from_low = src.from.id_low;
    dest.to_high = src.to.id_high;
    dest.to_low = src.to.id_low;
    dest.weight = src.weight;
    dest.obs_count = src.obs_count;
    dest.rel_type = static_cast<std::int16_t>(src.rel_type);
    dest.context_high = src.context.id_high;
    dest.context_low = src.context.id_low;
}

/// Convert TrajectoryPoint to HartonomousTrajectoryPoint
inline void convert_trajectory_point(
    const db::TrajectoryPoint& src,
    HartonomousTrajectoryPoint& dest)
{
    dest.page = src.page;
    dest.type = src.type;
    dest.base = src.base;
    dest.variant = src.variant;
    dest.count = src.count;
}

/// Convert HartonomousTrajectoryPoint to TrajectoryPoint
inline db::TrajectoryPoint convert_from_c_trajectory_point(
    const HartonomousTrajectoryPoint& src)
{
    db::TrajectoryPoint pt;
    pt.page = src.page;
    pt.type = src.type;
    pt.base = src.base;
    pt.variant = src.variant;
    pt.count = src.count;
    return pt;
}

/// Build Trajectory from C array
inline db::Trajectory build_trajectory_from_c(
    const HartonomousTrajectoryPoint* points,
    std::int32_t point_count,
    double weight = 1.0)
{
    db::Trajectory traj;
    traj.weight = weight;
    traj.points.reserve(static_cast<std::size_t>(point_count));
    
    for (std::int32_t i = 0; i < point_count; ++i) {
        traj.points.push_back(convert_from_c_trajectory_point(points[i]));
    }
    
    return traj;
}

/// Create NodeRef from high/low parts
inline NodeRef make_node_ref(std::int64_t high, std::int64_t low) {
    return NodeRef::comp(high, low);
}

// =============================================================================
// RESULT COPY HELPERS
// =============================================================================

/// Copy spatial matches to C array
inline void copy_spatial_matches(
    const std::vector<db::SpatialMatch>& matches,
    HartonomousSpatialMatch* results,
    std::int32_t capacity,
    std::int32_t* count)
{
    copy_to_array(matches, results, capacity, count, convert_spatial_match);
}

/// Copy relationships to C array
inline void copy_relationships(
    const std::vector<db::Relationship>& rels,
    HartonomousRelationship* results,
    std::int32_t capacity,
    std::int32_t* count)
{
    copy_to_array(rels, results, capacity, count, convert_relationship);
}

/// Copy NodeRefs to paired int64 array (high, low interleaved)
inline void copy_node_refs_interleaved(
    const std::vector<NodeRef>& refs,
    std::int64_t* results,
    std::int32_t capacity,
    std::int32_t* count)
{
    std::int32_t max_count = static_cast<std::int32_t>(
        std::min(refs.size(), static_cast<std::size_t>(capacity)));
    
    for (std::int32_t i = 0; i < max_count; ++i) {
        results[i * 2] = refs[i].id_high;
        results[i * 2 + 1] = refs[i].id_low;
    }
    
    *count = max_count;
}

} // namespace hartonomous::api
