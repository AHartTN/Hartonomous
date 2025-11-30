"""
Dictionary-based compression for repeated patterns.
Maintains a global dictionary for deduplication across atoms.
"""

import hashlib
import struct
from typing import Dict, Tuple


class DictionaryCompressor:
    """
    Global dictionary for deduplicating repeated data patterns.
    Thread-safe for concurrent compression operations.
    """

    def __init__(self, max_entries: int = 1000000):
        """
        Initialize dictionary compressor.

        Args:
            max_entries: Maximum dictionary entries before eviction
        """
        self.dictionary: Dict[bytes, int] = {}
        self.reverse_dict: Dict[int, bytes] = {}
        self.max_entries = max_entries
        self.next_id = 0

    def compress(self, data: bytes, min_pattern_length: int = 8) -> Tuple[bytes, bool]:
        """
        Compress data using dictionary.

        Args:
            data: Input bytes
            min_pattern_length: Minimum length for dictionary entry

        Returns:
            Tuple of (compressed_data, used_dictionary)
        """
        if len(data) < min_pattern_length:
            return (data, False)

        # Check if entire block exists in dictionary
        data_hash = hashlib.blake2b(data, digest_size=16).digest()

        if data_hash in self.dictionary:
            dict_id = self.dictionary[data_hash]
            # Format: 0xFF | dict_id (4 bytes) | original_size (4 bytes)
            result = struct.pack("<BII", 0xFF, dict_id, len(data))
            return (result, True)

        # Add to dictionary if beneficial
        if len(self.dictionary) < self.max_entries:
            dict_id = self.next_id
            self.next_id += 1
            self.dictionary[data_hash] = dict_id
            self.reverse_dict[dict_id] = data

        return (data, False)

    def decompress(self, data: bytes) -> bytes:
        """
        Decompress data using dictionary.

        Args:
            data: Compressed bytes

        Returns:
            Decompressed bytes
        """
        if len(data) >= 9 and data[0] == 0xFF:
            # Dictionary reference
            dict_id, original_size = struct.unpack("<II", data[1:9])

            if dict_id in self.reverse_dict:
                return self.reverse_dict[dict_id]
            else:
                raise ValueError(f"Dictionary entry {dict_id} not found")

        return data

    def clear(self):
        """Clear dictionary (for testing/reset)."""
        self.dictionary.clear()
        self.reverse_dict.clear()
        self.next_id = 0
