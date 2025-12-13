"""
Multi-threaded image decomposition test
"""

import psycopg2
from psycopg2.pool import ThreadedConnectionPool
import numpy as np
from PIL import Image
from pathlib import Path
import time
from concurrent.futures import ThreadPoolExecutor, as_completed
import multiprocessing

output_dir = Path("test_data")
output_dir.mkdir(exist_ok=True)

import sys
sys.path.insert(0, str(Path(__file__).parent.parent))

from connector.image_decomposer import ImageDecomposer

# Create connection pool
pool = ThreadedConnectionPool(
    minconn=1,
    maxconn=multiprocessing.cpu_count(),
    host='localhost',
    dbname='hartonomous',
    user='hartonomous'
)

def decompose_single_image(size_tuple):
    """Decompose one image using a pooled connection"""
    width, height = size_tuple
    
    # Get connection from pool
    conn = pool.getconn()
    
    try:
        decomposer = ImageDecomposer(conn)
        
        # Generate image
        img = Image.new('RGB', (width, height))
        pixels = img.load()
        
        for y in range(height):
            for x in range(width):
                r = int((x / width) * 255)
                g = int((y / height) * 255)
                b = 128
                pixels[x, y] = (r, g, b)
        
        img_path = output_dir / f"parallel_{width}x{height}_{time.time()}.png"
        img.save(img_path)
        
        # Decompose
        start = time.time()
        comp_id = decomposer.decompose_image(img_path)
        elapsed = (time.time() - start) * 1000
        
        return {
            'size': f"{width}x{height}",
            'pixels': width * height,
            'time_ms': elapsed,
            'comp_id': comp_id.hex()[:16]
        }
    finally:
        # Return connection to pool
        pool.putconn(conn)

# Test parallel decomposition
print(f"=== Multi-threaded Image Decomposition ===")
print(f"CPU cores: {multiprocessing.cpu_count()}\n")

# Generate 8 images to decompose in parallel
test_images = [(256, 256) for _ in range(8)]

# Single-threaded baseline
print("Single-threaded (sequential):")
start = time.time()
for size in test_images:
    result = decompose_single_image(size)
single_time = time.time() - start
print(f"  Total time: {single_time:.2f}s")
print(f"  Images/sec: {len(test_images)/single_time:.2f}\n")

# Multi-threaded
print(f"Multi-threaded ({multiprocessing.cpu_count()} workers):")
start = time.time()

with ThreadPoolExecutor(max_workers=multiprocessing.cpu_count()) as executor:
    futures = [executor.submit(decompose_single_image, size) for size in test_images]
    
    results = []
    for future in as_completed(futures):
        result = future.result()
        results.append(result)
        print(f"  Completed {result['size']}: {result['time_ms']:.2f}ms")

multi_time = time.time() - start
print(f"\n  Total time: {multi_time:.2f}s")
print(f"  Images/sec: {len(test_images)/multi_time:.2f}")
print(f"  Speedup: {single_time/multi_time:.2f}x")

pool.closeall()
