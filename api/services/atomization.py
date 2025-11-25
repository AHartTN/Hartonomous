"""
Atomization service for converting content to atoms.

Wraps SQL function calls with Python convenience methods.

Copyright © 2025 Anthony Hart. All Rights Reserved.
"""

import logging
from typing import Dict, Any, Optional
from psycopg import AsyncConnection

logger = logging.getLogger(__name__)


class AtomizationService:
    """Service for atomizing content into the database."""
    
    @staticmethod
    async def atomize_text(
        conn: AsyncConnection,
        text: str,
        metadata: Optional[Dict[str, Any]] = None
    ) -> Dict[str, Any]:
        """
        Atomize text content.
        
        Args:
            conn: Database connection
            text: Text to atomize
            metadata: Optional metadata
        
        Returns:
            dict: {atom_count: int, root_atom_id: int}
        """
        try:
            async with conn.cursor() as cur:
                # Call atomize_text SQL function
                await cur.execute(
                    "SELECT atomize_text(%s, %s);",
                    (text, metadata)
                )
                
                result = await cur.fetchone()
                root_atom_id = result[0] if result else None
                
                # Count created atoms (approximate - count characters)
                atom_count = len(text)
                
                logger.info(
                    f"Atomized text: {atom_count} atoms, "
                    f"root_id={root_atom_id}"
                )
                
                return {
                    "atom_count": atom_count,
                    "root_atom_id": root_atom_id
                }
        
        except Exception as e:
            logger.error(f"Text atomization failed: {e}", exc_info=True)
            raise
    
    @staticmethod
    async def atomize_image(
        conn: AsyncConnection,
        image_data: bytes,
        width: int,
        height: int,
        metadata: Optional[Dict[str, Any]] = None
    ) -> Dict[str, Any]:
        """
        Atomize image content.
        
        Args:
            conn: Database connection
            image_data: Image bytes
            width: Image width
            height: Image height
            metadata: Optional metadata
        
        Returns:
            dict: {atom_count: int, root_atom_id: int}
        """
        try:
            # Decode image to pixels
            import numpy as np
            from io import BytesIO
            from PIL import Image
            
            # Load image
            img = Image.open(BytesIO(image_data))
            img = img.convert('RGB')  # Ensure RGB
            pixels = np.array(img)
            
            # Validate dimensions
            if pixels.shape[:2] != (height, width):
                raise ValueError(
                    f"Image dimensions mismatch: "
                    f"expected {width}x{height}, got {pixels.shape[1]}x{pixels.shape[0]}"
                )
            
            # Flatten to list of (r, g, b, x, y) tuples
            pixel_data = []
            for y in range(height):
                for x in range(width):
                    r, g, b = pixels[y, x]
                    pixel_data.append((int(r), int(g), int(b), x, y))
            
            async with conn.cursor() as cur:
                # Call atomize_image_vectorized SQL function
                # Convert to PostgreSQL array format
                await cur.execute("""
                    SELECT atomize_image_vectorized(
                        %s::pixel_data[]
                    );
                """, (pixel_data,))
                
                result = await cur.fetchone()
                root_atom_id = result[0] if result else None
                
                atom_count = len(pixel_data)
                
                logger.info(
                    f"Atomized image: {width}x{height} = {atom_count} pixels, "
                    f"root_id={root_atom_id}"
                )
                
                return {
                    "atom_count": atom_count,
                    "root_atom_id": root_atom_id
                }
        
        except Exception as e:
            logger.error(f"Image atomization failed: {e}", exc_info=True)
            raise
    
    @staticmethod
    async def atomize_audio(
        conn: AsyncConnection,
        audio_data: bytes,
        sample_rate: int,
        channels: int,
        metadata: Optional[Dict[str, Any]] = None
    ) -> Dict[str, Any]:
        """
        Atomize audio content.
        
        Args:
            conn: Database connection
            audio_data: Audio bytes
            sample_rate: Sample rate (Hz)
            channels: Number of channels
            metadata: Optional metadata
        
        Returns:
            dict: {atom_count: int, root_atom_id: int}
        """
        try:
            import numpy as np
            from io import BytesIO
            import wave
            
            # Parse WAV file
            with wave.open(BytesIO(audio_data), 'rb') as wav:
                n_frames = wav.getnframes()
                audio_bytes = wav.readframes(n_frames)
                
                # Convert to numpy array
                if wav.getsampwidth() == 2:  # 16-bit
                    samples = np.frombuffer(audio_bytes, dtype=np.int16)
                else:
                    raise ValueError("Only 16-bit audio supported")
                
                # Reshape for channels
                if channels > 1:
                    samples = samples.reshape(-1, channels)
            
            # Create sample data (time, amplitude) tuples
            sample_data = []
            for i, sample in enumerate(samples):
                time_ms = int((i / sample_rate) * 1000)
                amplitude = int(sample) if channels == 1 else int(sample[0])
                sample_data.append((time_ms, amplitude))
            
            async with conn.cursor() as cur:
                # Call atomize_audio_sparse SQL function
                # Use sparse encoding (only significant samples)
                await cur.execute("""
                    SELECT atomize_audio_sparse(
                        %s::audio_sample[]
                    );
                """, (sample_data,))
                
                result = await cur.fetchone()
                root_atom_id = result[0] if result else None
                
                atom_count = len(sample_data)
                
                logger.info(
                    f"Atomized audio: {sample_rate}Hz x {channels}ch = "
                    f"{atom_count} samples, root_id={root_atom_id}"
                )
                
                return {
                    "atom_count": atom_count,
                    "root_atom_id": root_atom_id
                }
        
        except Exception as e:
            logger.error(f"Audio atomization failed: {e}", exc_info=True)
            raise


__all__ = ["AtomizationService"]
