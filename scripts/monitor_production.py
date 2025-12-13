#!/usr/bin/env python3
"""
Continuous monitoring daemon for Hartonomous production
Tracks performance metrics, triggers alerts, and manages Cortex cycles
"""

import time
import psycopg2
import json
from datetime import datetime
import signal
import sys

class HartonomousMonitor:
    def __init__(self, db_config: dict):
        self.db_config = db_config
        self.running = True
        self.metrics_history = []
        
        # Thresholds
        self.max_query_time_ms = 1000
        self.min_qps = 100
        self.max_replication_lag_kb = 10240  # 10MB
        self.cortex_cycle_interval = 60  # seconds
        self.last_cortex_cycle = 0
        
        signal.signal(signal.SIGINT, self._shutdown)
        signal.signal(signal.SIGTERM, self._shutdown)
    
    def _shutdown(self, signum, frame):
        print("\nShutting down monitor...")
        self.running = False
    
    def run(self):
        """Main monitoring loop"""
        print("=== Hartonomous Monitor Starting ===")
        print(f"Database: {self.db_config['dbname']}@{self.db_config['host']}")
        print(f"Cortex cycle interval: {self.cortex_cycle_interval}s\n")
        
        while self.running:
            try:
                metrics = self.collect_metrics()
                self.check_thresholds(metrics)
                self.trigger_cortex_if_needed()
                
                time.sleep(5)  # Poll every 5 seconds
                
            except KeyboardInterrupt:
                break
            except Exception as e:
                print(f"Error: {e}")
                time.sleep(10)
        
        print("Monitor stopped")
    
    def collect_metrics(self) -> dict:
        """Collect system metrics"""
        conn = psycopg2.connect(**self.db_config)
        cursor = conn.cursor()
        
        metrics = {
            'timestamp': datetime.now().isoformat()
        }
        
        # Atom counts
        cursor.execute("SELECT atom_class, COUNT(*) FROM atom GROUP BY atom_class")
        for atom_class, count in cursor.fetchall():
            metrics[f'atoms_class_{atom_class}'] = count
        
        # Cortex metrics
        cursor.execute("SELECT * FROM v_cortex_metrics")
        cortex = cursor.fetchone()
        if cortex:
            metrics['cortex_atoms_processed'] = cortex[1]
            metrics['cortex_recalibrations'] = cortex[2]
            metrics['cortex_landmarks'] = cortex[4]
            metrics['cortex_seconds_since_cycle'] = cortex[6]
        
        # Query performance (from pg_stat_statements if available)
        try:
            cursor.execute("""
                SELECT 
                    COUNT(*) as query_count,
                    AVG(mean_exec_time) as avg_time_ms,
                    MAX(max_exec_time) as max_time_ms
                FROM pg_stat_statements
                WHERE dbid = (SELECT oid FROM pg_database WHERE datname = current_database())
            """)
            stats = cursor.fetchone()
            if stats:
                metrics['query_count'] = stats[0]
                metrics['avg_query_time_ms'] = stats[1]
                metrics['max_query_time_ms'] = stats[2]
        except psycopg2.Error:
            pass  # pg_stat_statements not installed
        
        # Connection pool
        cursor.execute("SELECT * FROM v_connection_pool_stats")
        pool = cursor.fetchone()
        if pool:
            metrics['active_connections'] = pool[4]
            metrics['idle_connections'] = pool[5]
        
        # Index health
        cursor.execute("""
            SELECT 
                schemaname, 
                tablename, 
                indexname,
                idx_scan,
                idx_tup_read,
                idx_tup_fetch
            FROM pg_stat_user_indexes
            WHERE schemaname = 'public'
              AND tablename = 'atom'
        """)
        metrics['indexes'] = cursor.fetchall()
        
        conn.close()
        
        self.metrics_history.append(metrics)
        if len(self.metrics_history) > 1000:
            self.metrics_history.pop(0)
        
        return metrics
    
    def check_thresholds(self, metrics: dict):
        """Check metrics against thresholds and alert"""
        
        # Slow queries
        if metrics.get('max_query_time_ms', 0) > self.max_query_time_ms:
            self.alert(
                'SLOW_QUERY',
                f"Query exceeded {self.max_query_time_ms}ms: {metrics['max_query_time_ms']:.2f}ms"
            )
        
        # Cortex lag
        if metrics.get('cortex_seconds_since_cycle', 0) > self.cortex_cycle_interval * 2:
            self.alert(
                'CORTEX_LAG',
                f"Cortex hasn't cycled in {metrics['cortex_seconds_since_cycle']:.0f}s"
            )
        
        # Connection pool exhaustion
        total_conn = metrics.get('active_connections', 0) + metrics.get('idle_connections', 0)
        if total_conn > 90:  # Approaching limit of 100
            self.alert(
                'CONNECTION_POOL_HIGH',
                f"Connection pool at {total_conn}/100"
            )
        
        # Print status
        print(f"[{metrics['timestamp']}] "
              f"Atoms: {metrics.get('atoms_class_0', 0)} constants, {metrics.get('atoms_class_1', 0)} compositions | "
              f"Cortex: {metrics.get('cortex_landmarks', 0)} landmarks, "
              f"{metrics.get('cortex_seconds_since_cycle', 0):.0f}s since cycle | "
              f"Conn: {metrics.get('active_connections', 0)} active")
    
    def trigger_cortex_if_needed(self):
        """Trigger Cortex recalibration if interval elapsed"""
        now = time.time()
        if now - self.last_cortex_cycle >= self.cortex_cycle_interval:
            try:
                conn = psycopg2.connect(**self.db_config)
                cursor = conn.cursor()
                
                start = time.time()
                cursor.execute("SELECT cortex_cycle_once()")
                elapsed = (time.time() - start) * 1000
                
                conn.commit()
                conn.close()
                
                print(f"  → Cortex cycle triggered ({elapsed:.2f}ms)")
                self.last_cortex_cycle = now
                
            except Exception as e:
                self.alert('CORTEX_CYCLE_FAILED', str(e))
    
    def alert(self, alert_type: str, message: str):
        """Send alert (print for now, could integrate PagerDuty/Slack)"""
        print(f"⚠️  ALERT [{alert_type}]: {message}")
        
        # Log to file
        with open('monitor_alerts.log', 'a') as f:
            f.write(f"{datetime.now().isoformat()} [{alert_type}] {message}\n")
    
    def dump_metrics(self, filepath: str = 'metrics_dump.json'):
        """Save metrics history to file"""
        with open(filepath, 'w') as f:
            json.dump(self.metrics_history, f, indent=2)

if __name__ == '__main__':
    monitor = HartonomousMonitor({
        'host': 'localhost',
        'dbname': 'hartonomous',
        'user': 'hartonomous'
    })
    
    try:
        monitor.run()
    finally:
        monitor.dump_metrics()
        print("Metrics saved to metrics_dump.json")
