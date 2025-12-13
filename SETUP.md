# Hartonomous Development Environment Setup

## Prerequisites

### Windows
```powershell
# Install PostgreSQL 16
winget install PostgreSQL.PostgreSQL.16

# Install PostGIS
# Download from: https://postgis.net/windows_downloads/
# Run installer and select PostgreSQL 16

# Install Rust
winget install Rustlang.Rustup

# Install Visual Studio Build Tools (for C++ compilation)
winget install Microsoft.VisualStudio.2022.BuildTools

# Install Python 3.11+
winget install Python.Python.3.11
```

### Linux (Ubuntu/Debian)
```bash
# PostgreSQL + PostGIS
sudo apt update
sudo apt install postgresql-16 postgresql-16-postgis-3 postgresql-server-dev-16

# Rust
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh

# C++ build tools
sudo apt install build-essential libeigen3-dev

# Python
sudo apt install python3.11 python3-pip
```

### macOS
```bash
# PostgreSQL + PostGIS
brew install postgresql@16 postgis

# Rust
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh

# C++ build tools
brew install eigen

# Python
brew install python@3.11
```

## Environment Variables

```powershell
# Windows (PowerShell)
$env:PGHOST = "localhost"
$env:PGPORT = "5432"
$env:PGDATABASE = "hartonomous"
$env:PGUSER = "postgres"
$env:PGPASSWORD = "your_password"
```

```bash
# Linux/macOS
export PGHOST=localhost
export PGPORT=5432
export PGDATABASE=hartonomous
export PGUSER=postgres
export PGPASSWORD=your_password
```

## Quick Start

```powershell
# Clone repository
git clone <repo_url>
cd Hartonomous

# Run integration test (does everything)
.\scripts\run_integration_test.ps1
```

## Component-by-Component Setup

### 1. Database
```powershell
.\scripts\setup_database.ps1
psql -d hartonomous -f database\test_data.sql
```

### 2. Shader (Rust)
```powershell
cd shader
cargo build --release
cargo test
cd ..
```

### 3. Cortex (C Extension)
```bash
# Linux/macOS only
cd cortex
make
sudo make install
psql -d hartonomous -c "CREATE EXTENSION cortex;"
cd ..
```

### 4. Python Connector
```powershell
cd connector
pip install -r requirements.txt
cd ..
python -m unittest discover tests
```

## Verification

```powershell
# Check database
psql -d hartonomous -c "SELECT COUNT(*) FROM atom;"

# Check Shader
.\shader\target\release\hartonomous-shader.exe --help

# Check Cortex (Linux/macOS)
psql -d hartonomous -c "SELECT cortex_cycle_once();"

# Check Python connector
python -c "from connector import Hartonomous; h = Hartonomous(); print(h.status()); h.close()"
```

## Troubleshooting

### PostgreSQL Connection Failed
- Verify service is running: `Get-Service postgresql*`
- Check `pg_hba.conf` for auth settings
- Verify environment variables

### Shader Build Failed
- Update Rust: `rustup update`
- Check Cargo.toml dependencies
- Run `cargo clean` and retry

### Cortex Build Failed
- Install Eigen: `sudo apt install libeigen3-dev` (Linux)
- Check PostgreSQL dev headers: `pg_config --includedir`
- Verify PostgreSQL version >= 16

### Python Tests Failed
- Install dependencies: `pip install -r connector/requirements.txt`
- Populate test data: `psql -d hartonomous -f database/test_data.sql`
- Check database connection with psql first

## IDE Setup

### VS Code
```json
{
  "rust-analyzer.cargo.features": "all",
  "python.linting.enabled": true,
  "python.linting.pylintEnabled": true,
  "files.associations": {
    "*.sql": "postgres"
  }
}
```

### PyCharm
- Mark `connector` as Sources Root
- Configure Python interpreter (3.11+)
- Add PostgreSQL data source

## Performance Tuning

### PostgreSQL (postgresql.conf)
```ini
shared_buffers = 4GB
effective_cache_size = 12GB
maintenance_work_mem = 1GB
random_page_cost = 1.1
effective_io_concurrency = 200
max_parallel_workers_per_gather = 4
```

### Connection Pooling
```python
# connector/pool.py default settings
min_connections = 2
max_connections = 10

# Increase for production
HartonomousPool(min_connections=10, max_connections=50)
```
