"""Image parser - handles images with pixel-level atomization."""

import hashlib
from pathlib import Path
from typing import Any, Dict

import numpy as np
from PIL import Image

from src.core.atomization import BaseAtomizer


class ImageParser(BaseAtomizer):
    """Parse and atomize images via SQL functions."""

    async def parse(self, image_path: Path, conn) -> int:
        """
        Parse image file into atoms.

        Process:
        1. Load image via PIL
        2. Convert to numpy array (RGB float32)
        3. Create parent atom for image
        4. Chunk pixels and atomize via SQL
        5. Build composition with sequence_index

        Returns parent atom_id.
        """
        # Load and normalize image
        img = Image.open(image_path)
        if img.mode != "RGB":
            img = img.convert("RGB")

        img_array = np.array(img, dtype=np.float32) / 255.0

        # Create parent atom for full image
        image_hash = hashlib.sha256(img_array.tobytes()).digest()
        parent_atom_id = await self.create_atom(
            conn,
            image_hash,
            str(image_path.name),
            {
                "modality": "image",
                "width": img_array.shape[1],
                "height": img_array.shape[0],
                "channels": img_array.shape[2],
                "file_path": str(image_path),
            },
        )

        # Flatten to RGB triples
        pixels = img_array.reshape(-1, 3)

        # Chunk pixels (12 bytes per pixel RGB float32 = 4 chunks/atom at 48 byte limit)
        chunk_size = 4  # 4 pixels = 48 bytes

        self.stats["total_processed"] = len(pixels)

        # Collect all non-sparse chunks first
        component_ids = []
        sequence_indices = []
        
        for idx in range(0, len(pixels), chunk_size):
            chunk = pixels[idx : idx + chunk_size].flatten()

            # Sparse: skip near-black pixels
            if np.abs(chunk).max() < self.threshold:
                self.stats["sparse_skipped"] += 1
                continue

            chunk_bytes = chunk.astype(np.float32).tobytes()

            # Check size constraint
            if len(chunk_bytes) > 64:
                raise ValueError(f"Chunk exceeds 64 bytes: {len(chunk_bytes)}")

            component_id = await self.create_atom(
                conn, chunk_bytes, None, {"dtype": "float32", "channels": 3}
            )
            component_ids.append(component_id)
            sequence_indices.append(idx // chunk_size)
            self.stats["atoms_created"] += 1

        # Batch create all compositions at once
        if component_ids:
            await self.create_compositions_batch(
                conn, parent_atom_id, component_ids, sequence_indices
            )

        return parent_atom_id
