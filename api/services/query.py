"""
Query service for retrieving atoms and provenance.

Copyright © 2025 Anthony Hart. All Rights Reserved.
"""

import logging
from typing import Dict, Any, List, Optional
from psycopg import AsyncConnection
from psycopg.rows import dict_row

logger = logging.getLogger(__name__)


class QueryService:
    """Service for querying atoms and provenance."""
    
    @staticmethod
    async def get_atom_by_id(
        conn: AsyncConnection,
        atom_id: int
    ) -> Optional[Dict[str, Any]]:
        """
        Get atom by ID.
        
        Args:
            conn: Database connection
            atom_id: Atom ID to retrieve
        
        Returns:
            dict with atom data or None if not found
        """
        try:
            async with conn.cursor(row_factory=dict_row) as cur:
                await cur.execute("""
                    SELECT 
                        atom_id,
                        content_hash,
                        canonical_text,
                        byte_value,
                        ST_AsText(spatial_key) as spatial_position,
                        metadata,
                        created_at
                    FROM atom
                    WHERE atom_id = %s;
                """, (atom_id,))
                
                result = await cur.fetchone()
                
                if result:
                    logger.info(f"Retrieved atom {atom_id}")
                    return dict(result)
                else:
                    logger.warning(f"Atom {atom_id} not found")
                    return None
        
        except Exception as e:
            logger.error(f"Failed to retrieve atom {atom_id}: {e}", exc_info=True)
            raise
    
    @staticmethod
    async def get_atom_lineage(
        conn: AsyncConnection,
        atom_id: int,
        max_depth: int = 50
    ) -> Dict[str, Any]:
        """
        Get atom provenance lineage.
        
        Args:
            conn: Database connection
            atom_id: Starting atom ID
            max_depth: Maximum depth to traverse
        
        Returns:
            dict: {nodes: List[dict], total_ancestors: int}
        """
        try:
            async with conn.cursor(row_factory=dict_row) as cur:
                # Call SQL lineage function
                await cur.execute("""
                    SELECT * FROM get_atom_lineage(%s, %s);
                """, (atom_id, max_depth))
                
                nodes = []
                async for row in cur:
                    nodes.append(dict(row))
                
                logger.info(
                    f"Retrieved lineage for atom {atom_id}: "
                    f"{len(nodes)} ancestors (max depth {max_depth})"
                )
                
                return {
                    "nodes": nodes,
                    "total_ancestors": len(nodes)
                }
        
        except Exception as e:
            logger.error(
                f"Failed to retrieve lineage for atom {atom_id}: {e}",
                exc_info=True
            )
            raise
    
    @staticmethod
    async def spatial_search(
        conn: AsyncConnection,
        query: str,
        limit: int = 10,
        radius: Optional[float] = None
    ) -> List[Dict[str, Any]]:
        """
        Spatial KNN search for similar atoms.
        
        Args:
            conn: Database connection
            query: Search query (text or atom ID)
            limit: Maximum results
            radius: Optional spatial radius constraint
        
        Returns:
            list of dicts: [{atom_id, canonical_text, distance, relevance}, ...]
        """
        try:
            async with conn.cursor(row_factory=dict_row) as cur:
                # First, find the query atom (or create temporary)
                # If numeric, treat as atom_id; otherwise as text
                try:
                    query_atom_id = int(query)
                    # Use existing atom
                    await cur.execute("""
                        SELECT spatial_key
                        FROM atom
                        WHERE atom_id = %s;
                    """, (query_atom_id,))
                    
                    result = await cur.fetchone()
                    if not result:
                        raise ValueError(f"Query atom {query_atom_id} not found")
                    
                except ValueError:
                    # Text query - find atoms with similar text
                    # Use full-text search + spatial KNN
                    await cur.execute("""
                        SELECT atom_id
                        FROM atom
                        WHERE canonical_text ILIKE %s
                        LIMIT 1;
                    """, (f"%{query}%",))
                    
                    result = await cur.fetchone()
                    if not result:
                        # No exact match - return empty
                        logger.warning(f"No atoms found matching '{query}'")
                        return []
                    
                    query_atom_id = result["atom_id"]
                
                # Perform KNN search
                if radius:
                    # Distance-constrained search
                    await cur.execute("""
                        SELECT 
                            a.atom_id,
                            a.canonical_text,
                            ST_Distance(a.spatial_key, q.spatial_key) as distance,
                            1.0 / (1.0 + ST_Distance(a.spatial_key, q.spatial_key)) as relevance
                        FROM atom a
                        CROSS JOIN (
                            SELECT spatial_key FROM atom WHERE atom_id = %s
                        ) q
                        WHERE a.atom_id != %s
                        AND ST_DWithin(a.spatial_key, q.spatial_key, %s)
                        ORDER BY a.spatial_key <-> q.spatial_key
                        LIMIT %s;
                    """, (query_atom_id, query_atom_id, radius, limit))
                else:
                    # Pure KNN search
                    await cur.execute("""
                        SELECT 
                            a.atom_id,
                            a.canonical_text,
                            ST_Distance(a.spatial_key, q.spatial_key) as distance,
                            1.0 / (1.0 + ST_Distance(a.spatial_key, q.spatial_key)) as relevance
                        FROM atom a
                        CROSS JOIN (
                            SELECT spatial_key FROM atom WHERE atom_id = %s
                        ) q
                        WHERE a.atom_id != %s
                        AND a.spatial_key IS NOT NULL
                        ORDER BY a.spatial_key <-> q.spatial_key
                        LIMIT %s;
                    """, (query_atom_id, query_atom_id, limit))
                
                results = []
                async for row in cur:
                    results.append(dict(row))
                
                logger.info(
                    f"Spatial search for '{query}': {len(results)} results"
                )
                
                return results
        
        except Exception as e:
            logger.error(f"Spatial search failed for '{query}': {e}", exc_info=True)
            raise


__all__ = ["QueryService"]
