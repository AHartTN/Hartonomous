"""Test CPE (Compositional Pair Encoding) with Shader Rust library"""

import sys
sys.path.append('connector')

import psycopg2
from semantic_projector import SemanticProjector
import time

# Connect to database
conn = psycopg2.connect(
    host="127.0.0.1",
    database="hartonomous",
    user="postgres"
)
conn.autocommit = True
cursor = conn.cursor()

# Clear database
print("Clearing database...")
cursor.execute("TRUNCATE atom, atom_compositions CASCADE")

# Create sample sequence for CPE testing
print("\nGenerating sample text sequence...")
projector = SemanticProjector(scale=100.0)

# Sample sentence: "the cat sat on the mat the cat ran"
tokens = ["the", "cat", "sat", "on", "the", "mat", "the", "cat", "ran"]

# Insert tokens as atoms
from blake3 import blake3
atom_positions = []

for idx, token in enumerate(tokens):
    # Generate atom_id
    atom_id = blake3(b'\x01\x02\x00' + token.encode()).digest()
    
    # Project to semantic space
    x, y, z, m = projector.project_token(token)
    x, y, z, m = float(x), float(y), float(z), float(m)
    
    # Store for CPE
    atom_positions.append((atom_id, x, y, z, m))
    
    # Insert atom
    cursor.execute("""
        INSERT INTO atom (atom_id, atom_class, modality, subtype, atomic_value, geom, hilbert_index)
        VALUES (%s, 0, 1, 'token', %s, ST_MakePoint(%s, %s, %s, %s), 0)
        ON CONFLICT DO NOTHING
    """, (atom_id, token.encode(), x, y, z, m))

print(f"Inserted {len(tokens)} token atoms")

# Manually build compositions (Rust CPE would do this)
print("\nBuilding compositions...")

# Count pairs
from collections import Counter
pairs = []
for i in range(len(atom_positions) - 1):
    pair = (atom_positions[i], atom_positions[i+1])
    pairs.append(pair)

# Find frequent pairs (threshold = 2)
pair_counts = Counter()
pair_data = {}
for i, ((id1, x1, y1, z1, m1), (id2, x2, y2, z2, m2)) in enumerate(pairs):
    key = (bytes(id1), bytes(id2))
    pair_counts[key] += 1
    if key not in pair_data:
        pair_data[key] = ((id1, x1, y1, z1, m1), (id2, x2, y2, z2, m2))

# Insert compositions
comp_count = 0
for (id1, id2), count in pair_counts.items():
    if count >= 2:  # Frequency threshold
        (atom1_id, x1, y1, z1, m1), (atom2_id, x2, y2, z2, m2) = pair_data[(id1, id2)]
        
        # Generate composition ID (deterministic from constituents)
        comp_input = bytes(atom1_id) + bytes(atom2_id)
        comp_id = blake3(comp_input).digest()
        
        # Build LINESTRING geometry
        linestring_wkt = f"LINESTRING ZM({x1} {y1} 1.0 {count}, {x2} {y2} 1.0 {count})"
        
        # Insert composition atom
        cursor.execute("""
            INSERT INTO atom (atom_id, atom_class, modality, geom, hilbert_index)
            VALUES (%s, 1, 99, ST_GeomFromText(%s, 0), 0)
            ON CONFLICT DO NOTHING
        """, (comp_id, linestring_wkt))
        
        # Insert constituent relationships
        cursor.execute("""
            INSERT INTO atom_compositions (parent_atom_id, component_atom_id, sequence_index)
            VALUES (%s, %s, 0), (%s, %s, 1)
            ON CONFLICT DO NOTHING
        """, (comp_id, bytes(atom1_id), comp_id, bytes(atom2_id)))
        
        comp_count += 1
        print(f"  Created composition for pair with frequency {count}")

print(f"\nTotal compositions created: {comp_count}")

# Verify hierarchy
cursor.execute("""
    SELECT 
        atom_class,
        COUNT(*) as count,
        AVG(ST_ZMin(geom)) as avg_z
    FROM atom
    GROUP BY atom_class
    ORDER BY atom_class
""")

print("\nAtom hierarchy:")
for row in cursor.fetchall():
    atom_class, count, avg_z = row
    class_name = "Constants" if atom_class == 0 else "Compositions"
    print(f"  {class_name} (class={atom_class}): {count} atoms, avg Z-level: {avg_z:.2f}")

# Test composition queries
cursor.execute("""
    SELECT 
        ac.parent_atom_id,
        COUNT(*) as constituent_count,
        ST_AsText(a.geom) as geometry
    FROM atom_compositions ac
    JOIN atom a ON a.atom_id = ac.parent_atom_id
    GROUP BY ac.parent_atom_id, a.geom
    LIMIT 3
""")

print("\nSample compositions:")
for row in cursor.fetchall():
    comp_id, const_count, geom = row
    print(f"  Composition {comp_id.hex()[:16]}...")
    print(f"    Constituents: {const_count}")
    print(f"    Geometry: {geom}")

# Test hierarchy traversal
print("\nTesting hierarchy traversal (abstraction)...")
cursor.execute("""
    WITH target AS (
        SELECT geom FROM atom WHERE atom_class = 0 LIMIT 1
    )
    SELECT 
        ST_ZMin(a.geom) as z_level,
        a.atom_class,
        ST_AsText(a.geom) as geometry
    FROM atom a, target t
    WHERE ST_ZMin(a.geom) > ST_ZMin(t.geom)
    ORDER BY ST_3DDistance(a.geom, t.geom)
    LIMIT 5
""")

print("\nNearby higher-level atoms:")
for row in cursor.fetchall():
    z, atom_class, geom = row
    print(f"  Z={z:.1f}, class={atom_class}: {geom[:60]}...")

conn.close()
print("\n✓ CPE test complete")
