"""
Test Hilbert indexing: Verify spatial locality preservation

Hilbert curves map 4D space to 1D while preserving locality.
Close points in 4D should have close Hilbert indices.
"""

import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent.parent / "connector"))

import psycopg2
from hilbert_indexer import HilbertIndexer


def main():
    print("=" * 60)
    print("HILBERT INDEXING TEST")
    print("=" * 60)
    
    conn = psycopg2.connect(host="127.0.0.1", database="hartonomous", user="postgres")
    
    # Reindex all atoms
    indexer = HilbertIndexer(conn)
    count = indexer.reindex_all_atoms(batch_size=5000)
    
    # Verify indexing
    cursor = conn.cursor()
    
    print("\n[Verification] Checking index distribution...")
    cursor.execute("""
        SELECT 
            COUNT(*) as total,
            COUNT(DISTINCT hilbert_index) as unique_indices,
            MIN(hilbert_index) as min_idx,
            MAX(hilbert_index) as max_idx
        FROM atom
        WHERE hilbert_index != 0
    """)
    total, unique, min_idx, max_idx = cursor.fetchone()
    print(f"  Total indexed: {total:,}")
    print(f"  Unique indices: {unique:,}")
    print(f"  Range: {min_idx:,} to {max_idx:,}")
    
    # Test locality preservation
    print("\n[Locality Test] Comparing spatial vs Hilbert distance...")
    cursor.execute("""
        WITH samples AS (
            SELECT atom_id, geom, hilbert_index
            FROM atom
            WHERE subtype='weight' AND hilbert_index != 0
            LIMIT 100
        ),
        pairs AS (
            SELECT 
                a.atom_id as id1,
                b.atom_id as id2,
                ST_Distance(a.geom, b.geom) as spatial_dist,
                ABS(a.hilbert_index - b.hilbert_index) as hilbert_dist
            FROM samples a
            CROSS JOIN samples b
            WHERE a.atom_id < b.atom_id
            LIMIT 50
        )
        SELECT 
            CORR(spatial_dist, hilbert_dist) as correlation,
            AVG(spatial_dist) as avg_spatial,
            AVG(hilbert_dist) as avg_hilbert
        FROM pairs
    """)
    row = cursor.fetchone()
    if row and row[0]:
        corr, avg_spatial, avg_hilbert = row
        print(f"  Correlation (spatial ↔ Hilbert): {corr:.3f}")
        print(f"  Avg spatial distance: {avg_spatial:.2f}")
        print(f"  Avg Hilbert distance: {avg_hilbert:,.0f}")
    
    # Query performance comparison
    print("\n[Performance] Query time with Hilbert ordering...")
    cursor.execute("EXPLAIN ANALYZE SELECT * FROM atom ORDER BY hilbert_index LIMIT 1000")
    plan = cursor.fetchall()
    for line in plan:
        if 'Execution Time' in str(line):
            print(f"  {line[0]}")
    
    cursor.close()
    conn.close()
    
    print("\n" + "=" * 60)
    print("INDEXING COMPLETE")
    print("=" * 60)


if __name__ == "__main__":
    main()
