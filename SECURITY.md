# Security Policy

## Supported Versions

Currently supported versions of Hartonomous:

| Version | Supported          |
| ------- | ------------------ |
| 0.5.x   | :white_check_mark: |
| < 0.5   | :x:                |

---

## Reporting a Vulnerability

**We take security seriously.** If you discover a security vulnerability, please report it responsibly.

### ?? Private Disclosure

**DO NOT** open a public GitHub issue for security vulnerabilities.

Instead, email: **aharttn@gmail.com** with:
- Subject: `[SECURITY] Brief description`
- Detailed description of the vulnerability
- Steps to reproduce
- Potential impact
- Suggested fix (if any)

---

## Response Timeline

1. **Acknowledgment**: Within 48 hours
2. **Initial Assessment**: Within 5 business days
3. **Fix Development**: Depends on severity
4. **Public Disclosure**: After patch release

---

## Security Considerations

### PostgreSQL Security

**Hartonomous inherits PostgreSQL's security model:**

#### Authentication
```sql
-- Use strong authentication
ALTER USER hartonomous WITH PASSWORD 'strong-random-password';

-- Require SSL connections
ALTER SYSTEM SET ssl = on;
```

#### Authorization
```sql
-- Principle of least privilege
CREATE ROLE hartonomous_readonly;
GRANT SELECT ON ALL TABLES IN SCHEMA public TO hartonomous_readonly;

-- Revoke dangerous permissions
REVOKE DELETE, TRUNCATE ON atom FROM hartonomous_writer;
```

---

### PL/Python Security

**PL/Python can execute arbitrary Python code:**

#### Use plpython3u with caution
```sql
-- plpython3u = untrusted (can access filesystem, network)
-- Only grant to superusers
REVOKE ALL ON LANGUAGE plpython3u FROM PUBLIC;
```

#### Validate input
```python
# Inside PL/Python function
def validate_input(value):
    if not isinstance(value, (int, float)):
        raise ValueError("Invalid input type")
    if value < 0 or value > 1000:
        raise ValueError("Value out of range")
    return value
```

---

### Content-Addressable Storage

**SHA-256 hashing provides tamper-evidence:**

#### Benefits
- ? Any modification changes the hash
- ? Duplicate content detected automatically
- ? Immutable history via temporal versioning

#### Limitations
- ?? Does not encrypt data
- ?? Does not prevent unauthorized access
- ?? Still need proper access controls

---

### Network Security

#### Firewall PostgreSQL Port
```bash
# Allow only specific IPs
sudo ufw allow from 192.168.1.0/24 to any port 5432
sudo ufw deny 5432
```

#### Use SSL/TLS
```sql
-- Require encrypted connections
ALTER SYSTEM SET ssl = on;
ALTER SYSTEM SET ssl_cert_file = '/path/to/server.crt';
ALTER SYSTEM SET ssl_key_file = '/path/to/server.key';
```

#### VPN for Remote Access
```bash
# Use WireGuard or OpenVPN
# Don't expose PostgreSQL directly to internet
```

---

### Data Protection

#### Encryption at Rest
```bash
# Use encrypted filesystem (LUKS, BitLocker)
cryptsetup luksFormat /dev/sdb
cryptsetup open /dev/sdb hartonomous_data
mkfs.ext4 /dev/mapper/hartonomous_data
```

#### Encryption in Transit
```sql
-- Force SSL for specific users
ALTER USER hartonomous_remote SSLMODE require;
```

#### Backup Encryption
```bash
# Encrypt backups with GPG
pg_dump hartonomous | gpg --encrypt --recipient admin@example.com > backup.sql.gpg
```

---

### Audit Logging

#### Enable PostgreSQL Audit
```sql
-- Log all queries
ALTER SYSTEM SET log_statement = 'all';

-- Log connections
ALTER SYSTEM SET log_connections = on;
ALTER SYSTEM SET log_disconnections = on;

-- Reload config
SELECT pg_reload_conf();
```

#### Monitor Logs
```bash
# Watch PostgreSQL logs
tail -f /var/log/postgresql/postgresql-15-main.log

# Look for suspicious activity
grep "FATAL" /var/log/postgresql/*.log
```

---

### Known Security Considerations

#### 1. PL/Python Code Execution

**Risk:** PL/Python functions can execute arbitrary Python code.

**Mitigation:**
- Only grant `plpython3u` to trusted users
- Review all PL/Python functions before deployment
- Use `plpython3u` (untrusted) instead of `plpythonu` (trusted)

---

#### 2. SQL Injection

**Risk:** User input in SQL queries can allow injection attacks.

**Mitigation:**
```sql
-- ? BAD: Concatenation
EXECUTE 'SELECT * FROM atom WHERE canonical_text = ' || user_input;

-- ? GOOD: Parameterized query
EXECUTE 'SELECT * FROM atom WHERE canonical_text = $1' USING user_input;
```

---

#### 3. Denial of Service

**Risk:** Expensive queries can overload the database.

**Mitigation:**
```sql
-- Set query timeouts
SET statement_timeout = '30s';

-- Limit connections
ALTER SYSTEM SET max_connections = 100;

-- Use connection pooling
-- (PgBouncer, pgpool-II)
```

---

#### 4. Data Leakage via Provenance

**Risk:** Provenance tracking reveals data lineage.

**Mitigation:**
- Implement row-level security for sensitive atoms
- Redact provenance for compliance (GDPR right to be forgotten)
- Use separate databases for different security domains

```sql
-- Row-level security
CREATE POLICY sensitive_atoms ON atom
    FOR SELECT
    USING (metadata->>'security_level' <= current_user_security_level());
```

---

## Compliance

### GDPR (General Data Protection Regulation)

**Right to be Forgotten:**
```sql
-- Delete user data (soft delete via temporal tables)
UPDATE atom SET valid_to = NOW()
WHERE metadata->>'user_id' = 'user123';

-- Hard delete (use with caution)
DELETE FROM atom WHERE metadata->>'user_id' = 'user123';
```

**Data Portability:**
```sql
-- Export user data
SELECT * FROM atom WHERE metadata->>'user_id' = 'user123'
INTO OUTFILE '/export/user123.json' FORMAT JSON;
```

---

### HIPAA (Health Insurance Portability and Accountability Act)

**Audit Trails:**
- Enable PostgreSQL audit logging
- Track all access to PHI (Protected Health Information)

**Access Controls:**
- Role-based access control (RBAC)
- Principle of least privilege

**Encryption:**
- Encrypt at rest (filesystem encryption)
- Encrypt in transit (SSL/TLS)

---

### SOC 2

**Security:** 
- Multi-factor authentication (MFA)
- Regular security audits
- Incident response plan

**Availability:**
- High availability (replication)
- Backup and recovery procedures

**Confidentiality:**
- Encryption at rest and in transit
- Access logging

---

## Security Best Practices

### 1. Principle of Least Privilege

```sql
-- Create role with minimal permissions
CREATE ROLE app_user;
GRANT SELECT, INSERT, UPDATE ON atom TO app_user;
REVOKE DELETE ON atom FROM app_user;
```

---

### 2. Regular Updates

```bash
# Keep PostgreSQL updated
sudo apt update
sudo apt upgrade postgresql-15

# Keep extensions updated
sudo apt upgrade postgresql-15-postgis-3
sudo apt upgrade postgresql-15-age
```

---

### 3. Secure Configuration

```sql
-- Disable dangerous features
ALTER SYSTEM SET allow_system_table_mods = off;

-- Limit connection sources
-- Edit pg_hba.conf:
# host    all    all    192.168.1.0/24    scram-sha-256
```

---

### 4. Regular Backups

```bash
# Automated backups
pg_dump -Fc hartonomous > backup-$(date +%Y%m%d).dump

# Store off-site (encrypted)
gpg --encrypt backup-*.dump
aws s3 cp backup-*.dump.gpg s3://backups/
```

---

### 5. Monitoring

```bash
# Monitor for suspicious activity
# - Failed login attempts
# - Unusual query patterns
# - High resource usage
# - Unauthorized access attempts

# Set up alerts
# (Prometheus, Grafana, PagerDuty)
```

---

## Contact

**Security Team:** aharttn@gmail.com  
**PGP Key:** [View on keybase.io](https://keybase.io/aharttn) *(if available)*

---

## Acknowledgments

We thank the following security researchers for responsibly disclosing vulnerabilities:

*(None yet - be the first!)*

---

**Last Updated:** 2025-01-25  
**Version:** 1.0
