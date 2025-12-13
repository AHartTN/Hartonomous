"""
Neural network weight ingestion: Convert trained models into spatial atoms
Decomposes PyTorch/TensorFlow models into tensor atoms for spatial traversal
"""

import numpy as np
from typing import Dict, List, Tuple
import struct
from .semantic_projector import SemanticProjector

class NeuralWeightIngester:
    def __init__(self, db_conn):
        self.db = db_conn
        self.projector = SemanticProjector(scale=100.0)
    
    def ingest_pytorch_model(self, model_path: str) -> bytes:
        """Load PyTorch model and decompose weights into atoms"""
        import torch
        
        # Try safetensors first, fallback to torch.load
        if model_path.endswith('.safetensors'):
            try:
                from safetensors import safe_open
                state_dict = {}
                with safe_open(model_path, framework="pt", device="cpu") as f:
                    for key in f.keys():
                        state_dict[key] = f.get_tensor(key)
            except ImportError:
                # Fallback to torch if safetensors not available
                state_dict = torch.load(model_path, map_location='cpu', weights_only=False)
        else:
            state_dict = torch.load(model_path, map_location='cpu', weights_only=False)
        
        import time
        total_layers = len(state_dict)
        print(f"  Model has {total_layers} layers")
        
        layer_comps = []
        for idx, (layer_name, tensor) in enumerate(state_dict.items(), 1):
            print(f"  [{idx}/{total_layers}] Processing layer: {layer_name}")
            start = time.time()
            
            # Convert to numpy if torch tensor
            if hasattr(tensor, 'numpy'):
                tensor_np = tensor.numpy()
            else:
                tensor_np = np.array(tensor)
                
            layer_comp_id = self._decompose_tensor(
                tensor_np,
                metadata={'layer_name': layer_name, 'framework': 'pytorch'},
                layer_name=layer_name
            )
            layer_comps.append(layer_comp_id)
            elapsed = time.time() - start
            print(f"      OK - Completed in {elapsed:.2f}s")
        
        # Create model composition from all layers
        model_comp_id = self._create_composition(
            layer_comps,
            z_level=3,
            metadata={'model_path': model_path, 'framework': 'pytorch'}
        )
        
        return model_comp_id
    
    def ingest_tensorflow_model(self, model_path: str) -> bytes:
        """Load TensorFlow/Keras model and decompose"""
        import tensorflow as tf
        
        model = tf.keras.models.load_model(model_path)
        
        layer_comps = []
        for layer in model.layers:
            weights = layer.get_weights()
            for i, weight_array in enumerate(weights):
                layer_comp_id = self._decompose_tensor(
                    weight_array,
                    metadata={
                        'layer_name': layer.name,
                        'weight_index': i,
                        'framework': 'tensorflow'
                    }
                )
                layer_comps.append(layer_comp_id)
        
        model_comp_id = self._create_composition(
            layer_comps,
            z_level=3,
            metadata={'model_path': model_path, 'framework': 'tensorflow'}
        )
        
        return model_comp_id
    
    def _decompose_tensor(self, tensor: np.ndarray, metadata: dict, layer_name: str = None) -> bytes:
        """Decompose tensor into quantized weight atoms"""
        # Flatten tensor
        flat = tensor.flatten()
        total_weights = len(flat)
        
        if layer_name:
            print(f"    Layer shape: {tensor.shape} -> {total_weights:,} weights")
        
        # Quantize weights to 2 decimals
        quantized = np.round(flat, 2)
        
        atom_ids = []
        batch = []
        
        for idx, weight_val in enumerate(quantized):
            atom_id = self._generate_weight_atom_id(weight_val)
            atom_ids.append(atom_id)
            batch.append((atom_id, weight_val))
            
            # Batch insert every 1000
            if len(batch) >= 1000:
                self._batch_insert_weights(batch)
                batch = []
                if (idx + 1) % 10000 == 0:
                    print(f"      Progress: {idx+1:,}/{total_weights:,} ({100*(idx+1)/total_weights:.1f}%)")
        
        # Insert remaining
        if batch:
            self._batch_insert_weights(batch)
        
        # Create tensor composition
        comp_id = self._create_composition(
            atom_ids,
            z_level=1,
            metadata={
                **metadata,
                'shape': list(tensor.shape),
                'dtype': str(tensor.dtype),
                'min': float(np.min(tensor)),
                'max': float(np.max(tensor)),
                'mean': float(np.mean(tensor)),
                'std': float(np.std(tensor))
            }
        )
        
        return comp_id
    
    def _generate_weight_atom_id(self, weight: float) -> bytes:
        """Generate SDI for neural network weight"""
        from blake3 import blake3
        
        modality = 4  # Tensor modality
        semantic_class = 1  # Weight class
        
        # Quantize to 2 decimals for determinism
        quantized = round(weight, 2)
        
        return blake3(
            modality.to_bytes(1, 'big') +
            semantic_class.to_bytes(2, 'big') +
            struct.pack('f', quantized)
        ).digest()
    
    def _ensure_weight_atom(self, atom_id: bytes, weight: float):
        """Ensure weight constant atom exists"""
        cursor = self.db.cursor()
        
        # Pack weight as 4-byte float
        weight_bytes = struct.pack('f', weight)
        
    
    def _batch_insert_weights(self, batch):
        """Batch insert weight atoms for efficiency"""
        cursor = self.db.cursor()
        
        values = []
        for atom_id, weight in batch:
            weight_bytes = struct.pack('f', weight)
            x, y, z, m = self.projector.project_weight(float(weight))
            values.append((atom_id, weight_bytes, float(x), float(y), float(z), float(m)))
        
        # Use execute_values for bulk insert
        from psycopg2.extras import execute_values
        execute_values(cursor, """
            INSERT INTO atom (atom_id, atom_class, modality, subtype, atomic_value, geom, hilbert_index)
            VALUES %s
            ON CONFLICT (atom_id) DO NOTHING
        """, [(v[0], 0, 4, 'weight', v[1], f'POINT ZM({v[2]} {v[3]} {v[4]} {v[5]})', 0) for v in values])
        
        self.db.commit()
        # Project weight into semantic space
        x, y, z, m = self.projector.project_weight(float(weight))
        
        cursor.execute("""
            INSERT INTO atom (atom_id, atom_class, modality, subtype, atomic_value, geom, hilbert_index)
            VALUES (%s, 0, 4, 'weight', %s, ST_MakePoint(%s, %s, %s, %s), 0)
            ON CONFLICT (atom_id) DO NOTHING
        """, (atom_id, weight_bytes, float(x), float(y), float(z), float(m)))
        
        self.db.commit()
    
    def _create_composition(self, atom_ids: List[bytes], z_level: int, metadata: dict) -> bytes:
        """Create composition atom from sequence"""
        import json
        from blake3 import blake3
        
        # Generate composition SDI
        content = b''.join(atom_ids)
        comp_id = blake3(b'\x01' + z_level.to_bytes(2, 'big') + content).digest()
        
        # Get constituent positions (if they exist)
        cursor = self.db.cursor()
        
        # Handle both POINT and LINESTRING geometries
        cursor.execute("""
            SELECT 
                CASE 
                    WHEN GeometryType(geom) = 'POINT' THEN ST_X(geom)
                    ELSE ST_X(ST_Centroid(geom))
                END,
                CASE 
                    WHEN GeometryType(geom) = 'POINT' THEN ST_Y(geom)
                    ELSE ST_Y(ST_Centroid(geom))
                END,
                ST_Z(ST_StartPoint(geom)),
                ST_M(ST_StartPoint(geom))
            FROM atom
            WHERE atom_id = ANY(%s)
            ORDER BY array_position(%s, atom_id)
        """, (atom_ids, atom_ids))
        
        coords = cursor.fetchall()
        
        if not coords:
            coords = [(0.0, 0.0, z_level, 1.0)]
        
        # Create LineString
        linestring_wkt = 'LINESTRING ZM(' + ','.join(
            f'{x} {y} {z} {m}' for x, y, z, m in coords
        ) + ')'
        
        # Insert composition
        cursor.execute("""
            INSERT INTO atom (atom_id, atom_class, modality, subtype, geom, hilbert_index, metadata)
            VALUES (%s, 1, 4, 'tensor', %s::geometry, 0, %s)
            ON CONFLICT (atom_id) DO NOTHING
        """, (comp_id, linestring_wkt, json.dumps(metadata)))
        
        # Insert composition relationships
        for order, atom_id in enumerate(atom_ids):
            cursor.execute("""
            INSERT INTO atom_compositions (parent_atom_id, component_atom_id, sequence_index)
            VALUES (%s, %s, %s)
            ON CONFLICT DO NOTHING
            """, (comp_id, atom_id, order))
        
        self.db.commit()
        return comp_id
    
    def reconstruct_tensor(self, comp_id: bytes) -> np.ndarray:
        """Reconstruct tensor from composition"""
        cursor = self.db.cursor()
        
        # Get metadata
        cursor.execute("""
            SELECT metadata
            FROM atom
            WHERE atom_id = %s
        """, (comp_id,))
        
        metadata = cursor.fetchone()[0]
        shape = tuple(metadata['shape'])
        
        # Reconstruct weights
        cursor.execute("""
            SELECT a.atomic_value
            FROM atom_compositions c
            JOIN atom a ON a.atom_id = c.component_atom_id
            WHERE c.parent_atom_id = %s
            ORDER BY c.component_order
        """, (comp_id,))
        
        weights = []
        for row in cursor.fetchall():
            weight_bytes = bytes(row[0])
            weight = struct.unpack('f', weight_bytes)[0]
            weights.append(weight)
        
        # Reshape
        tensor = np.array(weights).reshape(shape)
        return tensor
    
    def query_similar_weights(self, target_weight: float, k: int = 100) -> List[Tuple[bytes, float]]:
        """Find k most similar weight values (spatial k-NN)"""
        cursor = self.db.cursor()
        
        # Generate target atom_id
        target_id = self._generate_weight_atom_id(target_weight)
        
        # Spatial k-NN query
        cursor.execute("""
            SELECT
                atom_id,
                ST_Distance(geom, (SELECT geom FROM atom WHERE atom_id = %s)) as dist
            FROM atom
            WHERE modality = 4 AND subtype = 'weight'
              AND atom_id != %s
            ORDER BY geom <-> (SELECT geom FROM atom WHERE atom_id = %s)
            LIMIT %s
        """, (target_id, target_id, target_id, k))
        
        return [(row[0], row[1]) for row in cursor.fetchall()]
