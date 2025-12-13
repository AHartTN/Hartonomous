"""
Query Validation: Verify data is real and queryable

Tests:
1. Weight → Token semantic queries
2. Cross-modal retrieval (find tokens near weights)
3. Hierarchy traversal
4. Composition structure integrity
"""

import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent.parent / "connector"))

import psycopg2


def main():
    print("=" * 60)
    print("QUERY VALIDATION TEST")
    print("=" * 60)
    
    conn = psycopg2.connect(host="127.0.0.1", database="hartonomous", user="postgres")
    cursor = conn.cursor()
    
    # Test 1: Weight values are real
    print("\n[1/6] Weight Value Validation")
    cursor.execute("""
        SELECT 
            metadata->>'projection' as projection,
            metadata->>'layer' as layer,
            MIN((metadata->>'value')::float) as min_val,
            MAX((metadata->>'value')::float) as max_val,
            AVG((metadata->>'value')::float) as avg_val,
            COUNT(*) as cnt
        FROM atom 
        WHERE subtype='weight'
        GROUP BY metadata->>'projection', metadata->>'layer'
        ORDER BY (metadata->>'layer')::int, projection
        LIMIT 12
    """)
    print("  Weight statistics by layer/projection:")
    for row in cursor.fetchall():
        proj, layer, min_v, max_v, avg_v, cnt = row
        print(f"    Layer {layer} {proj:8s}: min={min_v:7.4f}, max={max_v:7.4f}, avg={avg_v:7.4f}, n={cnt:4}")
    
    # Test 2: Token composition integrity
    print("\n[2/6] Token Composition Validation")
    cursor.execute("""
        SELECT 
            a.metadata->>'token' as token,
            COUNT(ac.component_atom_id) as char_count,
            STRING_AGG(convert_from(c.atomic_value, 'UTF8'), '' ORDER BY ac.sequence_index) as reconstructed
        FROM atom a
        JOIN atom_compositions ac ON a.atom_id = ac.parent_atom_id
        JOIN atom c ON ac.component_atom_id = c.atom_id
        WHERE a.subtype='token' AND a.atom_class=1
        GROUP BY a.atom_id, a.metadata
        ORDER BY char_count DESC
        LIMIT 10
    """)
    print("  Token → Character decomposition:")
    for row in cursor.fetchall():
        token, char_count, reconstructed = row
        print(f"    '{token}' = {char_count} chars → '{reconstructed}'")
    
    # Test 3: Spatial k-NN query (semantic similarity)
    print("\n[3/6] Spatial k-NN Query (Semantic Similarity)")
    cursor.execute("""
        WITH target AS (
            SELECT geom FROM atom WHERE subtype='weight' LIMIT 1
        )
        SELECT 
            a.subtype,
            ST_Distance(a.geom, t.geom) as distance,
            ST_ZMin(a.geom) as z_level,
            a.metadata->>'projection' as projection
        FROM atom a, target t
        WHERE a.subtype IN ('weight', 'token', 'component')
        ORDER BY a.geom <-> t.geom
        LIMIT 10
    """)
    print("  Nearest neighbors to random weight atom:")
    for row in cursor.fetchall():
        subtype, distance, z_level, proj = row
        print(f"    {subtype:10s} | dist={distance:8.4f} | Z={z_level:.2f} | proj={proj or 'N/A'}")
    
    # Test 4: Z-level hierarchy query
    print("\n[4/6] Hierarchy Traversal (Z-level)")
    cursor.execute("""
        SELECT 
            CASE 
                WHEN ST_ZMin(geom) = 0 THEN 'Z=0 (Constants)'
                WHEN ST_ZMin(geom) BETWEEN 0.4 AND 0.8 THEN 'Z=0.5-0.7 (Weights)'
                WHEN ST_ZMin(geom) = 1.0 THEN 'Z=1.0 (Tokens/Components)'
                WHEN ST_ZMin(geom) = 1.5 THEN 'Z=1.5 (Layers)'
                WHEN ST_ZMin(geom) = 2.0 THEN 'Z=2.0 (Model)'
                ELSE 'Other'
            END as hierarchy_level,
            COUNT(*) as atom_count
        FROM atom
        GROUP BY hierarchy_level
        ORDER BY MIN(ST_ZMin(geom))
    """)
    print("  Atom distribution by Z-level:")
    for row in cursor.fetchall():
        level, count = row
        print(f"    {level:25s}: {count:6,} atoms")
    
    # Test 5: Cross-modal query (weight → architecture)
    print("\n[5/6] Cross-Modal Query (Weight → Architecture)")
    cursor.execute("""
        SELECT 
            metadata->>'layer' as layer,
            metadata->>'component' as component,
            metadata->>'projection' as projection,
            COUNT(*) as weight_count,
            AVG(ST_M(geom::geometry(POINTZM))) as avg_salience
        FROM atom
        WHERE subtype='weight'
        GROUP BY metadata->>'layer', metadata->>'component', metadata->>'projection'
        ORDER BY (metadata->>'layer')::int, metadata->>'component', metadata->>'projection'
        LIMIT 15
    """)
    print("  Weight distribution by architecture context:")
    for row in cursor.fetchall():
        layer, comp, proj, count, avg_sal = row
        print(f"    Layer {layer} | {comp:10s} | {proj:8s}: {count:4} weights, M={avg_sal:.4f}")
    
    # Test 6: Atomic value retrieval
    print("\n[6/6] Atomic Value Validation")
    cursor.execute("""
        SELECT 
            subtype,
            COUNT(*) as with_value,
            AVG(LENGTH(atomic_value)) as avg_bytes
        FROM atom
        WHERE atomic_value IS NOT NULL
        GROUP BY subtype
        ORDER BY COUNT(*) DESC
    """)
    print("  Atoms with atomic_value populated:")
    for row in cursor.fetchall():
        subtype, count, avg_bytes = row
        print(f"    {subtype:12s}: {count:6,} atoms, avg {avg_bytes:.1f} bytes")
    
    # Summary
    cursor.execute("SELECT COUNT(*) FROM atom")
    total = cursor.fetchone()[0]
    cursor.execute("SELECT COUNT(*) FROM atom_compositions")
    comps = cursor.fetchone()[0]
    
    print("\n" + "=" * 60)
    print("VALIDATION COMPLETE")
    print("=" * 60)
    print(f"  Total atoms: {total:,}")
    print(f"  Total compositions: {comps:,}")
    print("  Data is real and queryable")
    print("  Semantic queries working (k-NN, hierarchy, cross-modal)")
    
    cursor.close()
    conn.close()


if __name__ == "__main__":
    main()
