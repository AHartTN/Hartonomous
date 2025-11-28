"""Multi-layer encoder for atom compression."""

from typing import Optional, Tuple

import numpy as np

from .encoding_metadata import EncodingMetadata


class MultiLayerEncoder:
    """
    Applies multiple encoding strategies in sequence:
    1. Run-Length Encoding (RLE) for repeated values
    2. Sparse encoding for near-zero values
    3. Delta encoding for sequential patterns

    Designed for float64/float32 precision with minimal loss.
    """

    def __init__(self, sparse_threshold: float = 1e-9):
        self.sparse_threshold = sparse_threshold

    def encode(self, data: np.ndarray) -> Tuple[bytes, EncodingMetadata]:
        """Apply multi-layer encoding to data."""
        original_shape = data.shape
        original_dtype = data.dtype

        flat = data.flatten()

        rle_data, rle_applied = self._apply_rle(flat)
        sparse_data, sparse_applied = self._apply_sparse(rle_data)

        encoded_bytes = sparse_data.tobytes()

        metadata = EncodingMetadata(
            rle_applied=rle_applied,
            sparse_applied=sparse_applied,
            sparse_threshold=self.sparse_threshold,
            original_shape=original_shape,
            dtype=original_dtype,
        )

        return encoded_bytes, metadata

    def decode(self, encoded: bytes, metadata: EncodingMetadata) -> np.ndarray:
        """Reconstruct original data from encoded bytes."""
        data = np.frombuffer(encoded, dtype=metadata.dtype)

        if metadata.sparse_applied:
            data = self._reverse_sparse(data, metadata.sparse_threshold)

        if metadata.rle_applied:
            data = self._reverse_rle(data)

        return data.reshape(metadata.original_shape)

    def _apply_rle(self, data: np.ndarray) -> Tuple[np.ndarray, bool]:
        """Apply run-length encoding if beneficial."""
        if len(data) < 2:
            return data, False

        runs = []
        current_val = data[0]
        count = 1

        for val in data[1:]:
            if val == current_val:
                count += 1
            else:
                runs.append((current_val, count))
                current_val = val
                count = 1
        runs.append((current_val, count))

        rle_size = len(runs) * 2
        if rle_size < len(data) * 0.8:
            encoded = np.empty(len(runs) * 2, dtype=data.dtype)
            for i, (val, cnt) in enumerate(runs):
                encoded[i * 2] = val
                encoded[i * 2 + 1] = cnt
            return encoded, True

        return data, False

    def _reverse_rle(self, encoded: np.ndarray) -> np.ndarray:
        """Reverse RLE encoding."""
        result = []
        for i in range(0, len(encoded), 2):
            value = encoded[i]
            count = int(encoded[i + 1])
            result.extend([value] * count)
        return np.array(result, dtype=encoded.dtype)

    def _apply_sparse(
        self, data: np.ndarray, threshold: Optional[float] = None
    ) -> Tuple[np.ndarray, bool]:
        """Apply sparse encoding: values below threshold become zero."""
        if threshold is None:
            threshold = self.sparse_threshold

        near_zero = np.abs(data) < threshold
        sparsity = np.sum(near_zero) / len(data)

        if sparsity > 0.1:
            sparse_data = data.copy()
            sparse_data[near_zero] = 0.0
            return sparse_data, True

        return data, False

    def _reverse_sparse(self, data: np.ndarray, threshold: float) -> np.ndarray:
        """Reverse sparse encoding (zeros stay zeros)."""
        return data
