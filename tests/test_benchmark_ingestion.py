"""
Benchmark: Complete ingestion pipeline with detailed performance logging
"""

import psycopg2
import time
import json
from pathlib import Path
import sys
sys.path.insert(0, str(Path(__file__).parent.parent / 'connector'))

from vocabulary_ingester import VocabularyIngester
from architecture_loader import ArchitectureLoader
from weight_ingester import WeightIngester


def benchmark_ingestion():
    conn = psycopg2.connect(
        host='127.0.0.1',
        port=5432,
        user='postgres',
        password='postgres',
        database='hartonomous'
    )
    
    stats = {
        'start_time': time.time(),
        'phases': {}
    }
    
    model_dir = Path(r"D:\models\embedding_models\all-MiniLM-L6-v2\snapshots\c9745ed1d9f207416be6d2e6f8de32d1f16199bf")
    
    # Phase 1: Vocabulary
    t0 = time.time()
    vocab_ingester = VocabularyIngester(conn)
    tokens = vocab_ingester.ingest_huggingface_tokenizer(str(model_dir / 'tokenizer.json'))
    t1 = time.time()
    stats['phases']['vocabulary'] = {
        'duration': t1 - t0,
        'tokens': tokens,
        'rate': tokens / (t1 - t0)
    }
    print(f"[VOCAB] {tokens:,} tokens | {t1-t0:.2f}s | {tokens/(t1-t0):.0f} tok/s")
    
    # Phase 2: Architecture
    t0 = time.time()
    arch_loader = ArchitectureLoader(conn)
    arch_atoms = arch_loader.ingest_bert_architecture(str(model_dir / 'config.json'))
    t1 = time.time()
    stats['phases']['architecture'] = {
        'duration': t1 - t0,
        'atoms': arch_atoms
    }
    print(f"[ARCH] {arch_atoms} atoms | {t1-t0:.2f}s")
    
    # Phase 3: Drop indexes and pre-cache existing atoms
    print("\n[INDEX] Dropping indexes for bulk weight load...")
    weight_ingester = WeightIngester(conn)
    weight_ingester.drop_indexes_for_bulk_load()
    weight_ingester.load_existing_atom_ids()
    
    # Phase 4: Weights
    t0 = time.time()
    weight_stats = weight_ingester.ingest_bert_weights(str(model_dir / 'model.safetensors'))
    t1 = time.time()
    stats['phases']['weights'] = {
        'duration': t1 - t0,
        'atoms': weight_stats['weight_atoms_created'],
        'rate': weight_stats['weight_atoms_created'] / (t1 - t0)
    }
    print(f"[WEIGHTS] {weight_stats['weight_atoms_created']:,} atoms | {t1-t0:.2f}s | {weight_stats['weight_atoms_created']/(t1-t0):.0f} atoms/s")
    
    # Phase 5: Recreate indexes CONCURRENTLY
    print("\n[INDEX] Recreating indexes with CONCURRENTLY...")
    weight_ingester.recreate_indexes()
    
    stats['end_time'] = time.time()
    stats['total_duration'] = stats['end_time'] - stats['start_time']
    
    # Database stats
    cursor = conn.cursor()
    cursor.execute("SELECT COUNT(*) FROM atom")
    total_atoms = cursor.fetchone()[0]
    cursor.execute("SELECT COUNT(*) FROM atom_compositions")
    total_comps = cursor.fetchone()[0]
    cursor.execute("SELECT pg_size_pretty(pg_database_size('hartonomous'))")
    db_size = cursor.fetchone()[0]
    cursor.close()
    
    stats['database'] = {
        'total_atoms': total_atoms,
        'total_compositions': total_comps,
        'database_size': db_size,
        'atoms_per_second': total_atoms / stats['total_duration']
    }
    
    conn.close()
    
    print("\n" + "="*60)
    print("BENCHMARK SUMMARY")
    print("="*60)
    print(f"Total duration: {stats['total_duration']:.2f}s")
    print(f"Total atoms: {total_atoms:,}")
    print(f"Total compositions: {total_comps:,}")
    print(f"Database size: {db_size}")
    print(f"Overall throughput: {total_atoms/stats['total_duration']:.0f} atoms/s")
    print("="*60)
    
    with open('benchmark_results.json', 'w') as f:
        json.dump(stats, f, indent=2)
    print("Results saved to benchmark_results.json")


if __name__ == "__main__":
    benchmark_ingestion()
