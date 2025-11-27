"""
Multi-layer compression and encoding system for atoms.
Leverages multiple compression strategies simultaneously for maximum density.
"""

from .atom_compressor import AtomCompressor, CompressionResult
from .multi_layer import compress_atom, decompress_atom
from .sparse_encoding import apply_sparse_encoding, decode_sparse
from .run_length import apply_rle, decode_rle
from .dictionary import DictionaryCompressor

__all__ = [
    'AtomCompressor',
    'CompressionResult',
    'compress_atom',
    'decompress_atom',
    'apply_sparse_encoding',
    'decode_sparse',
    'apply_rle',
    'decode_rle',
    'DictionaryCompressor',
]
