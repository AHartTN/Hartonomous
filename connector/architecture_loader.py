"""
Architecture metadata loader: Parse model config and map layers to semantic structure

Neural network architectures define WHAT weights DO:
- Attention: Q/K/V projections relate tokens through similarity
- FFN: Up/down projections transform representations
- Embeddings: Map token_id → semantic vector

This provides the context needed for weight atoms to be meaningful relationships.
"""

import json
from pathlib import Path
from typing import Dict, List, Tuple
import psycopg2
from blake3 import blake3


class ArchitectureLoader:
    def __init__(self, conn):
        self.conn = conn
    
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
    
    def load_transformers_config(self, config_path: str) -> Dict:
        """Load HuggingFace transformers config.json"""
        with open(config_path, encoding='utf-8') as f:
            return json.load(f)
    
    def ingest_bert_architecture(self, config_path: str, model_name: str = "BERT") -> int:
        """
        Ingest BERT model architecture as compositional structure
        
        Architecture hierarchy:
        Z=2: Model (full BERT)
        Z=1.5: Layers (encoder blocks 0-11)
        Z=1.0: Components (attention, FFN, LayerNorm)
        Z=0.5: Projections (Q, K, V, up, down)
        
        Returns: Number of architecture atoms created
        """
        config = self.load_transformers_config(config_path)
        
        with self.conn.cursor() as cursor:
            atoms_created = 0
            
            # Model-level atom (Z=2)
            model_metadata = {
                'model_type': config.get('model_type', 'bert'),
                'vocab_size': config.get('vocab_size'),
                'hidden_size': config.get('hidden_size'),
                'num_hidden_layers': config.get('num_hidden_layers'),
                'num_attention_heads': config.get('num_attention_heads'),
                'intermediate_size': config.get('intermediate_size'),
                'max_position_embeddings': config.get('max_position_embeddings')
            }
            
            model_id = blake3(f"model:{model_name}".encode('utf-8')).digest()
            
            # Compute Hilbert index for model atom
            hilbert_idx = self._encode_hilbert_4d(0.0, 0.0, 2.0, 1.0)
            
            cursor.execute("""
                INSERT INTO atom (
                    atom_id, atom_class, modality, subtype,
                    atomic_value, geom, hilbert_index, metadata
                ) VALUES (
                    %s, 1, 2, 'model',
                    NULL, 'SRID=0;POINT ZM(0 0 2.0 1.0)', %s, %s
                ) ON CONFLICT (atom_id) DO NOTHING
            """, (model_id, hilbert_idx, json.dumps(model_metadata)))
            atoms_created += 1
            
            # Layer atoms (Z=1.5)
            num_layers = config.get('num_hidden_layers', 12)
            for layer_idx in range(num_layers):
                layer_id = blake3(f"layer:{model_name}:{layer_idx}".encode('utf-8')).digest()
                layer_metadata = {
                    'layer_index': layer_idx,
                    'component_type': 'transformer_encoder',
                    'model': model_name
                }
                
                # Compute Hilbert index for layer atom
                hilbert_idx = self._encode_hilbert_4d(0.0, 0.0, 1.5, 1.0)
                
                cursor.execute("""
                    INSERT INTO atom (
                        atom_id, atom_class, modality, subtype,
                        atomic_value, geom, hilbert_index, metadata
                    ) VALUES (
                        %s, 1, 2, 'layer',
                        NULL, 'SRID=0;POINT ZM(0 0 1.5 1.0)', %s, %s
                    ) ON CONFLICT (atom_id) DO NOTHING
                """, (layer_id, hilbert_idx, json.dumps(layer_metadata)))
                atoms_created += 1
                
                # Layer composition (model → layer)
                cursor.execute("""
                    INSERT INTO atom_compositions (parent_atom_id, component_atom_id, sequence_index)
                    VALUES (%s, %s, %s)
                    ON CONFLICT DO NOTHING
                """, (model_id, layer_id, layer_idx))
                
                # Component atoms (Z=1.0): attention, FFN, LayerNorm
                components = [
                    ('attention', {'heads': config.get('num_attention_heads')}),
                    ('ffn', {'intermediate_size': config.get('intermediate_size')}),
                    ('layer_norm', {})
                ]
                
                for comp_idx, (comp_type, comp_meta) in enumerate(components):
                    comp_id = blake3(f"component:{model_name}:{layer_idx}:{comp_type}".encode('utf-8')).digest()
                    comp_meta['layer_index'] = layer_idx
                    comp_meta['component_type'] = comp_type
                    comp_meta['model'] = model_name
                    
                    # Compute Hilbert index for component atom
                    hilbert_idx = self._encode_hilbert_4d(0.0, 0.0, 1.0, 1.0)
                    
                    cursor.execute("""
                        INSERT INTO atom (
                            atom_id, atom_class, modality, subtype,
                            atomic_value, geom, hilbert_index, metadata
                        ) VALUES (
                            %s, 1, 2, 'component',
                            NULL, 'SRID=0;POINT ZM(0 0 1.0 1.0)', %s, %s
                        ) ON CONFLICT (atom_id) DO NOTHING
                    """, (comp_id, hilbert_idx, json.dumps(comp_meta)))
                    atoms_created += 1
                    
                    # Layer → component composition
                    cursor.execute("""
                        INSERT INTO atom_compositions (parent_atom_id, component_atom_id, sequence_index)
                        VALUES (%s, %s, %s)
                        ON CONFLICT DO NOTHING
                    """, (layer_id, comp_id, comp_idx))
            
            self.conn.commit()
            return atoms_created
    
    def get_layer_components(self, cursor, model_name: str, layer_idx: int) -> Dict[str, bytes]:
        """Get component atom IDs for a specific layer"""
        cursor.execute("""
            SELECT metadata->>'component_type', atom_id
            FROM atom
            WHERE subtype = 'component'
              AND metadata->>'model' = %s
              AND (metadata->>'layer_index')::int = %s
        """, (model_name, layer_idx))
        
        return {row[0]: row[1] for row in cursor.fetchall()}
    
    def get_projection_atom_id(self, model_name: str, layer_idx: int, 
                              component: str, projection: str) -> bytes:
        """
        Generate deterministic atom ID for a projection
        
        Args:
            model_name: e.g., "BERT"
            layer_idx: 0-11 for BERT-base
            component: "attention", "ffn", "layer_norm"
            projection: "query", "key", "value", "output", "up", "down", etc.
        
        Returns: BLAKE3 hash as atom_id
        """
        return blake3(f"projection:{model_name}:{layer_idx}:{component}:{projection}".encode('utf-8')).digest()
