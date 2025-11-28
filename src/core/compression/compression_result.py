"""Compression result dataclass."""

from dataclasses import dataclass


@dataclass
class CompressionResult:
    """Result of compression operation."""
    data: bytes
    compression_type: int
    metadata: dict
    original_size: int
    compressed_size: int
    
    @property
    def ratio(self) -> float:
        """Compression ratio."""
        if self.compressed_size == 0:
            return 0.0
        return self.original_size / self.compressed_size
