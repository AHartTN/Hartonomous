"""Database ingestion layer - writes atoms/landmarks/geometry to PostgreSQL."""

import psycopg2
from psycopg2.extras import execute_values, execute_batch
import numpy as np
from typing import Dict, List, Any, Optional
from ..core.atomization import Atom
from ..core.landmark import LandmarkPosition as Landmark
import logging

logger = logging.getLogger(__name__)


class IngestionDB:
    """
    Async-compatible database ingestion layer.
    Handles atomic writes with proper error handling and batching.
    """
    
    def __init__(self, connection_string: str):
        self.conn_str = connection_string
        self.conn = None
        self._batch_size = 1000
    
    def connect(self):
        """Establish database connection."""
        if not self.conn or self.conn.closed:
            self.conn = psycopg2.connect(self.conn_str)
            self.conn.autocommit = False
        return self.conn
    
    def close(self):
        """Close database connection."""
        if self.conn and not self.conn.closed:
            self.conn.close()
    
    async def store_atom(self, atom: Atom) -> int:
        """
        Store single atom in database.
        Returns atom_id (existing or newly created).
        """
        with self.conn.cursor() as cur:
            try:
                # Use atomize_value function which handles deduplication
                cur.execute("""
                    SELECT atomize_value(
                        %s::bytea,
                        %s::text,
                        %s::jsonb
                    )
                """, (
                    psycopg2.Binary(atom.data),
                    atom.metadata.get('canonical_text', ''),
                    psycopg2.extras.Json(atom.metadata)
                ))
                
                atom_id = cur.fetchone()[0]
                self.conn.commit()
                
                logger.debug(f"Stored atom {atom_id}")
                return atom_id
                
            except Exception as e:
                self.conn.rollback()
                logger.error(f"Failed to store atom: {e}")
                raise
    
    async def store_atoms_batch(self, atoms: List[Atom]) -> List[int]:
        """
        Batch store atoms efficiently.
        Returns list of atom_ids.
        """
        atom_ids = []
        
        with self.conn.cursor() as cur:
            try:
                # Prepare batch data
                batch_data = [
                    (
                        psycopg2.Binary(atom.data),
                        atom.metadata.get('canonical_text', ''),
                        psycopg2.extras.Json(atom.metadata)
                    )
                    for atom in atoms
                ]
                
                # Batch insert using atomize_value
                execute_batch(cur, """
                    SELECT atomize_value(%s::bytea, %s::text, %s::jsonb)
                """, batch_data, page_size=self._batch_size)
                
                # Collect all atom_ids
                for row in cur.fetchall():
                    atom_ids.append(row[0])
                
                self.conn.commit()
                logger.info(f"Stored {len(atom_ids)} atoms in batch")
                
                return atom_ids
                
            except Exception as e:
                self.conn.rollback()
                logger.error(f"Batch atom storage failed: {e}")
                raise
    
    async def store_landmark(self, landmark: Landmark) -> int:
        """
        Store landmark with spatial geometry using SQL function.
        Returns landmark_id.
        """
        with self.conn.cursor() as cur:
            try:
                # Use SQL function
                cur.execute("""
                    SELECT store_landmark(
                        %s::text,
                        %s::double precision,
                        %s::double precision,
                        %s::double precision,
                        %s::real,
                        %s::jsonb
                    )
                """, (
                    landmark.name,
                    landmark.position[0],
                    landmark.position[1],
                    landmark.position[2],
                    landmark.weight,
                    psycopg2.extras.Json(landmark.metadata)
                ))
                
                landmark_id = cur.fetchone()[0]
                self.conn.commit()
                
                logger.debug(f"Stored landmark {landmark_id}")
                return landmark_id
                
            except Exception as e:
                self.conn.rollback()
                logger.error(f"Failed to store landmark: {e}")
                raise
    
    async def create_association(self, atom_id: int, landmark_id: int) -> int:
        """
        Create association between atom and landmark using SQL function.
        Returns association_id.
        """
        with self.conn.cursor() as cur:
            try:
                # Use SQL function
                cur.execute("""
                    SELECT create_association(
                        %s::bigint,
                        %s::bigint,
                        '{}'::jsonb
                    )
                """, (atom_id, landmark_id))
                
                association_id = cur.fetchone()[0]
                self.conn.commit()
                
                return association_id
                
            except Exception as e:
                self.conn.rollback()
                logger.error(f"Failed to create association: {e}")
                raise
    
    async def create_composition(
        self,
        parent_atom_id: int,
        component_atom_ids: List[int],
        metadata: Optional[Dict] = None
    ) -> List[int]:
        """
        Create hierarchical composition using SQL function.
        Returns list of composition_ids.
        """
        composition_ids = []
        
        with self.conn.cursor() as cur:
            try:
                for seq_idx, comp_id in enumerate(component_atom_ids):
                    # Use SQL function instead of direct INSERT
                    cur.execute("""
                        SELECT create_composition(
                            %s::bigint,
                            %s::bigint,
                            %s::bigint,
                            %s::jsonb
                        )
                    """, (
                        parent_atom_id,
                        comp_id,
                        seq_idx,
                        psycopg2.extras.Json(metadata or {})
                    ))
                    
                    result = cur.fetchone()
                    if result:
                        composition_ids.append(result[0])
                
                self.conn.commit()
                logger.debug(f"Created {len(composition_ids)} compositions")
                
                return composition_ids
                
            except Exception as e:
                self.conn.rollback()
                logger.error(f"Failed to create composition: {e}")
                raise
    
    async def create_relation(
        self,
        source_atom_id: int,
        target_atom_id: int,
        relation_type: str,
        weight: float = 0.5,
        metadata: Optional[Dict] = None
    ) -> int:
        """
        Create semantic relation between atoms.
        Returns relation_id.
        """
        with self.conn.cursor() as cur:
            try:
                cur.execute("""
                    SELECT create_relation(
                        %s::bigint,
                        %s::bigint,
                        %s::text,
                        %s::real,
                        %s::jsonb
                    )
                """, (
                    source_atom_id,
                    target_atom_id,
                    relation_type,
                    weight,
                    psycopg2.extras.Json(metadata or {})
                ))
                
                relation_id = cur.fetchone()[0]
                self.conn.commit()
                
                logger.debug(f"Created relation {relation_id}")
                return relation_id
                
            except Exception as e:
                self.conn.rollback()
                logger.error(f"Failed to create relation: {e}")
                raise
    
    def ingest_record(self, record: Dict[str, Any]):
        """Legacy method - kept for compatibility."""
        with self.conn.cursor() as cur:
            try:
                atom = record['atom']
                landmark = record['landmark']
                
                # Store atom
                atom_id = self.store_atom(atom)
                
                # Store landmark
                landmark_id = self.store_landmark(landmark)
                
                # Create association
                self.create_association(atom_id, landmark_id)
                
                self.conn.commit()
                
            except Exception as e:
                self.conn.rollback()
                raise
