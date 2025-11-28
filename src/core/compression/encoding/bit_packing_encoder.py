"""Bit packing encoder for reduced precision storage."""

import numpy as np


class BitPackingEncoder:
    """
    Bit-packing for reduced-precision storage.
    Converts float64 -> quantized integers -> packed bits.
    """
    
    @staticmethod
    def pack_float32_to_float16(data: np.ndarray) -> np.ndarray:
        """Reduce float32/float64 to float16 for storage."""
        return data.astype(np.float16)
    
    @staticmethod
    def unpack_float16_to_float32(data: np.ndarray) -> np.ndarray:
        """Restore from float16 to float32."""
        return data.astype(np.float32)
