"""Test atomization functionality."""
import numpy as np
from src.core.atomization import Atomizer, ModalityType

atomizer = Atomizer()

# Test model weights atomization
weights = np.random.randn(100, 50).astype(np.float32)
atoms = atomizer.atomize_array(weights, ModalityType.MODEL_WEIGHT)

print(f'Array shape: {weights.shape}')
print(f'Array size: {weights.nbytes} bytes')
print(f'Number of atoms created: {len(atoms)}')
print(f'First atom size: {len(atoms[0].data)} bytes')
print(f'Deduplication stats: {atomizer.get_deduplication_stats()}')

# Test reassembly
restored = atomizer.reassemble_from_atoms(atoms, target_shape=weights.shape)
print(f'Reassembled shape: {restored.shape}')
print(f'Data matches: {np.allclose(weights, restored)}')
