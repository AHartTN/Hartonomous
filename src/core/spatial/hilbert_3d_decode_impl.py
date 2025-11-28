"""Hilbert 3D decode - CORRECT inverse of compact algorithm."""

from typing import Tuple


def hilbert_3d_decode(index: int, order: int) -> Tuple[int, int, int]:
    """
    Decode Hilbert index to 3D coordinates - inverse of encoding.

    Uses inverse Gray code and reverse rotations.
    """
    if order <= 0:
        return (0, 0, 0)

    x = y = z = 0

    for i in range(order - 1, -1, -1):
        # Extract 3-bit Gray-coded value
        gray = (index >> (3 * i)) & 0x7

        # Inverse Gray code
        octant = gray
        octant ^= (octant >> 2)
        octant ^= (octant >> 1)
        octant &= 0x7

        # Extract coordinate bits
        xi = (octant >> 2) & 1
        yi = (octant >> 1) & 1
        zi = octant & 1

        # Apply inverse rotation BEFORE adding bits
        xi, yi, zi = _inverse_rotate(xi, yi, zi, octant, i)

        # Add bits to coordinates
        x = (x << 1) | xi
        y = (y << 1) | yi
        z = (z << 1) | zi

    return (x, y, z)


def _inverse_rotate(xi: int, yi: int, zi: int, octant: int, level: int) -> tuple:
    """
    Inverse rotation to undo what encoding did.
    Must be exact inverse of _rotate() in encoder.
    """
    if level == 0:
        return xi, yi, zi

    # Apply inverse octant-specific rotation
    if octant == 0:
        # Was: swap x,z -> inverse: swap x,z
        xi, zi = zi, xi
    elif octant == 1:
        # Was: swap y,z -> inverse: swap y,z
        yi, zi = zi, yi
    elif octant == 3:
        # Was: invert x, swap x,y -> inverse: swap x,y, invert x
        xi, yi = yi, xi
        xi = xi ^ 1
    elif octant == 4:
        # Was: invert y, swap x,z -> inverse: swap x,z, invert y
        xi, zi = zi, xi
        yi = yi ^ 1
    elif octant == 5:
        # Was: invert x and y -> inverse: invert x and y
        xi = xi ^ 1
        yi = yi ^ 1
    elif octant == 6:
        # Was: invert z, swap y,z -> inverse: swap y,z, invert z
        yi, zi = zi, yi
        zi = zi ^ 1
    elif octant == 7:
        # Was: invert all, swap x,y -> inverse: swap x,y, invert all
        xi, yi = yi, xi
        xi = xi ^ 1
        yi = yi ^ 1
        zi = zi ^ 1

    return xi, yi, zi
