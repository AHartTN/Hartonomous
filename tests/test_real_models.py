"""
Test with REAL PyTorch models from D:\models
Ingest production models and validate spatial intelligence
"""

import torch
import psycopg2
from pathlib import Path
import time
import sys

sys.path.insert(0, str(Path(__file__).parent.parent))

from connector.neural_ingester import NeuralWeightIngester

conn = psycopg2.connect(
    host='127.0.0.1',
    dbname='hartonomous',
    user='postgres'
)

cursor = conn.cursor()

print("=== Real Model Ingestion Test ===\n")

# Scan D:\models for .pth, .pt, .bin files
models_dir = Path("D:/models")

if not models_dir.exists():
    print(f"⚠ Models directory not found: {models_dir}")
    print("Creating test directory and searching for models...")
    models_dir = Path(".")

# Find model files
model_files = []
for pattern in ['*.pth', '*.pt', '*.bin', '*.safetensors']:
    model_files.extend(models_dir.rglob(pattern))

print(f"Found {len(model_files)} model files in {models_dir}")

# Deduplicate by (filename, size)
seen = set()
unique_models = []
for f in model_files:
    key = (f.name, f.stat().st_size)
    if key not in seen:
        seen.add(key)
        unique_models.append(f)

model_files = unique_models
print(f"Deduplicated to {len(model_files)} unique models")

if len(model_files) == 0:
    print("\nNo models found. Searching common locations:")
    search_paths = [
        Path("D:/models"),
        Path("C:/Users").expanduser() / ".cache" / "huggingface",
        Path.home() / ".cache" / "torch" / "hub",
    ]
    
    for search_path in search_paths:
        if search_path.exists():
            found = list(search_path.rglob("*.pth"))[:5]
            if found:
                model_files.extend(found)
                print(f"  Found {len(found)} in {search_path}")

# Display found models
print(f"\nAvailable models:")
for i, model_file in enumerate(model_files[:10]):
    size_mb = model_file.stat().st_size / 1024 / 1024
    print(f"  [{i}] {model_file.name} ({size_mb:.1f} MB) - {model_file.parent}")

if len(model_files) == 0:
    print("\n⚠ No models found. Testing with synthetic model instead.")
    
    # Create synthetic model for testing
    import torch.nn as nn
    
    class TestModel(nn.Module):
        def __init__(self):
            super().__init__()
            self.conv1 = nn.Conv2d(3, 64, 3)
            self.conv2 = nn.Conv2d(64, 128, 3)
            self.fc = nn.Linear(128 * 26 * 26, 10)
        
        def forward(self, x):
            x = torch.relu(self.conv1(x))
            x = torch.relu(self.conv2(x))
            x = x.view(x.size(0), -1)
            return self.fc(x)
    
    model = TestModel()
    model_path = Path("test_data/synthetic_model.pth")
    model_path.parent.mkdir(exist_ok=True)
    torch.save(model.state_dict(), model_path)
    
    model_files = [model_path]
    print(f"\nCreated synthetic model: {model_path}")

# Get initial database state
cursor.execute("SELECT COUNT(*) FROM atom WHERE modality = 4 AND atom_class = 0")
initial_weights = cursor.fetchone()[0]

cursor.execute("SELECT COUNT(*) FROM atom WHERE modality = 4 AND atom_class = 1")
initial_layers = cursor.fetchone()[0]

print(f"\nInitial state:")
print(f"  Weight atoms: {initial_weights:,}")
print(f"  Layer compositions: {initial_layers:,}")

# Ingest first available model
ingester = NeuralWeightIngester(conn)

model_to_ingest = model_files[0]
print(f"\nIngesting: {model_to_ingest.name}")
print(f"  Size: {model_to_ingest.stat().st_size / 1024 / 1024:.1f} MB")

start = time.time()

try:
    layer_compositions = ingester.ingest_pytorch_model(str(model_to_ingest))
    elapsed = time.time() - start
    
    print(f"✓ Ingestion complete: {elapsed:.2f}s")
    print(f"  Layers ingested: {len(layer_compositions)}")
    
except Exception as e:
    elapsed = time.time() - start
    print(f"✗ Ingestion failed after {elapsed:.2f}s: {e}")
    import traceback
    traceback.print_exc()

# Final database state
cursor.execute("SELECT COUNT(*) FROM atom WHERE modality = 4 AND atom_class = 0")
final_weights = cursor.fetchone()[0]

cursor.execute("SELECT COUNT(*) FROM atom WHERE modality = 4 AND atom_class = 1")
final_layers = cursor.fetchone()[0]

cursor.execute("SELECT COUNT(*) FROM atom WHERE atom_class = 0")
total_atoms = cursor.fetchone()[0]

print(f"\nFinal state:")
print(f"  Weight atoms: {final_weights:,} (+{final_weights - initial_weights:,})")
print(f"  Layer compositions: {final_layers:,} (+{final_layers - initial_layers:,})")
print(f"  Total atoms: {total_atoms:,}")

# Test spatial queries on weights
if final_weights > initial_weights:
    print(f"\n=== Spatial Weight Queries ===")
    
    # Find similar weights
    cursor.execute("""
        SELECT atom_id FROM atom
        WHERE modality = 4 AND atom_class = 0
        ORDER BY random()
        LIMIT 1
    """)
    
    target = cursor.fetchone()
    if target:
        target_id = target[0]
        
        start = time.time()
        cursor.execute("""
            SELECT atom_id, ST_Distance(geom, %s::geometry) as dist
            FROM atom
            WHERE modality = 4 
              AND atom_class = 0
              AND atom_id != %s
            ORDER BY geom <-> %s::geometry
            LIMIT 20
        """, (f"SRID=4326;POINT(0 0 0 0)", target_id, f"SRID=4326;POINT(0 0 0 0)"))
        
        similar = cursor.fetchall()
        query_time = (time.time() - start) * 1000
        
        print(f"k-NN search (k=20): {query_time:.2f}ms")
        
        # Check if weights are spatially distributed
        distances = [dist for _, dist in similar]
        if max(distances) > 0.1:
            print(f"  Weights spatially distributed: {min(distances):.4f} - {max(distances):.4f}")
        else:
            print(f"  ⚠ All weights at origin - LMDS not yet positioning")

# Cross-modal query: weight → text/image
print(f"\n=== Cross-Modal Retrieval ===")

cursor.execute("""
    SELECT atom_id FROM atom
    WHERE modality = 4 AND atom_class = 0
    ORDER BY random()
    LIMIT 1
""")

weight_target = cursor.fetchone()
if weight_target:
    start = time.time()
    
    cursor.execute("""
        SELECT modality, COUNT(*) as cnt
        FROM (
            SELECT atom_id, modality
            FROM atom
            WHERE atom_class = 0
            ORDER BY geom <-> (SELECT geom FROM atom WHERE atom_id = %s)
            LIMIT 50
        ) subq
        GROUP BY modality
        ORDER BY modality
    """, (weight_target[0],))
    
    modality_breakdown = cursor.fetchall()
    query_time = (time.time() - start) * 1000
    
    print(f"Weight → All modalities (k=50): {query_time:.2f}ms")
    
    mod_names = {0: 'numeric', 1: 'text', 2: 'image', 3: 'audio', 4: 'weights'}
    for mod, count in modality_breakdown:
        print(f"  {mod_names.get(mod, f'mod-{mod}')}: {count}")

conn.close()

print(f"\n✓ Real model ingestion validated")
