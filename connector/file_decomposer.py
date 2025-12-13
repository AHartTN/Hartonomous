"""
File decomposition pipeline: Convert arbitrary files into atom sequences
Handles text, images, audio, and binary data
"""

import hashlib
from pathlib import Path
from typing import List, Tuple, Optional
import struct
from .semantic_projector import SemanticProjector

class FileDecomposer:
    def __init__(self, db_conn):
        self.db = db_conn
        self.projector = SemanticProjector(scale=100.0)
        self.chunk_size = 4096  # Bytes per chunk
        
    def decompose_file(self, file_path: Path) -> bytes:
        """Decompose file into atoms and return composition atom_id"""
        
        # Detect file type
        suffix = file_path.suffix.lower()
        
        if suffix in ['.txt', '.md', '.py', '.rs', '.sql', '.json']:
            return self._decompose_text(file_path)
        elif suffix in ['.png', '.jpg', '.jpeg', '.bmp']:
            return self._decompose_image(file_path)
        elif suffix in ['.wav', '.mp3', '.flac']:
            return self._decompose_audio(file_path)
        else:
            return self._decompose_binary(file_path)
    
    def _decompose_text(self, file_path: Path) -> bytes:
        """Text files: Tokenize → atoms → RLE → CPE hierarchy"""
        from connector.operations import HartonomousConnector
        
        text = file_path.read_text(encoding='utf-8')
        
        # Tokenize (simple whitespace for now, can upgrade to BPE)
        tokens = text.split()
        
        atom_ids = []
        for token in tokens:
            # Generate SDI for token
            atom_id = self._generate_token_atom_id(token)
            atom_ids.append(atom_id)
            
            # Ensure token atom exists
            self._ensure_atom_exists(atom_id, token, modality=1, subtype='token')
        
        # Apply RLE
        rle_atoms = self._apply_rle(atom_ids)
        
        # Build CPE hierarchy
        comp_id = self._build_cpe_composition(rle_atoms, z_level=1)
        
        # Store file metadata
        self._store_file_metadata(comp_id, file_path)
        
        return comp_id
    
    def _decompose_image(self, file_path: Path) -> bytes:
        """Images: Quantize pixels → color atoms → spatial composition"""
        from PIL import Image
        import numpy as np
        
        img = Image.open(file_path).convert('RGB')
        width, height = img.size
        pixels = np.array(img)
        
        # Quantize to 216-color web palette (6×6×6 cube)
        quantized = (pixels // 43) * 43  # 256/6 ≈ 43
        
        atom_ids = []
        for y in range(height):
            for x in range(width):
                r, g, b = quantized[y, x]
                color_atom_id = self._generate_color_atom_id(r, g, b)
                atom_ids.append(color_atom_id)
                
                self._ensure_atom_exists(
                    color_atom_id,
                    struct.pack('BBB', r, g, b),
                    modality=2,
                    subtype='pixel'
                )
        
        # Create composition with spatial metadata
        comp_id = self._create_composition(
            atom_ids,
            z_level=0,
            metadata={'width': width, 'height': height, 'format': 'RGB'}
        )
        
        self._store_file_metadata(comp_id, file_path)
        return comp_id
    
    def _decompose_audio(self, file_path: Path) -> bytes:
        """Audio: Sample quantization → waveform atoms → temporal composition"""
        import wave
        
        with wave.open(str(file_path), 'rb') as wav:
            framerate = wav.getframerate()
            n_channels = wav.getnchannels()
            sampwidth = wav.getsampwidth()
            n_frames = wav.getnframes()
            
            frames = wav.readframes(n_frames)
        
        # Quantize samples to 8-bit
        if sampwidth == 2:  # 16-bit PCM
            samples = struct.unpack(f'{n_frames * n_channels}h', frames)
            quantized = [int((s + 32768) / 256) for s in samples]
        else:
            quantized = list(frames)
        
        atom_ids = []
        for sample_val in quantized:
            atom_id = self._generate_sample_atom_id(sample_val)
            atom_ids.append(atom_id)
            
            self._ensure_atom_exists(
                atom_id,
                struct.pack('B', sample_val),
                modality=3,
                subtype='sample'
            )
        
        comp_id = self._create_composition(
            atom_ids,
            z_level=0,
            metadata={
                'framerate': framerate,
                'channels': n_channels,
                'sampwidth': sampwidth
            }
        )
        
        self._store_file_metadata(comp_id, file_path)
        return comp_id
    
    def _decompose_binary(self, file_path: Path) -> bytes:
        """Binary files: Byte-level RLE → composition"""
        data = file_path.read_bytes()
        
        atom_ids = []
        for byte_val in data:
            atom_id = self._generate_byte_atom_id(byte_val)
            atom_ids.append(atom_id)
            
            self._ensure_atom_exists(
                atom_id,
                bytes([byte_val]),
                modality=4,
                subtype='byte'
            )
        
        # RLE compression
        rle_atoms = self._apply_rle(atom_ids)
        
        comp_id = self._create_composition(rle_atoms, z_level=0)
        self._store_file_metadata(comp_id, file_path)
        return comp_id
    
    def _apply_rle(self, atom_ids: List[bytes]) -> List[Tuple[bytes, int]]:
        """Run-length encoding: returns (atom_id, run_length) pairs"""
        if not atom_ids:
            return []
        
        rle = []
        current_id = atom_ids[0]
        count = 1
        
        for atom_id in atom_ids[1:]:
            if atom_id == current_id:
                count += 1
            else:
                rle.append((current_id, count))
                current_id = atom_id
                count = 1
        
        rle.append((current_id, count))
        return rle
    
    def _build_cpe_composition(self, rle_atoms: List[Tuple[bytes, int]], z_level: int) -> bytes:
        """Build CPE hierarchy from RLE atoms"""
        # For now, just create single composition
        # TODO: Implement full CPE pair encoding
        atom_ids = [aid for aid, _ in rle_atoms]
        return self._create_composition(atom_ids, z_level)
    
    def _create_composition(self, atom_ids: List[bytes], z_level: int, metadata: dict = None) -> bytes:
        """Create composition atom from sequence"""
        import json
        from blake3 import blake3
        
        # Generate composition SDI
        content = b''.join(atom_ids)
        comp_id = blake3(b'\x01' + z_level.to_bytes(2, 'big') + content).digest()
        
        # Build LineString geometry from constituent atom positions
        cursor = self.db.cursor()
        
        # Get constituent atom geometries
        cursor.execute("""
            SELECT ST_X(geom), ST_Y(geom), ST_Z(geom), ST_M(geom)
            FROM atom
            WHERE atom_id = ANY(%s)
            ORDER BY array_position(%s, atom_id)
        """, (atom_ids, atom_ids))
        
        coords = cursor.fetchall()
        
        if not coords:
            # No constituents yet - use default position
            coords = [(0.0, 0.0, z_level, 1.0)]
        
        # Create LineString
        linestring_wkt = 'LINESTRING ZM(' + ','.join(
            f'{x} {y} {z} {m}' for x, y, z, m in coords
        ) + ')'
        
        # Insert composition
        cursor.execute("""
            INSERT INTO atom (atom_id, atom_class, modality, subtype, geom, metadata)
            VALUES (%s, 1, %s, 'composition', %s::geometry, %s)
            ON CONFLICT (atom_id) DO NOTHING
        """, (comp_id, 1, linestring_wkt, json.dumps(metadata or {})))
        
        # Insert composition relationships
        for order, atom_id in enumerate(atom_ids):
            cursor.execute("""
                INSERT INTO atom_compositions (parent_atom_id, component_atom_id, sequence_index)
                VALUES (%s, %s, %s)
                ON CONFLICT DO NOTHING
            """, (comp_id, atom_id, order))
        
        self.db.commit()
        return comp_id
    
    def _ensure_atom_exists(self, atom_id: bytes, value: bytes, modality: int, subtype: str):
        """Ensure constant atom exists in database"""
        cursor = self.db.cursor()
        
        # Project based on modality
        if modality == 1:  # Text
            try:
                token_str = value.decode('utf-8', errors='ignore')
                x, y, z, m = self.projector.project_token(token_str)
            except:
                x, y, z, m = 0.0, 0.0, 0.0, 1.0
        elif modality == 0:  # Numeric
            try:
                num_val = struct.unpack('d', value)[0]
                x, y, z, m = self.projector.project_numeric(num_val)
            except:
                x, y, z, m = 0.0, 0.0, 0.0, 1.0
        else:
            x, y, z, m = 0.0, 0.0, 0.0, 1.0
        
        cursor.execute("""
            INSERT INTO atom (atom_id, atom_class, modality, subtype, atomic_value, geom)
            VALUES (%s, 0, %s, %s, %s, ST_MakePoint(%s, %s, %s, %s))
            ON CONFLICT (atom_id) DO NOTHING
        """, (atom_id, modality, subtype, value, float(x), float(y), float(z), float(m)))
        
        self.db.commit()
    
    def _generate_token_atom_id(self, token: str) -> bytes:
        """Generate SDI for text token"""
        from blake3 import blake3
        modality = 1
        semantic_class = 0
        return blake3(
            modality.to_bytes(1, 'big') +
            semantic_class.to_bytes(2, 'big') +
            token.encode('utf-8')
        ).digest()
    
    def _generate_color_atom_id(self, r: int, g: int, b: int) -> bytes:
        """Generate SDI for RGB color"""
        from blake3 import blake3
        modality = 2
        return blake3(
            modality.to_bytes(1, 'big') +
            struct.pack('BBB', r, g, b)
        ).digest()
    
    def _generate_sample_atom_id(self, sample: int) -> bytes:
        """Generate SDI for audio sample"""
        from blake3 import blake3
        modality = 3
        return blake3(
            modality.to_bytes(1, 'big') +
            struct.pack('B', sample)
        ).digest()
    
    def _generate_byte_atom_id(self, byte_val: int) -> bytes:
        """Generate SDI for binary byte"""
        from blake3 import blake3
        modality = 4
        return blake3(
            modality.to_bytes(1, 'big') +
            bytes([byte_val])
        ).digest()
    
    def _store_file_metadata(self, comp_id: bytes, file_path: Path):
        """Store file metadata linking composition to original file"""
        import json
        cursor = self.db.cursor()
        cursor.execute("""
            UPDATE atom
            SET metadata = metadata || %s::jsonb
            WHERE atom_id = %s
        """, (json.dumps({
            'file_path': str(file_path),
            'file_name': file_path.name,
            'file_size': file_path.stat().st_size
        }), comp_id))
        self.db.commit()
