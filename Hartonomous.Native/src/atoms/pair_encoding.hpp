#pragma once

// Cascading Pair Encoding (CPE) - Aggregation Header
//
// This header provides convenient access to all pair encoding types.
// Pair encoding is a compression technique that builds a Merkle DAG
// from byte sequences, storing each unique composition exactly once.
//
// Two engines are available:
// - PairEncodingCascade: Fast parallel tree building (no vocabulary learning)
// - PairEncodingEngine: BPE-style vocabulary learning for better compression
//
// Each type is defined in its own header per single-responsibility principle.

#include "hash_utils.hpp"
#include "byte_atom_table.hpp"
#include "rle_sequence.hpp"
#include "pair_frequency_counter.hpp"
#include "composition_store.hpp"
#include "work_unit.hpp"
#include "pair_encoding_config.hpp"
#include "pair_encoding_cascade.hpp"
#include "pair_encoding_engine.hpp"
