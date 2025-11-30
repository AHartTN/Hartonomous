"""
BPE Crystallizer: Autonomous Pattern Learning for Fractal Compression

Implements Byte Pair Encoding (BPE) / LZW logic for the "OODA Loop" of
semantic compression:

OBSERVE: Count pair frequencies across all streams
ORIENT: Identify most frequent pairs
DECIDE: Mint new composition atoms for frequent pairs
ACT: Replace pairs with composition atoms

This enables autonomous learning:
- System discovers "Legal Disclaimer" is one concept (not 500 chars)
- Recursive merging: "(A,B) → C; (C,C) → D"
- No manual pattern definition required

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import logging
from collections import Counter
from typing import Dict, List, Tuple

logger = logging.getLogger(__name__)


class BPECrystallizer:
    """
    BPE-based semantic compressor for fractal atomization.

    Learns frequent patterns autonomously and mints composition atoms.
    """

    def __init__(
        self,
        min_frequency: int = 100,
        max_vocab_size: int = 100000,
        merge_threshold: float = 0.01,  # Merge if pair appears in >1% of positions
    ):
        """
        Initialize BPE crystallizer.

        Args:
            min_frequency: Minimum occurrences before minting composition
            max_vocab_size: Maximum composition atoms to create
            merge_threshold: Merge if pair frequency > threshold
        """
        self.min_frequency = min_frequency
        self.max_vocab_size = max_vocab_size
        self.merge_threshold = merge_threshold

        # Pair statistics (OBSERVE phase)
        self.pair_counts: Counter[Tuple[int, int]] = Counter()
        self.total_pairs = 0

        # Learned merges (ORIENT/DECIDE phase)
        self.merge_rules: Dict[Tuple[int, int], int] = {}  # (a, b) → composition_id

        # Vocabulary tracking
        self.vocab_size = 0

    def observe_sequence(self, atom_ids: List[int]):
        """
        OBSERVE: Count pairs in this sequence.

        Call this during ingestion to build statistics.

        Args:
            atom_ids: Sequence of atom IDs
        """
        for i in range(len(atom_ids) - 1):
            pair = (atom_ids[i], atom_ids[i + 1])
            self.pair_counts[pair] += 1
            self.total_pairs += 1

    def get_merge_candidates(
        self, top_k: int = 10
    ) -> List[Tuple[Tuple[int, int], int]]:
        """
        ORIENT: Identify most frequent pairs.

        Args:
            top_k: Return top K candidates

        Returns:
            List of ((atom_a, atom_b), count) sorted by frequency
        """
        return self.pair_counts.most_common(top_k)

    async def decide_and_mint(
        self, fractal_atomizer, auto_mint: bool = True
    ) -> List[Tuple[Tuple[int, int], int]]:
        """
        DECIDE + ACT: Mint composition atoms for frequent pairs.

        Args:
            fractal_atomizer: FractalAtomizer instance for creating compositions
            auto_mint: If True, automatically mint compositions for frequent pairs

        Returns:
            List of minted compositions: ((atom_a, atom_b), composition_id)
        """
        minted = []

        for pair, count in self.pair_counts.most_common():
            # Stop if we've hit vocabulary limit
            if self.vocab_size >= self.max_vocab_size:
                break

            # Skip if below threshold
            if count < self.min_frequency:
                break

            # Skip if already merged
            if pair in self.merge_rules:
                continue

            # Check if frequency is significant
            frequency = count / self.total_pairs if self.total_pairs > 0 else 0
            if frequency < self.merge_threshold:
                continue

            if auto_mint:
                # Mint new composition atom
                composition_id = await fractal_atomizer.get_or_create_composition(
                    list(pair),
                    is_stable=True,  # Frequent pattern = stable concept
                    metadata={"bpe_frequency": count, "bpe_ratio": frequency},
                )

                self.merge_rules[pair] = composition_id
                self.vocab_size += 1
                minted.append((pair, composition_id))

                logger.info(
                    f"Minted composition {composition_id} for pair {pair} (freq={count}, ratio={frequency:.4f})"
                )

        return minted

    def apply_merges(self, atom_ids: List[int], max_iterations: int = 10) -> List[int]:
        """
        ACT: Apply learned merges to compress sequence.

        Recursively applies merge rules until no more merges possible.

        Args:
            atom_ids: Original sequence
            max_iterations: Maximum recursive depth

        Returns:
            Compressed sequence
        """
        sequence = atom_ids

        for iteration in range(max_iterations):
            changed = False
            compressed = []
            i = 0

            while i < len(sequence):
                # Try to merge current pair
                if i + 1 < len(sequence):
                    pair = (sequence[i], sequence[i + 1])
                    if pair in self.merge_rules:
                        compressed.append(self.merge_rules[pair])
                        i += 2
                        changed = True
                        continue

                # No merge - keep original
                compressed.append(sequence[i])
                i += 1

            sequence = compressed

            # Stop if no changes
            if not changed:
                break

        return sequence

    async def crystallize_with_bpe(
        self, sequence: List[bytes], fractal_atomizer, learn: bool = True
    ) -> List[int]:
        """
        Full BPE crystallization pipeline.

        1. Convert to primitive atom IDs
        2. OBSERVE: Count pairs (if learn=True)
        3. ACT: Apply existing merge rules

        Args:
            sequence: Raw byte sequence
            fractal_atomizer: FractalAtomizer instance
            learn: If True, observe this sequence for learning

        Returns:
            Compressed atom ID sequence
        """
        # Convert to primitive atoms
        primitive_ids = []
        for value in sequence:
            atom_id = await fractal_atomizer.get_or_create_primitive(value)
            primitive_ids.append(atom_id)

        # OBSERVE phase (if learning enabled)
        if learn:
            self.observe_sequence(primitive_ids)

        # ACT phase: Apply learned merges
        compressed_ids = self.apply_merges(primitive_ids)

        return compressed_ids

    def get_stats(self) -> Dict:
        """Get compression statistics."""
        return {
            "total_pairs_observed": self.total_pairs,
            "unique_pairs": len(self.pair_counts),
            "merge_rules_learned": len(self.merge_rules),
            "vocab_size": self.vocab_size,
            "top_10_pairs": self.pair_counts.most_common(10),
        }

    def save_state(self) -> Dict:
        """Save crystallizer state for persistence."""
        return {
            "pair_counts": dict(self.pair_counts),
            "merge_rules": self.merge_rules,
            "vocab_size": self.vocab_size,
            "total_pairs": self.total_pairs,
        }

    def load_state(self, state: Dict):
        """Load crystallizer state from persistence."""
        self.pair_counts = Counter(
            {
                tuple(map(int, k.strip("()").split(", "))): v
                for k, v in state.get("pair_counts", {}).items()
            }
        )
        self.merge_rules = {
            tuple(map(int, k.strip("()").split(", "))): v
            for k, v in state.get("merge_rules", {}).items()
        }
        self.vocab_size = state.get("vocab_size", 0)
        self.total_pairs = state.get("total_pairs", 0)
