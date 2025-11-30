"""
Model Atomization - DEPRECATED - Use api.services.atomization instead.

This module is maintained for backward compatibility only.
New code should import from api.services.atomization.

The 1362-line monolith has been refactored into clean, focused modules:
- weight_processor.py: Weight deduplication and caching
- composition_builder.py: Bulk composition creation  
- tensor_atomizer.py: Tensor processing logic
- gguf_atomizer.py: GGUF file orchestration

Each module follows SOLID principles with single responsibility.
"""

# Re-export from new modular structure
from api.services.atomization import GGUFAtomizer

__all__ = ["GGUFAtomizer"]
