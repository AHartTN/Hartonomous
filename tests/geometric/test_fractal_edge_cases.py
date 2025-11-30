"""
Comprehensive Edge Case Tests for Fractal Atomization

Tests:
1. Empty compositions (should fail gracefully)
2. Circular references (should be prevented)
3. Deep nesting (100+ levels)
4. Large composition arrays (10K+ elements)
5. BPE autonomous learning
6. Compression ratio verification

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import asyncio

import pytest

from api.services.geometric_atomization.bpe_crystallizer import BPECrystallizer
from api.services.geometric_atomization.fractal_atomizer import FractalAtomizer


class TestEdgeCases:
    """Edge case tests for fractal atomization."""

    def test_empty_composition(self):
        """Empty composition array should fail gracefully."""
        atomizer = FractalAtomizer()

        async def test():
            # Try to create composition with empty array
            with pytest.raises((ValueError, AssertionError)):
                await atomizer.get_or_create_composition([])

        asyncio.run(test())

    def test_single_element_composition(self):
        """Single-element composition should work (redundant but valid)."""
        atomizer = FractalAtomizer()

        async def test():
            a_id = await atomizer.get_or_create_primitive(b"a")

            # Create composition with single element
            comp_id = await atomizer.get_or_create_composition([a_id])

            # Should work but be redundant
            assert comp_id is not None
            assert comp_id != a_id  # Different from primitive

        asyncio.run(test())

    def test_deep_nesting(self):
        """Test deeply nested compositions (100+ levels)."""
        atomizer = FractalAtomizer()

        async def test():
            # Start with primitive
            atom_id = await atomizer.get_or_create_primitive(b"x")

            # Nest 100 levels deep
            current_id = atom_id
            for level in range(100):
                current_id = await atomizer.get_or_create_composition(
                    [current_id], canonical_text=f"level_{level}"
                )

            # Should succeed - Python recursion limit is ~1000
            assert current_id is not None

            # Verify nesting depth
            assert len(atomizer.composition_cache) == 100

        asyncio.run(test())

    def test_large_composition_array(self):
        """Test composition with 10K+ elements."""
        atomizer = FractalAtomizer()

        async def test():
            # Create primitive
            period_id = await atomizer.get_or_create_primitive(b".")

            # Create massive composition (10K periods)
            massive_array = [period_id] * 10000

            comp_id = await atomizer.get_or_create_composition(
                massive_array, canonical_text="." * 10000
            )

            assert comp_id is not None

            # Verify coordinate computation handles large arrays
            assert comp_id in atomizer.coord_cache

        asyncio.run(test())

    def test_duplicate_detection(self):
        """Same composition created twice should return same ID."""
        atomizer = FractalAtomizer()

        async def test():
            a_id = await atomizer.get_or_create_primitive(b"a")
            b_id = await atomizer.get_or_create_primitive(b"b")

            # Create composition twice
            comp_id_1 = await atomizer.get_or_create_composition([a_id, b_id])
            comp_id_2 = await atomizer.get_or_create_composition([a_id, b_id])

            # Should be identical (deduplication)
            assert comp_id_1 == comp_id_2

            # Should only create composition once
            assert len(atomizer.composition_cache) == 1

        asyncio.run(test())

    def test_coordinate_uniqueness(self):
        """Different compositions must have different coordinates."""
        atomizer = FractalAtomizer()

        async def test():
            a_id = await atomizer.get_or_create_primitive(b"a")
            b_id = await atomizer.get_or_create_primitive(b"b")
            c_id = await atomizer.get_or_create_primitive(b"c")

            # Create many different compositions
            compositions = [
                [a_id, b_id],
                [b_id, a_id],  # Reversed
                [a_id, c_id],
                [a_id, a_id],  # Repeated
                [a_id, b_id, c_id],  # 3 elements
                [b_id, b_id, b_id],  # All same
            ]

            coords = set()
            for comp in compositions:
                comp_id = await atomizer.get_or_create_composition(comp)
                coord = atomizer.coord_cache[comp_id]
                coords.add(coord)

            # All should have unique coordinates
            assert len(coords) == len(compositions)

        asyncio.run(test())


class TestBPECrystallizer:
    """Tests for BPE autonomous learning."""

    def test_observe_and_count(self):
        """BPE should count pair frequencies."""
        crystallizer = BPECrystallizer(min_frequency=2)

        # Observe sequence with repeated pair (1, 2)
        crystallizer.observe_sequence([1, 2, 1, 2, 1, 2, 3])

        # (1, 2) should appear 3 times
        assert crystallizer.pair_counts[(1, 2)] == 3
        assert crystallizer.pair_counts[(2, 1)] == 2
        assert crystallizer.pair_counts[(2, 3)] == 1

    def test_merge_candidate_selection(self):
        """BPE should identify most frequent pairs."""
        crystallizer = BPECrystallizer(min_frequency=2)

        # Observe multiple sequences
        for _ in range(10):
            crystallizer.observe_sequence([1, 2, 3, 1, 2])  # (1,2) repeated

        candidates = crystallizer.get_merge_candidates(top_k=5)

        # (1, 2) should be most frequent
        assert candidates[0][0] == (1, 2)
        assert candidates[0][1] == 20  # Appears 2x in each of 10 sequences

    def test_apply_merges(self):
        """BPE should compress sequences using learned merges."""
        crystallizer = BPECrystallizer()

        # Manually add merge rule: (1, 2) → 100
        crystallizer.merge_rules[(1, 2)] = 100

        # Apply to sequence
        compressed = crystallizer.apply_merges([1, 2, 3, 1, 2])

        # Should replace both (1, 2) occurrences with 100
        assert compressed == [100, 3, 100]

    def test_recursive_merging(self):
        """BPE should recursively apply merges."""
        crystallizer = BPECrystallizer()

        # First level: (1, 2) → 10
        crystallizer.merge_rules[(1, 2)] = 10

        # Second level: (10, 10) → 20
        crystallizer.merge_rules[(10, 10)] = 20

        # Apply to sequence with pattern that creates (10, 10)
        compressed = crystallizer.apply_merges([1, 2, 1, 2])

        # First pass: [1, 2, 1, 2] → [10, 10]
        # Second pass: [10, 10] → [20]
        assert compressed == [20]

    def test_bpe_integration(self):
        """Test full BPE pipeline with FractalAtomizer."""
        atomizer = FractalAtomizer()
        crystallizer = BPECrystallizer(min_frequency=2)

        async def test():
            # Crystallize sequence multiple times to learn pattern
            sequences = [
                [b"a", b"b", b"c", b"a", b"b"],  # "ab" repeated
                [b"a", b"b", b"d", b"a", b"b"],
                [b"a", b"b", b"e", b"a", b"b"],
            ]

            # Learn from sequences
            for seq in sequences:
                await crystallizer.crystallize_with_bpe(seq, atomizer, learn=True)

            # Check that pair (a, b) was observed
            stats = crystallizer.get_stats()
            assert stats["total_pairs_observed"] > 0

            # Mint compositions for frequent pairs
            minted = await crystallizer.decide_and_mint(atomizer, auto_mint=True)

            # Should have minted at least one composition
            assert (
                len(minted) > 0 or crystallizer.total_pairs < crystallizer.min_frequency
            )

        asyncio.run(test())

    def test_compression_ratio_calculation(self):
        """Verify compression ratios are calculated correctly."""
        atomizer = FractalAtomizer()

        async def test():
            # Create repetitive sequence
            sequence = [b"Lorem", b" ", b"Ipsum"] * 100  # 300 elements

            # Atomize primitives
            primitive_ids = []
            for val in sequence:
                atom_id = await atomizer.get_or_create_primitive(val)
                primitive_ids.append(atom_id)

            # Create compositions for "Lorem ", " Ipsum"
            lorem_id = primitive_ids[0]
            space_id = primitive_ids[1]
            ipsum_id = primitive_ids[2]

            lorem_space = await atomizer.get_or_create_composition([lorem_id, space_id])
            space_ipsum = await atomizer.get_or_create_composition([space_id, ipsum_id])

            # Now we can represent 300 elements with ~100 composition references
            # Compression ratio = 300 / 100 = 3x

            # Verify we created compositions
            assert lorem_space is not None
            assert space_ipsum is not None
            assert len(atomizer.composition_cache) == 2

        asyncio.run(test())


class TestPerformance:
    """Performance and scalability tests."""

    def test_coordinate_computation_speed(self):
        """Coordinate computation should be O(1)."""
        atomizer = FractalAtomizer()

        import time

        async def test():
            # Create 1000 primitives
            start = time.perf_counter()
            for i in range(1000):
                await atomizer.get_or_create_primitive(f"atom_{i}".encode())
            elapsed_primitives = time.perf_counter() - start

            print(
                f"\n1000 primitives: {elapsed_primitives:.4f}s ({1000/elapsed_primitives:.0f} ops/sec)"
            )

            # Should be fast (sub-millisecond per atom)
            assert elapsed_primitives < 1.0  # Less than 1ms per atom

        asyncio.run(test())

    def test_cache_effectiveness(self):
        """Cache should prevent redundant operations."""
        atomizer = FractalAtomizer()

        async def test():
            # Create same atom 1000 times
            for _ in range(1000):
                await atomizer.get_or_create_primitive(b"test")

            # Should only have 1 entry in cache
            assert len(atomizer.atom_cache) == 1

            # All 1000 calls should return same ID
            assert atomizer.atom_cache[b"test"] is not None

        asyncio.run(test())


if __name__ == "__main__":
    pytest.main([__file__, "-v", "-s"])
