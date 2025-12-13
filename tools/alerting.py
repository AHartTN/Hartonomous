"""
Automated alert system for Hartonomous monitoring.
Checks critical metrics and sends notifications when thresholds exceeded.
"""

import psycopg2
from psycopg2.extras import RealDictCursor
import os
import smtplib
from email.mime.text import MIMEText
from email.mime.multipart import MIMEMultipart
from typing import List, Dict
from dataclasses import dataclass
from datetime import datetime

@dataclass
class Alert:
    """Represents a monitoring alert."""
    severity: str  # CRITICAL, WARNING, INFO
    component: str
    message: str
    value: float
    threshold: float
    timestamp: str

class AlertConfig:
    """Alert threshold configuration."""
    
    # Database health
    MAX_DB_SIZE_GB = 100
    MAX_CONNECTIONS = 80
    MAX_IDLE_CONNECTIONS = 50
    
    # Performance
    MIN_INDEX_SCAN_RATE = 0.1  # scans per second
    MAX_QUERY_TIME_MS = 1000
    
    # Data integrity
    MAX_INVALID_GEOMETRY_PCT = 0.01
    MAX_ORPHANED_COMPOSITIONS = 10
    
    # Cortex
    MAX_STRESS = 0.5
    MIN_RECALIBRATION_INTERVAL_HOURS = 48

class MonitoringAlerter:
    """
    Monitors Hartonomous metrics and generates alerts.
    """
    
    def __init__(self, conn, config: AlertConfig = None):
        """
        Initialize alerter.
        
        Args:
            conn: Database connection
            config: Alert configuration (uses defaults if None)
        """
        self.conn = conn
        self.config = config or AlertConfig()
        self.alerts: List[Alert] = []
    
    def check_database_health(self):
        """Check database size and connection health."""
        cur = self.conn.cursor(cursor_factory=RealDictCursor)
        
        # Database size
        cur.execute("SELECT pg_database_size('hartonomous') / 1024.0 / 1024.0 / 1024.0 as size_gb")
        size_gb = cur.fetchone()['size_gb']
        
        if size_gb > self.config.MAX_DB_SIZE_GB:
            self.alerts.append(Alert(
                severity="WARNING",
                component="Database",
                message=f"Database size exceeds threshold",
                value=size_gb,
                threshold=self.config.MAX_DB_SIZE_GB,
                timestamp=datetime.now().isoformat()
            ))
        
        # Connection count
        cur.execute("""
            SELECT 
                COUNT(*) as total,
                COUNT(*) FILTER (WHERE state = 'idle') as idle
            FROM pg_stat_activity
            WHERE datname = 'hartonomous'
        """)
        conns = cur.fetchone()
        
        if conns['total'] > self.config.MAX_CONNECTIONS:
            self.alerts.append(Alert(
                severity="CRITICAL",
                component="Connections",
                message="Connection pool exhausted",
                value=conns['total'],
                threshold=self.config.MAX_CONNECTIONS,
                timestamp=datetime.now().isoformat()
            ))
        
        if conns['idle'] > self.config.MAX_IDLE_CONNECTIONS:
            self.alerts.append(Alert(
                severity="WARNING",
                component="Connections",
                message="Excessive idle connections",
                value=conns['idle'],
                threshold=self.config.MAX_IDLE_CONNECTIONS,
                timestamp=datetime.now().isoformat()
            ))
        
        cur.close()
    
    def check_index_performance(self):
        """Check GiST index usage and efficiency."""
        cur = self.conn.cursor(cursor_factory=RealDictCursor)
        
        cur.execute("""
            SELECT 
                indexrelname,
                idx_scan,
                idx_tup_read,
                pg_size_pretty(pg_relation_size(indexrelid)) as size
            FROM pg_stat_user_indexes
            WHERE schemaname = 'public' AND relname = 'atom'
        """)
        
        indexes = cur.fetchall()
        
        for idx in indexes:
            if 'gist' in idx['indexrelname'] and idx['idx_scan'] < 10:
                self.alerts.append(Alert(
                    severity="WARNING",
                    component="Index",
                    message=f"Low scan count for {idx['indexrelname']}",
                    value=idx['idx_scan'],
                    threshold=10,
                    timestamp=datetime.now().isoformat()
                ))
        
        cur.close()
    
    def check_data_integrity(self):
        """Check for data quality issues."""
        cur = self.conn.cursor(cursor_factory=RealDictCursor)
        
        # Invalid geometries
        cur.execute("""
            SELECT 
                COUNT(*) FILTER (WHERE NOT ST_IsValid(geom)) as invalid,
                COUNT(*) as total
            FROM atom
        """)
        stats = cur.fetchone()
        
        invalid_pct = (stats['invalid'] / stats['total'] * 100) if stats['total'] > 0 else 0
        
        if invalid_pct > self.config.MAX_INVALID_GEOMETRY_PCT:
            self.alerts.append(Alert(
                severity="CRITICAL",
                component="Geometry",
                message=f"{stats['invalid']} atoms with invalid geometry",
                value=invalid_pct,
                threshold=self.config.MAX_INVALID_GEOMETRY_PCT,
                timestamp=datetime.now().isoformat()
            ))
        
        # Orphaned compositions (skip if constituents column doesn't exist)
        try:
            cur.execute("""
                SELECT COUNT(*) as cnt
                FROM atom
                WHERE atom_class = 1 
                  AND (constituents IS NULL OR array_length(constituents, 1) < 2)
            """)
            orphaned = cur.fetchone()['cnt']
            
            if orphaned > self.config.MAX_ORPHANED_COMPOSITIONS:
                self.alerts.append(Alert(
                    severity="WARNING",
                    component="Compositions",
                    message=f"{orphaned} compositions missing constituents",
                    value=orphaned,
                    threshold=self.config.MAX_ORPHANED_COMPOSITIONS,
                    timestamp=datetime.now().isoformat()
                ))
        except Exception:
            # constituents column not yet added via migration
            pass
        
        cur.close()
    
    def check_cortex_status(self):
        """Check Cortex background worker health."""
        cur = self.conn.cursor(cursor_factory=RealDictCursor)
        
        try:
            cur.execute("SELECT * FROM cortex_state")
            cortex = cur.fetchone()
            
            if cortex:
                # Check stress level
                if cortex['average_stress'] > self.config.MAX_STRESS:
                    self.alerts.append(Alert(
                        severity="WARNING",
                        component="Cortex",
                        message="High geometric stress detected",
                        value=cortex['average_stress'],
                        threshold=self.config.MAX_STRESS,
                        timestamp=datetime.now().isoformat()
                    ))
                
                # Check last cycle time
                if cortex['last_cycle_at']:
                    hours_since = (datetime.now() - cortex['last_cycle_at']).total_seconds() / 3600
                    if hours_since > self.config.MIN_RECALIBRATION_INTERVAL_HOURS:
                        self.alerts.append(Alert(
                            severity="INFO",
                            component="Cortex",
                            message=f"No recalibration in {hours_since:.1f} hours",
                            value=hours_since,
                            threshold=self.config.MIN_RECALIBRATION_INTERVAL_HOURS,
                            timestamp=datetime.now().isoformat()
                        ))
        except Exception:
            # Cortex extension not installed or table missing
            pass
        
        cur.close()
    
    def run_all_checks(self) -> List[Alert]:
        """
        Execute all monitoring checks.
        
        Returns:
            List of generated alerts
        """
        self.alerts = []
        
        self.check_database_health()
        self.check_index_performance()
        self.check_data_integrity()
        self.check_cortex_status()
        
        return self.alerts
    
    def format_alerts_text(self) -> str:
        """Format alerts as plain text for email."""
        if not self.alerts:
            return "✅ All monitoring checks passed - no alerts"
        
        critical = [a for a in self.alerts if a.severity == "CRITICAL"]
        warnings = [a for a in self.alerts if a.severity == "WARNING"]
        info = [a for a in self.alerts if a.severity == "INFO"]
        
        lines = ["HARTONOMOUS MONITORING ALERTS", "=" * 60, ""]
        
        if critical:
            lines.append(f"🔴 CRITICAL ({len(critical)})")
            for alert in critical:
                lines.append(f"  [{alert.component}] {alert.message}")
                lines.append(f"    Value: {alert.value} | Threshold: {alert.threshold}")
            lines.append("")
        
        if warnings:
            lines.append(f"⚠️  WARNING ({len(warnings)})")
            for alert in warnings:
                lines.append(f"  [{alert.component}] {alert.message}")
                lines.append(f"    Value: {alert.value} | Threshold: {alert.threshold}")
            lines.append("")
        
        if info:
            lines.append(f"ℹ️  INFO ({len(info)})")
            for alert in info:
                lines.append(f"  [{alert.component}] {alert.message}")
            lines.append("")
        
        lines.append(f"Generated: {datetime.now().isoformat()}")
        
        return "\n".join(lines)

def send_email_alert(alerts: List[Alert], 
                    smtp_server: str, smtp_port: int,
                    from_addr: str, to_addrs: List[str],
                    smtp_user: str = None, smtp_password: str = None):
    """
    Send alert email via SMTP.
    
    Args:
        alerts: List of alerts to send
        smtp_server: SMTP server hostname
        smtp_port: SMTP port (587 for TLS, 465 for SSL)
        from_addr: Sender email address
        to_addrs: List of recipient email addresses
        smtp_user: SMTP username (if auth required)
        smtp_password: SMTP password (if auth required)
    """
    if not alerts:
        return
    
    critical_count = sum(1 for a in alerts if a.severity == "CRITICAL")
    
    subject = f"[Hartonomous] {len(alerts)} Alert(s)"
    if critical_count > 0:
        subject += f" - {critical_count} CRITICAL"
    
    alerter = MonitoringAlerter(None)
    alerter.alerts = alerts
    body = alerter.format_alerts_text()
    
    msg = MIMEMultipart()
    msg['From'] = from_addr
    msg['To'] = ', '.join(to_addrs)
    msg['Subject'] = subject
    msg.attach(MIMEText(body, 'plain'))
    
    try:
        server = smtplib.SMTP(smtp_server, smtp_port)
        server.starttls()
        
        if smtp_user and smtp_password:
            server.login(smtp_user, smtp_password)
        
        server.send_message(msg)
        server.quit()
        
        print(f"✅ Alert email sent to {len(to_addrs)} recipient(s)")
        
    except Exception as e:
        print(f"❌ Failed to send email: {e}")

def main():
    """Run monitoring checks and display alerts."""
    import os
    
    conn = psycopg2.connect(
        host="localhost",
        port=5432,
        database="hartonomous",
        user="hartonomous",
        password=os.environ.get("PGPASSWORD", "")
    )
    
    alerter = MonitoringAlerter(conn)
    alerts = alerter.run_all_checks()
    
    print(alerter.format_alerts_text())
    
    # Example: Send email if configured
    # send_email_alert(
    #     alerts,
    #     smtp_server="smtp.gmail.com",
    #     smtp_port=587,
    #     from_addr="hartonomous@example.com",
    #     to_addrs=["admin@example.com"],
    #     smtp_user=os.environ.get("SMTP_USER"),
    #     smtp_password=os.environ.get("SMTP_PASSWORD")
    # )
    
    conn.close()
    
    # Exit with error code if critical alerts
    critical_count = sum(1 for a in alerts if a.severity == "CRITICAL")
    return 1 if critical_count > 0 else 0

if __name__ == "__main__":
    exit(main())
