"""
Geometric Atomization: Trajectory-Based Storage Architecture

BREAKTHROUGH ARCHITECTURE:
- Atoms: Constants at fixed semantic coordinates (deterministic via hash)
- Compositions: LINESTRING trajectories visiting coordinates in sequence
- Storage: LINESTRINGZM(x1 y1 z1 m1, x2 y2 z2 m2, ...)
  * (x,y,z) = semantic coordinate of atom
  * m = sequence index for reconstruction
- Reconstruction: Walk LINESTRING, lookup atoms at coordinates
- Inference: Spatial queries (ST_DWithin) replace MatMul

This eliminates record explosion:
- "Hello World" = 1 LINESTRING row (not 11 composition rows)
- Tensor with 53M weights = 1 (or few) LINESTRING rows (not 53M rows)

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

from .atom_locator import AtomLocator
from .trajectory_builder import TrajectoryBuilder
from .spatial_reconstructor import SpatialReconstructor
from .geometric_atomizer import GeometricAtomizer

__all__ = [
    "AtomLocator",
    "TrajectoryBuilder",
    "SpatialReconstructor",
    "GeometricAtomizer",
]
