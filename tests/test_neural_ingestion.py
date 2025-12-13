"""
Ingest a real PyTorch model - decompose neural network weights into atoms
"""

import torch
import torch.nn as nn
import psycopg2
from pathlib import Path
import time

import sys
sys.path.insert(0, str(Path(__file__).parent.parent))

from connector.neural_ingester import NeuralWeightIngester

# Create a simple CNN model
class SimpleCNN(nn.Module):
    def __init__(self):
        super(SimpleCNN, self).__init__()
        self.conv1 = nn.Conv2d(3, 16, kernel_size=3)
        self.conv2 = nn.Conv2d(16, 32, kernel_size=3)
        self.fc1 = nn.Linear(32 * 6 * 6, 128)
        self.fc2 = nn.Linear(128, 10)
        
    def forward(self, x):
        x = torch.relu(self.conv1(x))
        x = torch.max_pool2d(x, 2)
        x = torch.relu(self.conv2(x))
        x = torch.max_pool2d(x, 2)
        x = x.view(-1, 32 * 6 * 6)
        x = torch.relu(self.fc1(x))
        x = self.fc2(x)
        return x

print("=== Neural Network Weight Ingestion ===\n")

# Create and save model
model = SimpleCNN()

# Initialize with random weights
for param in model.parameters():
    if len(param.shape) > 1:
        nn.init.xavier_uniform_(param)

model_path = Path("test_data/simple_cnn.pth")
model_path.parent.mkdir(exist_ok=True)
torch.save(model.state_dict(), model_path)

print(f"Created SimpleCNN model")
print(f"  Parameters: {sum(p.numel() for p in model.parameters()):,}")

# Count parameters by layer
for name, param in model.state_dict().items():
    print(f"  {name}: {param.numel():,} weights, shape {list(param.shape)}")

# Ingest into Hartonomous
conn = psycopg2.connect(
    host='127.0.0.1',
    dbname='hartonomous',
    user='hartonomous'
)

ingester = NeuralWeightIngester(conn)

print(f"\nIngesting model weights...")
start = time.time()

layer_compositions = ingester.ingest_pytorch_model(str(model_path))

elapsed = time.time() - start

print(f"Ingestion complete: {elapsed:.2f}s")
print(f"  Layers ingested: {len(layer_compositions)}")

# Check database state
cursor = conn.cursor()

cursor.execute("SELECT COUNT(*) FROM atom WHERE modality = 4 AND atom_class = 0")
weight_atoms = cursor.fetchone()[0]

cursor.execute("SELECT COUNT(*) FROM atom WHERE modality = 4 AND atom_class = 1")
layer_comps = cursor.fetchone()[0]

print(f"\nDatabase state:")
print(f"  Weight atoms (modality 4): {weight_atoms:,}")
print(f"  Layer compositions: {layer_comps}")

# Test similarity search on weights
print(f"\nTesting weight similarity search...")

# Get a random weight atom
cursor.execute("""
    SELECT atom_id FROM atom 
    WHERE modality = 4 AND atom_class = 0 
    ORDER BY random() 
    LIMIT 1
""")

target_weight = cursor.fetchone()[0]

start = time.time()

cursor.execute("""
    SELECT 
        atom_id,
        ST_Distance(geom, (SELECT geom FROM atom WHERE atom_id = %s)) as dist
    FROM atom
    WHERE modality = 4 
      AND atom_class = 0
      AND atom_id != %s
    ORDER BY geom <-> (SELECT geom FROM atom WHERE atom_id = %s)
    LIMIT 10
""", (target_weight, target_weight, target_weight))

similar_weights = cursor.fetchall()
query_time = (time.time() - start) * 1000

print(f"  Found {len(similar_weights)} similar weights in {query_time:.2f}ms")
print(f"  Distance range: {similar_weights[0][1]:.4f} - {similar_weights[-1][1]:.4f}")

# Test reconstruction
print(f"\nTesting layer reconstruction...")

# Get first layer composition ID
cursor.execute("""
    SELECT atom_id FROM atom 
    WHERE atom_class = 1 AND modality = 4 AND subtype = 'tensor'
    LIMIT 1
""")

comp_id = cursor.fetchone()
if comp_id:
    comp_id = comp_id[0]
    
    start = time.time()
    
    cursor.execute("""
        SELECT COUNT(*) FROM atom_compositions
        WHERE parent_atom_id = %s
    """, (comp_id,))
    
    component_count = cursor.fetchone()[0]
    recon_time = (time.time() - start) * 1000
    
    print(f"  Layer has {component_count:,} weight atoms")
    print(f"  Query time: {recon_time:.2f}ms")
else:
    print(f"  No layer compositions found")

conn.close()

print(f"\n✓ Neural network weights successfully ingested")
print(f"  Model intelligence now queryable via spatial geometry")
