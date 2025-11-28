"""Gray code encoding for 3-bit value."""


def gray_encode_3bit(n: int) -> int:
    """Gray code encoding for 3-bit value."""
    return n ^ (n >> 1)
