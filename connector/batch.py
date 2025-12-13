"""Batch operations for efficient bulk processing"""

from typing import List, Iterator
from dataclasses import dataclass
from .pool import HartonomousPool
from .connector import Atom


@dataclass
class BatchConfig:
    """Configuration for batch operations"""
    batch_size: int = 1000
    max_retries: int = 3
    retry_delay_ms: int = 100


class BatchProcessor:
    """Efficient batch operations on atoms"""
    
    def __init__(self, pool: HartonomousPool, config: BatchConfig = None):
        self.pool = pool
        self.config = config or BatchConfig()
    
    def batch_insert(self, atoms: List[Atom]) -> int:
        """Insert atoms in batches
        
        Args:
            atoms: List of atoms to insert
            
        Returns:
            Number of atoms inserted
        """
        total_inserted = 0
        
        for batch in self._chunk(atoms, self.config.batch_size):
            inserted = self._insert_batch(batch)
            total_inserted += inserted
        
        return total_inserted
    
    def _insert_batch(self, atoms: List[Atom]) -> int:
        """Insert single batch with retry logic"""
        for attempt in range(self.config.max_retries):
            try:
                with self.pool.connection() as conn:
                    with conn.cursor() as cur:
                        # Use COPY for maximum performance
                        cur.execute("CREATE TEMP TABLE temp_atoms (LIKE atom) ON COMMIT DROP;")
                        
                        # Prepare data
                        values = []
                        for atom in atoms:
                            values.append((
                                atom.atom_hash,
                                atom.atom_class,
                                atom.modality,
                                f"POINT ZM({atom.x} {atom.y} {atom.z} {atom.m})",
                                0,  # hilbert_index placeholder
                                None  # metadata
                            ))
                        
                        # Bulk insert to temp table
                        execute_values(
                            cur,
                            "INSERT INTO temp_atoms (atom_id, atom_class, modality, geom, hilbert_index, metadata) VALUES %s",
                            values,
                            template="(%s, %s, %s, ST_GeomFromEWKT(%s), %s, %s)"
                        )
                        
                        # Move to main table (handles duplicates)
                        cur.execute("""
                            INSERT INTO atom 
                            SELECT * FROM temp_atoms
                            ON CONFLICT (atom_id) DO NOTHING;
                        """)
                        
                        return cur.rowcount
            except Exception as e:
                if attempt == self.config.max_retries - 1:
                    raise
                continue
        
        return 0
    
    def batch_update_geometry(
        self, 
        updates: List[tuple[bytes, float, float, float, float]]
    ) -> int:
        """Batch update atom geometries
        
        Args:
            updates: List of (atom_hash, x, y, z, m) tuples
            
        Returns:
            Number of atoms updated
        """
        total_updated = 0
        
        for batch in self._chunk(updates, self.config.batch_size):
            with self.pool.connection() as conn:
                with conn.cursor() as cur:
                    # Use unnest for efficient batch update
                    cur.execute("""
                        UPDATE atom AS ua
                        SET 
                            geom = ST_SetSRID(ST_MakePoint(u.x, u.y, u.z, u.m), 4326),
                            updated_at = now()
                        FROM (
                            SELECT 
                                unnest(%s::bytea[]) as atom_id,
                                unnest(%s::float8[]) as x,
                                unnest(%s::float8[]) as y,
                                unnest(%s::float8[]) as z,
                                unnest(%s::float8[]) as m
                        ) AS u
                        WHERE ua.atom_id = u.atom_id;
                    """, (
                        [u[0] for u in batch],
                        [u[1] for u in batch],
                        [u[2] for u in batch],
                        [u[3] for u in batch],
                        [u[4] for u in batch]
                    ))
                    
                    total_updated += cur.rowcount
        
        return total_updated
    
    def stream_atoms(
        self, 
        batch_size: int = None
    ) -> Iterator[List[Atom]]:
        """Stream all atoms in batches (server-side cursor)
        
        Args:
            batch_size: Size of each batch
            
        Yields:
            Batches of atoms
        """
        batch_size = batch_size or self.config.batch_size
        
        with self.pool.connection() as conn:
            # Server-side cursor for large result sets
            with conn.cursor(name='atom_stream') as cur:
                cur.itersize = batch_size
                
                cur.execute("""
                    SELECT
                        atom_id,
                        ST_X(geom), ST_Y(geom), ST_Z(geom), ST_M(geom),
                        atom_class, modality, metadata
                    FROM atom
                    ORDER BY hilbert_index;
                """)
                
                while True:
                    rows = cur.fetchmany(batch_size)
                    if not rows:
                        break
                    
                    atoms = [
                        Atom(
                            atom_hash=row[0],
                            x=row[1], y=row[2], z=row[3], m=row[4],
                            atom_class=row[5],
                            modality=row[6],
                            metadata=row[7]
                        )
                        for row in rows
                    ]
                    
                    yield atoms
    
    @staticmethod
    def _chunk(items: List, size: int) -> Iterator[List]:
        """Split list into chunks"""
        for i in range(0, len(items), size):
            yield items[i:i + size]


# Import for execute_values
try:
    from psycopg2.extras import execute_values
except ImportError:
    # Fallback if extras not available
    execute_values = None
