"""
Architecture-aware weight ingestion: Weights as token→token transformations

CRITICAL ARCHITECTURE:
- Weights are NOT standalone floats
- Weights are transformation matrices: token_embedding → hidden_state
- Need BOTH vocabulary AND architecture context
- Use Shader Rust CopyLoader for parallel batch processing

Weight Relationships:
- Attention Q/K/V: token_i → query/key/value space
- FFN up/down: hidden[i] → intermediate[j]
- Output: hidden → logits[vocab_token]

Proper Flow:
1. Load vocabulary (30K tokens)
2. Load architecture (12 layers × 3 components)
3. Decompose weights with context (layer, component, projection)
4. Batch via Rust Shader COPY protocol
"""

import numpy as np
from pathlib import Path
from typing import Dict, List, Tuple
import json
import psycopg2
from blake3 import blake3
from safetensors.torch import safe_open


class WeightIngester:
    def __init__(self, conn):
        self.conn = conn
        self.weight_batch = []  # Accumulate all weights across matrices
    
    def _encode_hilbert_4d(self, x, y, z, m, resolution=10):
        """Encode 4D point to Hilbert index"""
        # Normalize to [0, 1]
        x_norm = (x + 50.0) / 100.0
        y_norm = (y + 50.0) / 100.0
        z_norm = z / 3.0
        m_norm = m
        
        # Convert to integer coordinates
        max_coord = (1 << resolution) - 1
        ix = max(0, min(max_coord, int(x_norm * max_coord)))
        iy = max(0, min(max_coord, int(y_norm * max_coord)))
        iz = max(0, min(max_coord, int(z_norm * max_coord)))
        im = max(0, min(max_coord, int(m_norm * max_coord)))
        
        # 4D Hilbert encoding via bit interleaving
        hilbert = 0
        for i in range(resolution):
            bit_pos = resolution - 1 - i
            x_bit = (ix >> bit_pos) & 1
            y_bit = (iy >> bit_pos) & 1
            z_bit = (iz >> bit_pos) & 1
            m_bit = (im >> bit_pos) & 1
            hilbert = (hilbert << 4) | (x_bit << 3) | (y_bit << 2) | (z_bit << 1) | m_bit
        
        return hilbert
        
    def ingest_bert_weights(self, model_path: str, model_name: str = "BERT") -> Dict[str, int]:
        """
        Ingest BERT weights with architecture context
        
        Args:
            model_path: Path to model.safetensors or pytorch_model.bin
            model_name: Model identifier (used for architecture lookup)
        
        Returns: Statistics (layers_processed, weights_created, etc.)
        """
        stats = {
            'layers_processed': 0,
            'weight_matrices': 0,
            'weight_atoms_created': 0,
            'skipped_small': 0
        }
        
        # Open safetensors file
        with safe_open(model_path, framework="pt", device="cpu") as f:
            tensor_keys = f.keys()
            
            print(f"Model layers: {len([k for k in tensor_keys if 'weight' in k])}")
            
            # Process each weight matrix
            for key in sorted(tensor_keys):
                if 'weight' not in key:
                    continue
                
                # Parse layer structure
                # Example key: "encoder.layer.0.attention.self.query.weight"
                parts = key.split('.')
                if 'layer' not in parts:
                    continue
                
                layer_idx = int(parts[parts.index('layer') + 1])
                
                # Determine component and projection
                component = self._parse_component(parts)
                projection = self._parse_projection(parts)
                
                if not component or not projection:
                    continue
                
                # Load weight tensor
                tensor = f.get_tensor(key)
                shape = tensor.shape
                
                print(f"Layer {layer_idx:2d} | {component:12s} | {projection:8s} | Shape: {shape}")
                
                # Skip biases and small tensors
                if len(shape) != 2 or min(shape) < 10:
                    stats['skipped_small'] += 1
                    continue
                
                # Decompose weight matrix
                self._ingest_weight_matrix(
                    tensor.numpy(),
                    model_name=model_name,
                    layer_idx=layer_idx,
                    component=component,
                    projection=projection
                )
                
                stats['weight_matrices'] += 1
                stats['layers_processed'] = max(stats['layers_processed'], layer_idx + 1)
        
        # Bulk commit all weights
        stats['weight_atoms_created'] = self.flush_weights()
        print(f"\nBulk inserted {stats['weight_atoms_created']:,} weight atoms")
        
        return stats
    
    def _parse_component(self, parts: List[str]) -> str:
        """Extract component type from layer key"""
        if 'attention' in parts:
            return 'attention'
        elif 'intermediate' in parts or 'output' in parts:
            return 'ffn'
        elif 'LayerNorm' in parts:
            return 'layer_norm'
        return None
    
    def _parse_projection(self, parts: List[str]) -> str:
        """Extract projection type from layer key"""
        if 'query' in parts:
            return 'query'
        elif 'key' in parts:
            return 'key'
        elif 'value' in parts:
            return 'value'
        elif 'dense' in parts and 'attention' in parts:
            return 'output'
        elif 'intermediate' in parts:
            return 'up'
        elif 'output' in parts and 'intermediate' not in parts:
            return 'down'
        return None
    
    def _ingest_weight_matrix(self, weights: np.ndarray, model_name: str,
                             layer_idx: int, component: str, projection: str):
        """
        Ingest weight matrix with token relationship context
        
        Weight matrix W[i,j] represents transformation strength:
        - Attention Q: token_embedding[i] → query_space[j]
        - FFN up: hidden[i] → intermediate[j]
        - Output: hidden[i] → vocab_token[j]
        
        We store SALIENT weights (top-k by magnitude) as relationship atoms
        """
        import struct
        import time
        
        t_start = time.time()
        rows, cols = weights.shape
        
        # Ingest ALL weights
        flat = weights.flatten()
        
        # Convert to row, col indices
        row_indices = np.arange(rows).repeat(cols)
        col_indices = np.tile(np.arange(cols), rows)
        values = flat
        
        t_prep = time.time()
        print(f"    Ingesting {len(values):,} weights | prep: {(t_prep-t_start)*1000:.0f}ms", end='', flush=True)
        
        # Accumulate weights in batch (commit once at end)
        t_encode_start = time.time()
        batch_count = 0
        for row, col, value in zip(row_indices, col_indices, values):
            batch_count += 1
            if batch_count % 50000 == 0:
                elapsed = time.time() - t_encode_start
                rate = batch_count / elapsed
                print(f" | {batch_count:,}/{len(values):,} ({100*batch_count/len(values):.1f}%) @ {rate:.0f}/s", end='', flush=True)
            # Weight atom metadata
            weight_meta = {
                'model': model_name,
                'layer': layer_idx,
                'component': component,
                'projection': projection,
                'from_dim': int(row),
                'to_dim': int(col),
                'value': float(value),
                'shape': f"{rows}x{cols}"
            }
            
            # Deterministic ID
            weight_id = blake3(
                f"weight:{model_name}:{layer_idx}:{component}:{projection}:{row}:{col}".encode('utf-8')
            ).digest()
            
            # Semantic position: encode dimensional relationship
            x = (row / rows) * 100 - 50  # [-50, 50]
            y = (col / cols) * 100 - 50
            z = 0.5 + (layer_idx / 12) * 0.5  # [0.5, 1.0]
            m = abs(float(value))  # Salience = magnitude
            
            # Pack weight value as bytea
            atomic_value = psycopg2.Binary(struct.pack('f', float(value)))
            geom_wkt = f"SRID=0;POINT ZM({x} {y} {z} {m})"
            
            # Compute Hilbert index from geometry
            hilbert_idx = self._encode_hilbert_4d(x, y, z, m)
            
            self.weight_batch.append((
                weight_id,
                0,  # atom_class: Constant
                2,  # modality: Neural
                'weight',
                atomic_value,
                geom_wkt,
                hilbert_idx,  # computed at ingestion
                json.dumps(weight_meta)
            ))
        
        t_total = time.time() - t_start
        print(f" | DONE {t_total:.2f}s ({len(values)/t_total:.0f} atoms/s)")
    
    def flush_weights(self):
        """Bulk insert all accumulated weights"""
        import time
        from psycopg2.extras import execute_values
        
        if not self.weight_batch:
            return 0
        
        t_start = time.time()
        
        print(f"\nBulk inserting {len(self.weight_batch):,} weight atoms...", end='', flush=True)
        
        with self.conn.cursor() as cursor:
            execute_values(cursor, """
                INSERT INTO atom (
                    atom_id, atom_class, modality, subtype,
                    atomic_value, geom, hilbert_index, metadata
                ) VALUES %s
                ON CONFLICT (atom_id) DO NOTHING
            """, self.weight_batch)
            
            t_insert = time.time()
            print(f" insert: {(t_insert-t_start):.2f}s", end='', flush=True)
            
            self.conn.commit()
            count = len(self.weight_batch)
            self.weight_batch = []
            
            t_commit = time.time()
            print(f" | commit: {(t_commit-t_insert):.2f}s | TOTAL: {(t_commit-t_start):.2f}s ({count/(t_commit-t_start):.0f} atoms/s)")
            
            return count
