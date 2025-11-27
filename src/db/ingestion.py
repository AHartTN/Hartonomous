"""Database ingestion layer - writes atoms/landmarks/geometry to PostgreSQL."""

import psycopg2
from psycopg2.extras import execute_values
import numpy as np
from typing import Dict, List, Any
from ..core.atomization import Atom
from ..core.landmark_projection import Landmark


class IngestionWriter:
    """Handles atomic writes to database."""
    
    def __init__(self, connection_string: str):
        self.conn_str = connection_string
        self.conn = None
    
    def connect(self):
        self.conn = psycopg2.connect(self.conn_str)
    
    def close(self):
        if self.conn:
            self.conn.close()
    
    def write_atom(self, atom: Atom, cursor) -> int:
        cursor.execute("""
            INSERT INTO atoms (atom_data, modality, compression_type, metadata)
            VALUES (%s, %s, %s, %s)
            RETURNING atom_id
        """, (psycopg2.Binary(atom.data), int(atom.modality), atom.compression_type, atom.metadata))
        return cursor.fetchone()[0]
    
    def ingest_record(self, record: Dict[str, Any]):
        with self.conn.cursor() as cur:
            try:
                atom = record['atom']
                landmark = record['landmark']
                atom_id = self.write_atom(atom, cur)
                self.conn.commit()
            except Exception as e:
                self.conn.rollback()
                raise
