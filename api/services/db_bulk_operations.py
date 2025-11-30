"""
Database Bulk Operations Utility.

Provides reusable, optimized bulk database operations with progress tracking.
Follows SOLID principles with clean separation of concerns.

Design Principles:
------------------
- **Single Responsibility**: Each class/function has one clear purpose
- **Open/Closed**: Extensible for new operations without modifying existing code
- **Liskov Substitution**: BulkCopyOperation can be subclassed for specialized behavior
- **Interface Segregation**: Small, focused interfaces (binary vs text operations)
- **Dependency Inversion**: Depends on abstractions (AsyncConnection, Callable)

Usage Examples:
--------------
Basic atom insertion:
    ```python
    from api.services.db_bulk_operations import bulk_insert_atoms
    
    rows = [(hash1, text1, metadata1), (hash2, text2, metadata2)]
    elapsed, results = await bulk_insert_atoms(conn, rows)
    ```

Custom COPY operation:
    ```python
    from api.services.db_bulk_operations import BulkCopyOperation
    
    operation = BulkCopyOperation(
        conn, 
        "COPY my_table (col1, col2) FROM STDIN",
        chunk_size=10000
    )
    
    # Binary data
    rows = [(val1, val2), (val3, val4)]
    elapsed = await operation.execute_binary(rows, "Inserting data")
    
    # Text/TSV data
    def format_tsv(batch):
        return "\\n".join(f"{r[0]}\\t{r[1]}" for r in batch) + "\\n"
    
    elapsed = await operation.execute_text(rows, format_tsv, "Inserting data")
    ```

Parallel batch processing:
    ```python
    async def insert_batch(batch_data):
        async with pool.connection() as conn:
            await bulk_insert_compositions(
                conn, parent_id, batch_data,
                show_progress=False  # Disable for parallel
            )
    
    tasks = [insert_batch(batch) for batch in batches]
    await asyncio.gather(*tasks)
    ```

Performance Characteristics:
---------------------------
- **Binary COPY**: ~100-200K rows/sec for complex types with bytea
- **TSV COPY**: ~500K-1M rows/sec for simple integer/text types
- **Chunking**: Reduces memory overhead, enables progress tracking
- **Connection pooling**: Enables parallel batch processing

Thread Safety:
-------------
- BulkCopyOperation is NOT thread-safe (uses single connection)
- For parallel execution, create separate operation instances per connection
- Use AsyncConnectionPool for concurrent batch processing
"""

import time
from typing import Any, Callable, List, Optional, Tuple, Union
from psycopg import AsyncConnection
from tqdm.asyncio import tqdm
import logging

logger = logging.getLogger(__name__)


class BulkCopyOperation:
    """
    Generic bulk COPY operation with progress tracking.
    
    Provides a clean, reusable interface for PostgreSQL COPY operations
    with automatic progress bars, chunking, and performance metrics.
    """
    
    def __init__(
        self,
        conn: AsyncConnection,
        copy_statement: str,
        chunk_size: int = 5000,
        show_progress: bool = True
    ):
        """
        Initialize bulk COPY operation.
        
        Args:
            conn: Database connection
            copy_statement: PostgreSQL COPY statement (e.g., "COPY table (...) FROM STDIN")
            chunk_size: Number of rows per batch
            show_progress: Whether to show tqdm progress bar
        """
        self.conn = conn
        self.copy_statement = copy_statement
        self.chunk_size = chunk_size
        self.show_progress = show_progress
        self.start_time = None
        self.total_rows = 0
    
    async def execute_binary(
        self,
        rows: List[Tuple],
        description: str = "COPY operation"
    ) -> float:
        """
        Execute COPY with binary row data (using write_row).
        
        Use this for data with bytea fields or complex types.
        
        Args:
            rows: List of tuples to insert
            description: Description for progress bar
            
        Returns:
            Elapsed time in seconds
        """
        self.total_rows = len(rows)
        self.start_time = time.time()
        
        async with self.conn.cursor() as cur:
            async with cur.copy(self.copy_statement) as copy:
                if self.show_progress:
                    with tqdm(total=self.total_rows, desc=description, unit="rows", ncols=100) as pbar:
                        await self._write_binary_with_progress(copy, rows, pbar)
                else:
                    await self._write_binary_no_progress(copy, rows)
        
        return time.time() - self.start_time
    
    async def execute_text(
        self,
        rows: List[Any],
        formatter: Callable[[List[Any]], str],
        description: str = "COPY operation"
    ) -> float:
        """
        Execute COPY with text/TSV data (using write).
        
        Use this for simple data types that can be TSV-formatted.
        
        Args:
            rows: List of row data to insert
            formatter: Function that converts a batch of rows to TSV string
            description: Description for progress bar
            
        Returns:
            Elapsed time in seconds
        """
        self.total_rows = len(rows)
        self.start_time = time.time()
        
        async with self.conn.cursor() as cur:
            async with cur.copy(self.copy_statement) as copy:
                if self.show_progress:
                    with tqdm(total=self.total_rows, desc=description, unit="rows", ncols=100) as pbar:
                        await self._write_text_with_progress(copy, rows, formatter, pbar)
                else:
                    await self._write_text_no_progress(copy, rows, formatter)
        
        return time.time() - self.start_time
    
    async def _write_binary_with_progress(self, copy, rows: List[Tuple], pbar):
        """Write binary rows with progress updates."""
        for i in range(0, len(rows), self.chunk_size):
            batch_end = min(i + self.chunk_size, len(rows))
            batch = rows[i:batch_end]
            
            for row in batch:
                await copy.write_row(row)
            
            pbar.update(len(batch))
    
    async def _write_binary_no_progress(self, copy, rows: List[Tuple]):
        """Write binary rows without progress tracking."""
        for row in rows:
            await copy.write_row(row)
    
    async def _write_text_with_progress(self, copy, rows: List[Any], formatter: Callable, pbar):
        """Write text rows with progress updates."""
        for i in range(0, len(rows), self.chunk_size):
            batch_end = min(i + self.chunk_size, len(rows))
            batch = rows[i:batch_end]
            
            formatted = formatter(batch)
            await copy.write(formatted)
            
            pbar.update(len(batch))
    
    async def _write_text_no_progress(self, copy, rows: List[Any], formatter: Callable):
        """Write text rows without progress tracking."""
        for i in range(0, len(rows), self.chunk_size):
            batch_end = min(i + self.chunk_size, len(rows))
            batch = rows[i:batch_end]
            
            formatted = formatter(batch)
            await copy.write(formatted)
    
    def get_stats(self) -> dict:
        """Get performance statistics for the operation."""
        if self.start_time is None:
            return {}
        
        elapsed = time.time() - self.start_time
        rate = self.total_rows / elapsed if elapsed > 0 else 0
        
        return {
            'total_rows': self.total_rows,
            'elapsed_seconds': elapsed,
            'rows_per_second': rate,
            'chunk_size': self.chunk_size
        }


# Convenience functions for common patterns

async def bulk_insert_atoms(
    conn: AsyncConnection,
    rows: List[Tuple],
    include_spatial: bool = False,
    chunk_size: int = 5000,
    show_progress: bool = True
) -> Tuple[float, List[Tuple]]:
    """
    Bulk insert atoms using COPY.
    
    Args:
        conn: Database connection
        rows: List of tuples (content_hash, canonical_text, metadata[, spatial_key])
        include_spatial: Whether rows include spatial_key column
        chunk_size: Rows per batch
        show_progress: Show progress bar
        
    Returns:
        Tuple of (elapsed_time, results) where results is list of (content_hash, atom_id)
    """
    if include_spatial:
        copy_stmt = "COPY atom (content_hash, canonical_text, metadata, spatial_key) FROM STDIN"
    else:
        copy_stmt = "COPY atom (content_hash, canonical_text, metadata) FROM STDIN"
    
    operation = BulkCopyOperation(conn, copy_stmt, chunk_size, show_progress)
    elapsed = await operation.execute_binary(rows, f"Inserting {len(rows):,} atoms")
    
    # Retrieve inserted atom IDs
    async with conn.cursor() as cur:
        hashes = [row[0] for row in rows]
        await cur.execute(
            "SELECT content_hash, atom_id FROM atom WHERE content_hash = ANY(%s::bytea[])",
            (hashes,)
        )
        results = await cur.fetchall()
    
    return elapsed, results


async def bulk_insert_compositions(
    conn: AsyncConnection,
    parent_id: int,
    component_ids: List[int],
    sequence_indices: List[int],
    chunk_size: int = 100000,
    show_progress: bool = True
) -> float:
    """
    Bulk insert compositions using COPY with TSV formatting.
    
    Args:
        conn: Database connection
        parent_id: Parent atom ID
        component_ids: List of component atom IDs
        sequence_indices: List of sequence indices
        chunk_size: Rows per batch
        show_progress: Show progress bar
        
    Returns:
        Elapsed time in seconds
    """
    copy_stmt = "COPY atom_composition (parent_atom_id, component_atom_id, sequence_index) FROM STDIN"
    
    def format_tsv(batch: List[Tuple[int, int]]) -> str:
        """Format batch of (component_id, sequence_idx) as TSV."""
        parent_str = str(parent_id)
        lines = [f"{parent_str}\t{comp_id}\t{seq_idx}\n" 
                 for comp_id, seq_idx in batch]
        return "".join(lines)
    
    # Prepare row data
    rows = list(zip(component_ids, sequence_indices))
    
    operation = BulkCopyOperation(conn, copy_stmt, chunk_size, show_progress)
    elapsed = await operation.execute_text(rows, format_tsv, f"Inserting {len(rows):,} compositions")
    
    return elapsed


async def bulk_retrieve_by_hashes(
    conn: AsyncConnection,
    content_hashes: List[bytes],
    table: str = "atom",
    batch_size: int = 50000
) -> List[Tuple]:
    """
    Retrieve records by content hashes in batches.
    
    Handles large hash lists by batching queries to avoid query size limits.
    
    Args:
        conn: Database connection
        content_hashes: List of content hashes
        table: Table name to query
        batch_size: Number of hashes per query batch
        
    Returns:
        List of (content_hash, atom_id) tuples
    """
    results = []
    
    for i in range(0, len(content_hashes), batch_size):
        batch_end = min(i + batch_size, len(content_hashes))
        batch = content_hashes[i:batch_end]
        
        async with conn.cursor() as cur:
            await cur.execute(
                f"SELECT content_hash, atom_id FROM {table} WHERE content_hash = ANY(%s::bytea[])",
                (batch,)
            )
            batch_results = await cur.fetchall()
            results.extend(batch_results)
    
    return results
