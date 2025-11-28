"""
Compression module exports.
"""

from .compression_result import CompressionResult
from .atom_compressor import AtomCompressor
from .compress_atom import compress_atom
from .decompress_atom import decompress_atom
from .compression_magic import COMPRESSION_MAGIC

__all__ = [
    'CompressionResult',
    'AtomCompressor',
    'compress_atom',
    'decompress_atom',
    'COMPRESSION_MAGIC',
]
