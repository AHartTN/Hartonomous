"""
Hilbert indexing integration: Call Rust shader for spatial indexing

All atoms need hilbert_index computed for efficient spatial queries.
Currently all are 0 - need to batch update with proper indices.
"""

import subprocess
import json
import tempfile
import psycopg2
from pathlib import Path


class HilbertIndexer:
    def __init__(self, conn, shader_path: Path = None):
        self.conn = conn
        if shader_path is None:
            self.shader_path = Path(__file__).parent.parent / "target" / "release" / "shader.exe"
        else:
            self.shader_path = shader_path
    
    def reindex_all_atoms(self, batch_size: int = 10000):
        """Compute Hilbert indices for all atoms in database"""
        cursor = self.conn.cursor()
        
        # Get total count
        cursor.execute("SELECT COUNT(*) FROM atom WHERE hilbert_index = 0")
        total = cursor.fetchone()[0]
        print(f"Reindexing {total:,} atoms...")
        
        processed = 0
        while processed < total:
            # Fetch batch
            cursor.execute("""
                SELECT atom_id, ST_X(geom), ST_Y(geom), ST_ZMin(geom), ST_M(geom::geometry(POINTZM))
                FROM atom
                WHERE hilbert_index = 0
                  AND ST_GeometryType(geom) = 'ST_Point'
                LIMIT %s
            """, (batch_size,))
            
            batch = cursor.fetchall()
            if not batch:
                break
            
            # Compute indices
            updates = []
            for atom_id, x, y, z, m in batch:
                # Normalize to [0,1]
                x_norm = (x + 50) / 100  # Assuming [-50,50] range
                y_norm = (y + 50) / 100
                z_norm = z / 3.0  # Z range [0,3]
                m_norm = m  # Already [0,1]
                
                hilbert_idx = self._encode_hilbert_python(x_norm, y_norm, z_norm, m_norm)
                updates.append((hilbert_idx, atom_id))
            
            # Bulk update
            cursor.executemany("UPDATE atom SET hilbert_index = %s WHERE atom_id = %s", updates)
            self.conn.commit()
            
            processed += len(batch)
            print(f"  Indexed {processed:,} / {total:,} ({100*processed/total:.1f}%)")
        
        print(f"Reindexing complete: {processed:,} atoms updated")
        return processed
    
    def _encode_hilbert_python(self, x: float, y: float, z: float, m: float, resolution: int = 10) -> int:
        """
        Pure Python Hilbert encoding (fallback if Rust shader unavailable)
        
        Args:
            x, y, z, m: Normalized coordinates [0.0, 1.0]
            resolution: Bits per dimension (default 10)
        
        Returns:
            64-bit Hilbert index
        """
        max_val = (1 << resolution) - 1
        
        # Clamp and convert to integer grid
        xi = int(max(0.0, min(1.0, x)) * max_val)
        yi = int(max(0.0, min(1.0, y)) * max_val)
        zi = int(max(0.0, min(1.0, z)) * max_val)
        mi = int(max(0.0, min(1.0, m)) * max_val)
        
        index = 0
        coords = [xi, yi, zi, mi]
        
        for i in range(resolution - 1, -1, -1):
            bits = [(c >> i) & 1 for c in coords]
            quadrant = bits[0] | (bits[1] << 1) | (bits[2] << 2) | (bits[3] << 3)
            index = (index << 4) | quadrant
            
            # Rotation
            self._rotate_4d(coords, quadrant)
        
        return index
    
    def _rotate_4d(self, coords: list, quadrant: int):
        """Rotate coordinates based on quadrant"""
        if quadrant == 0:
            coords[0], coords[3] = coords[3], coords[0]
        elif quadrant == 1:
            coords[1], coords[2] = coords[2], coords[1]
        elif quadrant == 7:
            coords.reverse()
