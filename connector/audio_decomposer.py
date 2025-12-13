"""
Audio processing pipeline: Decompose audio files into waveform atoms
Handles WAV, MP3, FLAC with quantization and temporal composition
"""

import wave
import struct
import numpy as np
from pathlib import Path
from typing import List, Tuple
import json
from .semantic_projector import SemanticProjector

class AudioDecomposer:
    def __init__(self, db_conn):
        self.db = db_conn
        self.projector = SemanticProjector(scale=100.0)
        self.sample_quantization = 256  # 8-bit quantization
        
    def decompose_wav(self, file_path: Path) -> bytes:
        """Decompose WAV file into sample atoms"""
        with wave.open(str(file_path), 'rb') as wav:
            params = wav.getparams()
            framerate = params.framerate
            n_channels = params.nchannels
            sampwidth = params.sampwidth
            n_frames = params.nframes
            
            frames = wav.readframes(n_frames)
        
        # Convert to numpy array
        if sampwidth == 1:  # 8-bit
            samples = np.frombuffer(frames, dtype=np.uint8)
        elif sampwidth == 2:  # 16-bit
            samples = np.frombuffer(frames, dtype=np.int16)
            # Quantize to 8-bit
            samples = ((samples.astype(np.float32) + 32768) / 256).astype(np.uint8)
        else:
            raise ValueError(f"Unsupported sample width: {sampwidth}")
        
        # Deinterleave channels
        if n_channels > 1:
            samples = samples.reshape(-1, n_channels)
            # Mix to mono for simplicity
            samples = samples.mean(axis=1).astype(np.uint8)
        
        # Create sample atoms
        atom_ids = []
        for sample_val in samples:
            atom_id = self._generate_sample_atom_id(int(sample_val))
            atom_ids.append(atom_id)
            self._ensure_sample_atom(atom_id, int(sample_val))
        
        # Apply RLE compression
        rle_atoms = self._apply_rle(atom_ids)
        
        # Create waveform composition
        comp_id = self._create_waveform_composition(
            rle_atoms,
            metadata={
                'file_path': str(file_path),
                'framerate': framerate,
                'channels': n_channels,
                'sampwidth': sampwidth,
                'duration': n_frames / framerate,
                'n_samples': len(samples)
            }
        )
        
        return comp_id
    
    def decompose_mp3(self, file_path: Path) -> bytes:
        """Decompose MP3 using pydub"""
        try:
            from pydub import AudioSegment
        except ImportError:
            raise ImportError("pydub required for MP3: pip install pydub")
        
        audio = AudioSegment.from_mp3(str(file_path))
        
        # Convert to WAV in memory
        wav_data = audio.export(format='wav')
        
        # Process as WAV
        return self._process_audio_segment(audio, file_path)
    
    def _process_audio_segment(self, audio, file_path: Path) -> bytes:
        """Process pydub AudioSegment"""
        samples = np.array(audio.get_array_of_samples())
        
        # Quantize to 8-bit
        if audio.sample_width == 2:
            samples = ((samples.astype(np.float32) + 32768) / 256).astype(np.uint8)
        
        # Mix to mono if stereo
        if audio.channels == 2:
            samples = samples.reshape(-1, 2).mean(axis=1).astype(np.uint8)
        
        atom_ids = []
        for sample_val in samples:
            atom_id = self._generate_sample_atom_id(int(sample_val))
            atom_ids.append(atom_id)
            self._ensure_sample_atom(atom_id, int(sample_val))
        
        rle_atoms = self._apply_rle(atom_ids)
        
        comp_id = self._create_waveform_composition(
            rle_atoms,
            metadata={
                'file_path': str(file_path),
                'framerate': audio.frame_rate,
                'channels': audio.channels,
                'sampwidth': audio.sample_width,
                'duration': len(audio) / 1000.0,
                'n_samples': len(samples)
            }
        )
        
        return comp_id
    
    def _apply_rle(self, atom_ids: List[bytes]) -> List[Tuple[bytes, int]]:
        """Run-length encoding"""
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
    
    def _generate_sample_atom_id(self, sample: int) -> bytes:
        """Generate SDI for audio sample"""
        from blake3 import blake3
        
        modality = 3  # Audio modality
        semantic_class = 0
        
        return blake3(
            modality.to_bytes(1, 'big') +
            semantic_class.to_bytes(2, 'big') +
            sample.to_bytes(1, 'big')
        ).digest()
    
    def _ensure_sample_atom(self, atom_id: bytes, sample: int):
        """Ensure sample constant exists"""
        cursor = self.db.cursor()
        
        sample_bytes = struct.pack('B', sample)
        sample_signed = sample - 128  # Convert to signed for projection
        x, y, z, m = self.projector.project_audio_sample(sample_signed, bit_depth=8)
        
        cursor.execute("""
            INSERT INTO atom (atom_id, atom_class, modality, subtype, atomic_value, geom)
            VALUES (%s, 0, 3, 'sample', %s, ST_MakePoint(%s, %s, %s, %s))
            ON CONFLICT (atom_id) DO NOTHING
        """, (atom_id, sample_bytes, float(x), float(y), float(z), float(m)))
        
        self.db.commit()
    
    def _create_waveform_composition(self, rle_atoms: List[Tuple[bytes, int]], metadata: dict) -> bytes:
        """Create waveform composition from RLE atoms"""
        from blake3 import blake3
        
        # Expand RLE for geometry
        atom_ids = []
        m_values = []
        for atom_id, count in rle_atoms:
            atom_ids.append(atom_id)
            m_values.append(count)
        
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
        
        if not coords:
            coords = [(0.0, 0.0, 0.0, 1.0)]
        
        # Create LineString with M values from RLE
        linestring_wkt = 'LINESTRING ZM(' + ','.join(
            f'{x} {y} {z} {m_values[i] if i < len(m_values) else m}'
            for i, (x, y, z, m) in enumerate(coords)
        ) + ')'
        
        # Insert composition
        cursor.execute("""
            INSERT INTO atom (atom_id, atom_class, modality, subtype, geom, metadata)
            VALUES (%s, 1, 3, 'waveform', %s::geometry, %s)
            ON CONFLICT (atom_id) DO NOTHING
        """, (comp_id, linestring_wkt, json.dumps(metadata)))
        
        # Insert composition relationships (batched for performance)
        from psycopg2.extras import execute_values
        
        relationship_rows = [
            (comp_id, atom_id, order)
            for order, (atom_id, _) in enumerate(rle_atoms)
        ]
        
        execute_values(cursor, """
            INSERT INTO atom_compositions (parent_atom_id, component_atom_id, sequence_index)
            VALUES %s
            ON CONFLICT DO NOTHING
        """, relationship_rows)
        
        self.db.commit()
        return comp_id
    
    def reconstruct_wav(self, comp_id: bytes, output_path: Path):
        """Reconstruct WAV file from composition"""
        cursor = self.db.cursor()
        
        # Get metadata
        cursor.execute("""
            SELECT metadata
            FROM atom
            WHERE atom_id = %s
        """, (comp_id,))
        
        metadata = cursor.fetchone()[0]
        
        # Reconstruct samples
        cursor.execute("""
            SELECT a.atomic_value
            FROM atom_compositions c
            JOIN atom a ON a.atom_id = c.component_atom_id
            WHERE c.parent_atom_id = %s
            ORDER BY c.sequence_index
        """, (comp_id,))
        
        samples = []
        for row in cursor.fetchall():
            sample_bytes = bytes(row[0])
            sample = struct.unpack('B', sample_bytes)[0]
            samples.append(sample)
        
        # Convert to 16-bit
        samples_16bit = np.array(samples, dtype=np.uint8)
        samples_16bit = ((samples_16bit.astype(np.float32) * 256) - 32768).astype(np.int16)
        
        # Write WAV
        with wave.open(str(output_path), 'wb') as wav:
            wav.setnchannels(metadata.get('channels', 1))
            wav.setsampwidth(2)  # 16-bit
            wav.setframerate(metadata.get('framerate', 44100))
            wav.writeframes(samples_16bit.tobytes())
