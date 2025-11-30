"""
Model Atomization - DEPRECATED - Use api.services.geometric_atomization instead.

This module is maintained for backward compatibility only.
New code should import from api.services.geometric_atomization.

The legacy atomization approach has been replaced with geometric/fractal storage:
- Tensors stored as LINESTRING trajectories (one row per tensor)
- NOT millions of composition rows
- 70B parameter model = ~500 rows instead of 70 billion rows
"""

# Re-export from new geometric structure
from api.services.geometric_atomization.gguf_atomizer import GGUFAtomizer

__all__ = ["GGUFAtomizer"]
