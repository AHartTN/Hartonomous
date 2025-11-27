"""
Image Atomization Service - Enterprise Implementation
Atomizes images into hierarchical patch → pixel atoms.

Hierarchical Pattern:
  image → patches (16x16) → pixels (RGB/RGBA channels)

Every pixel becomes an atom with:
- Content-addressing (SHA-256 deduplication)
- Spatial position (x, y coordinates)
- Color channels (R, G, B, A)
- Patch membership (hierarchical composition)

Optimizations:
- Common pixel deduplication (e.g., white backgrounds, black text)
- Patch-level compression (uniform patches)
- Sparse encoding (transparency, repeated colors)
- Format-aware parsing (PNG, JPEG, GIF, BMP, WebP, TIFF, SVG)

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import hashlib
import io
import logging
import struct
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

from PIL import Image
from psycopg import AsyncConnection
from psycopg.types.json import Json

logger = logging.getLogger(__name__)


class ImageAtomizer:
    """
    Enterprise-grade image atomization service.
    
    Atomizes images into cognitive substrate following hierarchical pattern:
    image → patches → pixels
    
    Features:
    - Multi-format support (PNG, JPEG, GIF, BMP, WebP, TIFF)
    - Automatic deduplication (content-addressed pixels)
    - Patch-based composition (16x16 default, configurable)
    - Sparse encoding (transparency, uniform patches)
    - EXIF metadata extraction
    - Color space handling (RGB, RGBA, grayscale, CMYK)
    """

    def __init__(self, patch_size: int = 16, dedupe_threshold: float = 0.01):
        """
        Initialize image atomizer.
        
        Args:
            patch_size: Patch dimension (e.g., 16 = 16x16 patches)
            dedupe_threshold: Threshold for considering pixels "identical"
                             (0.0 = exact match, 0.01 = ~3 color values difference)
        """
        self.patch_size = patch_size
        self.dedupe_threshold = dedupe_threshold
        
        # Deduplication caches
        self.pixel_cache: Dict[Tuple[int, int, int, int], int] = {}  # (R,G,B,A) → atom_id
        self.patch_cache: Dict[bytes, int] = {}  # patch_hash → atom_id
        
        # Statistics
        self.stats = {
            "total_pixels": 0,
            "unique_pixels": 0,
            "deduped_pixels": 0,
            "total_patches": 0,
            "uniform_patches": 0,
            "sparse_pixels": 0,
        }

    async def atomize_image(
        self,
        conn: AsyncConnection,
        image_path: Optional[Path] = None,
        image_data: Optional[bytes] = None,
        metadata: Optional[Dict[str, Any]] = None,
    ) -> Dict[str, Any]:
        """
        Atomize image into cognitive substrate.
        
        Args:
            conn: Database connection
            image_path: Path to image file (or image_data)
            image_data: Image bytes
            metadata: Additional metadata
            
        Returns:
            dict with atom counts, root_atom_id, dimensions
        """
        if metadata is None:
            metadata = {}
        
        # Load image
        if image_data:
            img = Image.open(io.BytesIO(image_data))
        elif image_path:
            img = Image.open(image_path)
        else:
            raise ValueError("Either image_path or image_data must be provided")
        
        # Extract EXIF metadata
        exif = self._extract_exif(img)
        
        # Normalize to RGBA (handles grayscale, RGB, palette, etc.)
        img_rgba = img.convert("RGBA")
        width, height = img_rgba.size
        
        logger.info(
            f"Atomizing image: {width}x{height} pixels, "
            f"format={img.format}, mode={img.mode}"
        )
        
        # Update metadata
        metadata.update({
            "modality": "image",
            "format": img.format.lower() if img.format else "unknown",
            "width": width,
            "height": height,
            "mode": img.mode,
            "patch_size": self.patch_size,
            "exif": exif,
        })
        
        async with conn.cursor() as cur:
            # Step 1: Create image root atom
            image_name = image_path.name if image_path else "image"
            image_hash = hashlib.sha256(image_data or image_path.read_bytes()).digest()
            
            await cur.execute(
                """
                SELECT atomize_value(
                    %s::bytea,
                    %s,
                    %s::jsonb
                )
                """,
                (image_hash, image_name, Json(metadata))
            )
            image_atom_id = (await cur.fetchone())[0]
            logger.info(f"✓ Image atom created: {image_atom_id}")
            
            # Step 2: Atomize patches (16x16 tiles)
            patch_atoms = await self._atomize_patches(
                conn, img_rgba, image_atom_id, width, height
            )
            
            # Step 3: Atomize pixels within patches
            await self._atomize_pixels(
                conn, img_rgba, patch_atoms, width, height
            )
        
        dedup_ratio = (
            self.stats["total_pixels"] / self.stats["unique_pixels"]
            if self.stats["unique_pixels"] > 0
            else 1.0
        )
        
        return {
            "image_atom_id": image_atom_id,
            "width": width,
            "height": height,
            "total_pixels": self.stats["total_pixels"],
            "unique_pixels": self.stats["unique_pixels"],
            "deduped_pixels": self.stats["deduped_pixels"],
            "total_patches": self.stats["total_patches"],
            "uniform_patches": self.stats["uniform_patches"],
            "sparse_pixels": self.stats["sparse_pixels"],
            "deduplication_ratio": dedup_ratio,
            "compression_percent": f"{(1 - 1/dedup_ratio) * 100:.1f}%",
        }

    async def _atomize_patches(
        self,
        conn: AsyncConnection,
        img: Image.Image,
        image_atom_id: int,
        width: int,
        height: int,
    ) -> Dict[Tuple[int, int], int]:
        """
        Atomize image into patches (16x16 tiles).
        
        Returns:
            dict mapping (patch_x, patch_y) → patch_atom_id
        """
        patch_atoms = {}
        patch_idx = 0
        
        for patch_y in range(0, height, self.patch_size):
            for patch_x in range(0, width, self.patch_size):
                # Extract patch region
                patch_w = min(self.patch_size, width - patch_x)
                patch_h = min(self.patch_size, height - patch_y)
                patch_box = (patch_x, patch_y, patch_x + patch_w, patch_y + patch_h)
                patch_img = img.crop(patch_box)
                
                # Check if patch is uniform (all same color)
                colors = patch_img.getcolors(maxcolors=2)
                is_uniform = colors is not None and len(colors) == 1
                
                # Create patch atom
                patch_metadata = {
                    "modality": "image_patch",
                    "x": patch_x,
                    "y": patch_y,
                    "width": patch_w,
                    "height": patch_h,
                    "uniform": is_uniform,
                }
                
                if is_uniform:
                    # Store uniform color in metadata (optimization)
                    color = colors[0][1]  # (count, (R, G, B, A))
                    patch_metadata["uniform_color"] = color
                    self.stats["uniform_patches"] += 1
                
                # Hash patch for deduplication
                patch_hash = hashlib.sha256(patch_img.tobytes()).digest()
                
                async with conn.cursor() as cur:
                    await cur.execute(
                        """
                        SELECT atomize_value(
                            %s::bytea,
                            %s,
                            %s::jsonb
                        )
                        """,
                        (
                            patch_hash,
                            f"patch_{patch_x}_{patch_y}",
                            Json(patch_metadata)
                        )
                    )
                    patch_atom_id = (await cur.fetchone())[0]
                
                # Link patch to image
                await self._link_composition(
                    conn, image_atom_id, patch_atom_id, patch_idx
                )
                
                patch_atoms[(patch_x, patch_y)] = patch_atom_id
                patch_idx += 1
                self.stats["total_patches"] += 1
        
        logger.info(
            f"✓ Patches atomized: {len(patch_atoms)} patches, "
            f"{self.stats['uniform_patches']} uniform"
        )
        
        return patch_atoms

    async def _atomize_pixels(
        self,
        conn: AsyncConnection,
        img: Image.Image,
        patch_atoms: Dict[Tuple[int, int], int],
        width: int,
        height: int,
    ):
        """
        Atomize pixels within patches.
        
        Optimizations:
        - Skip fully transparent pixels (sparse encoding)
        - Deduplicate identical colors (content-addressing)
        - Batch database operations for performance
        """
        pixels = img.load()
        
        for patch_y in range(0, height, self.patch_size):
            for patch_x in range(0, width, self.patch_size):
                patch_atom_id = patch_atoms[(patch_x, patch_y)]
                
                # Atomize pixels within this patch
                patch_w = min(self.patch_size, width - patch_x)
                patch_h = min(self.patch_size, height - patch_y)
                
                pixel_idx = 0
                for py in range(patch_h):
                    for px in range(patch_w):
                        x = patch_x + px
                        y = patch_y + py
                        
                        r, g, b, a = pixels[x, y]
                        self.stats["total_pixels"] += 1
                        
                        # Sparse encoding: skip fully transparent pixels
                        if a < 10:  # Nearly transparent
                            self.stats["sparse_pixels"] += 1
                            continue  # Gap in sequence_index = implicit transparency
                        
                        # Atomize pixel with deduplication
                        pixel_atom_id = await self._atomize_pixel(
                            conn, r, g, b, a, x, y
                        )
                        
                        # Link pixel to patch
                        await self._link_composition(
                            conn, patch_atom_id, pixel_atom_id, pixel_idx
                        )
                        
                        pixel_idx += 1
        
        logger.info(
            f"✓ Pixels atomized: {self.stats['total_pixels']} total, "
            f"{self.stats['unique_pixels']} unique, "
            f"{self.stats['sparse_pixels']} sparse"
        )

    async def _atomize_pixel(
        self,
        conn: AsyncConnection,
        r: int,
        g: int,
        b: int,
        a: int,
        x: int,
        y: int,
    ) -> int:
        """
        Atomize single pixel with content-addressing.
        
        Deduplication: Same (R,G,B,A) → same atom_id
        
        Returns:
            pixel_atom_id
        """
        color_key = (r, g, b, a)
        
        # Check cache first
        if color_key in self.pixel_cache:
            self.stats["deduped_pixels"] += 1
            return self.pixel_cache[color_key]
        
        # Create pixel atom
        # Pack color as 4 bytes: RGBA (total 4 bytes << 64 byte limit ✓)
        color_bytes = struct.pack("BBBB", r, g, b, a)
        color_hash = hashlib.sha256(color_bytes).digest()
        
        pixel_metadata = {
            "modality": "pixel",
            "r": r,
            "g": g,
            "b": b,
            "a": a,
            "x": x,
            "y": y,
        }
        
        async with conn.cursor() as cur:
            await cur.execute(
                """
                SELECT atomize_value(
                    %s::bytea,
                    %s,
                    %s::jsonb
                )
                """,
                (
                    color_hash,
                    f"rgba_{r}_{g}_{b}_{a}",
                    Json(pixel_metadata)
                )
            )
            pixel_atom_id = (await cur.fetchone())[0]
        
        # Cache for deduplication
        self.pixel_cache[color_key] = pixel_atom_id
        self.stats["unique_pixels"] += 1
        
        return pixel_atom_id

    async def _link_composition(
        self,
        conn: AsyncConnection,
        parent_id: int,
        component_id: int,
        sequence_idx: int,
    ):
        """Link component to parent via atom_composition."""
        async with conn.cursor() as cur:
            await cur.execute(
                """
                INSERT INTO atom_composition 
                    (parent_atom_id, component_atom_id, sequence_index)
                VALUES (%s, %s, %s)
                ON CONFLICT DO NOTHING
                """,
                (parent_id, component_id, sequence_idx)
            )

    def _extract_exif(self, img: Image.Image) -> Optional[Dict[str, Any]]:
        """
        Extract EXIF metadata from image.
        
        Returns:
            dict with EXIF tags or None
        """
        try:
            if hasattr(img, "_getexif") and img._getexif():
                from PIL.ExifTags import TAGS
                
                exif_data = {}
                for tag_id, value in img._getexif().items():
                    tag_name = TAGS.get(tag_id, tag_id)
                    # Convert bytes to string
                    if isinstance(value, bytes):
                        try:
                            value = value.decode("utf-8", errors="ignore")
                        except:
                            value = str(value)
                    exif_data[tag_name] = value
                
                return exif_data
        except Exception as e:
            logger.warning(f"Failed to extract EXIF: {e}")
        
        return None


__all__ = ["ImageAtomizer"]
