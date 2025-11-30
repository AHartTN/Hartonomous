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

FRACTAL DEDUPLICATION (Phase 2):
- Compositions ARE atoms (stored in atom table with composition_ids BIGINT[])
- O(1) deduplication via coordinate collision
- Greedy crystallization: collapse sequences into largest known chunks
- "Lorem Ipsum" 1000x = reference ONE paragraph atom 1000 times

This eliminates record explosion:
- "Hello World" = 1 LINESTRING row (not 11 composition rows)
- Tensor with 53M weights = 1 (or few) LINESTRING rows (not 53M rows)
- "Lorem Ipsum" repeated = 1 atom referenced 1000 times (not 5000 character atoms)

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

from .atom_locator import AtomLocator
from .trajectory_builder import TrajectoryBuilder
from .spatial_reconstructor import SpatialReconstructor
from .geometric_atomizer import GeometricAtomizer
from .fractal_atomizer import FractalAtomizer
from .bpe_crystallizer import BPECrystallizer
from .base_geometric_parser import BaseGeometricParser
from .gguf_atomizer import GGUFAtomizer

__all__ = [
    "AtomLocator",
    "TrajectoryBuilder",
    "SpatialReconstructor",
    "GeometricAtomizer",
    "FractalAtomizer",
    "BPECrystallizer",
    "BaseGeometricParser",
    "GGUFAtomizer",
]

