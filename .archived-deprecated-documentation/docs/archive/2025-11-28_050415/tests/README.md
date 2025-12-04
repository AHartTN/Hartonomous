"""Test README."""
# Hartonomous Tests

Comprehensive test suite for Hartonomous atomization system.

## Structure

```
tests/
??? unit/              # Unit tests (no DB)
??? integration/       # Integration tests (with DB)
??? sql/               # SQL function tests
??? conftest.py        # Pytest fixtures
??? .env.test          # Test environment
```

## Running Tests

### All tests with coverage:
```bash
python run_tests.py
```

### Specific test file:
```bash
pytest tests/unit/test_compression.py -v
```

### SQL tests only:
```bash
pytest tests/sql/ -v
```

### Integration tests only:
```bash
pytest tests/integration/ -v
```

## Requirements

```bash
pip install -r requirements-test.txt
```

## Test Database

Tests require a PostgreSQL database with PostGIS:

```sql
CREATE DATABASE hartonomous_test;
\c hartonomous_test
CREATE EXTENSION postgis;
CREATE EXTENSION plpython3u;
```

Then run schema:
```bash
psql -d hartonomous_test -f schema/core/tables/*.sql
psql -d hartonomous_test -f schema/core/functions/**/*.sql
```

## Coverage

Target: 100% coverage of:
- ? SQL functions (87 functions)
- ? Python atomization code
- ? Parsers
- ? Compression
- ? Spatial functions

Current coverage report: `htmlcov/index.html`
