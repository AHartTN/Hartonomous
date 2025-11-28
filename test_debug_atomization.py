"""Debug atomization round-trip."""
import numpy as np
from src.core.atomization import Atomizer, ModalityType

atomizer = Atomizer()

weights = np.random.randn(100, 50).astype(np.float32)
print(f'Original shape: {weights.shape}')
print(f'Original first 5: {weights.flat[:5]}')

atoms = atomizer.atomize_array(weights, ModalityType.MODEL_WEIGHT)
print(f'\nCreated {len(atoms)} atoms')
print(f'First atom metadata: {atoms[0].metadata}')

restored = atomizer.reassemble_from_atoms(atoms, target_shape=weights.shape)
print(f'\nRestored shape: {restored.shape}')
print(f'Restored first 5: {restored.flat[:5]}')

print(f'\nMatches: {np.allclose(weights, restored)}')
print(f'Max diff: {np.max(np.abs(weights - restored))}')

# Check where they differ
diffs = np.abs(weights - restored)
if np.max(diffs) > 0:
    bad_idx = np.unravel_index(np.argmax(diffs), weights.shape)
    print(f'Worst mismatch at {bad_idx}: original={weights[bad_idx]}, restored={restored[bad_idx]}')
