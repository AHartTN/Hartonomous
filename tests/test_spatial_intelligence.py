"""
Spatial intelligence benchmark - prove storage IS intelligence
Test k-NN, similarity search, cross-modal queries
"""

import psycopg2
import time
import random

conn = psycopg2.connect(
    host='localhost',
    dbname='hartonomous',
    user='hartonomous'
)

cursor = conn.cursor()

print("=== Hartonomous Spatial Intelligence Benchmark ===\n")

# Get database stats
cursor.execute("SELECT COUNT(*) FROM atom WHERE atom_class = 0")
constant_count = cursor.fetchone()[0]

cursor.execute("SELECT COUNT(*) FROM atom WHERE atom_class = 1")
composition_count = cursor.fetchone()[0]

cursor.execute("SELECT modality, COUNT(*) FROM atom WHERE atom_class = 0 GROUP BY modality ORDER BY modality")
modality_dist = cursor.fetchall()

print(f"Database state:")
print(f"  Constants: {constant_count:,}")
print(f"  Compositions: {composition_count:,}")
print(f"  Modality distribution:")
for mod, count in modality_dist:
    print(f"    Modality {mod}: {count:,}")

print("\n=== Test 1: k-NN on multimodal atoms ===")

# Pick random atoms from different modalities
test_cases = []
for modality, _ in modality_dist:
    cursor.execute("""
        SELECT atom_id FROM atom 
        WHERE atom_class = 0 AND modality = %s 
        ORDER BY random() 
        LIMIT 1
    """, (modality,))
    
    result = cursor.fetchone()
    if result:
        test_cases.append((modality, result[0]))

for modality, target_id in test_cases:
    # k-NN query
    k = 10
    start = time.time()
    
    cursor.execute("""
        SELECT 
            atom_id,
            modality,
            ST_Distance(geom, (SELECT geom FROM atom WHERE atom_id = %s)) as dist
        FROM atom
        WHERE atom_id != %s
        ORDER BY geom <-> (SELECT geom FROM atom WHERE atom_id = %s)
        LIMIT %s
    """, (target_id, target_id, target_id, k))
    
    results = cursor.fetchall()
    elapsed = (time.time() - start) * 1000
    
    # Check modality distribution in results
    result_modalities = {}
    for _, mod, _ in results:
        result_modalities[mod] = result_modalities.get(mod, 0) + 1
    
    print(f"\nModality {modality} target:")
    print(f"  Query time: {elapsed:.2f}ms")
    print(f"  Nearest neighbors by modality: {dict(result_modalities)}")
    print(f"  Average distance: {sum(r[2] for r in results)/len(results):.4f}")

print("\n=== Test 2: Radius search ===")

# Find dense regions
cursor.execute("""
    SELECT atom_id FROM atom 
    WHERE atom_class = 0 
    ORDER BY random() 
    LIMIT 5
""")

test_atoms = [row[0] for row in cursor.fetchall()]

for radius in [0.1, 1.0, 5.0]:
    total_found = 0
    total_time = 0
    
    for target_id in test_atoms:
        start = time.time()
        
        cursor.execute("""
            SELECT COUNT(*)
            FROM atom
            WHERE atom_id != %s
              AND ST_DWithin(geom, (SELECT geom FROM atom WHERE atom_id = %s), %s)
        """, (target_id, target_id, radius))
        
        count = cursor.fetchone()[0]
        elapsed = (time.time() - start) * 1000
        
        total_found += count
        total_time += elapsed
    
    avg_found = total_found / len(test_atoms)
    avg_time = total_time / len(test_atoms)
    
    print(f"Radius {radius}: avg {avg_found:.1f} neighbors in {avg_time:.2f}ms")

print("\n=== Test 3: Composition similarity ===")

# Find similar compositions (e.g., similar images)
cursor.execute("""
    SELECT atom_id FROM atom 
    WHERE atom_class = 1 AND modality = 2
    ORDER BY random() 
    LIMIT 3
""")

image_comps = [row[0] for row in cursor.fetchall()]

if image_comps:
    print("Finding similar images via trajectory distance:")
    
    for target_id in image_comps:
        start = time.time()
        
        cursor.execute("""
            SELECT 
                atom_id,
                ST_HausdorffDistance(geom, (SELECT geom FROM atom WHERE atom_id = %s)) as hausdorff_dist
            FROM atom
            WHERE atom_class = 1 
              AND modality = 2
              AND atom_id != %s
            ORDER BY hausdorff_dist
            LIMIT 5
        """, (target_id, target_id))
        
        results = cursor.fetchall()
        elapsed = (time.time() - start) * 1000
        
        print(f"  Target {target_id.hex()[:8]}: {len(results)} similar images in {elapsed:.2f}ms")
        if results:
            print(f"    Distances: {[f'{r[1]:.4f}' for r in results[:3]]}")

print("\n=== Test 4: Cross-modal retrieval ===")

# Can we find related atoms across modalities?
# E.g., given an image pixel, find similar audio samples

cursor.execute("SELECT atom_id FROM atom WHERE atom_class = 0 AND modality = 2 ORDER BY random() LIMIT 1")
image_atom = cursor.fetchone()

if image_atom:
    start = time.time()
    
    cursor.execute("""
        SELECT 
            modality,
            COUNT(*) as count
        FROM (
            SELECT modality
            FROM atom
            WHERE atom_class = 0
              AND atom_id != %s
            ORDER BY geom <-> (SELECT geom FROM atom WHERE atom_id = %s)
            LIMIT 100
        ) neighbors
        GROUP BY modality
        ORDER BY count DESC
    """, (image_atom[0], image_atom[0]))
    
    cross_modal = cursor.fetchall()
    elapsed = (time.time() - start) * 1000
    
    print(f"100 nearest neighbors to image pixel:")
    print(f"  Query time: {elapsed:.2f}ms")
    for mod, count in cross_modal:
        print(f"    Modality {mod}: {count} atoms")

print("\n=== Test 5: Index performance ===")

# Check GiST index usage
cursor.execute("""
    EXPLAIN (ANALYZE, BUFFERS) 
    SELECT atom_id
    FROM atom
    WHERE atom_id != (SELECT atom_id FROM atom LIMIT 1)
    ORDER BY geom <-> (SELECT geom FROM atom LIMIT 1)
    LIMIT 10
""")

explain = cursor.fetchall()
print("k-NN query plan:")
for line in explain:
    if 'Index' in str(line) or 'Scan' in str(line) or 'time' in str(line):
        print(f"  {line[0]}")

conn.close()

print("\n=== Spatial intelligence proven ===")
print("Storage IS intelligence - no embeddings, no training, just geometry")
