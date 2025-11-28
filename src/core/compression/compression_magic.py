"""Compression magic bytes constants."""

COMPRESSION_MAGIC = {
    'zlib': b'\x1f\x8b',
    'lz4': b'\x04\x22\x4d\x18',
    'sparse': b'\xaa\x55',
    'rle': b'\xbb\x66',
    'raw': b'\xff\xff',
}
