"""
Benchmark: Query performance on 10M+ atom database
"""

import psycopg2
import time
import statistics


def benchmark_queries():
    conn = psycopg2.connect(
        host='127.0.0.1',
        port=5432,
        user='postgres',
        password='postgres',
        database='hartonomous'
    )
    
    cursor = conn.cursor()
    
    print("="*60)
    print("QUERY BENCHMARKS")
    print("="*60)
    
    # Benchmark 1: k-NN spatial query (with Hilbert index)
    times = []
    for _ in range(10):
        t0 = time.time()
        cursor.execute("""
            SELECT atom_id, ST_X(geom), ST_Y(geom)
            FROM atom
            WHERE subtype = 'weight'
            ORDER BY hilbert_index
            LIMIT 1000
        """)
        results = cursor.fetchall()
        t1 = time.time()
        times.append((t1 - t0) * 1000)
    
    print(f"\n[1] Hilbert-ordered k-NN (1000 weights from 10M)")
    print(f"    Avg: {statistics.mean(times):.2f}ms")
    print(f"    Min: {min(times):.2f}ms")
    print(f"    Max: {max(times):.2f}ms")
    
    # Benchmark 2: Composition reconstruction
    times = []
    for _ in range(10):
        t0 = time.time()
        cursor.execute("""
            SELECT 
                parent.metadata->>'token' as token,
                STRING_AGG(char.metadata->>'char', '' ORDER BY ac.sequence_index) as reconstructed
            FROM atom parent
            JOIN atom_compositions ac ON ac.parent_atom_id = parent.atom_id
            JOIN atom char ON char.atom_id = ac.component_atom_id
            WHERE parent.metadata->>'token' = 'considerably'
            GROUP BY parent.atom_id, parent.metadata->>'token'
        """)
        results = cursor.fetchall()
        t1 = time.time()
        times.append((t1 - t0) * 1000)
    
    print(f"\n[2] Token composition reconstruction")
    print(f"    Avg: {statistics.mean(times):.2f}ms")
    print(f"    Min: {min(times):.2f}ms")
    print(f"    Max: {max(times):.2f}ms")
    
    # Benchmark 3: Hierarchy traversal
    times = []
    for _ in range(10):
        t0 = time.time()
        cursor.execute("""
            SELECT subtype, COUNT(*), AVG(ST_Z(geom))
            FROM atom
            GROUP BY subtype
            ORDER BY AVG(ST_Z(geom)) DESC
        """)
        results = cursor.fetchall()
        t1 = time.time()
        times.append((t1 - t0) * 1000)
    
    print(f"\n[3] Hierarchy aggregation (Z-level grouping)")
    print(f"    Avg: {statistics.mean(times):.2f}ms")
    print(f"    Min: {min(times):.2f}ms")
    print(f"    Max: {max(times):.2f}ms")
    
    # Benchmark 4: Cross-modal query (weights → architecture)
    times = []
    for _ in range(10):
        t0 = time.time()
        cursor.execute("""
            SELECT 
                w.metadata->>'layer' as layer,
                w.metadata->>'component' as component,
                COUNT(*) as weight_count,
                AVG((w.metadata->>'value')::float) as avg_value
            FROM atom w
            WHERE w.subtype = 'weight' 
                AND w.metadata->>'layer' = '0'
            GROUP BY w.metadata->>'layer', w.metadata->>'component'
        """)
        results = cursor.fetchall()
        t1 = time.time()
        times.append((t1 - t0) * 1000)
    
    print(f"\n[4] Cross-modal aggregation (weights by architecture)")
    print(f"    Avg: {statistics.mean(times):.2f}ms")
    print(f"    Min: {min(times):.2f}ms")
    print(f"    Max: {max(times):.2f}ms")
    
    # Benchmark 5: Metadata JSON query
    times = []
    for _ in range(10):
        t0 = time.time()
        cursor.execute("""
            SELECT metadata->>'token', COUNT(*)
            FROM atom
            WHERE subtype = 'token' 
                AND metadata->>'token' LIKE 'the%'
            GROUP BY metadata->>'token'
            LIMIT 100
        """)
        results = cursor.fetchall()
        t1 = time.time()
        times.append((t1 - t0) * 1000)
    
    print(f"\n[5] Metadata search (JSON field query)")
    print(f"    Avg: {statistics.mean(times):.2f}ms")
    print(f"    Min: {min(times):.2f}ms")
    print(f"    Max: {max(times):.2f}ms")
    
    # Benchmark 6: Spatial distance calculation
    times = []
    for _ in range(10):
        t0 = time.time()
        cursor.execute("""
            SELECT a1.metadata->>'token', a2.metadata->>'token', ST_Distance(a1.geom, a2.geom) as dist
            FROM atom a1
            CROSS JOIN LATERAL (
                SELECT metadata, geom FROM atom a2
                WHERE a2.subtype = 'token' 
                    AND a2.atom_id != a1.atom_id
                ORDER BY a1.geom <-> a2.geom
                LIMIT 5
            ) a2
            WHERE a1.metadata->>'token' = 'the'
        """)
        results = cursor.fetchall()
        t1 = time.time()
        times.append((t1 - t0) * 1000)
    
    print(f"\n[6] Spatial k-NN with distance (nearest neighbors to 'the')")
    print(f"    Avg: {statistics.mean(times):.2f}ms")
    print(f"    Min: {min(times):.2f}ms")
    print(f"    Max: {max(times):.2f}ms")
    
    print("\n" + "="*60)
    
    cursor.close()
    conn.close()


if __name__ == "__main__":
    benchmark_queries()
