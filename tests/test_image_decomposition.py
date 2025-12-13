"""
Test image decomposition with actual PNG
"""

import psycopg2
import numpy as np
from PIL import Image, ImageDraw
from pathlib import Path

# Generate test image
output_dir = Path("test_data")
output_dir.mkdir(exist_ok=True)

img_path = output_dir / "test_pattern.png"

# Create 64x64 gradient pattern
img = Image.new('RGB', (64, 64))
pixels = img.load()

for y in range(64):
    for x in range(64):
        r = int((x / 64) * 255)
        g = int((y / 64) * 255)
        b = 128
        pixels[x, y] = (r, g, b)

img.save(img_path)

print(f"Generated test image: {img_path}")
print(f"  Size: 64x64 pixels = {64*64} atoms")

# Test decomposition
import sys
sys.path.insert(0, str(Path(__file__).parent.parent))

from connector.image_decomposer import ImageDecomposer
import time

conn = psycopg2.connect(
    host='localhost',
    dbname='hartonomous',
    user='hartonomous'
)

decomposer = ImageDecomposer(conn)

print("\nDecomposing image...")
start = time.time()
comp_id = decomposer.decompose_image(img_path)
decomp_time = (time.time() - start) * 1000

print(f"Composition ID: {comp_id.hex()[:16]}...")
print(f"Decomposition time: {decomp_time:.2f}ms")

# Check results
cursor = conn.cursor()

cursor.execute("""
    SELECT COUNT(*) FROM atom_compositions WHERE parent_atom_id = %s
""", (comp_id,))

component_count = cursor.fetchone()[0]
print(f"Components: {component_count}")

cursor.execute("""
    SELECT metadata FROM atom WHERE atom_id = %s
""", (comp_id,))

metadata = cursor.fetchone()[0]
print(f"Metadata: {metadata}")

# Test reconstruction
print("\nReconstructing image...")
start = time.time()
reconstructed_path = output_dir / "reconstructed.png"
decomposer.reconstruct_image(comp_id, reconstructed_path)
recon_time = (time.time() - start) * 1000
print(f"Reconstruction time: {recon_time:.2f}ms")

print(f"Reconstructed to: {reconstructed_path}")

# Verify
recon_img = Image.open(reconstructed_path)
print(f"  Size: {recon_img.size}")

# Check atom counts
cursor.execute("SELECT COUNT(*) FROM atom WHERE modality=2 AND atom_class=0")
pixel_atoms = cursor.fetchone()[0]
print(f"\nTotal pixel atoms in database: {pixel_atoms}")

cursor.execute("SELECT COUNT(*) FROM atom WHERE modality=2 AND atom_class=1")
image_comps = cursor.fetchone()[0]
print(f"Total image compositions: {image_comps}")

conn.close()
print("\n✓ Image decomposition test complete")
