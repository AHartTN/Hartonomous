# Hartonomous Performance Tuning Guide

## PostgreSQL Configuration

### Memory Settings (postgresql.conf)

```ini
# Assuming 16GB RAM available for PostgreSQL

# Working memory for complex queries
work_mem = 256MB                 # Per operation (sort, hash)
maintenance_work_mem = 2GB       # VACUUM, CREATE INDEX

# Shared buffers - 25% of system RAM
shared_buffers = 4GB

# Effective cache size - OS + PG cache combined
effective_cache_size = 12GB

# Write-ahead log
wal_buffers = 16MB
checkpoint_timeout = 15min
checkpoint_completion_target = 0.9

# Query planner
random_page_cost = 1.1           # SSD assumption
effective_io_concurrency = 200   # SSD parallel I/O

# Connection pooling
max_connections = 100
