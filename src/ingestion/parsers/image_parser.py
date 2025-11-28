"""Image parser - handles images with pixel-level atomization."""

import numpy as np
from typing import Dict, Any, Iterator
from pathlib import Path
from PIL import Image

from ...core.atomization import BaseAtomizer, ModalityType
from ...core.landmark import LandmarkProjector


class ImageParser(BaseAtomizer):
    """Parse and atomize images."""
    
    def __init__(self):
        super().__init__()
        self.landmark_projector = LandmarkProjector()
    
    async def parse(self, image_path: Path, conn) -> int:
        """Parse image file into atoms. Returns parent atom_id."""
        img = Image.open(image_path)
        img_array = np.array(img)
        
        if img_array.dtype != np.float64:
            img_array = img_array.astype(np.float64) / 255.0
        
        # Create parent atom for image
        import hashlib
        image_hash = hashlib.sha256(img_array.tobytes()).digest()
        parent_atom_id = await self.create_atom(
            conn,
            image_hash,
            str(image_path),
            {
                'modality': 'image',
                'shape': list(img_array.shape),
                'image_path': str(image_path)
            }
        )
        
        # Atomize pixels as components
        flat_pixels = img_array.flatten()
        chunk_size = 12  # 48 bytes / 4 bytes per float32
        
        for idx in range(0, len(flat_pixels), chunk_size):
            chunk = flat_pixels[idx:idx+chunk_size]
            
            if np.abs(chunk).max() < self.threshold:
                continue
            
            chunk_bytes = chunk.astype(np.float32).tobytes()
            component_id = await self.create_atom(conn, chunk_bytes, None, {'dtype': 'float32'})
            await self.create_composition(conn, parent_atom_id, component_id, idx // chunk_size)
        
        return parent_atom_id
