"""
Quick validation test for ingestion pipeline.
Tests atomization, compression, and landmark projection.
"""

import numpy as np
from pathlib import Path
import sys

# Add src to path
sys.path.insert(0, str(Path(__file__).parent / "src"))

from core.atomization import Atomizer, ModalityType
from core.landmark import LandmarkProjector
from core.compression import AtomCompressor


def test_atomization():
    """Test basic atomization."""
    print("Testing atomization...")

    atomizer = Atomizer()

    # Test with simple array
    data = np.array([1.0, 2.0, 3.0, 4.0, 5.0], dtype=np.float64)
    atoms = atomizer.atomize_array(data, ModalityType.MODEL_WEIGHT)

    print(f"  ✅ Created {len(atoms)} atoms from {len(data)} values")
    print(f"  ✅ Atom size: {len(atoms[0].data)} bytes")
    print(f"  ✅ Deduplication cache: {len(atomizer.atom_cache)} unique atoms")

    # Test reassembly
    reconstructed = atomizer.reassemble_from_atoms(atoms, data.shape)
    assert np.allclose(data, reconstructed), "Reassembly failed"
    print("  ✅ Reassembly successful")

    return True


def test_compression():
    """Test compression strategies."""
    print("\nTesting compression...")

    compressor = AtomCompressor()

    # Test sparse data
    sparse_data = np.array([0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 2.0, 0.0], dtype=np.float64)
    compressed = compressor.compress(sparse_data, sparse_threshold=1e-6)

    print(f"  ✅ Compression type: {compressed.compression_type}")
    print(f"  ✅ Compression ratio: {compressed.ratio:.2f}x")
    print(f"  ✅ Original size: {compressed.original_size} bytes")
    print(f"  ✅ Compressed size: {compressed.compressed_size} bytes")

    # Test decompression
    decompressed = compressor.decompress(compressed)
    assert np.allclose(sparse_data, decompressed), "Decompression failed"
    print("  ✅ Decompression successful")

    return True


def test_landmark_projection():
    """Test landmark projection."""
    print("\nTesting landmark projection...")

    projector = LandmarkProjector()

    # Test with model weights - use actual API
    weights = np.random.randn(10, 10).astype(np.float64)

    # Project from content
    landmark_pos = projector.project_from_content(
        weights.flatten()[:100], modality="model", category="weight"  # Use subset
    )

    print(f"  ✅ Generated landmark position")
    print(
        f"  ✅ Position: x={landmark_pos.x:.3f}, y={landmark_pos.y:.3f}, z={landmark_pos.z:.3f}"
    )
    print(f"  ✅ Landmark type: {landmark_pos.landmark_type}")

    # Test multiple projections
    for i in range(3):
        test_data = np.random.randn(50).astype(np.float64)
        pos = projector.project_from_content(
            test_data, modality="model", category="activation"
        )
        print(f"  ✅ Projection {i+1}: ({pos.x:.3f}, {pos.y:.3f}, {pos.z:.3f})")

    return True


def test_end_to_end():
    """Test end-to-end pipeline."""
    print("\nTesting end-to-end pipeline...")

    atomizer = Atomizer()
    projector = LandmarkProjector()

    # Simulate small model weights
    layer_weights = np.random.randn(5, 5).astype(np.float64)

    # Atomize
    atoms = atomizer.atomize_array(layer_weights, ModalityType.MODEL_WEIGHT)
    print(f"  ✅ Created {len(atoms)} atoms")

    # Project to landmarks
    landmark_positions = []
    for i in range(min(3, len(atoms))):
        atom = atoms[i]
        # Decompress atom data for projection
        from core.compression import CompressionResult

        comp_result = CompressionResult(
            data=atom.data,
            compression_type=atom.compression_type,
            metadata=atom.metadata,
            original_size=0,
            compressed_size=len(atom.data),
        )
        decompressed = atomizer.compressor.decompress(comp_result)

        # Project
        pos = projector.project_from_content(
            decompressed, modality="model", category="weight"
        )
        landmark_positions.append(pos)

    print(f"  ✅ Created {len(landmark_positions)} landmark positions")

    # Simulate associations
    associations = []
    for atom in atoms:
        for pos in landmark_positions:
            # Calculate Euclidean distance
            # In real system, this would use PostGIS distance functions
            dist = np.sqrt(pos.x**2 + pos.y**2 + pos.z**2)
            associations.append(
                {"atom_id": atom.content_hash, "landmark": pos, "distance": dist}
            )

    print(f"  ✅ Created {len(associations)} associations")
    print(
        f"  ✅ Average distance: {np.mean([a['distance'] for a in associations]):.3f}"
    )

    return True


def main():
    """Run all tests."""
    print("=" * 60)
    print("Hartonomous Implementation Validation")
    print("=" * 60)

    try:
        test_atomization()
        test_compression()
        test_landmark_projection()
        test_end_to_end()

        print("\n" + "=" * 60)
        print("✅ ALL TESTS PASSED")
        print("=" * 60)
        return 0

    except Exception as e:
        print(f"\n❌ TEST FAILED: {e}")
        import traceback

        traceback.print_exc()
        return 1


if __name__ == "__main__":
    sys.exit(main())
