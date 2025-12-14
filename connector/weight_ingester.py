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
    def __init__(self, conn, salience_threshold=0.0001):
        self.conn = conn
        self.salience_threshold = salience_threshold  # Filter weights below this
        self.weight_batch = []  # Constant atoms (unique values)
        self.composition_batch = []  # Composition atoms (relationships)
        self.batch_size = 100000  # Flush every 100K atoms
        self.total_flushed = 0  # Accumulate all weights across matrices
        self.filtered_count = 0  # Track filtered low-salience weights
        self.existing_atom_ids = set()  # Cache for pre-filtering
    
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
        Ingest BERT weights with architecture context (parallel processing)
        
        Args:
            model_path: Path to model.safetensors or pytorch_model.bin
            model_name: Model identifier (used for architecture lookup)
        
        Returns: Statistics (layers_processed, weights_created, etc.)
        """
        import multiprocessing as mp
        from functools import partial
        
        stats = {
            'layers_processed': 0,
            'weight_matrices': 0,
            'weight_atoms_created': 0,
            'skipped_small': 0
        }
        
        # Open safetensors file and collect weight matrix tasks
        weight_tasks = []
        with safe_open(model_path, framework="pt", device="cpu") as f:
            tensor_keys = f.keys()
            
            print(f"Model layers: {len([k for k in tensor_keys if 'weight' in k])}")
            
            # Collect tasks for parallel processing
            for key in sorted(tensor_keys):
                if 'weight' not in key:
                    continue
                
                # Parse layer structure
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
                
                # Skip biases and small tensors
                if len(shape) != 2 or min(shape) < 10:
                    stats['skipped_small'] += 1
                    continue
                
                print(f"Layer {layer_idx:2d} | {component:12s} | {projection:8s} | Shape: {shape}")
                
                weight_tasks.append({
                    'weights': tensor.numpy(),
                    'model_name': model_name,
                    'layer_idx': layer_idx,
                    'component': component,
                    'projection': projection
                })
                
                stats['weight_matrices'] += 1
                stats['layers_processed'] = max(stats['layers_processed'], layer_idx + 1)
        
        # Process matrices sequentially (multiprocessing requires serialization overhead)
        # For now, process synchronously but batched
        for task in weight_tasks:
            self._ingest_weight_matrix(
                task['weights'],
                task['model_name'],
                task['layer_idx'],
                task['component'],
                task['projection']
            )
        
        # Final flush for any remaining weights
        if self.weight_batch or self.composition_batch:
            self.flush_weights()
        
        stats['weight_atoms_created'] = self.total_flushed
        stats['filtered_low_salience'] = self.filtered_count
        print(f"\nTotal ingested: {self.total_flushed:,} salient weights (filtered {self.filtered_count:,} below threshold)")
        
        return stats
    
    def drop_indexes_for_bulk_load(self):
        \"\"\"Drop all non-PK indexes before bulk load (PostgreSQL Section 14.4.3)\"\"\"
        indexes_to_drop = [
            'idx_atoms_hilbert',
            'idx_atoms_class',
            'idx_atoms_modality',
            'idx_atoms_geom_gist',
            'idx_atoms_metadata',
            'idx_atoms_created'
        ]
        
        with self.conn.cursor() as cursor:
            for idx_name in indexes_to_drop:
                try:
                    cursor.execute(f\"DROP INDEX IF EXISTS {idx_name};\")
                    print(f\"  Dropped index: {idx_name}\")
                except Exception as e:
                    print(f\"  Warning: Could not drop {idx_name}: {e}\")
        
        self.conn.commit()
        print(\"[INDEX] Dropped 6 indexes for bulk load\")
    
    def recreate_indexes(self):
        \"\"\"Recreate indexes after bulk load using CONCURRENTLY (PostgreSQL Section 14.4.3)\"\"\"
        index_definitions = [
            \"CREATE INDEX CONCURRENTLY idx_atoms_hilbert ON atom(hilbert_index);\",
            \"CREATE INDEX CONCURRENTLY idx_atoms_class ON atom(atom_class);\",
            \"CREATE INDEX CONCURRENTLY idx_atoms_modality ON atom(modality);\",
            \"CREATE INDEX CONCURRENTLY idx_atoms_geom_gist ON atom USING GIST(geom);\",
            \"CREATE INDEX CONCURRENTLY idx_atoms_metadata ON atom USING GIN(metadata);\",
            \"CREATE INDEX CONCURRENTLY idx_atoms_created ON atom(created_at);\"
        ]
        
        # CONCURRENTLY requires autocommit mode
        old_isolation = self.conn.isolation_level
        self.conn.set_isolation_level(0)
        
        with self.conn.cursor() as cursor:
            for idx_sql in index_definitions:
                try:
                    cursor.execute(idx_sql)
                    idx_name = idx_sql.split()[3]
                    print(f\"  Created: {idx_name}\")
                except Exception as e:
                    print(f\"  Warning: {e}\")
        
        self.conn.set_isolation_level(old_isolation)
        print(\"[INDEX] Recreated 6 indexes with CONCURRENTLY\")
    
    def load_existing_atom_ids(self, atom_class=None):
        \"\"\"Pre-load existing atom_ids to avoid ON CONFLICT overhead\"\"\"
        with self.conn.cursor() as cursor:
            if atom_class is not None:
                cursor.execute(
                    \"SELECT atom_id FROM atom WHERE atom_class = %s;\",
                    (atom_class,)
                )
            else:
                cursor.execute(\"SELECT atom_id FROM atom;\")
            
            self.existing_atom_ids = set(row[0].tobytes() for row in cursor.fetchall())
        print(f\"[CACHE] Loaded {len(self.existing_atom_ids)} existing atom_ids\")

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
        
        # SEMANTIC FILTERING: Only ingest salient weights
        flat = weights.flatten()
        row_indices = np.arange(rows).repeat(cols)
        col_indices = np.tile(np.arange(cols), rows)
        
        # Filter by salience threshold
        salience = np.abs(flat)
        salient_mask = salience >= self.salience_threshold
        
        salient_values = flat[salient_mask]
        salient_rows = row_indices[salient_mask]
        salient_cols = col_indices[salient_mask]
        salient_magnitudes = salience[salient_mask]
        
        filtered = len(flat) - len(salient_values)
        self.filtered_count += filtered
        
        # APP-LAYER DEDUPLICATION: Extract unique values FIRST
        unique_values = np.unique(salient_values)
        
        t_prep = time.time()
        print(f"    Ingesting {len(salient_values):,}/{len(flat):,} salient ({len(unique_values):,} unique, filtered {filtered:,}) | prep: {(t_prep-t_start)*1000:.0f}ms", end='', flush=True)
        
        # PHASE 1: Create constant atoms for UNIQUE values only
        value_id_map = {}  # value -> atom_id lookup
        
        for value in unique_values:
            value_bytes = struct.pack('f', float(value))
            value_id = blake3(value_bytes).digest()
            value_id_map[float(value)] = value_id
            
            magnitude = abs(float(value))
            angle = np.arctan2(float(value), magnitude) if magnitude > 0 else 0
            x_val = magnitude * np.cos(angle) * 50
            y_val = magnitude * np.sin(angle) * 50
            z_val = 0.0
            m_val = magnitude
            
            atomic_value = psycopg2.Binary(value_bytes)
            geom_wkt_val = f"SRID=0;POINT ZM({x_val} {y_val} {z_val} {m_val})"
            hilbert_val = self._encode_hilbert_4d(x_val, y_val, z_val, m_val)
            value_meta_json = json.dumps({'type': 'weight_constant', 'value': float(value)})
            
            self.weight_batch.append((
                value_id,
                0,  # atom_class: Constant
                2,  # modality: Neural
                'weight',
                atomic_value,
                geom_wkt_val,
                hilbert_val,
                value_meta_json
            ))
        
        # Flush constant atoms before creating compositions
        if self.weight_batch:
            self.flush_weights()
        
        # PHASE 2: Create composition atoms for all positions
        t_encode_start = time.time()
        batch_count = 0
        
        for row, col, value, magnitude in zip(salient_rows, salient_cols, salient_values, salient_magnitudes):
            batch_count += 1
            if batch_count % 50000 == 0:
                elapsed = time.time() - t_encode_start
                rate = batch_count / elapsed
                print(f" | {batch_count:,}/{len(salient_values):,} ({100*batch_count/len(salient_values):.1f}%) @ {rate:.0f}/s", end='', flush=True)
            
            # Look up existing constant atom
            value_id = value_id_map[float(value)]
            
            # COMPOSITION ATOM: Relationship (model-specific position)
            comp_meta_json = json.dumps({
                'model': model_name,
                'layer': layer_idx,
                'component': component,
                'projection': projection,
                'from_dim': int(row),
                'to_dim': int(col),
                'shape': f"{rows}x{cols}"
            })
            
            comp_id = blake3(
                f"edge:{model_name}:{layer_idx}:{component}:{projection}:{row}:{col}".encode('utf-8')
            ).digest()
            
            # Semantic position: encode transformation pathway
            x_comp = (row / rows) * 100 - 50
            y_comp = (col / cols) * 100 - 50
            z_comp = 0.5 + (layer_idx / 12) * 0.5
            m_comp = magnitude
            
            # Get weight value position for LINESTRING endpoint
            value_bytes = struct.pack('f', float(value))
            angle = np.arctan2(float(value), magnitude) if magnitude > 0 else 0
            x_val = magnitude * np.cos(angle) * 50
            y_val = magnitude * np.sin(angle) * 50
            z_val = 0.0
            m_val = magnitude
            
            geom_wkt_comp = f"SRID=0;LINESTRING ZM({x_comp} {y_comp} {z_comp} {m_comp}, {x_val} {y_val} {z_val} {m_val})"
            hilbert_comp = self._encode_hilbert_4d(x_comp, y_comp, z_comp, m_comp)
            
            self.composition_batch.append((
                comp_id,
                1,  # atom_class: Composition
                2,  # modality: Neural
                'transformation',
                None,
                geom_wkt_comp,
                hilbert_comp,
                comp_meta_json,
                value_id
            ))
            
            # Auto-flush compositions when batch fills
            if len(self.composition_batch) >= self.batch_size:
                self.flush_weights()
        
        t_total = time.time() - t_start
        print(f" | DONE {t_total:.2f}s ({len(salient_values)/t_total:.0f} atoms/s)")
    
    def flush_weights(self):
        """Flush weight batches using execute_values (faster than staging tables)"""
        from psycopg2.extras import execute_values
        import time
        
        if not self.weight_batch and not self.composition_batch:
            return 0
        
        t_start = time.time()
        constant_count = len(self.weight_batch)
        composition_count = len(self.composition_batch)
        
        with self.conn.cursor() as cursor:
            # PHASE 1: Insert constant atoms (pre-filtered)
            if self.weight_batch:
                new_weight_batch = [
                    atom for atom in self.weight_batch
                    if atom[0] not in self.existing_atom_ids
                ]
                
                if new_weight_batch:
                    execute_values(cursor, """
                        INSERT INTO atom (atom_id, atom_class, modality, subtype, atomic_value, geom, hilbert_index, metadata)
                        VALUES %s
                    """, new_weight_batch, page_size=10000)
                    
                    # Update cache
                    for atom in new_weight_batch:
                        self.existing_atom_ids.add(atom[0])
                
                t_const = time.time()
            
            # PHASE 2: Insert composition atoms (pre-filtered)
            if self.composition_batch:
                # Prepare composition data (without value_id link for now)
                comp_data = [(row[0], row[1], row[2], row[3], row[4], row[5], row[6], row[7]) 
                            for row in self.composition_batch]
                
                new_comp_data = [
                    atom for atom in comp_data
                    if atom[0] not in self.existing_atom_ids
                ]
                
                if new_comp_data:
                    execute_values(cursor, """
                        INSERT INTO atom (atom_id, atom_class, modality, subtype, atomic_value, geom, hilbert_index, metadata)
                        VALUES %s
                    """, new_comp_data, page_size=10000)
                    
                    # Update cache
                    for atom in new_comp_data:
                        self.existing_atom_ids.add(atom[0])
                
                t_comp = time.time()
                
                # Insert composition links (parent → constituent)
                if new_comp_data:
                    comp_links = [(row[0], row[8], 0) for row in self.composition_batch if row[0] in {a[0] for a in new_comp_data}]
                    
                    if comp_links:
                        execute_values(cursor, """
                            INSERT INTO atom_compositions (parent_atom_id, component_atom_id, sequence_index)
                            VALUES %s
                            ON CONFLICT DO NOTHING
                        """, comp_links, page_size=10000)
                    
                    t_links = time.time()
        
        self.conn.commit()
        self.weight_batch = []
        self.composition_batch = []
        self.total_flushed += constant_count + composition_count
        
        t_end = time.time()
        
        # Timing breakdown
        timing_parts = []
        if constant_count > 0:
            timing_parts.append(f"const:{(t_const-t_start):.2f}s")
        if composition_count > 0:
            if constant_count > 0:
                timing_parts.append(f"comp:{(t_comp-t_const):.2f}s")
                timing_parts.append(f"links:{(t_links-t_comp):.2f}s")
            else:
                timing_parts.append(f"comp:{(t_comp-t_start):.2f}s")
                timing_parts.append(f"links:{(t_links-t_comp):.2f}s")
        
        timing_str = " | ".join(timing_parts) if timing_parts else ""
        print(f"    Flushed {constant_count:,} constants + {composition_count:,} compositions | {(t_end-t_start):.2f}s ({timing_str}) | Total: {self.total_flushed:,}")
        
        return constant_count + composition_count
