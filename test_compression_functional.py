"""Test compression functionality."""
import numpy as np
from src.core.compression.compress_atom import compress_atom
from src.core.compression.decompress_atom import decompress_atom

original = np.random.randn(1000).astype(np.float32)
compressed, metadata = compress_atom(original, sparse_threshold=0.01)
restored = decompress_atom(compressed, metadata)

print(f'Original size: {original.nbytes} bytes')
print(f'Compressed size: {len(compressed)} bytes')
print(f'Compression ratio: {metadata["compression_ratio"]:.2f}x')
print(f'Data integrity: {np.allclose(original, restored)}')
print(f'Max error: {np.max(np.abs(original - restored)):.6f}')
