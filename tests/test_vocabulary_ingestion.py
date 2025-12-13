"""Test vocabulary ingestion with BERT tokenizer"""

import sys
sys.path.append('connector')

import psycopg2
from vocabulary_ingester import VocabularyIngester
from pathlib import Path
import time

conn = psycopg2.connect(host="127.0.0.1", database="hartonomous", user="postgres")
conn.autocommit = True
cursor = conn.cursor()

print("=== Vocabulary Ingestion Test ===\n")

# Clear database
cursor.execute("TRUNCATE atom, atom_compositions CASCADE")

tokenizer_path = Path("D:/models/embedding_models/all-MiniLM-L6-v2/snapshots/c9745ed1d9f207416be6d2e6f8de32d1f16199bf/tokenizer.json")

if not tokenizer_path.exists():
    print(f"Tokenizer not found: {tokenizer_path}")
    sys.exit(1)

print(f"Loading: {tokenizer_path.name}")

ingester = VocabularyIngester(conn)

start = time.time()
tokens_ingested = ingester.ingest_huggingface_tokenizer(str(tokenizer_path))
elapsed = time.time() - start

print(f"\n✓ Completed in {elapsed:.2f}s")
print(f"  Tokens ingested: {tokens_ingested:,}")

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
    class_name = "Characters (Z=0)" if atom_class == 0 else "Tokens (Z=1)"
    print(f"  {class_name}: {count:,} atoms")

# Sample tokens
cursor.execute("""
    SELECT metadata->>'token' as token
    FROM atom
    WHERE atom_class = 1
    LIMIT 10
""")

print("\nSample tokens:")
for row in cursor.fetchall():
    print(f"  {row[0]}")

# Test composition query
cursor.execute("""
    SELECT 
        a.metadata->>'token' as token,
        COUNT(ac.component_atom_id) as char_count
    FROM atom a
    JOIN atom_compositions ac ON ac.parent_atom_id = a.atom_id
    WHERE a.atom_class = 1
    GROUP BY a.atom_id, a.metadata
    ORDER BY char_count DESC
    LIMIT 5
""")

print("\nLongest tokens:")
for token, char_count in cursor.fetchall():
    print(f"  '{token}': {char_count} characters")

conn.close()
print("\n✓ Vocabulary ingestion validated")
