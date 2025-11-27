"""
Run-length encoding for repeated byte patterns.
Effective for repetitive data like blue pixels, repeated weights, etc.
"""

from typing import List


def apply_rle(data: bytes) -> bytes:
    """
    Apply run-length encoding to byte sequence.
    
    Format: For each run:
    - If count <= 127: count (1 byte) + value (1 byte)
    - If count > 127: 0x80 | (count-128 in 2 bytes) + value (1 byte)
    
    Args:
        data: Input bytes
        
    Returns:
        RLE-encoded bytes
    """
    if not data:
        return b''
    
    result = bytearray()
    i = 0
    length = len(data)
    
    while i < length:
        # Count consecutive identical bytes
        current = data[i]
        count = 1
        
        while i + count < length and data[i + count] == current and count < 32767:
            count += 1
        
        # Encode run
        if count <= 127:
            result.append(count)
            result.append(current)
        else:
            # Extended format for long runs
            result.append(0x80 | ((count - 128) >> 8))
            result.append((count - 128) & 0xFF)
            result.append(current)
        
        i += count
    
    return bytes(result)


def decode_rle(data: bytes) -> bytes:
    """
    Decode run-length encoded bytes.
    
    Args:
        data: RLE-encoded bytes
        
    Returns:
        Decoded bytes
    """
    if not data:
        return b''
    
    result = bytearray()
    i = 0
    length = len(data)
    
    while i < length:
        count_byte = data[i]
        
        if count_byte & 0x80:
            # Extended format
            if i + 2 >= length:
                break
            count = ((count_byte & 0x7F) << 8) | data[i + 1]
            count += 128
            value = data[i + 2]
            i += 3
        else:
            # Short format
            if i + 1 >= length:
                break
            count = count_byte
            value = data[i + 1]
            i += 2
        
        result.extend([value] * count)
    
    return bytes(result)
