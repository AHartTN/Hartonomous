"""Gray code decoding for 3-bit value."""


def gray_decode_3bit(n: int) -> int:
    """Gray code decoding for 3-bit value."""
    n ^= n >> 2
    n ^= n >> 1
    return n & 0x7
