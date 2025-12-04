# Testing Infrastructure Documentation

## Problem Statement

The Hartonomous project had multiple connection patterns, inconsistent atomizer initialization, and no centralized fixture management. This led to:

1. **Multiple connection methods**: Direct `get_connection()`, pool creation in tests, manual connection strings
2. **Import inconsistency**: `from api.services.model_atomization` vs `from api.services.geometric_atomization`
3. **Atomizer initialization chaos**: Different tests creating atomizers with different parameters
4. **No connection pool management**: Each test creating its own connections

## Solution: Centralized Fixtures

### Database Connection Hierarchy

```
.env file (PGHOST=localhost, PGPORT=5432)
    ↓
tests/fixtures/database.py
    ↓
    ├── db_pool (session-scoped connection pool)
    ├── db_connection (session-scoped, schema-validated)
    └── clean_db (function-scoped, cleans before each test)
```

### Atomizer Fixtures

```
tests/fixtures/atomizers.py
    ├── fractal_atomizer (with db_connection)
    ├── gguf_atomizer (standalone, creates internal FractalAtomizer)
    ├── geometric_atomizer (with db_connection)
    ├── bpe_crystallizer (standalone)
    └── trajectory_builder (standalone)
```

### Import Paths (Standardized)

**ALWAYS use**: `from api.services.geometric_atomization import <Class>`

**DEPRECATED**: `from api.services.model_atomization import GGUFAtomizer`
- This still works (re-exports) but is legacy

## Environment Configuration

### Testing: Localhost PostgreSQL
- **Host**: localhost
- **Port**: 5432
- **Database**: hartonomous
- **User**: hartonomous (from .env)
- **Password**: Revolutionary-AI-2025!Geometry (from .env)

### Docker: Containerized PostgreSQL (for API/production)
- **Host**: postgres (container name)
- **Port**: 5432 (mapped to host 5432)
- **Database**: hartonomous
- **User**: hartonomous (from docker-compose.yml env vars)

**KEY DISTINCTION**: Tests use localhost DB, Docker API uses containerized DB. They are SEPARATE databases on the same port (one via localhost, one via Docker network).

## Usage in Tests

### Basic Test Pattern

```python
import pytest
from api.services.geometric_atomization import FractalAtomizer

class TestMyFeature:
    async def test_atomization(self, db_connection, clean_db, fractal_atomizer):
        """Test using standard fixtures."""
        # fractal_atomizer is already connected to db_connection
        atom_id = await fractal_atomizer.get_or_create_primitive(
            value=b"test",
            modality="text"
        )
        assert atom_id > 0
```

### GGUF Atomizer Pattern

```python
async def test_gguf_ingestion(self, db_connection, db_pool, clean_db, gguf_atomizer):
    """Test GGUF model ingestion."""
    result = await gguf_atomizer.atomize_model(
        file_path=test_model_path,
        model_name="test-model",
        conn=db_connection,  # Pass connection explicitly
        pool=db_pool         # Pass pool explicitly
    )
    assert result["atoms_created"] > 0
```

## Migration Guide

### Old Pattern (DON'T USE)
```python
from api.services.model_atomization import GGUFAtomizer  # Legacy
from api.core.db_helpers import get_connection
from api.config import settings

conn = await get_connection(settings.get_connection_string())  # Manual
atomizer = GGUFAtomizer(threshold=0.01, parallel_processing=True)
```

### New Pattern (USE THIS)
```python
from api.services.geometric_atomization import GGUFAtomizer  # Standard

# In test:
async def test_something(self, db_connection, gguf_atomizer):
    # Fixtures provide everything
    result = await gguf_atomizer.atomize_model(
        file_path=path,
        model_name="test",
        conn=db_connection,  # From fixture
        pool=None  # Optional
    )
```

## Fixture Scope Strategy

- **session**: `db_pool`, `event_loop`, path fixtures (expensive setup)
- **session**: `db_connection` (validated once, reused across tests)
- **function**: `clean_db` (ensures test isolation)
- **function**: atomizer fixtures (lightweight, test-specific)

## Benefits

1. **Single source of truth**: All connections come from fixtures
2. **Consistent initialization**: Atomizers always properly configured
3. **Easy testing**: Just inject fixtures, no setup code
4. **Clear separation**: localhost (tests) vs Docker (production API)
5. **Performance**: Session-scoped pool/connection, function-scoped cleanup
6. **Maintainability**: Change connection logic once, affects all tests

## Troubleshooting

### "Database not initialized" error
```bash
.\scripts\Initialize-Database.ps1
```

### "Connection refused" error
- Check localhost PostgreSQL is running: `psql -h localhost -U hartonomous -d hartonomous`
- Verify .env file has correct credentials
- Confirm not conflicting with Docker postgres (different networks)

### "Import not found" error
- Use `from api.services.geometric_atomization import <Class>`
- NOT `from api.services.model_atomization`

### Tests passing individually but failing together
- Check `clean_db` fixture is used
- Verify no shared state between tests
- Ensure atomizers don't persist across tests

## Next Steps

1. Update all tests to use centralized fixtures
2. Remove manual connection creation from tests
3. Standardize all imports to geometric_atomization
4. Add integration tests for Docker environment (separate suite)
5. Document Docker testing strategy for API tests
