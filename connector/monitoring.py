"""Performance monitoring utilities"""

from typing import Dict, List, Any
from .pool import HartonomousPool


class PerformanceMonitor:
    """Monitor and analyze database performance"""
    
    def __init__(self, pool: HartonomousPool):
        self.pool = pool
    
    def get_query_stats(self, top_n: int = 10) -> List[Dict[str, Any]]:
        """Get top N slowest queries
        
        Args:
            top_n: Number of queries to return
            
        Returns:
            List of query statistics
        """
        query = """
        SELECT
            query,
            calls,
            mean_exec_time,
            max_exec_time,
            stddev_exec_time
        FROM v_query_performance
        LIMIT %s;
        """
        
        with self.pool.connection() as conn:
            with conn.cursor() as cur:
                cur.execute(query, (top_n,))
                rows = cur.fetchall()
        
        return [
            {
                'query': row[0],
                'calls': row[1],
                'mean_time_ms': row[2],
                'max_time_ms': row[3],
                'stddev_ms': row[4]
            }
            for row in rows
        ]
    
    def get_index_usage(self) -> List[Dict[str, Any]]:
        """Get index usage statistics
        
        Returns:
            List of index usage stats
        """
        query = "SELECT * FROM v_index_usage;"
        
        with self.pool.connection() as conn:
            with conn.cursor() as cur:
                cur.execute(query)
                rows = cur.fetchall()
        
        return [
            {
                'schema': row[0],
                'table': row[1],
                'index': row[2],
                'scans': row[3],
                'tuples_read': row[4],
                'tuples_fetched': row[5],
                'size': row[6]
            }
            for row in rows
        ]
    
    def get_table_bloat(self) -> List[Dict[str, Any]]:
        """Get table bloat statistics
        
        Returns:
            List of table bloat metrics
        """
        query = "SELECT * FROM v_table_bloat;"
        
        with self.pool.connection() as conn:
            with conn.cursor() as cur:
                cur.execute(query)
                rows = cur.fetchall()
        
        return [
            {
                'schema': row[0],
                'table': row[1],
                'total_size': row[2],
                'table_size': row[3],
                'index_size': row[4],
                'dead_tuples': row[5],
                'live_tuples': row[6],
                'dead_percent': row[7]
            }
            for row in rows
        ]
    
    def explain_query(self, atom_hash: bytes, k: int = 10) -> List[str]:
        """Get query execution plan
        
        Args:
            atom_hash: Target atom
            k: Number of neighbors
            
        Returns:
            Query plan lines
        """
        query = "SELECT plan_line FROM explain_spatial_query(%s, %s);"
        
        with self.pool.connection() as conn:
            with conn.cursor() as cur:
                cur.execute(query, (atom_hash, k))
                rows = cur.fetchall()
        
        return [row[0] for row in rows]
    
    def run_maintenance(self) -> None:
        """Run routine table maintenance"""
        with self.pool.connection() as conn:
            with conn.cursor() as cur:
                cur.execute("SELECT maintain_atoms_table();")
