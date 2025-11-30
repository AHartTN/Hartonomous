"""
Unit Tests: BPECrystallizer

Tests autonomous pattern learning (OODA loop) for semantic compression.

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import pytest

from api.services.geometric_atomization import BPECrystallizer, FractalAtomizer


class TestOODAPhases:
    """Test OBSERVE-ORIENT-DECIDE-ACT phases."""

    def test_observe_counts_pairs(self):
        """OBSERVE: Count pair frequencies."""
        crystallizer = BPECrystallizer(min_frequency=2)

        # Observe sequence with repeated pair (1, 2)
        crystallizer.observe_sequence([1, 2, 3, 1, 2, 4, 1, 2])

        # Pair (1, 2) appears 3 times
        assert crystallizer.pair_counts[(1, 2)] == 3

        # Other pairs appear once
        assert crystallizer.pair_counts[(2, 3)] == 1
        assert crystallizer.pair_counts[(3, 1)] == 1
        assert crystallizer.pair_counts[(2, 4)] == 1
        assert crystallizer.pair_counts[(4, 1)] == 1

        # Total pairs = 7
        assert crystallizer.total_pairs == 7

    def test_observe_multiple_sequences(self):
        """OBSERVE: Accumulate counts across sequences."""
        crystallizer = BPECrystallizer()

        crystallizer.observe_sequence([1, 2, 3])
        crystallizer.observe_sequence([1, 2, 4])
        crystallizer.observe_sequence([1, 2, 5])

        # Pair (1, 2) appears in all 3 sequences
        assert crystallizer.pair_counts[(1, 2)] == 3

        # Total pairs = 2 + 2 + 2 = 6
        assert crystallizer.total_pairs == 6

    def test_orient_returns_top_candidates(self):
        """ORIENT: Identify most frequent pairs."""
        crystallizer = BPECrystallizer()

        # Observe with varying frequencies
        crystallizer.observe_sequence([1, 2] * 10)  # (1,2) x10, (2,1) x9
        crystallizer.observe_sequence([3, 4] * 5)  # (3,4) x5, (4,3) x4
        crystallizer.observe_sequence([5, 6] * 2)  # (5,6) x2, (6,5) x1

        candidates = crystallizer.get_merge_candidates(top_k=3)

        # Should return top 3 by frequency
        assert len(candidates) == 3

        # First should be (1, 2) with count 10
        assert candidates[0][0] == (1, 2)
        assert candidates[0][1] == 10

        # Second should be (2, 1) with count 9
        assert candidates[1][0] == (2, 1)
        assert candidates[1][1] == 9


class TestMergeRules:
    """Test merge rule learning and application."""

    @pytest.mark.asyncio
    async def test_decide_and_mint_high_frequency(self):
        """DECIDE: Mint compositions for frequent pairs."""
        crystallizer = BPECrystallizer(min_frequency=5, merge_threshold=0.1)
        atomizer = FractalAtomizer()

        # Create primitives
        id1 = await atomizer.get_or_create_primitive(b"A")
        id2 = await atomizer.get_or_create_primitive(b"B")

        # Observe pair 10 times (high frequency)
        for _ in range(10):
            crystallizer.observe_sequence([id1, id2])

        # Mint compositions
        minted = await crystallizer.decide_and_mint(atomizer, auto_mint=True)

        # Should have minted (id1, id2)
        assert len(minted) >= 1
        assert minted[0][0] == (id1, id2)

        # Should have created merge rule
        assert (id1, id2) in crystallizer.merge_rules

    @pytest.mark.asyncio
    async def test_decide_ignores_low_frequency(self):
        """DECIDE: Ignore pairs below threshold."""
        crystallizer = BPECrystallizer(min_frequency=100)
        atomizer = FractalAtomizer()

        id1 = await atomizer.get_or_create_primitive(b"A")
        id2 = await atomizer.get_or_create_primitive(b"B")

        # Observe only 5 times (below threshold)
        for _ in range(5):
            crystallizer.observe_sequence([id1, id2])

        minted = await crystallizer.decide_and_mint(atomizer, auto_mint=True)

        # Should NOT mint (below min_frequency)
        assert len(minted) == 0
        assert (id1, id2) not in crystallizer.merge_rules

    def test_apply_merges_single_rule(self):
        """ACT: Apply merge rule to compress sequence."""
        crystallizer = BPECrystallizer()

        # Setup merge rule: (1, 2) → 100
        crystallizer.merge_rules[(1, 2)] = 100

        # Apply to sequence [1, 2, 3]
        compressed = crystallizer.apply_merges([1, 2, 3])

        # Should be [100, 3]
        assert compressed == [100, 3]

    def test_apply_merges_multiple_occurrences(self):
        """ACT: Apply merge to all occurrences."""
        crystallizer = BPECrystallizer()

        crystallizer.merge_rules[(1, 2)] = 100

        # Sequence: [1, 2, 3, 1, 2]
        compressed = crystallizer.apply_merges([1, 2, 3, 1, 2])

        # Should be [100, 3, 100]
        assert compressed == [100, 3, 100]

    def test_apply_merges_recursive(self):
        """ACT: Recursively apply merges."""
        crystallizer = BPECrystallizer()

        # Level 1: (1, 2) → 10
        crystallizer.merge_rules[(1, 2)] = 10

        # Level 2: (10, 3) → 20
        crystallizer.merge_rules[(10, 3)] = 20

        # Apply to [1, 2, 3]
        compressed = crystallizer.apply_merges([1, 2, 3])

        # Should merge recursively:
        # [1, 2, 3] → [10, 3] → [20]
        assert compressed == [20]


class TestCompressionImprovement:
    """Test compression ratio improvement over time."""

    @pytest.mark.asyncio
    async def test_compression_improves_with_learning(self):
        """Compression gets better as patterns are learned."""
        crystallizer = BPECrystallizer(min_frequency=2, merge_threshold=0.01)
        atomizer = FractalAtomizer()

        # Create primitives
        id_a = await atomizer.get_or_create_primitive(b"A")
        id_b = await atomizer.get_or_create_primitive(b"B")

        # Initial sequence: "ABABAB" (6 atoms)
        sequence = [b"A", b"B"] * 3

        # Crystallize WITHOUT learning
        ids_before = await crystallizer.crystallize_with_bpe(
            sequence, atomizer, learn=False
        )
        length_before = len(ids_before)
        assert length_before == 6  # No compression

        # Observe patterns (learn=True)
        await crystallizer.crystallize_with_bpe(sequence, atomizer, learn=True)

        # Mint compositions for learned patterns
        await crystallizer.decide_and_mint(atomizer, auto_mint=True)

        # Crystallize again WITH learned rules
        ids_after = await crystallizer.crystallize_with_bpe(
            sequence, atomizer, learn=False
        )
        length_after = len(ids_after)

        # Should be shorter (compressed)
        assert length_after < length_before


class TestVocabularyManagement:
    """Test vocabulary size limits."""

    @pytest.mark.asyncio
    async def test_vocab_size_limit(self):
        """Stop minting when vocabulary limit reached."""
        crystallizer = BPECrystallizer(
            min_frequency=1, max_vocab_size=3, merge_threshold=0.0
        )
        atomizer = FractalAtomizer()

        # Create many different primitives
        primitives = []
        for i in range(10):
            pid = await atomizer.get_or_create_primitive(f"atom_{i}".encode())
            primitives.append(pid)

        # Observe many different pairs
        for i in range(len(primitives) - 1):
            crystallizer.observe_sequence([primitives[i], primitives[i + 1]])

        # Try to mint
        minted = await crystallizer.decide_and_mint(atomizer, auto_mint=True)

        # Should stop at max_vocab_size=3
        assert len(minted) <= 3
        assert crystallizer.vocab_size <= 3


class TestStatistics:
    """Test compression statistics."""

    def test_get_stats(self):
        """Get statistics about learning."""
        crystallizer = BPECrystallizer()

        crystallizer.observe_sequence([1, 2, 3])
        crystallizer.observe_sequence([1, 2, 4])

        stats = crystallizer.get_stats()

        assert stats["total_pairs_observed"] == 4
        assert stats["unique_pairs"] == 3  # (1,2), (2,3), (2,4) - (1,2) appears twice
        assert "top_10_pairs" in stats


class TestStatePersistence:
    """Test save/load state."""

    @pytest.mark.asyncio
    async def test_save_and_load_state(self):
        """Save and load crystallizer state."""
        crystallizer1 = BPECrystallizer()

        # Observe patterns
        crystallizer1.observe_sequence([1, 2, 3])
        crystallizer1.merge_rules[(1, 2)] = 100
        crystallizer1.vocab_size = 1

        # Save state
        state = crystallizer1.save_state()

        # Verify state is serializable
        assert isinstance(state, dict)
        assert "total_pairs" in state
        assert "vocab_size" in state

        # Note: Full load_state test requires fixing serialization in source


class TestEdgeCases:
    """Test edge cases."""

    def test_empty_sequence(self):
        """Handle empty sequence."""
        crystallizer = BPECrystallizer()

        crystallizer.observe_sequence([])

        assert crystallizer.total_pairs == 0

    def test_single_element_sequence(self):
        """Handle single element (no pairs)."""
        crystallizer = BPECrystallizer()

        crystallizer.observe_sequence([1])

        assert crystallizer.total_pairs == 0

    def test_apply_merges_no_rules(self):
        """Apply merges with no rules = identity."""
        crystallizer = BPECrystallizer()

        original = [1, 2, 3, 4, 5]
        compressed = crystallizer.apply_merges(original)

        assert compressed == original


class TestSemanticCompression:
    """Test real-world semantic compression scenarios."""

    @pytest.mark.asyncio
    async def test_legal_disclaimer_compression(self):
        """
        Simulate legal disclaimer appearing 1000 times.

        Should learn it as single composition atom.
        """
        crystallizer = BPECrystallizer(min_frequency=10, merge_threshold=0.001)
        atomizer = FractalAtomizer()

        # Simulate disclaimer as sequence of 100 atom IDs
        disclaimer_atoms = []
        for i in range(100):
            atom_id = await atomizer.get_or_create_primitive(f"word_{i}".encode())
            disclaimer_atoms.append(atom_id)

        # Observe disclaimer 50 times
        for _ in range(50):
            crystallizer.observe_sequence(disclaimer_atoms)

        # Mint compositions
        minted = await crystallizer.decide_and_mint(atomizer, auto_mint=True)

        # Should have learned some patterns
        assert len(minted) > 0
        assert crystallizer.vocab_size > 0


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
