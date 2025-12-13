"""
Profile image decomposition - where does the time go?
"""

import psycopg2
import numpy as np
from PIL import Image
from pathlib import Path
import time
import struct

output_dir = Path("test_data")
output_dir.mkdir(exist_ok=True)

import sys
sys.path.insert(0, str(Path(__file__).parent.parent))

conn = psycopg2.connect(
    host='localhost',
    dbname='hartonomous',
    user='hartonomous'
)

# Test 256x256 image with detailed timing
width, height = 256, 256
print(f"Profiling {width}x{height} image decomposition...\n")

# Generate image
img = Image.new('RGB', (width, height))
pixels = img.load()

for y in range(height):
    for x in range(width):
        r = int((x / width) * 255)
        g = int((y / height) * 255)
        b = 128
        pixels[x, y] = (r, g, b)

img_path = output_dir / f"profile_test.png"
img.save(img_path)

# Manual decomposition with timing
from blake3 import blake3

# Step 1: Load and quantize
start = time.time()
img_loaded = Image.open(img_path).convert('RGB')
pixels_array = np.array(img_loaded)
quantize_factor = 256 // 6
quantized = (pixels_array // quantize_factor) * quantize_factor
load_time = (time.time() - start) * 1000
print(f"1. Load & quantize: {load_time:.2f}ms")

# Step 2: Generate atom IDs
start = time.time()
atom_ids = []
modality = 2
semantic_class = 0

for y in range(height):
    for x in range(width):
        r, g, b = quantized[y, x]
        atom_id = blake3(
            modality.to_bytes(1, 'big') +
            semantic_class.to_bytes(2, 'big') +
            struct.pack('BBB', r, g, b)
        ).digest()
        atom_ids.append((atom_id, r, g, b))

hash_time = (time.time() - start) * 1000
print(f"2. Generate {len(atom_ids)} atom IDs: {hash_time:.2f}ms ({len(atom_ids)/(hash_time/1000):.0f} hashes/sec)")

# Step 3: Ensure atoms exist (with deduplication)
start = time.time()
cursor = conn.cursor()

unique_atoms = {}
for atom_id, r, g, b in atom_ids:
    unique_atoms[atom_id] = (r, g, b)

print(f"   Unique atoms: {len(unique_atoms)} (dedup ratio: {len(atom_ids)/len(unique_atoms):.1f}x)")

inserted = 0
already_existed = 0

for atom_id, (r, g, b) in unique_atoms.items():
    pixel_bytes = struct.pack('BBB', r, g, b)
    
    cursor.execute("""
        INSERT INTO atom (atom_id, atom_class, modality, subtype, atomic_value, geom)
        VALUES (%s, 0, 2, 'pixel', %s, ST_MakePoint(0, 0, 0, 1)::geometry)
        ON CONFLICT (atom_id) DO NOTHING
    """, (atom_id, pixel_bytes))
    
    if cursor.rowcount > 0:
        inserted += 1
    else:
        already_existed += 1

conn.commit()
insert_time = (time.time() - start) * 1000
print(f"3. Insert atoms: {insert_time:.2f}ms")
print(f"   New atoms: {inserted}, Already existed: {already_existed}")

# Step 4: Create composition
start = time.time()

# Generate composition ID
content = b''.join([aid[0] for aid in atom_ids])
comp_id = blake3(b'\x01\x00\x00' + content).digest()

# Get constituent positions
cursor.execute("""
    SELECT ST_X(geom), ST_Y(geom), ST_Z(geom), ST_M(geom)
    FROM atom
    WHERE atom_id = ANY(%s)
    ORDER BY array_position(%s, atom_id)
""", ([aid[0] for aid in atom_ids], [aid[0] for aid in atom_ids]))

coords = cursor.fetchall()

linestring_wkt = 'SRID=4326;LINESTRING ZM(' + ','.join(
    f'{x} {y} {z} {m}' for x, y, z, m in coords
) + ')'

cursor.execute("""
    INSERT INTO atom (atom_id, atom_class, modality, subtype, geom, metadata)
    VALUES (%s, 1, 2, 'image', %s::geometry, %s)
    ON CONFLICT (atom_id) DO NOTHING
""", (comp_id, linestring_wkt, '{"width": 256, "height": 256}'))

comp_create_time = (time.time() - start) * 1000
print(f"4. Create composition geometry: {comp_create_time:.2f}ms")

# Step 5: Insert composition relationships (BATCHED)
start = time.time()

# Prepare all rows
relationship_rows = [
    (comp_id, atom_id, order)
    for order, (atom_id, _, _, _) in enumerate(atom_ids)
]

# Use executemany for batch insert
from psycopg2.extras import execute_values

execute_values(cursor, """
    INSERT INTO atom_compositions (parent_atom_id, component_atom_id, sequence_index)
    VALUES %s
    ON CONFLICT DO NOTHING
""", relationship_rows)

conn.commit()
relationship_time = (time.time() - start) * 1000
print(f"5. Insert {len(atom_ids)} composition relationships (BATCHED): {relationship_time:.2f}ms")

total_time = load_time + hash_time + insert_time + comp_create_time + relationship_time

print(f"\n=== Total: {total_time:.2f}ms ===")
print(f"Load/quantize:     {load_time/total_time*100:5.1f}%")
print(f"Hash generation:   {hash_time/total_time*100:5.1f}%")
print(f"Atom insertion:    {insert_time/total_time*100:5.1f}%")
print(f"Composition geom:  {comp_create_time/total_time*100:5.1f}%")
print(f"Relationships:     {relationship_time/total_time*100:5.1f}%")

conn.close()
