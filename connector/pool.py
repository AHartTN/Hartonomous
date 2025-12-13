"""Connection pooling for PostgreSQL with PostGIS"""

import psycopg2
from psycopg2 import pool
from contextlib import contextmanager
from typing import Generator, Dict, Optional
import os


class HartonomousPool:
    """PostgreSQL connection pool manager
    
    Manages persistent connections with automatic reconnection
    and resource cleanup.
    """
    
    def __init__(
        self,
        config: Optional[Dict[str, str]] = None,
        min_connections: int = 2,
        max_connections: int = 10
    ):
        """Initialize connection pool
        
        Args:
            config: Database configuration dict. If None, uses environment variables
            min_connections: Minimum number of connections to maintain
            max_connections: Maximum number of connections allowed
        """
        self._config = config or self._config_from_env()
        self._pool = psycopg2.pool.ThreadedConnectionPool(
            min_connections,
            max_connections,
            **self._config
        )
    
    @staticmethod
    def _config_from_env() -> Dict[str, str]:
        """Load configuration from environment variables"""
        return {
            'host': os.getenv('PGHOST', 'localhost'),
            'port': int(os.getenv('PGPORT', '5432')),
            'database': os.getenv('PGDATABASE', 'hartonomous'),
            'user': os.getenv('PGUSER', 'postgres'),
            'password': os.getenv('PGPASSWORD', ''),
            'options': '-c search_path=public',
        }
    
    @contextmanager
    def connection(self) -> Generator:
        """Get connection from pool with automatic cleanup
        
        Yields:
            psycopg2 connection object
            
        Example:
            >>> with pool.connection() as conn:
            ...     with conn.cursor() as cur:
            ...         cur.execute("SELECT 1")
        """
        conn = self._pool.getconn()
        try:
            yield conn
            conn.commit()
        except Exception:
            conn.rollback()
            raise
        finally:
            self._pool.putconn(conn)
    
    def close_all(self) -> None:
        """Close all connections in pool"""
        if self._pool:
            self._pool.closeall()
