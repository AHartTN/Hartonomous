"""
Tensor Reconstruction - Query compositions and rebuild tensors.

Proves atomization works: atom_composition → reconstructed tensor.
"""

import logging
import numpy as np
from typing import Tuple
from psycopg import AsyncConnection
import json

logger = logging.getLogger(__name__)


class TensorReconstructor:
    """Reconstruct tensors from atom compositions."""
    
    async def reconstruct_tensor(
        self,
        conn: AsyncConnection,
        tensor_atom_id: int
    ) -> Tuple[np.ndarray, dict]:
        """
        Reconstruct a tensor from its atom compositions.
        
        Args:
            conn: Database connection
            tensor_atom_id: Atom ID of the tensor
            
        Returns:
            Tuple of (reconstructed_tensor, metadata)
        """
        logger.info(f"\n{'='*80}")
        logger.info(f"Reconstructing tensor from atom_id={tensor_atom_id}")
        
        # Step 1: Get tensor metadata (shape, dtype, etc.)
        async with conn.cursor() as cur:
            await cur.execute(
                "SELECT metadata FROM atom WHERE atom_id = %s",
                (tensor_atom_id,)
            )
            result = await cur.fetchone()
            if not result:
                raise ValueError(f"Tensor atom {tensor_atom_id} not found")
            
            metadata = result[0]
            shape = tuple(metadata['shape'])
            dtype = np.dtype(metadata['dtype'])
            
            logger.info(f"  Tensor metadata:")
            logger.info(f"    Shape: {shape}")
            logger.info(f"    Dtype: {dtype}")
            logger.info(f"    Total elements: {metadata['total_elements']:,}")
        
        # Step 2: Query compositions with geometry
        async with conn.cursor() as cur:
            await cur.execute(
                """
                SELECT 
                    component_atom_id,
                    sequence_index,
                    ST_AsText(spatial_key) as geometry_wkt,
                    metadata
                FROM atom_composition
                WHERE parent_atom_id = %s
                ORDER BY sequence_index
                """,
                (tensor_atom_id,)
            )
            compositions = await cur.fetchall()
            
            logger.info(f"  Retrieved {len(compositions):,} compositions")
        
        # Step 3: Initialize tensor with zeros
        tensor = np.zeros(shape, dtype=dtype)
        
        # Step 4: Decode geometry and fill tensor
        from .hilbert_encoder import HilbertDecoder
        
        # Get bits from metadata (or calculate from shape)
        bits_needed = max(1, int(np.ceil(np.log2(max(shape)))))
        decoder = HilbertDecoder(bits=bits_needed)
        
        total_positions_filled = 0
        
        # Batch fetch all weight values once
        weight_map = {}
        unique_atom_ids = list(set(comp[0] for comp in compositions))
        async with conn.cursor() as cur:
            await cur.execute(
                "SELECT atom_id, canonical_text FROM atom WHERE atom_id = ANY(%s)",
                (unique_atom_ids,)
            )
            for atom_id, text in await cur.fetchall():
                weight_map[atom_id] = float(text)
        
        logger.info(f"  Loaded {len(weight_map):,} unique weight values")
        
        for component_atom_id, seq_idx, geom_wkt, comp_metadata in compositions:
            weight_value = weight_map[component_atom_id]
            
            # Parse geometry (POINTZM or LINESTRINGZM)
            if geom_wkt.startswith("POINT"):
                # Single position: POINTZM(x y z hilbert_idx)
                coords = geom_wkt.replace("POINTZM(", "").replace("POINT ZM (", "").replace(")", "").split()
                hilbert_idx = int(float(coords[3]))  # M coordinate is 4th value
                
                # Decode Hilbert index to (i, j, layer)
                positions = decoder.decode_batch(np.array([hilbert_idx]))
                i, j, layer = positions[0]
                
                # Fill tensor
                if len(shape) == 1:
                    tensor[i] = weight_value
                elif len(shape) == 2:
                    tensor[i, j] = weight_value
                else:
                    tensor[i, j, layer] = weight_value
                
                total_positions_filled += 1
                
            elif geom_wkt.startswith("LINESTRING"):
                # RLE run: LINESTRINGZM(x y z start, x y z end)
                coords = geom_wkt.replace("LINESTRINGZM(", "").replace("LINESTRING ZM (", "").replace(")", "").split(", ")
                start_coords = coords[0].split()
                end_coords = coords[1].split()
                hilbert_start = int(float(start_coords[3]))  # M coordinate
                hilbert_end = int(float(end_coords[3]))  # M coordinate
                
                # Decode all Hilbert indices in range
                hilbert_range = np.arange(hilbert_start, hilbert_end + 1)
                positions = decoder.decode_batch(hilbert_range)
                
                # Fill tensor for all positions
                for pos in positions:
                    i, j, layer = pos
                    if len(shape) == 1:
                        tensor[i] = weight_value
                    elif len(shape) == 2:
                        tensor[i, j] = weight_value
                    else:
                        tensor[i, j, layer] = weight_value
                
                total_positions_filled += len(positions)
        
        logger.info(f"  ✓ Filled {total_positions_filled:,} tensor positions")
        logger.info(f"  Non-zero elements: {np.count_nonzero(tensor):,}")
        logger.info(f"{'='*80}\n")
        
        return tensor, metadata
    
    async def verify_reconstruction(
        self,
        conn: AsyncConnection,
        tensor_atom_id: int,
        original_tensor: np.ndarray
    ) -> bool:
        """
        Verify reconstructed tensor matches original.
        
        Args:
            conn: Database connection
            tensor_atom_id: Atom ID of the tensor
            original_tensor: Original tensor to compare against
            
        Returns:
            True if bit-perfect match
        """
        reconstructed, metadata = await self.reconstruct_tensor(conn, tensor_atom_id)
        
        # Check shape
        if reconstructed.shape != original_tensor.shape:
            logger.error(f"❌ Shape mismatch: {reconstructed.shape} != {original_tensor.shape}")
            return False
        
        # Check values (bit-perfect for integers, close for floats)
        if np.issubdtype(original_tensor.dtype, np.integer):
            match = np.array_equal(reconstructed, original_tensor)
        else:
            match = np.allclose(reconstructed, original_tensor, atol=0, rtol=0)
        
        if match:
            logger.info(f"✅ Reconstruction verified: bit-perfect match")
        else:
            diff_count = np.sum(reconstructed != original_tensor)
            logger.error(f"❌ Reconstruction failed: {diff_count:,} mismatches")
            
            # Show first few mismatches
            diff_indices = np.where(reconstructed != original_tensor)
            for idx in zip(*diff_indices[:5]):
                orig_val = original_tensor[idx]
                recon_val = reconstructed[idx]
                logger.error(f"  Position {idx}: original={orig_val}, reconstructed={recon_val}")
        
        return match
