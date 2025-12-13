"""Core database connector with spatial query operations"""

from typing import List, Optional, Dict, Any, Tuple
from dataclasses import dataclass
from .pool import HartonomousPool


@dataclass
class Atom:
    """Representation of an atom in 4D semantic space"""
    atom_hash: bytes
    x: float
    y: float
    z: float
    m: float
    atom_class: int
    modality: int
    metadata: Optional[Dict[str, Any]] = None


class HartonomousConnector:
    """Low-level database connector for spatial operations
    
    Translates spatial operations into optimized SQL queries.
    Does NOT contain AI logic - intelligence is in database geometry.
    """
    
    def __init__(self, pool: HartonomousPool):
        """Initialize connector with connection pool
        
        Args:
            pool: HartonomousPool instance
        """
        self.pool = pool
        self._prepare_statements()
    
    def _prepare_statements(self) -> None:
        """Prepare frequently-used queries for performance"""
        with self.pool.connection() as conn:
            with conn.cursor() as cur:
                # k-NN query preparation
                cur.execute("""
                    PREPARE find_knn_stmt (bytea, int) AS
                    WITH target AS (
                        SELECT geom FROM atom WHERE atom_id = $1
                    )
                    SELECT
                        ua.atom_id, 
                        ST_X(ua.geom) as x, 
                        ST_Y(ua.geom) as y,
                        ST_Z(ua.geom) as z, 
                        ST_M(ua.geom) as m,
                        ua.atom_class,
                        ua.modality,
                        ua.metadata
                    FROM atom ua, target
                    WHERE ua.atom_id != $1
                    ORDER BY ua.geom <-> target.geom
                    LIMIT $2;
                """)
                
                # Radius search preparation
                cur.execute("""
                    PREPARE find_radius_stmt (bytea, float8) AS
                    WITH target AS (
                        SELECT geom FROM atom WHERE atom_id = $1
                    )
                    SELECT
                        ua.atom_id,
                        ST_X(ua.geom) as x,
                        ST_Y(ua.geom) as y,
                        ST_Z(ua.geom) as z,
                        ST_M(ua.geom) as m,
                        ua.atom_class,
                        ua.modality,
                        ua.metadata,
                        ST_3DDistance(ua.geom, target.geom) as distance
                    FROM atom ua, target
                    WHERE ua.atom_id != $1
                      AND ST_3DDWithin(ua.geom, target.geom, $2)
                    ORDER BY distance;
                """)
    
    def query_knn(self, atom_hash: bytes, k: int = 10) -> List[Atom]:
        """Find k nearest neighbors in semantic space
        
        This is the primary inference operation.
        
        Args:
            atom_hash: Target atom identifier
            k: Number of neighbors to return
            
        Returns:
            List of k nearest atoms sorted by distance
        """
        with self.pool.connection() as conn:
            with conn.cursor() as cur:
                cur.execute("EXECUTE find_knn_stmt (%s, %s)", (atom_hash, k))
                rows = cur.fetchall()
                
        return [
            Atom(
                atom_hash=row[0],
                x=row[1],
                y=row[2],
                z=row[3],
                m=row[4],
                atom_class=row[5],
                modality=row[6],
                metadata=row[7]
            )
            for row in rows
        ]
    
    def search_radius(self, atom_hash: bytes, radius: float) -> List[Tuple[Atom, float]]:
        """Find all atoms within radius of target
        
        Args:
            atom_hash: Target atom identifier
            radius: Search radius in semantic space
            
        Returns:
            List of (Atom, distance) tuples within radius
        """
        with self.pool.connection() as conn:
            with conn.cursor() as cur:
                cur.execute("EXECUTE find_radius_stmt (%s, %s)", (atom_hash, radius))
                rows = cur.fetchall()
        
        return [
            (
                Atom(
                    atom_hash=row[0],
                    x=row[1],
                    y=row[2],
                    z=row[3],
                    m=row[4],
                    atom_class=row[5],
                    modality=row[6],
                    metadata=row[7]
                ),
                row[8]  # distance
            )
            for row in rows
        ]
    
    def search_coordinates(
        self, 
        x: float, 
        y: float, 
        z: float, 
        m: float, 
        k: int = 10
    ) -> List[Atom]:
        """Search by explicit coordinates (not atom hash)
        
        Args:
            x, y, z, m: 4D coordinates
            k: Number of results
            
        Returns:
            k nearest atoms to specified point
        """
        query = """
        SELECT
            atom_id,
            ST_X(geom) as x,
            ST_Y(geom) as y,
            ST_Z(geom) as z,
            ST_M(geom) as m,
            atom_class,
            modality,
            metadata
        FROM atom
        ORDER BY geom <-> ST_SetSRID(ST_MakePoint(%s, %s, %s, %s), 4326)
        LIMIT %s;
        """
        
        with self.pool.connection() as conn:
            with conn.cursor() as cur:
                cur.execute(query, (x, y, z, m, k))
                rows = cur.fetchall()
        
        return [
            Atom(
                atom_hash=row[0],
                x=row[1],
                y=row[2],
                z=row[3],
                m=row[4],
                atom_class=row[5],
                modality=row[6],
                metadata=row[7]
            )
            for row in rows
        ]
    
    def insert_atom(self, atom: Atom, hilbert_index: int) -> bool:
        """Insert single atom (use bulk loader for production)
        
        Args:
            atom: Atom to insert
            hilbert_index: Pre-computed Hilbert index
            
        Returns:
            True if inserted, False if already exists
        """
        query = """
        INSERT INTO atom (
            atom_id, atom_class, modality, geom, hilbert_index, metadata
        )
        VALUES (
            %s, %s, %s, 
            ST_SetSRID(ST_MakePoint(%s, %s, %s, %s), 4326),
            %s, %s
        )
        ON CONFLICT (atom_id) DO NOTHING
        RETURNING atom_id;
        """
        
        with self.pool.connection() as conn:
            with conn.cursor() as cur:
                cur.execute(
                    query,
                    (
                        atom.atom_hash,
                        atom.atom_class,
                        atom.modality,
                        atom.x, atom.y, atom.z, atom.m,
                        hilbert_index,
                        atom.metadata
                    )
                )
                result = cur.fetchone()
                
        return result is not None
    
    def get_cortex_status(self) -> Dict[str, Any]:
        """Get Cortex physics engine status
        
        Returns:
            Dict with cortex metrics
        """
        query = """
        SELECT 
            model_version,
            atoms_processed,
            recalibrations,
            avg_stress,
            last_cycle_at,
            landmark_count
        FROM cortex_state
        WHERE id = 1;
        """
        
        with self.pool.connection() as conn:
            with conn.cursor() as cur:
                cur.execute(query)
                row = cur.fetchone()
        
        if not row:
            return {
                'is_running': False,
                'model_version': 0,
                'atoms_processed': 0,
                'recalibrations': 0,
                'current_stress': 0.0,
                'last_cycle': None,
                'landmarks': 0
            }
        
        return {
            'is_running': True,
            'model_version': row[0],
            'atoms_processed': row[1],
            'recalibrations': row[2],
            'current_stress': row[3],
            'last_cycle': row[4],
            'landmarks': row[5]
        }
