"""High-level API for Hartonomous operations"""

from typing import List, Optional, Dict, Any
from .pool import HartonomousPool
from .connector import HartonomousConnector, Atom


class Hartonomous:
    """High-level interface to Hartonomous spatial AI
    
    Simplifies common operations while exposing full connector API.
    """
    
    def __init__(self, config: Optional[Dict[str, str]] = None, **kwargs):
        """Initialize Hartonomous connection
        
        Args:
            config: Database configuration dict. If None, uses kwargs or environment.
            **kwargs: Individual connection parameters (dbname, user, password, host, port)
        """
        # Accept either config dict or kwargs (for compatibility with psycopg2 params)
        if config is None and kwargs:
            config = kwargs
        self.pool = HartonomousPool(config)
        self.connector = HartonomousConnector(self.pool)
    
    def query(self, atom_hash: bytes, k: int = 10) -> List[Atom]:
        """Find semantically similar atoms (inference operation)
        
        Args:
            atom_hash: Target atom identifier
            k: Number of similar atoms to return
            
        Returns:
            List of similar atoms
        """
        return self.connector.query_knn(atom_hash, k)
    
    def search(
        self, 
        x: float, 
        y: float, 
        z: float, 
        m: float = 0.0, 
        k: int = 10
    ) -> List[Atom]:
        """Search by coordinates in semantic space
        
        Args:
            x, y: Semantic coordinates
            z: Hierarchy level
            m: Salience value
            k: Number of results
            
        Returns:
            Nearest atoms to specified point
        """
        return self.connector.search_coordinates(x, y, z, m, k)
    
    def neighborhood(self, atom_hash: bytes, radius: float) -> List[Atom]:
        """Get semantic neighborhood around atom
        
        Args:
            atom_hash: Center atom
            radius: Neighborhood radius
            
        Returns:
            All atoms within radius
        """
        results = self.connector.search_radius(atom_hash, radius)
        return [atom for atom, _ in results]
    
    def abstract(self, atom_hash: bytes, levels: int = 1, k: int = 10) -> List[Atom]:
        """Move UP hierarchy (abstraction)
        
        Args:
            atom_hash: Starting atom
            levels: How many Z levels to ascend
            k: Results per level
            
        Returns:
            More abstract atoms
        """
        query = """
        WITH target AS (
            SELECT geom, ST_Z(geom) as z FROM atom WHERE atom_id = %s
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
        WHERE ST_Z(ua.geom) = target.z + %s
        ORDER BY ST_3DDistance(ua.geom, target.geom)
        LIMIT %s;
        """
        
        with self.pool.connection() as conn:
            with conn.cursor() as cur:
                cur.execute(query, (atom_hash, levels, k))
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
    
    def refine(self, atom_hash: bytes, levels: int = 1, k: int = 10) -> List[Atom]:
        """Move DOWN hierarchy (refinement)
        
        Args:
            atom_hash: Starting atom
            levels: How many Z levels to descend
            k: Results per level
            
        Returns:
            More specific atoms
        """
        query = """
        WITH target AS (
            SELECT geom, ST_Z(geom) as z FROM atom WHERE atom_id = %s
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
        WHERE ST_Z(ua.geom) = target.z - %s
        ORDER BY ST_3DDistance(ua.geom, target.geom)
        LIMIT %s;
        """
        
        with self.pool.connection() as conn:
            with conn.cursor() as cur:
                cur.execute(query, (atom_hash, levels, k))
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
    
    def status(self) -> Dict[str, Any]:
        """Get system status
        
        Returns:
            System metrics and Cortex status
        """
        return self.connector.get_cortex_status()
    
    def close(self) -> None:
        """Close all connections"""
        self.pool.close_all()


# Re-export for convenience
__all__ = ['Hartonomous', 'Atom']
