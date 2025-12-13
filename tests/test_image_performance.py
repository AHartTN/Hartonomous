"""
Test image decomposition at multiple resolutions
"""

import psycopg2
import numpy as np
from PIL import Image
from pathlib import Path
import time

output_dir = Path("test_data")
output_dir.mkdir(exist_ok=True)

import sys
sys.path.insert(0, str(Path(__file__).parent.parent))

from connector.image_decomposer import ImageDecomposer

conn = psycopg2.connect(
    host='localhost',
    dbname='hartonomous',
    user='hartonomous'
)

decomposer = ImageDecomposer(conn)

# Test different sizes
test_sizes = [
    (64, 64),
    (128, 128),
    (256, 256),
    (512, 512),
]

print("=== Image Decomposition Performance Test ===\n")

results = []

for width, height in test_sizes:
    print(f"Testing {width}x{height} ({width*height:,} pixels)...")
    
    # Generate gradient image
    img = Image.new('RGB', (width, height))
    pixels = img.load()
    
    for y in range(height):
        for x in range(width):
            r = int((x / width) * 255)
            g = int((y / height) * 255)
            b = 128 + int((x + y) / (width + height) * 127)
            pixels[x, y] = (r, g, b)
    
    img_path = output_dir / f"test_{width}x{height}.png"
    img.save(img_path)
    
    # Decompose
    start = time.time()
    comp_id = decomposer.decompose_image(img_path)
    decomp_time = (time.time() - start) * 1000
    
    # Check components
    cursor = conn.cursor()
    cursor.execute("SELECT COUNT(*) FROM atom_compositions WHERE parent_atom_id = %s", (comp_id,))
    components = cursor.fetchone()[0]
    
    # Reconstruct
    start = time.time()
    recon_path = output_dir / f"recon_{width}x{height}.png"
    decomposer.reconstruct_image(comp_id, recon_path)
    recon_time = (time.time() - start) * 1000
    
    # Calculate throughput
    pixels_per_sec = (width * height) / (decomp_time / 1000)
    
    results.append({
        'size': f"{width}x{height}",
        'pixels': width * height,
        'components': components,
        'decomp_ms': decomp_time,
        'recon_ms': recon_time,
        'pixels_per_sec': pixels_per_sec,
        'ratio': decomp_time / recon_time
    })
    
    print(f"  Decomposition: {decomp_time:.2f}ms ({pixels_per_sec:.0f} pixels/sec)")
    print(f"  Reconstruction: {recon_time:.2f}ms")
    print(f"  Components: {components:,}")
    print(f"  Ingestion/Query ratio: {decomp_time/recon_time:.1f}x\n")

# Summary
print("=== Summary ===")
print(f"{'Size':<12} {'Pixels':<10} {'Components':<12} {'Decomp (ms)':<15} {'Recon (ms)':<12} {'Throughput':<15}")
print("-" * 90)

for r in results:
    print(f"{r['size']:<12} {r['pixels']:<10,} {r['components']:<12,} {r['decomp_ms']:<15.2f} {r['recon_ms']:<12.2f} {r['pixels_per_sec']:<15,.0f}")

# Check total unique atoms created
cursor.execute("SELECT COUNT(DISTINCT component_atom_id) FROM atom_compositions ac JOIN atom a ON a.atom_id = ac.parent_atom_id WHERE a.modality = 2")
unique_pixels = cursor.fetchone()[0]

cursor.execute("SELECT COUNT(*) FROM atom WHERE modality = 2 AND atom_class = 0")
total_pixel_atoms = cursor.fetchone()[0]

print(f"\nTotal unique pixel atoms: {total_pixel_atoms:,}")
print(f"Pixels used in compositions: {unique_pixels:,}")

conn.close()
print("\nImage performance test complete")
