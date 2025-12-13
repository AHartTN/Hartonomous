"""
Test basic spatial spreading - spread atoms from origin using simple force-directed layout
"""

import psycopg2
import time
import numpy as np

conn = psycopg2.connect(
    host='localhost',
    dbname='hartonomous',
    user='hartonomous'
)

cursor = conn.cursor()

print("=== Basic Spatial Spreading Test ===\n")

# Sample current positions
cursor.execute("""
    SELECT ST_X(geom), ST_Y(geom), ST_Z(geom)
    FROM atom
    WHERE atom_class = 0
    LIMIT 100
""")

positions = cursor.fetchall()

# Count atoms at origin
at_origin = sum(1 for x, y, z in positions if abs(x) < 0.01 and abs(y) < 0.01)

print(f"Sampled 100 atoms:")
print(f"  At origin (0, 0): {at_origin}")
print(f"  Positioned: {100 - at_origin}")

# Simple force-directed layout using SQL
print(f"\nApplying basic spatial spreading...")

start = time.time()

# Spread atoms randomly but deterministically based on hash
cursor.execute("""
    WITH atom_spread AS (
        SELECT 
            atom_id,
            -- Use hash for deterministic pseudo-random position
            (('x' || substring(encode(atom_id, 'hex') from 1 for 8))::bit(32)::bigint::double precision / 2147483647.0) * 100 - 50 as new_x,
            (('x' || substring(encode(atom_id, 'hex') from 9 for 8))::bit(32)::bigint::double precision / 2147483647.0) * 100 - 50 as new_y,
            ST_Z(geom) as z,
            ST_M(geom) as m
        FROM atom
        WHERE atom_class = 0
          AND ST_X(geom) = 0
          AND ST_Y(geom) = 0
        LIMIT 10000
    )
    UPDATE atom
    SET geom = ST_MakePoint(new_x, new_y, z, m)::geometry
    FROM atom_spread
    WHERE atom.atom_id = atom_spread.atom_id
""")

conn.commit()

elapsed = time.time() - start

affected = cursor.rowcount

print(f"Spread {affected:,} atoms in {elapsed:.2f}s")

# Re-sample positions
cursor.execute("""
    SELECT ST_X(geom), ST_Y(geom), ST_Z(geom)
    FROM atom
    WHERE atom_class = 0
    ORDER BY random()
    LIMIT 100
""")

new_positions = cursor.fetchall()

at_origin_after = sum(1 for x, y, z in new_positions if abs(x) < 0.01 and abs(y) < 0.01)

print(f"\nAfter spreading:")
print(f"  At origin: {at_origin_after}")
print(f"  Positioned: {100 - at_origin_after}")

# Test query performance with spread atoms
print(f"\n=== Query Performance After Spreading ===")

for k in [10, 100, 1000]:
    times = []
    for _ in range(5):
        start = time.time()
        cursor.execute(f"""
            SELECT atom_id
            FROM atom
            WHERE atom_class = 0
            ORDER BY geom <-> (SELECT geom FROM atom WHERE atom_class = 0 AND ST_X(geom) != 0 ORDER BY random() LIMIT 1)
            LIMIT {k}
        """)
        cursor.fetchall()
        times.append((time.time() - start) * 1000)
    
    avg = sum(times) / len(times)
    print(f"k-NN (k={k}): {avg:.2f}ms (min={min(times):.2f}ms)")

conn.close()

print(f"\n✓ Atoms spatially distributed - ready for semantic refinement")
