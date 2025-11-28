"""Delta encoder for sequential data."""

import numpy as np


class DeltaEncoder:
    """
    Delta encoding for sequential/temporal data.
    Stores first value + differences.
    """

    @staticmethod
    def encode(data: np.ndarray) -> np.ndarray:
        """Encode as delta sequence."""
        if len(data) < 2:
            return data

        deltas = np.empty_like(data)
        deltas[0] = data[0]
        deltas[1:] = np.diff(data)
        return deltas

    @staticmethod
    def decode(deltas: np.ndarray) -> np.ndarray:
        """Reconstruct from deltas."""
        if len(deltas) < 2:
            return deltas

        return np.cumsum(deltas)
