"""
Composition Builder - Efficient bulk composition creation.

Single Responsibility: Create atom compositions with optimal performance.
"""

import asyncio
import logging
import time
from typing import List, Dict
from psycopg import AsyncConnection
from psycopg_pool import AsyncConnectionPool

from api.services.db_bulk_operations import bulk_insert_compositions
from api.config import settings

logger = logging.getLogger(__name__)


class CompositionBuilder:
    """Handles efficient creation of atom compositions."""
    
    async def create_batch_with_geometry(
        self,
        conn: AsyncConnection,
        composition_rows: List[tuple]
    ) -> float:
        """
        Create compositions with spatial geometry (POINT/LINESTRING).
        Uses batched INSERT with ST_GeomFromText for PostGIS geometry.
        
        Args:
            conn: Database connection
            composition_rows: List of (parent_id, component_id, sequence_idx, geometry_wkt, metadata_json)
            
        Returns:
            Elapsed time in seconds
        """
        start_time = time.time()
        
        if not composition_rows:
            return 0.0
        
        logger.info(f"    Inserting {len(composition_rows):,} compositions with geometry...")
        
        # Process in batches to avoid parameter limits
        BATCH_SIZE = 10000
        async with conn.cursor() as cur:
            for batch_start in range(0, len(composition_rows), BATCH_SIZE):
                batch_end = min(batch_start + BATCH_SIZE, len(composition_rows))
                batch = composition_rows[batch_start:batch_end]
                
                # Build parameterized INSERT with ST_GeomFromText
                placeholders = []
                params = []
                for row in batch:
                    parent_id, component_id, seq_idx, geom_wkt, metadata = row
                    placeholders.append("(%s, %s, %s, ST_GeomFromText(%s), %s::jsonb)")
                    # Use empty JSON if metadata is None
                    metadata_value = metadata if metadata is not None else '{}'
                    params.extend([parent_id, component_id, seq_idx, geom_wkt, metadata_value])
                
                insert_sql = f"""
                    INSERT INTO atom_composition 
                    (parent_atom_id, component_atom_id, sequence_index, spatial_key, metadata)
                    VALUES {','.join(placeholders)}
                """
                await cur.execute(insert_sql, params)
                
                if batch_end < len(composition_rows):
                    logger.info(f"    Progress: {batch_end:,}/{len(composition_rows):,} rows inserted...")
        
        elapsed = time.time() - start_time
        logger.info(f"    ✓ Geometry INSERT complete: {len(composition_rows):,} rows in {elapsed:.2f}s ({len(composition_rows)/elapsed:,.0f} rows/s)")
        
        return elapsed
    
    async def create_batch_sequential(
        self,
        conn: AsyncConnection,
        parent_id: int,
        component_ids,  # Accept NumPy array or list
        sequence_indices  # Accept NumPy array or list
    ) -> float:
        """
        Create compositions sequentially with progress bar.
        Accepts NumPy arrays directly (columnar design).
        
        Args:
            conn: Database connection
            parent_id: Parent atom ID
            component_ids: NumPy array or list of component atom IDs
            sequence_indices: NumPy array or list of sequence indices
            
        Returns:
            Elapsed time in seconds
        """
        import numpy as np
        
        # Convert NumPy arrays to lists if needed (bulk_insert_compositions handles both)
        if isinstance(component_ids, np.ndarray):
            component_ids_list = component_ids.tolist()
        else:
            component_ids_list = component_ids
            
        if isinstance(sequence_indices, np.ndarray):
            sequence_indices_list = sequence_indices.tolist()
        else:
            sequence_indices_list = sequence_indices
        
        copy_time = await bulk_insert_compositions(
            conn,
            parent_id,
            component_ids_list,
            sequence_indices_list,
            chunk_size=100000,
            show_progress=True
        )
        
        total_rows = len(component_ids_list)
        logger.info(
            f"    ✓ Composition COPY complete: {total_rows:,} rows in {copy_time:.2f}s "
            f"({total_rows/copy_time:,.0f} rows/s)"
        )
        
        return copy_time
    
    async def create_batch_parallel(
        self,
        parent_id: int,
        compositions: List[Dict[str, int]],
        pool: AsyncConnectionPool
    ) -> float:
        """
        Create compositions in parallel batches for maximum throughput.
        
        Args:
            parent_id: Parent atom ID
            compositions: List of composition dicts with component_id and sequence_idx
            pool: Connection pool for parallel execution
            
        Returns:
            Total elapsed time in seconds
        """
        logger.info(f"    Creating {len(compositions):,} compositions in parallel batches...")
        total_start = time.time()
        
        batch_size = 50000  # ~400KB per batch
        CONCURRENT_BATCHES = settings.pool_num_workers
        
        async def insert_batch(batch_idx: int, start: int, end: int):
            """Insert a single batch using pooled connection."""
            batch = compositions[start:end]
            component_ids = [comp['component_id'] for comp in batch]
            sequence_indices = [comp['sequence_idx'] for comp in batch]
            
            async with pool.connection() as batch_conn:
                await bulk_insert_compositions(
                    batch_conn,
                    parent_id,
                    component_ids,
                    sequence_indices,
                    chunk_size=10000,
                    show_progress=False
                )
            return batch_idx, end - start
        
        # Process batches in concurrent groups
        tasks = []
        for batch_idx, i in enumerate(range(0, len(compositions), batch_size)):
            batch_end = min(i + batch_size, len(compositions))
            tasks.append(insert_batch(batch_idx, i, batch_end))
            
            # Process group when full or at end
            if len(tasks) >= CONCURRENT_BATCHES or batch_end >= len(compositions):
                results = await asyncio.gather(*tasks)
                
                # Progress reporting
                total_inserted = batch_end
                progress_pct = (total_inserted / len(compositions)) * 100
                elapsed = time.time() - total_start
                rate = total_inserted / elapsed if elapsed > 0 else 0
                eta = (len(compositions) - total_inserted) / rate if rate > 0 else 0
                
                logger.info(
                    f"    [{progress_pct:5.1f}%] Completed {len(results)} batches: "
                    f"Inserted {total_inserted:,}/{len(compositions):,} "
                    f"({rate:.0f} comps/s, ETA: {eta:.0f}s)"
                )
                tasks = []
        
        total_elapsed = time.time() - total_start
        logger.info(
            f"    ✓ All compositions created: {len(compositions):,} in {total_elapsed:.2f}s "
            f"({len(compositions)/total_elapsed:,.0f} comps/s)"
        )
        
        return total_elapsed
