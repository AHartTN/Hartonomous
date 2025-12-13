#!/usr/bin/env python3
"""
Production monitoring and observability for Hartonomous
"""
import psycopg2
import psycopg2.extras
from datetime import datetime, timedelta
import json

class HartonomousMonitor:
    def __init__(self, conninfo):
        self.conn = psycopg2.connect(conninfo)
        self.conn.autocommit = True
    
    def get_system_health(self):
        """Overall system health check"""
        cur = self.conn.cursor(cursor_factory=psycopg2.extras.RealDictCursor)
        
        health = {}
        
        # Database size
        cur.execute("""
            SELECT 
                pg_size_pretty(pg_database_size(current_database())) as db_size,
                pg_database_size(current_database()) as db_size_bytes
        """)
        health['database'] = dict(cur.fetchone())
        
        # Atom table stats
        cur.execute("""
            SELECT 
                COUNT(*) as total_atoms,
                COUNT(CASE WHEN atom_class = 0 THEN 1 END) as constants,
                COUNT(CASE WHEN atom_class = 1 THEN 1 END) as compositions,
                pg_size_pretty(pg_total_relation_size('atom')) as table_size,
                COUNT(DISTINCT modality) as unique_modalities
            FROM atom
        """)
        health['atoms'] = dict(cur.fetchone())
        
        # Index health
        cur.execute("""
            SELECT 
                indexrelname as indexname,
                pg_size_pretty(pg_relation_size(indexrelid)) as size,
                idx_scan as scans,
                idx_tup_read as tuples_read
            FROM pg_stat_user_indexes 
            WHERE schemaname = 'public' AND relname = 'atom'
            ORDER BY idx_scan DESC
        """)
        health['indexes'] = [dict(row) for row in cur.fetchall()]
        
        # Cortex state
        cur.execute("""
            SELECT 
                model_version,
                atoms_processed,
                recalibrations,
                avg_stress,
                last_cycle_at
            FROM cortex_state WHERE id = 1
        """)
        health['cortex'] = dict(cur.fetchone())
        
        # Active connections
        cur.execute("""
            SELECT 
                COUNT(*) as total,
                COUNT(CASE WHEN state = 'active' THEN 1 END) as active,
                COUNT(CASE WHEN state = 'idle' THEN 1 END) as idle
            FROM pg_stat_activity 
            WHERE datname = current_database()
        """)
        health['connections'] = dict(cur.fetchone())
        
        return health
    
    def get_query_performance(self, limit=20):
        """Top queries by execution time"""
        cur = self.conn.cursor(cursor_factory=psycopg2.extras.RealDictCursor)
        
        try:
            cur.execute("""
                SELECT 
                    LEFT(query, 100) as query_snippet,
                    calls,
                    ROUND(total_exec_time::numeric, 2) as total_ms,
                    ROUND(mean_exec_time::numeric, 2) as mean_ms,
                    ROUND(stddev_exec_time::numeric, 2) as stddev_ms,
                    ROUND((100 * total_exec_time / NULLIF(SUM(total_exec_time) OVER(), 0))::numeric, 2) as pct_total
                FROM pg_stat_statements
                WHERE query LIKE %s
                ORDER BY total_exec_time DESC
                LIMIT %s
            """, ('%atom%', limit))
            
            return [dict(row) for row in cur.fetchall()]
        except Exception as e:
            # pg_stat_statements might not be configured
            return []
    
    def get_spatial_distribution(self):
        """Analyze spatial distribution of atoms"""
        cur = self.conn.cursor(cursor_factory=psycopg2.extras.RealDictCursor)
        
        # Z-level distribution
        cur.execute("""
            SELECT 
                ROUND(ST_Z(geom)) as z_level,
                COUNT(*) as count,
                ROUND(100.0 * COUNT(*) / SUM(COUNT(*)) OVER(), 2) as percentage,
                ROUND(AVG(ST_M(geom))::numeric, 4) as avg_salience
            FROM atom
            GROUP BY ROUND(ST_Z(geom))
            ORDER BY z_level
        """)
        z_distribution = [dict(row) for row in cur.fetchall()]
        
        # Modality distribution
        cur.execute("""
            SELECT 
                modality,
                COUNT(*) as count,
                ROUND(100.0 * COUNT(*) / SUM(COUNT(*)) OVER(), 2) as percentage,
                ROUND(AVG(ST_X(geom))::numeric, 2) as centroid_x,
                ROUND(AVG(ST_Y(geom))::numeric, 2) as centroid_y
            FROM atom
            GROUP BY modality
            ORDER BY count DESC
        """)
        modality_distribution = [dict(row) for row in cur.fetchall()]
        
        # Spatial extent
        cur.execute("""
            SELECT 
                ROUND(MIN(ST_X(geom))::numeric, 2) as min_x,
                ROUND(MAX(ST_X(geom))::numeric, 2) as max_x,
                ROUND(MIN(ST_Y(geom))::numeric, 2) as min_y,
                ROUND(MAX(ST_Y(geom))::numeric, 2) as max_y,
                ROUND(MIN(ST_Z(geom))::numeric, 2) as min_z,
                ROUND(MAX(ST_Z(geom))::numeric, 2) as max_z
            FROM atom
        """)
        extent = dict(cur.fetchone())
        
        return {
            'z_levels': z_distribution,
            'modalities': modality_distribution,
            'extent': extent
        }
    
    def get_anomalies(self):
        """Detect potential issues"""
        cur = self.conn.cursor(cursor_factory=psycopg2.extras.RealDictCursor)
        anomalies = []
        
        # Orphaned compositions
        cur.execute("""
            SELECT COUNT(*) as orphaned_compositions
            FROM atom_compositions c
            WHERE NOT EXISTS (SELECT 1 FROM atom WHERE atom_id = c.parent_atom_id)
               OR NOT EXISTS (SELECT 1 FROM atom WHERE atom_id = c.component_atom_id)
        """)
        orphaned = cur.fetchone()['orphaned_compositions']
        if orphaned > 0:
            anomalies.append(f"⚠ {orphaned} orphaned composition records")
        
        # Invalid geometries
        cur.execute("SELECT COUNT(*) as cnt FROM atom WHERE NOT ST_IsValid(geom)")
        invalid = cur.fetchone()['cnt']
        if invalid > 0:
            anomalies.append(f"⚠ {invalid} atoms with invalid geometry")
        
        # Constraint violations (constants without values)
        cur.execute("SELECT COUNT(*) as cnt FROM atom WHERE atom_class = 0 AND atomic_value IS NULL")
        bad_constants = cur.fetchone()['cnt']
        if bad_constants > 0:
            anomalies.append(f"⚠ {bad_constants} constants missing atomic_value")
        
        # Unused indexes
        cur.execute("""
            SELECT indexrelname as indexname
            FROM pg_stat_user_indexes 
            WHERE schemaname = 'public' AND relname = 'atom' AND idx_scan = 0
        """)
        unused = [row['indexname'] for row in cur.fetchall()]
        if unused:
            anomalies.append(f"ℹ Unused indexes: {', '.join(unused)}")
        
        return anomalies
    
    def print_dashboard(self):
        """Print monitoring dashboard"""
        print("=" * 80)
        print("HARTONOMOUS MONITORING DASHBOARD")
        print(f"Generated: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
        print("=" * 80)
        
        # System Health
        health = self.get_system_health()
        print("\n📊 SYSTEM HEALTH")
        print(f"  Database Size: {health['database']['db_size']}")
        print(f"  Total Atoms: {health['atoms']['total_atoms']:,}")
        print(f"    Constants: {health['atoms']['constants']:,}")
        print(f"    Compositions: {health['atoms']['compositions']:,}")
        print(f"  Atom Table: {health['atoms']['table_size']}")
        print(f"  Modalities: {health['atoms']['unique_modalities']}")
        
        print(f"\n  Connections: {health['connections']['total']} total")
        print(f"    Active: {health['connections']['active']}")
        print(f"    Idle: {health['connections']['idle']}")
        
        print(f"\n  Cortex Status:")
        print(f"    Model Version: {health['cortex']['model_version']}")
        print(f"    Atoms Processed: {health['cortex']['atoms_processed']:,}")
        print(f"    Recalibrations: {health['cortex']['recalibrations']}")
        print(f"    Avg Stress: {health['cortex']['avg_stress']:.4f}")
        last_cycle = health['cortex']['last_cycle_at']
        print(f"    Last Cycle: {last_cycle if last_cycle else 'Never'}")
        
        # Index Performance
        print("\n🔍 INDEX PERFORMANCE")
        for idx in health['indexes'][:5]:
            print(f"  {idx['indexname']:25s} | {idx['size']:>10s} | {idx['scans']:>8,} scans")
        
        # Query Performance
        print("\n⚡ TOP QUERIES (by total time)")
        queries = self.get_query_performance(limit=5)
        for i, q in enumerate(queries, 1):
            print(f"  {i}. {q['query_snippet']}")
            print(f"     Calls: {q['calls']:,} | Mean: {q['mean_ms']}ms | Total: {q['total_ms']}ms ({q['pct_total']}%)")
        
        # Spatial Distribution
        print("\n🌐 SPATIAL DISTRIBUTION")
        dist = self.get_spatial_distribution()
        
        print("  Z-Levels:")
        for z in dist['z_levels']:
            print(f"    Z={z['z_level']}: {z['count']:>6,} atoms ({z['percentage']:>5}%) | Salience: {z['avg_salience']}")
        
        print("\n  Modalities:")
        for m in dist['modalities']:
            print(f"    M={m['modality']}: {m['count']:>6,} atoms ({m['percentage']:>5}%) | Centroid: ({m['centroid_x']}, {m['centroid_y']})")
        
        print(f"\n  Extent: X[{dist['extent']['min_x']}, {dist['extent']['max_x']}] "
              f"Y[{dist['extent']['min_y']}, {dist['extent']['max_y']}] "
              f"Z[{dist['extent']['min_z']}, {dist['extent']['max_z']}]")
        
        # Anomalies
        anomalies = self.get_anomalies()
        if anomalies:
            print("\n⚠ ANOMALIES DETECTED")
            for anomaly in anomalies:
                print(f"  {anomaly}")
        else:
            print("\n✅ No anomalies detected")
        
        print("\n" + "=" * 80)

if __name__ == "__main__":
    import os
    import subprocess
    
    # Get password from environment
    pg_pass = subprocess.run(['powershell', '-Command', '$env:PGPASSWORD'], 
                            capture_output=True, text=True).stdout.strip()
    
    conninfo = f"dbname=hartonomous user=hartonomous password={pg_pass} host=localhost"
    
    monitor = HartonomousMonitor(conninfo)
    monitor.print_dashboard()
