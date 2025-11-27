"""
Atomization engine - breaks down all data into atomic units (≤64 bytes).
Handles all modalities: models, images, text, audio, video, code, structured data.
"""

import numpy as np
from typing import Any, Dict, List, Tuple, Optional, Union
from dataclasses import dataclass
from enum import IntEnum
import hashlib

from .compression import AtomCompressor, CompressionResult


class ModalityType(IntEnum):
    """Data modality types."""
    UNKNOWN = 0
    MODEL_WEIGHT = 1
    MODEL_BIAS = 2
    MODEL_ACTIVATION = 3
    IMAGE_PIXEL = 10
    IMAGE_FEATURE = 11
    TEXT_TOKEN = 20
    TEXT_EMBEDDING = 21
    AUDIO_SAMPLE = 30
    AUDIO_FEATURE = 31
    VIDEO_FRAME = 40
    CODE_TOKEN = 50
    CODE_AST = 51
    STRUCTURED_FIELD = 60


@dataclass
class Atom:
    """
    Atomic data unit - fundamental storage unit.
    Maximum 64 bytes total.
    """
    atom_id: bytes  # 16-byte hash
    modality: ModalityType
    data: bytes  # Compressed data
    compression_type: int
    metadata: Dict[str, Any]
    
    def __post_init__(self):
        """Validate atom size constraint."""
        total_size = len(self.atom_id) + 1 + len(self.data) + 1  # id + modality + data + compression
        if total_size > 64:
            raise ValueError(f"Atom exceeds 64 bytes: {total_size} bytes")
    
    @property
    def content_hash(self) -> str:
        """Content-addressable hash."""
        return self.atom_id.hex()


class Atomizer:
    """
    Main atomization engine.
    Breaks down any data into ≤64 byte atoms with deduplication.
    """
    
    def __init__(self, sparse_threshold: float = 1e-6):
        self.compressor = AtomCompressor()
        self.sparse_threshold = sparse_threshold
        self.atom_cache: Dict[bytes, Atom] = {}  # Deduplication cache
    
    def _generate_atom_id(self, data: bytes, modality: ModalityType) -> bytes:
        """Generate content-addressable atom ID."""
        hasher = hashlib.blake2b(digest_size=16)
        hasher.update(data)
        hasher.update(modality.to_bytes(1, 'big'))
        return hasher.digest()
    
    def atomize_array(
        self,
        data: np.ndarray,
        modality: ModalityType,
        chunk_size: Optional[int] = None
    ) -> List[Atom]:
        """
        Atomize numpy array into atoms.
        Automatically chunks if needed to stay within 64-byte limit.
        """
        if data.size == 0:
            return []
        
        # Flatten for processing
        flat_data = data.flatten()
        
        # Determine optimal chunk size if not specified
        if chunk_size is None:
            # Estimate based on dtype - aim for ~48 bytes of data per atom (leaving room for metadata)
            max_data_bytes = 48
            chunk_size = max(1, max_data_bytes // data.dtype.itemsize)
        
        atoms = []
        
        for i in range(0, len(flat_data), chunk_size):
            chunk = flat_data[i:i+chunk_size]
            
            # Compress chunk
            compressed = self.compressor.compress(chunk, self.sparse_threshold)
            
            # Further split if still too large
            if compressed.compressed_size > 48:
                # Recursive split
                smaller_atoms = self.atomize_array(
                    chunk,
                    modality,
                    chunk_size=max(1, chunk_size // 2)
                )
                atoms.extend(smaller_atoms)
                continue
            
            # Generate atom ID
            atom_id = self._generate_atom_id(compressed.data, modality)
            
            # Check deduplication cache
            if atom_id in self.atom_cache:
                atoms.append(self.atom_cache[atom_id])
                continue
            
            # Create atom
            atom = Atom(
                atom_id=atom_id,
                modality=modality,
                data=compressed.data,
                compression_type=compressed.compression_type,
                metadata={
                    'dtype': str(data.dtype),
                    'original_shape': chunk.shape,
                    'compression_ratio': compressed.ratio,
                    'index_range': (i, min(i + chunk_size, len(flat_data)))
                }
            )
            
            self.atom_cache[atom_id] = atom
            atoms.append(atom)
        
        return atoms
    
    def atomize_model_weights(
        self,
        weights: Dict[str, np.ndarray],
        layer_name: str
    ) -> List[Tuple[str, List[Atom]]]:
        """
        Atomize model weights/parameters.
        Returns [(parameter_name, atoms)] for each parameter.
        """
        result = []
        
        for param_name, param_data in weights.items():
            # Determine modality based on parameter type
            if 'bias' in param_name.lower():
                modality = ModalityType.MODEL_BIAS
            elif 'weight' in param_name.lower():
                modality = ModalityType.MODEL_WEIGHT
            else:
                modality = ModalityType.MODEL_WEIGHT
            
            atoms = self.atomize_array(param_data, modality)
            result.append((f"{layer_name}.{param_name}", atoms))
        
        return result
    
    def atomize_image(self, image: np.ndarray) -> List[Atom]:
        """
        Atomize image data.
        Expects shape (H, W, C) or (H, W).
        """
        return self.atomize_array(image, ModalityType.IMAGE_PIXEL)
    
    def atomize_text_embeddings(self, embeddings: np.ndarray) -> List[Atom]:
        """Atomize text embeddings."""
        return self.atomize_array(embeddings, ModalityType.TEXT_EMBEDDING)
    
    def atomize_audio(self, audio: np.ndarray, sample_rate: int) -> List[Atom]:
        """
        Atomize audio samples.
        audio: 1D or 2D array of audio samples
        """
        atoms = self.atomize_array(audio, ModalityType.AUDIO_SAMPLE)
        
        # Add sample rate to metadata
        for atom in atoms:
            atom.metadata['sample_rate'] = sample_rate
        
        return atoms
    
    def reassemble_from_atoms(
        self,
        atoms: List[Atom],
        target_shape: Optional[Tuple] = None
    ) -> np.ndarray:
        """
        Reassemble atoms back into original array.
        Uses metadata to reconstruct proper shape and order.
        """
        if not atoms:
            return np.array([])
        
        # Sort by index range
        sorted_atoms = sorted(atoms, key=lambda a: a.metadata.get('index_range', (0, 0))[0])
        
        # Decompress each atom
        chunks = []
        for atom in sorted_atoms:
            compressed = CompressionResult(
                data=atom.data,
                compression_type=atom.compression_type,
                metadata=atom.metadata,
                original_size=0,  # Not needed for decompression
                compressed_size=len(atom.data)
            )
            chunk = self.compressor.decompress(compressed)
            chunks.append(chunk)
        
        # Concatenate chunks
        result = np.concatenate(chunks)
        
        # Reshape if target shape provided
        if target_shape is not None:
            result = result.reshape(target_shape)
        
        return result
    
    def get_deduplication_stats(self) -> Dict[str, int]:
        """Get statistics on atom deduplication."""
        return {
            'unique_atoms': len(self.atom_cache),
            'total_bytes': sum(len(atom.data) for atom in self.atom_cache.values())
        }
    
    def clear_cache(self):
        """Clear deduplication cache."""
        self.atom_cache.clear()
