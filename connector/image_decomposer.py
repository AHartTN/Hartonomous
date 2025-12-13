"""
Image processing: Decompose images into pixel atoms with 216-color palette
Handles PNG, JPEG, BMP with quantization and spatial composition
"""

from PIL import Image
import numpy as np
from pathlib import Path
from typing import List, Tuple
import struct
import json
from .semantic_projector import SemanticProjector

class ImageDecomposer:
    def __init__(self, db_conn):
        self.db = db_conn
        self.projector = SemanticProjector(scale=100.0)
        # 216-color web palette (6x6x6 cube)
        self.palette_steps = 6
        self.quantize_factor = 256 // self.palette_steps
        
    def decompose_image(self, file_path: Path) -> bytes:
        """Decompose image into pixel atoms"""
        img = Image.open(file_path).convert('RGB')
        width, height = img.size
        pixels = np.array(img)
        
        # Quantize to 216-color palette
        quantized = (pixels // self.quantize_factor) * self.quantize_factor
        
        atom_ids = []
        for y in range(height):
            for x in range(width):
                r, g, b = quantized[y, x]
                pixel_atom_id = self._generate_pixel_atom_id(r, g, b)
                atom_ids.append(pixel_atom_id)
                self._ensure_pixel_atom(pixel_atom_id, r, g, b)
        
        # Create image composition
        comp_id = self._create_image_composition(
            atom_ids,
            metadata={
                'file_path': str(file_path),
                'width': width,
                'height': height,
                'format': file_path.suffix.upper(),
                'n_pixels': width * height
            }
        )
        
        return comp_id
    
    def _generate_pixel_atom_id(self, r: int, g: int, b: int) -> bytes:
        """Generate SDI for RGB pixel"""
        from blake3 import blake3
        
        modality = 2  # Image modality
        semantic_class = 0
        
        return blake3(
            modality.to_bytes(1, 'big') +
            semantic_class.to_bytes(2, 'big') +
            struct.pack('BBB', r, g, b)
        ).digest()
    
    def _ensure_pixel_atom(self, atom_id: bytes, r: int, g: int, b: int):
        """Ensure pixel constant exists"""
        cursor = self.db.cursor()
        
        pixel_bytes = struct.pack('BBB', r, g, b)
        x, y, z, m = self.projector.project_color(r, g, b)
        
        cursor.execute("""
            INSERT INTO atom (atom_id, atom_class, modality, subtype, atomic_value, geom)
            VALUES (%s, 0, 2, 'pixel', %s, ST_MakePoint(%s, %s, %s, %s))
            ON CONFLICT (atom_id) DO NOTHING
        """, (atom_id, pixel_bytes, float(x), float(y), float(z), float(m)))
        
        self.db.commit()
    
    def _create_image_composition(self, atom_ids: List[bytes], metadata: dict) -> bytes:
        """Create image composition"""
        if len(atom_ids) < 2:
            raise ValueError(f"Image must have at least 2 pixels, got {len(atom_ids)}")
        
        from blake3 import blake3
        
        # Generate composition SDI
        content = b''.join(atom_ids)
        comp_id = blake3(b'\x01\x00\x00' + content).digest()
        
        # Get constituent positions
        cursor = self.db.cursor()
        
        cursor.execute("""
            SELECT ST_X(geom), ST_Y(geom), ST_Z(geom), ST_M(geom)
            FROM atom
            WHERE atom_id = ANY(%s)
            ORDER BY array_position(%s, atom_id)
        """, (atom_ids, atom_ids))
        
        coords = cursor.fetchall()
        
        if not coords or len(coords) < 2:
            # Not enough distinct atoms - create synthetic trajectory with duplicates
            if coords and len(coords) == 1:
                x, y, z, m = coords[0]
                coords = [(x, y, z, m), (x, y, z, m)]  # Duplicate to meet LINESTRING requirement
            else:
                coords = [(0.0, 0.0, 0.0, 1.0), (0.0, 0.0, 0.0, 1.0)]
        
        # Create LineString
        linestring_wkt = 'LINESTRING ZM(' + ','.join(
            f'{x} {y} {z} {m}' for x, y, z, m in coords
        ) + ')'
        
        # Insert composition
        cursor.execute("""
            INSERT INTO atom (atom_id, atom_class, modality, subtype, geom, metadata)
            VALUES (%s, 1, 2, 'image', %s::geometry, %s)
            ON CONFLICT (atom_id) DO NOTHING
        """, (comp_id, linestring_wkt, json.dumps(metadata)))
        
        # Insert composition relationships (batched for performance)
        from psycopg2.extras import execute_values
        
        relationship_rows = [
            (comp_id, atom_id, order)
            for order, atom_id in enumerate(atom_ids)
        ]
        
        execute_values(cursor, """
            INSERT INTO atom_compositions (parent_atom_id, component_atom_id, sequence_index)
            VALUES %s
            ON CONFLICT DO NOTHING
        """, relationship_rows)
        
        self.db.commit()
        return comp_id
    
    def reconstruct_image(self, comp_id: bytes, output_path: Path):
        """Reconstruct image from composition"""
        cursor = self.db.cursor()
        
        # Get metadata
        cursor.execute("""
            SELECT metadata
            FROM atom
            WHERE atom_id = %s
        """, (comp_id,))
        
        metadata = cursor.fetchone()[0]
        width = metadata['width']
        height = metadata['height']
        
        # Reconstruct pixels
        cursor.execute("""
            SELECT a.atomic_value
            FROM atom_compositions c
            JOIN atom a ON a.atom_id = c.component_atom_id
            WHERE c.parent_atom_id = %s
            ORDER BY c.sequence_index
        """, (comp_id,))
        
        pixels = []
        for row in cursor.fetchall():
            pixel_bytes = bytes(row[0])
            r, g, b = struct.unpack('BBB', pixel_bytes)
            pixels.append([r, g, b])
        
        # Reshape to image
        pixel_array = np.array(pixels, dtype=np.uint8).reshape(height, width, 3)
        
        # Create image
        img = Image.fromarray(pixel_array, 'RGB')
        img.save(output_path)
