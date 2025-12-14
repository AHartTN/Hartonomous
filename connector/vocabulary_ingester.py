"""
Vocabulary ingestion: Load tokenizer vocabulary and decompose to character atoms

Tokens are compositions of characters (Z=1), not raw constants.
This provides the actual semantic context for neural network weights.
"""

import json
import time
from pathlib import Path
import psycopg2
from blake3 import blake3
from semantic_projector import SemanticProjector
from psycopg2.extras import execute_values


class VocabularyIngester:
    def __init__(self, db_conn):
        self.db = db_conn
        self.projector = SemanticProjector(scale=100.0)
    
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
    
    def ingest_huggingface_tokenizer(self, tokenizer_path: str) -> int:
        """Load HuggingFace tokenizer.json and ingest vocabulary"""
        with open(tokenizer_path, encoding='utf-8') as f:
            tokenizer_data = json.load(f)
        
        vocab = tokenizer_data['model']['vocab']
        print(f"Loading vocabulary: {len(vocab):,} tokens")
        
        cursor = self.db.cursor()
        
        # Step 1: Ingest character constants (Z=0)
        chars_inserted = self._ingest_characters(vocab, cursor)
        print(f"  Character atoms: {chars_inserted}")
        
        # Step 2: Ingest token compositions (Z=1)
        tokens_inserted = self._ingest_tokens(vocab, cursor)
        print(f"  Token compositions: {tokens_inserted}")
        
        cursor.close()
        return tokens_inserted
    
    def _ingest_characters(self, vocab, cursor):
        """Ingest unique characters as Z=0 constants"""
        import time
        
        t_start = time.time()
        unique_chars = set()
        for token in vocab.keys():
            # Clean token (remove ## prefix for WordPiece)
            clean_token = token.replace('##', '')
            unique_chars.update(clean_token)
        
        t_collect = time.time()
        print(f"  Collected {len(unique_chars)} unique characters | {(t_collect-t_start)*1000:.0f}ms")
        
        char_batch = []
        for char in unique_chars:
            atom_id = blake3(b'\x01\x02\x00' + char.encode('utf-8', errors='ignore')).digest()
            x, y, z, m = self.projector.project_token(char)
            
            # Compute Hilbert index from geometry
            hilbert_idx = self._encode_hilbert_4d(float(x), float(y), 0.0, float(m))
            
            char_batch.append((
                atom_id,
                0,  # atom_class = constant
                1,  # modality = text
                'character',
                char.encode('utf-8', errors='ignore'),
                f'SRID=4326;POINTZM({x} {y} 0.0 {m})',
                hilbert_idx,
                json.dumps({'char': char, 'ord': ord(char)})
            ))
        
        t_batch = time.time()
        print(f"  Character batch prepared | {(t_batch-t_collect)*1000:.0f}ms", end='', flush=True)
        
        execute_values(cursor, """
            INSERT INTO atom (atom_id, atom_class, modality, subtype, atomic_value, geom, hilbert_index, metadata)
            VALUES %s
            ON CONFLICT (atom_id) DO NOTHING
        """, char_batch)
        
        t_insert = time.time()
        print(f" | insert: {(t_insert-t_batch)*1000:.0f}ms", end='', flush=True)
        
        self.db.commit()
        
        t_commit = time.time()
        print(f" | commit: {(t_commit-t_insert)*1000:.0f}ms | TOTAL: {(t_commit-t_start):.2f}s")
        
        return len(char_batch)
    
    def _ingest_tokens(self, vocab, cursor):
        """Ingest tokens as Z=1 compositions of characters"""
        t_start = time.time()
        token_batch = []
        composition_relationships = []
        
        for token, token_id in vocab.items():
            clean_token = token.replace('##', '')
            token_atom_id = blake3(b'\x01\x03\x01' + clean_token.encode('utf-8', errors='ignore')).digest()
            
            char_coords = []
            constituent_chars = []
            for char in clean_token:
                char_atom_id = blake3(b'\x01\x02\x00' + char.encode('utf-8', errors='ignore')).digest()
                x, y, z, m = self.projector.project_token(char)
                char_coords.append((x, y, 0.0, m))
                constituent_chars.append(char_atom_id)
            
            if len(char_coords) == 1:
                x, y, z, m = char_coords[0]
                geom_wkt = f'SRID=4326;POINTZM({x} {y} 1.0 {m})'
                hilbert_idx = self._encode_hilbert_4d(float(x), float(y), 1.0, float(m))
            else:
                centroid_x = sum(c[0] for c in char_coords) / len(char_coords)
                centroid_y = sum(c[1] for c in char_coords) / len(char_coords)
                avg_m = sum(c[3] for c in char_coords) / len(char_coords)
                linestring_points = ', '.join([f'{x} {y} {z} {m}' for x, y, z, m in char_coords])
                geom_wkt = f'SRID=4326;LINESTRINGZM({linestring_points})'
                hilbert_idx = self._encode_hilbert_4d(float(centroid_x), float(centroid_y), 1.0, float(avg_m))
            
            token_batch.append((
                token_atom_id,
                1,  # atom_class = composition
                1,  # modality = text
                'token',
                None,
                geom_wkt,
                hilbert_idx,
                json.dumps({'token': clean_token, 'token_id': token_id})
            ))
            
            for i, char_id in enumerate(constituent_chars):
                composition_relationships.append((token_atom_id, char_id, i))
        
        execute_values(cursor, """
            INSERT INTO atom (atom_id, atom_class, modality, subtype, atomic_value, geom, hilbert_index, metadata)
            VALUES %s
            ON CONFLICT (atom_id) DO NOTHING
        """, token_batch)
        
        execute_values(cursor, """
            INSERT INTO atom_compositions (parent_atom_id, component_atom_id, sequence_index)
            VALUES %s
            ON CONFLICT DO NOTHING
        """, composition_relationships)
        
        self.db.commit()
        t_end = time.time()
        print(f"  [{(t_end-t_start):.2f}s] {len(token_batch)} tokens, {len(composition_relationships)} compositions")
        return len(token_batch)

