"""Test spatial landmark positioning."""
from src.core.spatial.compute_position import compute_position

# Test code positioning
x, y, z, hilbert = compute_position(
    modality='code',
    category='function',
    specificity='concrete',
    identifier='my_function'
)

print(f'Position for code function: ({x:.3f}, {y:.3f}, {z:.3f})')
print(f'Hilbert index: {hilbert}')

# Test image positioning
x2, y2, z2, hilbert2 = compute_position(
    modality='image',
    category='literal',
    specificity='literal',
    identifier='pixel_data'
)

print(f'Position for image literal: ({x2:.3f}, {y2:.3f}, {z2:.3f})')
print(f'Hilbert index: {hilbert2}')

# Verify positions are different
from src.core.spatial.compute_distance import compute_distance
dist = compute_distance((x, y, z), (x2, y2, z2))
print(f'Distance between them: {dist:.3f}')
