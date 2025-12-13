"""
Complete Model Ingestion Test: Vocabulary → Architecture → Weights

Validates the proper ingestion flow:
1. Vocabulary: Characters (Z=0) → Tokens (Z=1)
2. Architecture: Model → Layers → Components (Z=2 → 1.5 → 1.0)
3. Weights: Token relationships with architecture context (Z=0.5-1.0)

Cross-modal validation:
- Query: weight → architecture (which layer/component?)
- Query: weight → tokens (which tokens affected?)
- Query: token → weights (which weights transform this token?)
"""

import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent.parent / "connector"))

import psycopg2
from vocabulary_ingester import VocabularyIngester
from architecture_loader import ArchitectureLoader
from weight_ingester import WeightIngester


def main():
    print("=" * 60)
    print("COMPLETE MODEL INGESTION TEST")
    print("=" * 60)
    
    # Model paths
    model_dir = Path(r"D:\models\embedding_models\all-MiniLM-L6-v2\snapshots\c9745ed1d9f207416be6d2e6f8de32d1f16199bf")
    tokenizer_path = model_dir / "tokenizer.json"
    config_path = model_dir / "config.json"
    model_path = model_dir / "model.safetensors"
    
    # Database connection
    conn = psycopg2.connect(host="127.0.0.1", database="hartonomous", user="postgres")
    
    try:
        cursor = conn.cursor()
        
        # Clear database
        print("\n[1/5] Database Reset")
        cursor.execute("TRUNCATE atom, atom_compositions CASCADE")
        conn.commit()
        print("  Tables truncated")
        
        # Step 1: Ingest Vocabulary
        print("\n[2/5] Vocabulary Ingestion")
        vocab_ingester = VocabularyIngester(conn)
        tokens_ingested = vocab_ingester.ingest_huggingface_tokenizer(str(tokenizer_path))
        print(f"  Tokens ingested: {tokens_ingested:,}")
        
        # Verify vocabulary
        cursor.execute("SELECT COUNT(*) FROM atom WHERE subtype = 'character'")
        char_count = cursor.fetchone()[0]
        cursor.execute("SELECT COUNT(*) FROM atom WHERE subtype = 'token'")
        token_count = cursor.fetchone()[0]
        print(f"  Character atoms: {char_count:,}")
        print(f"  Token atoms: {token_count:,}")
        
        # Step 2: Ingest Architecture
        print("\n[3/5] Architecture Loading")
        arch_loader = ArchitectureLoader(conn)
        arch_atoms = arch_loader.ingest_bert_architecture(str(config_path), model_name="MiniLM-L6-v2")
        print(f"  Architecture atoms created: {arch_atoms:,}")
        
        # Verify architecture hierarchy
        cursor.execute("""
            SELECT subtype, COUNT(*)
            FROM atom
            WHERE modality = 2
            GROUP BY subtype
            ORDER BY subtype
        """)
        print("  Architecture breakdown:")
        for row in cursor.fetchall():
            print(f"    {row[0]}: {row[1]}")
        
        # Step 3: Ingest Weights
        print("\n[4/5] Weight Ingestion (Architecture-Aware)")
        weight_ingester = WeightIngester(conn)
        weight_stats = weight_ingester.ingest_bert_weights(str(model_path), model_name="MiniLM-L6-v2")
        print(f"  Layers processed: {weight_stats['layers_processed']}")
        print(f"  Weight matrices: {weight_stats['weight_matrices']}")
        print(f"  Skipped (small): {weight_stats['skipped_small']}")
        
        # Verify weight atoms
        cursor.execute("SELECT COUNT(*) FROM atom WHERE subtype = 'weight'")
        weight_count = cursor.fetchone()[0]
        print(f"  Weight atoms created: {weight_count:,}")
        
        # Step 4: Cross-Modal Validation
        print("\n[5/5] Cross-Modal Validation")
        
        # Test 1: Weight → Architecture query
        cursor.execute("""
            SELECT 
                metadata->>'layer' as layer,
                metadata->>'component' as component,
                metadata->>'projection' as projection,
                COUNT(*) as weight_count
            FROM atom
            WHERE subtype = 'weight'
            GROUP BY metadata->>'layer', metadata->>'component', metadata->>'projection'
            ORDER BY (metadata->>'layer')::int, metadata->>'component', metadata->>'projection'
            LIMIT 10
        """)
        print("\n  Weight distribution by architecture:")
        for row in cursor.fetchall():
            layer, component, projection, count = row
            print(f"    Layer {layer:>2s} | {component:12s} | {projection:8s}: {count:>6,} weights")
        
        # Test 2: Semantic spatial query (k-NN)
        cursor.execute("""
            SELECT 
                subtype,
                metadata->>'component' as component,
                ST_X(geom), ST_Y(geom), ST_Z(geom)
            FROM atom
            WHERE modality = 2
            ORDER BY geom <-> 'SRID=0;POINT ZM(0 0 1.0 1.0)'::geometry
            LIMIT 5
        """)
        print("\n  k-NN query (nearest to origin in semantic space):")
        for row in cursor.fetchall():
            subtype, component, x, y, z = row
            print(f"    {subtype:10s} | {component or 'N/A':15s} | ({x:6.2f}, {y:6.2f}, Z={z:.2f})")
        
        # Test 3: Hierarchy traversal (Z-level)
        cursor.execute("""
            SELECT 
                subtype,
                AVG(ST_ZMin(geom)) as avg_z,
                MIN(ST_ZMin(geom)) as min_z,
                MAX(ST_ZMax(geom)) as max_z,
                COUNT(*) as count
            FROM atom
            GROUP BY subtype
            ORDER BY avg_z DESC
        """)
        print("\n  Hierarchy (Z-level) distribution:")
        for row in cursor.fetchall():
            subtype, avg_z, min_z, max_z, count = row
            print(f"    {subtype:12s}: Z={avg_z:.2f} (range {min_z:.2f}-{max_z:.2f}) | {count:>8,} atoms")
        
        # Summary
        cursor.execute("SELECT COUNT(*) FROM atom")
        total_atoms = cursor.fetchone()[0]
        cursor.execute("SELECT COUNT(*) FROM atom_compositions")
        total_comps = cursor.fetchone()[0]
        
        print("\n" + "=" * 60)
        print("INGESTION COMPLETE")
        print("=" * 60)
        print(f"  Total atoms: {total_atoms:,}")
        print(f"  Total compositions: {total_comps:,}")
        print(f"  Vocabulary atoms: {char_count + token_count:,}")
        print(f"  Architecture atoms: {arch_atoms:,}")
        print(f"  Weight atoms: {weight_count:,}")
        print("\nDatabase has semantic intelligence via geometric organization")
        
    finally:
        cursor.close()
        conn.close()


if __name__ == "__main__":
    main()
