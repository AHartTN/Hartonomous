"""
Utility for building composite atoms (LINESTRING trajectories).
Sequences constants into higher-order semantic structures.
"""

import psycopg2
from psycopg2.extras import RealDictCursor
import hashlib
from typing import List, Tuple, Optional

class CompositionBuilder:
    """
    Builds composition atoms from sequences of constituent atoms.
    Creates LINESTRING ZM geometry traversing semantic space.
    """
    
    def __init__(self, conn):
        """
        Initialize builder with database connection.
        
        Args:
            conn: psycopg2 connection object
        """
        self.conn = conn
    
    def build_sequence(self, atom_hashes: List[str], modality: int = 3, 
                      z_level: int = 1) -> str:
        """
        Create composition from ordered sequence of atoms.
        
        Args:
            atom_hashes: Ordered list of constituent atom hashes
            modality: Modality code for composition (default 3 = composition)
            z_level: Hierarchy level (1 = features, 2 = concepts, 3 = abstractions)
            
        Returns:
            Hash of created composition atom
        """
        if len(atom_hashes) < 2:
            raise ValueError("Composition requires at least 2 constituent atoms")
        
        cur = self.conn.cursor(cursor_factory=RealDictCursor)
        
        # Fetch constituent atom geometries
        cur.execute("""
            SELECT atom_hash, ST_X(geom) as x, ST_Y(geom) as y, 
                   ST_Z(geom) as z, ST_M(geom) as m
            FROM atom
            WHERE atom_hash = ANY(%s)
            ORDER BY array_position(%s, atom_hash)
        """, (atom_hashes, atom_hashes))
        
        constituents = cur.fetchall()
        
        if len(constituents) != len(atom_hashes):
            missing = set(atom_hashes) - set(c['atom_hash'] for c in constituents)
            raise ValueError(f"Atoms not found: {missing}")
        
        # Build LINESTRING from constituent coordinates
        # Z coordinate increases to hierarchy level, M averages salience
        points = []
        avg_m = sum(c['m'] for c in constituents) / len(constituents)
        
        for c in constituents:
            points.append(f"{c['x']} {c['y']} {z_level} {avg_m}")
        
        linestring_wkt = f"LINESTRING ZM({', '.join(points)})"
        
        # Generate composition hash (SDI for sequence)
        comp_hash = self._compute_composition_hash(atom_hashes, modality)
        
        # Insert composition atom
        cur.execute("""
            INSERT INTO atom (
                atom_hash, atom_class, modality, geom, 
                atomic_value, metadata, constituents
            ) VALUES (
                %s, 1, %s, ST_GeomFromText(%s, 4326),
                NULL, 
                jsonb_build_object('type', 'sequence', 'length', %s),
                %s
            )
            ON CONFLICT (atom_hash) DO NOTHING
            RETURNING atom_hash
        """, (comp_hash, modality, linestring_wkt, len(atom_hashes), atom_hashes))
        
        result = cur.fetchone()
        self.conn.commit()
        cur.close()
        
        if result:
            return result['atom_hash']
        else:
            # Composition already exists
            return comp_hash
    
    def build_from_text(self, text: str, modality: int = 3) -> str:
        """
        Create composition from text by tokenizing and sequencing.
        
        Args:
            text: Input text to compose
            modality: Modality code
            
        Returns:
            Hash of composition atom
        """
        # Simple whitespace tokenization
        tokens = text.lower().split()
        
        # Find or create token atoms
        cur = self.conn.cursor(cursor_factory=RealDictCursor)
        atom_hashes = []
        
        for token in tokens:
            # Generate token hash (modality 2 = text)
            token_hash = self._compute_constant_hash(token, modality=2)
            
            # Check if token exists
            cur.execute("SELECT atom_hash FROM atom WHERE atom_hash = %s", (token_hash,))
            if cur.fetchone():
                atom_hashes.append(token_hash)
            else:
                # Token not in vocabulary, skip (or could create it)
                pass
        
        cur.close()
        
        if len(atom_hashes) < 2:
            raise ValueError(f"Insufficient atoms found for text: '{text}'")
        
        return self.build_sequence(atom_hashes, modality=modality, z_level=1)
    
    def get_constituents(self, comp_hash: str) -> List[str]:
        """
        Retrieve constituent atom hashes for a composition.
        
        Args:
            comp_hash: Composition atom hash
            
        Returns:
            Ordered list of constituent hashes
        """
        cur = self.conn.cursor(cursor_factory=RealDictCursor)
        
        cur.execute("""
            SELECT constituents
            FROM atom
            WHERE atom_hash = %s AND atom_class = 1
        """, (comp_hash,))
        
        result = cur.fetchone()
        cur.close()
        
        if result:
            return result['constituents']
        else:
            raise ValueError(f"Composition not found: {comp_hash}")
    
    def _compute_composition_hash(self, atom_hashes: List[str], modality: int) -> str:
        """
        Generate deterministic hash for composition (SDI).
        
        Args:
            atom_hashes: Ordered constituent hashes
            modality: Modality code
            
        Returns:
            BLAKE3 hash (64 hex chars)
        """
        # SDI format: Modality + SemanticClass + Normalization + Value
        # For composition: modality + "COMP" + ordered constituent hashes
        content = f"{modality:04d}COMP{''.join(atom_hashes)}"
        
        # Use SHA256 as BLAKE3 not in standard library
        return hashlib.sha256(content.encode()).hexdigest()
    
    def _compute_constant_hash(self, value: str, modality: int) -> str:
        """Generate hash for constant atom."""
        content = f"{modality:04d}CONST{value}"
        return hashlib.sha256(content.encode()).hexdigest()

def main():
    """Demo composition building."""
    import os
    
    conn = psycopg2.connect(
        host="localhost",
        port=5432,
        database="hartonomous",
        user="hartonomous",
        password=os.environ.get("PGPASSWORD", "")
    )
    
    builder = CompositionBuilder(conn)
    
    print("🔨 Composition Builder Demo")
    print("=" * 60)
    
    # Example: Build composition from first 5 atoms
    cur = conn.cursor(cursor_factory=RealDictCursor)
    cur.execute("SELECT atom_hash FROM atom WHERE atom_class = 0 LIMIT 5")
    atoms = [row['atom_hash'] for row in cur.fetchall()]
    
    print(f"\n📦 Building composition from {len(atoms)} atoms...")
    comp_hash = builder.build_sequence(atoms)
    print(f"✅ Created: {comp_hash}")
    
    # Retrieve constituents
    constituents = builder.get_constituents(comp_hash)
    print(f"📋 Constituents: {len(constituents)}")
    for i, c in enumerate(constituents[:3]):
        print(f"   {i+1}. {c[:16]}...")
    
    conn.close()

if __name__ == "__main__":
    main()
