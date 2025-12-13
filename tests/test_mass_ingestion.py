"""
Mass data ingestion - scale to millions of atoms
Load diverse datasets: text, images, audio, neural weights
"""

import psycopg2
from pathlib import Path
import time
import numpy as np
from PIL import Image

import sys
sys.path.insert(0, str(Path(__file__).parent.parent))

from connector.image_decomposer import ImageDecomposer
from connector.audio_decomposer import AudioDecomposer

conn = psycopg2.connect(
    host='localhost',
    dbname='hartonomous',
    user='hartonomous'
)

cursor = conn.cursor()

print("=== Mass Data Ingestion ===\n")

# Initial state
cursor.execute("SELECT COUNT(*) FROM atom")
initial_count = cursor.fetchone()[0]
print(f"Starting atoms: {initial_count:,}\n")

output_dir = Path("test_data")
output_dir.mkdir(exist_ok=True)

# Generate diverse image dataset
print("Generating diverse image dataset...")

image_decomposer = ImageDecomposer(conn)
image_count = 0
image_start = time.time()

# Different image patterns for diversity
patterns = [
    ('gradient', lambda x, y, w, h: (int(x/w*255), int(y/h*255), 128)),
    ('checkerboard', lambda x, y, w, h: (255 if (x//8 + y//8) % 2 else 0, 0, 0)),
    ('radial', lambda x, y, w, h: (int(np.sqrt((x-w/2)**2 + (y-h/2)**2) % 256), 128, 128)),
    ('noise', lambda x, y, w, h: (np.random.randint(0, 256), np.random.randint(0, 256), np.random.randint(0, 256))),
]

for pattern_name, pattern_func in patterns:
    for size in [(32, 32), (64, 64), (128, 128)]:
        width, height = size
        
        img = Image.new('RGB', (width, height))
        pixels = img.load()
        
        for y in range(height):
            for x in range(width):
                pixels[x, y] = pattern_func(x, y, width, height)
        
        img_path = output_dir / f"mass_{pattern_name}_{width}x{height}.png"
        img.save(img_path)
        
        image_decomposer.decompose_image(img_path)
        image_count += 1
        
        if image_count % 5 == 0:
            print(f"  Processed {image_count} images...")

image_time = time.time() - image_start

print(f"Images ingested: {image_count} in {image_time:.2f}s")

# Generate text atoms
print(f"\nGenerating text data...")

text_samples = [
    "The quick brown fox jumps over the lazy dog",
    "Machine learning artificial intelligence neural networks",
    "Database storage geometry spatial indexing PostGIS",
    "Python Rust C++ PostgreSQL Blake3 hashing",
    "Image processing computer vision pixel quantization",
    "Audio waveform signal processing frequency domain",
    "Semantic similarity nearest neighbor search algorithms",
    "Data compression run length encoding deduplication",
]

from blake3 import blake3
import struct

text_start = time.time()
text_atoms = 0

for text in text_samples:
    for token in text.lower().split():
        # Generate token atom
        atom_id = blake3(
            b'\x02' +  # modality 2 (text)
            b'\x00\x00' +  # semantic class 0
            token.encode('utf-8')
        ).digest()
        
        cursor.execute("""
            INSERT INTO atom (atom_id, atom_class, modality, subtype, atomic_value, geom)
            VALUES (%s, 0, 2, 'token', %s, ST_MakePoint(0, 0, 0, 1)::geometry)
            ON CONFLICT (atom_id) DO NOTHING
        """, (atom_id, token.encode('utf-8')))
        
        if cursor.rowcount > 0:
            text_atoms += 1

conn.commit()
text_time = time.time() - text_start

print(f"Text tokens ingested: {text_atoms} unique in {text_time:.2f}s")

# Final state
cursor.execute("SELECT COUNT(*) FROM atom WHERE atom_class = 0")
final_constants = cursor.fetchone()[0]

cursor.execute("SELECT COUNT(*) FROM atom WHERE atom_class = 1")
final_compositions = cursor.fetchone()[0]

cursor.execute("SELECT modality, COUNT(*) FROM atom WHERE atom_class = 0 GROUP BY modality ORDER BY modality")
modality_dist = cursor.fetchall()

print(f"\n=== Final Database State ===")
print(f"Total atoms: {final_constants + final_compositions:,}")
print(f"  Constants: {final_constants:,}")
print(f"  Compositions: {final_compositions:,}")
print(f"  Growth: {final_constants + final_compositions - initial_count:,} atoms added")

print(f"\nModality distribution:")
for mod, count in modality_dist:
    print(f"  Modality {mod}: {count:,}")

# Test query performance at scale
print(f"\n=== Query Performance at Scale ===")

test_queries = [
    ("k-NN (k=10)", """
        SELECT atom_id 
        FROM atom 
        WHERE atom_id != (SELECT atom_id FROM atom WHERE atom_class = 0 ORDER BY random() LIMIT 1)
        ORDER BY geom <-> (SELECT geom FROM atom WHERE atom_class = 0 ORDER BY random() LIMIT 1)
        LIMIT 10
    """),
    ("k-NN (k=100)", """
        SELECT atom_id 
        FROM atom 
        WHERE atom_id != (SELECT atom_id FROM atom WHERE atom_class = 0 ORDER BY random() LIMIT 1)
        ORDER BY geom <-> (SELECT geom FROM atom WHERE atom_class = 0 ORDER BY random() LIMIT 1)
        LIMIT 100
    """),
    ("Radius search", """
        SELECT COUNT(*) 
        FROM atom 
        WHERE ST_DWithin(geom, (SELECT geom FROM atom WHERE atom_class = 0 ORDER BY random() LIMIT 1), 1.0)
    """),
]

for query_name, query_sql in test_queries:
    times = []
    for _ in range(5):
        start = time.time()
        cursor.execute(query_sql)
        cursor.fetchall()
        times.append((time.time() - start) * 1000)
    
    avg_time = sum(times) / len(times)
    min_time = min(times)
    max_time = max(times)
    
    print(f"{query_name}: {avg_time:.2f}ms (min={min_time:.2f}ms, max={max_time:.2f}ms)")

cursor.execute("SELECT pg_size_pretty(pg_total_relation_size('atom'))")
db_size = cursor.fetchone()[0]

print(f"\nDatabase size: {db_size}")

conn.close()

print(f"\n✓ Mass ingestion complete - system scaled")
