"""
Vocabulary ingestion: Load tokenizer vocabulary and decompose to character atoms

Tokens are compositions of characters (Z=1), not raw constants.
This provides the actual semantic context for neural network weights.
"""

import json
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
        ursor, """
        import time
        
        t_start = time.time()
        token_batch = []
        composition_relationships = []
        
        processed = 0
        for token, token_id in vocab.items():
            processed += 1
            if processed % 5000 == 0:
                print(f"  Processing tokens: {processed:,}/{len(vocab):,} ({100*processed/len(vocab):.1f}%)", end='\r', flush=True)
        
        self.db.commit()
        return len(char_batch)
    
    def _ingest_tokens(self, vocab, cursor):
        """Ingest tokens as Z=1 compositions of characters"""
        token_batch = []
        composition_relationships = []
        
        for token, token_id in vocab.items():
            # Clean token
            clean_token = token.replace('##', '')
            
            # Generate token composition ID
            comp_id = blake3(b'\x01\x01\x01' + token.encode('utf-8', errors='ignore')).digest()
            
            # Get character atom IDs for this token
            char_atoms = []
            for char in clean_token:
                char_atom_id = blake3(b'\x01\x02\x00' + char.encode('utf-8', errors='ignore')).digest()
                char_atoms.append(char_atom_id)
            
            if not char_atoms:
                continue
            
            # Build geometry through character positions
            # Query character positions
            cursor.execute("""
                SELECT ST_X(geom), ST_Y(geom), ST_M(geom)
                FROM atom
                WHERE atom_id = ANY(%s)
                ORDER BY atom_id
            """, (char_atoms,))
            
            coords = cursor.fetchall()
            if len(coords) != len(char_atoms):
                continue  # Missing character atoms
            
            # Single-character tokens are POINTZM, multi-character are LINESTRING ZM
            if len(coords) == 1:
                x, y, m = coords[0]
                hilbert_idx = self._encode_hilbert_4d(float(x), float(y), 1.0, float(m))
                geom_wkt = f"POINT ZM({x} {y} 1.0 {m})"
                atom_class = 0  # Constant
                atomic_value = psycopg2.Binary(clean_token.encode('utf-8'))
            else:
                # Multi-character: compute centroid for Hilbert indexing
                centroid_x = sum(c[0] for c in coords) / len(coords)
                centroid_y = sum(c[1] for c in coords) / len(coords)
                centroid_m = sum(c[2] for c in coords) / len(coords)
                hilbert_idx = self._encode_hilbert_4d(float(centroid_x), float(centroid_y), 1.0, float(centroid_m))
                
                linestring_parts = [f"{x} {y} 1.0 {m}" for x, y, m in coords]
                geom_wkt = f"LINESTRING ZM({', '.join(linestring_parts)})"
                atom_class = 1  # Composition
                atomic_value = None
        t_prep = time.time()
        print(f"\n  Token batch prepared: {len(token_batch):,} tokens | {(t_prep-t_start):.2f}s", end='', flush=True)
        
        # Insert token compositions
        execute_values(cursor, """
            INSERT INTO atom (atom_id, atom_class, modality, subtype, atomic_value, geom, hilbert_index, metadata)
            VALUES %s
            ON CONFLICT (atom_id) DO NOTHING
        """, token_batch)
        
        t_token_insert = time.time()
        print(f" | token insert: {(t_token_insert-t_prep):.2f}s", end='', flush=True)
        
        # Insert composition relationships
        execute_values(cursor, """
            INSERT INTO atom_compositions (parent_atom_id, component_atom_id, sequence_index)
            VALUES %s
            ON CONFLICT DO NOTHING
        """, composition_relationships)
        
        t_comp_insert = time.time()
        print(f" | compositions: {(t_comp_insert-t_token_insert):.2f}s", end='', flush=True)
        
        self.db.commit()
        
        t_commit = time.time()
        print(f" | commit: {(t_commit-t_comp_insert):.2f}s | TOTAL: {(t_commit-t_start):.2f}s")
        
        return len(token_batch)

