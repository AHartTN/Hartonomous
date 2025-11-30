"""
BPE Integration Tests with FractalAtomizer

Tests autonomous learning capabilities:
- System learns frequent patterns without manual definition
- "Legal Disclaimer" becomes single atom through observation
- Recursive merging: (A,B)→C; (C,C)→D
- OODA loop: Observe → Orient → Decide → Act

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import asyncio

import pytest

from api.services.geometric_atomization import BPECrystallizer, FractalAtomizer


class TestBPEIntegration:
    """Test BPE autonomous learning with FractalAtomizer."""

    def test_learn_repeated_phrase(self):
        """System should learn 'Legal Disclaimer' pattern from repetition."""
        atomizer = FractalAtomizer()
        crystallizer = BPECrystallizer(min_frequency=3)

        async def test():
            # Simulate documents with repeated phrase
            documents = [
                ["THIS", " ", "IS", " ", "A", " ", "LEGAL", " ", "DISCLAIMER", ". "]
                * 5,
                ["THIS", " ", "IS", " ", "A", " ", "LEGAL", " ", "DISCLAIMER", ". "]
                * 5,
                ["THIS", " ", "IS", " ", "A", " ", "LEGAL", " ", "DISCLAIMER", ". "]
                * 5,
            ]

            # Convert to bytes
            docs_bytes = [[token.encode() for token in doc] for doc in documents]

            # Learn from corpus
            for doc in docs_bytes:
                await crystallizer.crystallize_with_bpe(
                    doc, atomizer, learn=True  # Observe patterns
                )

            stats = crystallizer.get_stats()
            print(f"\nObserved {stats['total_pairs_observed']} pairs")
            print(f"Unique pairs: {stats['unique_pairs']}")

            # Should have observed pair frequencies
            assert stats["total_pairs_observed"] > 0
            assert stats["unique_pairs"] > 0

            # Mint compositions for frequent pairs
            minted = await crystallizer.decide_and_mint(atomizer, auto_mint=True)

            print(f"Minted {len(minted)} composition atoms")

            # Should have learned some patterns
            assert len(minted) > 0

            # Now compress a new document using learned patterns
            test_doc = [
                "THIS",
                " ",
                "IS",
                " ",
                "A",
                " ",
                "LEGAL",
                " ",
                "DISCLAIMER",
                ". ",
            ]
            test_bytes = [token.encode() for token in test_doc]

            compressed = await crystallizer.crystallize_with_bpe(
                test_bytes,
                atomizer,
                learn=False,  # Don't learn, just apply existing rules
            )

            # Compressed should be shorter than original
            print(f"Original: {len(test_bytes)} tokens")
            print(f"Compressed: {len(compressed)} atoms")
            assert len(compressed) < len(test_bytes)

        asyncio.run(test())

    def test_recursive_merging(self):
        """System should discover recursive patterns: (A,B)→C; (C,C)→D."""
        atomizer = FractalAtomizer()
        crystallizer = BPECrystallizer(min_frequency=2)

        async def test():
            # Sequence with recursive pattern: "XYXYXYXY..."
            sequence = [b"X", b"Y"] * 20  # XY repeated 20 times

            # First pass: learn (X,Y)
            await crystallizer.crystallize_with_bpe(sequence, atomizer, learn=True)

            # Mint composition for (X,Y) → XY
            minted_1 = await crystallizer.decide_and_mint(atomizer, auto_mint=True)
            print(f"\nFirst pass minted: {len(minted_1)} compositions")

            # Second pass: apply merges, creating [XY, XY, XY, ...]
            compressed_1 = await crystallizer.crystallize_with_bpe(
                sequence, atomizer, learn=True  # Observe the (XY, XY) pattern
            )

            print(f"After first merge: {len(sequence)} → {len(compressed_1)} atoms")
            assert len(compressed_1) < len(sequence)

            # Mint composition for (XY, XY) → XYXY if frequent enough
            minted_2 = await crystallizer.decide_and_mint(atomizer, auto_mint=True)
            print(f"Second pass minted: {len(minted_2)} compositions")

            # Third pass: apply second-level merges
            compressed_2 = await crystallizer.crystallize_with_bpe(
                sequence, atomizer, learn=False
            )

            print(
                f"After second merge: {len(compressed_1)} → {len(compressed_2)} atoms"
            )

            # Should get progressively more compressed
            assert len(compressed_2) <= len(compressed_1)

        asyncio.run(test())

    def test_autonomous_discovery(self):
        """System discovers patterns without manual definition."""
        atomizer = FractalAtomizer()
        crystallizer = BPECrystallizer(min_frequency=5)

        async def test():
            # Corpus with various patterns
            corpus = [
                ["The", " ", "quick", " ", "brown", " ", "fox"] * 10,  # Repeated phrase
                ["The", " ", "slow", " ", "brown", " ", "dog"] * 10,  # Partial overlap
                ["The", " ", "quick", " ", "red", " ", "fox"] * 10,  # Partial overlap
            ]

            corpus_bytes = [[token.encode() for token in doc] for doc in corpus]

            # Learn from corpus
            for doc in corpus_bytes:
                await crystallizer.crystallize_with_bpe(doc, atomizer, learn=True)

            # Get top patterns
            candidates = crystallizer.get_merge_candidates(top_k=10)
            print(f"\nTop patterns discovered:")
            for (a, b), count in candidates[:5]:
                # Lookup primitive values for readability
                a_text = atomizer.atom_cache.get(a, a)
                b_text = atomizer.atom_cache.get(b, b)
                print(f"  ({a_text}, {b_text}): {count} occurrences")

            # Should have found frequent pairs
            assert len(candidates) > 0

            # "The" + " " should be most frequent (appears 30 times)
            # (" ", "quick") appears 20 times
            # (" ", "brown") appears 20 times

            # Mint compositions
            minted = await crystallizer.decide_and_mint(atomizer, auto_mint=True)
            print(f"\nAutonomously minted {len(minted)} composition atoms")

            assert len(minted) > 0

        asyncio.run(test())

    def test_incremental_learning(self):
        """System learns incrementally as new data arrives."""
        atomizer = FractalAtomizer()
        crystallizer = BPECrystallizer(min_frequency=3)

        async def test():
            # First batch of data
            batch_1 = [[b"A", b"B", b"C"] * 5 for _ in range(2)]

            for doc in batch_1:
                await crystallizer.crystallize_with_bpe(doc, atomizer, learn=True)

            stats_1 = crystallizer.get_stats()
            print(f"\nAfter batch 1: {stats_1['total_pairs_observed']} pairs observed")

            # Mint first set of compositions
            minted_1 = await crystallizer.decide_and_mint(atomizer, auto_mint=True)
            print(f"Minted: {len(minted_1)} compositions")

            # Second batch introduces new pattern
            batch_2 = [[b"A", b"B", b"D"] * 5 for _ in range(2)]

            for doc in batch_2:
                await crystallizer.crystallize_with_bpe(doc, atomizer, learn=True)

            stats_2 = crystallizer.get_stats()
            print(f"After batch 2: {stats_2['total_pairs_observed']} pairs observed")

            # Should have more observations
            assert stats_2["total_pairs_observed"] > stats_1["total_pairs_observed"]

            # Mint additional compositions
            minted_2 = await crystallizer.decide_and_mint(atomizer, auto_mint=True)
            print(f"Minted additional: {len(minted_2)} compositions")

            # Total vocabulary should grow
            total_minted = len(minted_1) + len(minted_2)
            print(f"Total compositions: {total_minted}")
            assert total_minted > len(minted_1)

        asyncio.run(test())

    def test_compression_improves_over_time(self):
        """Compression ratio improves as system learns patterns."""
        atomizer = FractalAtomizer()
        crystallizer = BPECrystallizer(min_frequency=2)

        async def test():
            # Test document
            test_doc = [b"Lorem", b" ", b"ipsum", b" ", b"dolor"] * 10

            # Baseline: compress without learning
            baseline = await crystallizer.crystallize_with_bpe(
                test_doc, atomizer, learn=False
            )

            print(f"\nBaseline (no learning): {len(test_doc)} → {len(baseline)} atoms")

            # Train on similar documents
            training_corpus = [
                [b"Lorem", b" ", b"ipsum", b" ", b"dolor"] * 20,
                [b"Lorem", b" ", b"ipsum", b" ", b"sit"] * 20,
            ]

            for doc in training_corpus:
                await crystallizer.crystallize_with_bpe(doc, atomizer, learn=True)

            # Mint learned patterns
            minted = await crystallizer.decide_and_mint(atomizer, auto_mint=True)
            print(f"Learned {len(minted)} patterns")

            # Compress again with learned patterns
            improved = await crystallizer.crystallize_with_bpe(
                test_doc, atomizer, learn=False
            )

            print(f"After learning: {len(test_doc)} → {len(improved)} atoms")

            # Should compress better
            assert len(improved) <= len(baseline)

        asyncio.run(test())


class TestOODALoop:
    """Test OODA loop implementation."""

    def test_observe_phase(self):
        """OBSERVE: Count pair frequencies across streams."""
        crystallizer = BPECrystallizer()

        # Observe multiple sequences
        crystallizer.observe_sequence([1, 2, 3, 1, 2])
        crystallizer.observe_sequence([1, 2, 4, 1, 2])

        # (1, 2) should be most frequent
        assert crystallizer.pair_counts[(1, 2)] == 4

    def test_orient_phase(self):
        """ORIENT: Identify most frequent pairs."""
        crystallizer = BPECrystallizer(min_frequency=2)

        crystallizer.observe_sequence([1, 2] * 10)
        crystallizer.observe_sequence([3, 4] * 5)

        candidates = crystallizer.get_merge_candidates(top_k=5)

        # (1, 2) should rank first (10 occurrences)
        assert candidates[0][0] == (1, 2)
        assert candidates[0][1] == 10

    def test_decide_phase(self):
        """DECIDE: Mint new composition atoms for frequent pairs."""
        atomizer = FractalAtomizer()
        crystallizer = BPECrystallizer(min_frequency=2)

        async def test():
            # Create primitives
            a_id = await atomizer.get_or_create_primitive(b"A")
            b_id = await atomizer.get_or_create_primitive(b"B")

            # Observe frequent pair
            crystallizer.observe_sequence([a_id, b_id] * 10)

            # Decide and mint
            minted = await crystallizer.decide_and_mint(atomizer, auto_mint=True)

            # Should have minted composition for (A, B)
            assert len(minted) > 0
            assert (a_id, b_id) in crystallizer.merge_rules

        asyncio.run(test())

    def test_act_phase(self):
        """ACT: Apply learned merge rules to compress."""
        crystallizer = BPECrystallizer()

        # Manually add merge rule
        crystallizer.merge_rules[(1, 2)] = 100

        # Apply to sequence
        compressed = crystallizer.apply_merges([1, 2, 3, 1, 2])

        # Should replace (1, 2) with 100
        assert compressed == [100, 3, 100]


if __name__ == "__main__":
    pytest.main([__file__, "-v", "-s"])
