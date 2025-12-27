#pragma once

/// @file namespaces.hpp
/// @brief Namespace organization and aliases for the Hartonomous library.
///
/// The Hartonomous library uses a flat `hartonomous` namespace for simplicity,
/// but provides sub-namespace aliases for logical grouping when desired.
///
/// ## Usage Patterns
///
/// ### Flat namespace (recommended for most uses):
/// @code
/// using namespace hartonomous;
/// AtomId id = SemanticDecompose::get_atom_id('A');
/// @endcode
///
/// ### Sub-namespaces (for explicit organization):
/// @code
/// using namespace hartonomous;
/// AtomId id = atoms::SemanticDecompose::get_atom_id('A');
/// auto point = geometry::PointZM{1.0, 2.0, 3.0, 4.0};
/// @endcode
///
/// ## Module Organization
///
/// | Sub-namespace | Directory   | Purpose                              |
/// |---------------|-------------|--------------------------------------|
/// | atoms         | src/atoms/  | Core atom types and semantic encoding|
/// | geometry      | src/geometry| PostGIS-compatible geometry types    |
/// | hilbert       | src/hilbert | Hilbert curve encoding               |
/// | unicode       | src/unicode | Unicode character handling           |
/// | db            | src/db/     | Database operations                  |
/// | types         | src/types/  | Fundamental types (Int128, etc.)     |

namespace hartonomous {

// Forward declarations for namespace aliases
class AtomId;
class SemanticDecompose;
class SemanticHilbert;
class SemanticCoord;
class PairEncoding;
class ByteAtomTable;

class PointZM;
class LineStringZM;
class WeightedEdge;
class SunflowerSpiral;
class TesseractSurface;
class FibonacciLattice4D;

class HilbertEncoder;
class HilbertCurve4D;

class CanonicalBase;
class SemanticOrdering;
class CodepointMapper;
struct UnicodeRanges;

struct UInt128;

/// Sub-namespace aliases for logical grouping.
/// These reference the main namespace for backward compatibility.
namespace atoms = hartonomous;
namespace geometry = hartonomous;
namespace hilbert = hartonomous;
namespace unicode = hartonomous;
namespace types = hartonomous;

} // namespace hartonomous

// Also provide top-level aliases for explicit access
namespace hartonomous_atoms = hartonomous;
namespace hartonomous_geometry = hartonomous;
namespace hartonomous_hilbert = hartonomous;
namespace hartonomous_unicode = hartonomous;
