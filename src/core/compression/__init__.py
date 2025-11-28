"""
Compression module exports.
"""

from .atom_compressor import AtomCompressor
from .compress_atom import compress_atom
from .compression_magic import COMPRESSION_MAGIC
from .compression_result import CompressionResult
from .decompress_atom import decompress_atom

__all__ = [
    "CompressionResult",
    "AtomCompressor",
    "compress_atom",
    "decompress_atom",
    "COMPRESSION_MAGIC",
]
