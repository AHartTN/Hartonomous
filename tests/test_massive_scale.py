"""
Push to 50K+ atoms - stress test spatial indexing
"""

import psycopg2
import time
import numpy as np
import sys
sys.path.insert(0, '.')
from connector.semantic_projector import SemanticProjector

conn = psycopg2.connect(
    host='127.0.0.1',
    dbname='hartonomous',
    user='hartonomous'
)

cursor = conn.cursor()
projector = SemanticProjector(scale=100.0)

print("=== Scaling to 50K+ Atoms ===\n")

cursor.execute("SELECT COUNT(*) FROM atom WHERE atom_class = 0")
initial_atoms = cursor.fetchone()[0]
print(f"Starting: {initial_atoms:,} atoms\n")

from blake3 import blake3
from psycopg2.extras import execute_values

# Generate massive numeric dataset
print("Generating 20K numeric atoms...")
start = time.time()

numeric_rows = []
for i in np.linspace(-10000.0, 10000.0, 20000):
    val_bytes = np.float64(i).tobytes()
    atom_id = blake3(b'\x00\x00\x00' + val_bytes).digest()
    x, y, z, m = projector.project_numeric(float(i))
    numeric_rows.append((atom_id, 0, 0, 'float64', val_bytes, f'POINT ZM({x} {y} {z} {m})', 0))

execute_values(cursor, "INSERT INTO atom (atom_id, atom_class, modality, subtype, atomic_value, geom, hilbert_index) VALUES %s ON CONFLICT DO NOTHING", numeric_rows)
conn.commit()

print(f"  Inserted in {time.time() - start:.2f}s")

# Generate massive text dataset
print("Generating 30K text atoms...")
start = time.time()

text_rows = []

# Dictionary words simulation
for i in range(10000):
    for prefix in ['a', 'b', 'c']:
        token = f"{prefix}word{i}"
        atom_id = blake3(b'\x01\x02\x00' + token.encode()).digest()
        x, y, z, m = projector.project_token(token)
        text_rows.append((atom_id, 0, 1, 'token', token.encode(), f'POINT ZM({x} {y} {z} {m})', 0))

execute_values(cursor, "INSERT INTO atom (atom_id, atom_class, modality, subtype, atomic_value, geom, hilbert_index) VALUES %s ON CONFLICT DO NOTHING", text_rows)
conn.commit()

print(f"  Inserted in {time.time() - start:.2f}s")

# Final count
cursor.execute("SELECT COUNT(*) FROM atom WHERE atom_class = 0")
final_atoms = cursor.fetchone()[0]

print(f"\n=== Database at {final_atoms:,} Atoms ===")

# Performance benchmarks
print(f"\nQuery Performance:")

for k in [10, 100, 1000]:
    times = []
    for _ in range(5):
        start = time.time()
        cursor.execute(f"""
            SELECT atom_id
            FROM atom
            WHERE atom_class = 0
            ORDER BY geom <-> (SELECT geom FROM atom WHERE atom_class = 0 ORDER BY random() LIMIT 1)
            LIMIT {k}
        """)
        cursor.fetchall()
        times.append((time.time() - start) * 1000)
    
    avg = sum(times) / len(times)
    print(f"  k-NN (k={k}): {avg:.2f}ms (min={min(times):.2f}ms, max={max(times):.2f}ms)")

# Storage
cursor.execute("SELECT pg_size_pretty(pg_total_relation_size('atom')), pg_size_pretty(pg_indexes_size('atom'))")
table_size, idx_size = cursor.fetchone()

print(f"\nStorage:")
print(f"  Table: {table_size}")
print(f"  Indexes: {idx_size}")

# Check GiST index usage
cursor.execute("""
    EXPLAIN (ANALYZE, BUFFERS) 
    SELECT atom_id
    FROM atom
    WHERE atom_class = 0
    ORDER BY geom <-> (SELECT geom FROM atom WHERE atom_class = 0 LIMIT 1)
    LIMIT 10
""")

explain = cursor.fetchall()

print(f"\nQuery Plan:")
for line in explain:
    if 'Index Scan' in str(line) or 'Seq Scan' in str(line):
        print(f"  {line[0]}")

conn.close()

print(f"\n✓ Scaled to {final_atoms:,} atoms")
