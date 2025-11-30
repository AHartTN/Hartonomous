# Hartonomous Enterprise Test Suite

Enterprise-grade testing infrastructure with CI/CD integration for the Hartonomous codebase atomization platform.

## Test Structure

```
tests/
├── smoke/          # Quick validation tests (< 1 min)
│   ├── test_imports.py         # Import validation
│   └── test_connection.py      # Database connection checks
│
├── unit/           # Fast isolated tests (< 5 min)
│   ├── test_auth.py            # Authentication unit tests
│   ├── test_parser.py          # Parser unit tests
│   └── ...
│
├── integration/    # Database & service tests (< 15 min)
│   ├── test_gguf_ingestion.py  # GGUF model ingestion
│   ├── test_gguf_tokens.py     # GGUF tokenization
│   ├── test_implementation.py  # Implementation tests
│   └── ...
│
├── functional/     # End-to-end tests (< 30 min)
│   ├── test_compression.py     # Compression functionality
│   ├── test_hilbert.py         # Hilbert curve tests
│   └── test_positioning.py     # Spatial positioning tests
│
├── performance/    # Load & performance tests (variable)
│   ├── test_model_ingestion.py       # Model ingestion performance
│   ├── test_safetensors_ingestion.py # SafeTensors performance
│   └── test_semantic_embeddings.py   # Embedding performance
│
└── sql/            # PostgreSQL function tests
    └── test_functions.py       # SQL function validation
```

## Test Categories

### Smoke Tests (< 1 min)
Quick validation tests to ensure the system is minimally functional.

**Purpose:**
- Validate critical imports
- Check database connectivity
- Verify essential service health

**When to run:**
- Before committing code
- First step in CI/CD pipeline
- Quick developer sanity checks

**Commands:**
```bash
# Run smoke tests only
pytest tests/smoke -m smoke

# Or using test runner
python run_tests.py smoke
```

### Unit Tests (< 5 min)
Fast, isolated tests for individual functions and classes.

**Purpose:**
- Test business logic in isolation
- Validate edge cases
- High code coverage

**When to run:**
- During active development
- In CI/CD pipelines
- Before integration tests

**Commands:**
```bash
# Run unit tests with coverage
pytest tests/unit -m unit --cov=api --cov=src --cov-report=html

# Or using test runner
python run_tests.py unit --coverage -v
```

### Integration Tests (< 15 min)
Tests that verify interactions between components and external services.

**Purpose:**
- Database operations
- API endpoint validation
- Service integration verification

**When to run:**
- After unit tests pass
- Before functional tests
- On pull requests

**Commands:**
```bash
# Run integration tests
pytest tests/integration -m integration

# Or using test runner
python run_tests.py integration
```

### Functional Tests (< 30 min)
End-to-end tests that validate complete features.

**Purpose:**
- Compression pipeline validation
- Hilbert curve calculations
- Spatial positioning accuracy
- Complete feature workflows

**When to run:**
- Before releases
- On main/develop branches
- For critical features

**Commands:**
```bash
# Run functional tests
pytest tests/functional -m functional

# Or using test runner
python run_tests.py functional
```

### SQL Tests
Tests for PostgreSQL functions, triggers, and schema operations.

**Purpose:**
- Validate SQL functions
- Test database triggers
- Verify schema integrity

**When to run:**
- After schema changes
- With integration tests
- Before database migrations

**Commands:**
```bash
# Run SQL tests
pytest tests/sql -m sql

# Or using test runner
python run_tests.py sql
```

### Performance Tests (Variable Duration)
Load testing and performance benchmarking.

**Purpose:**
- Model ingestion performance
- SafeTensors loading benchmarks
- Semantic embedding performance
- Identify bottlenecks

**When to run:**
- Manually triggered
- On performance-critical PRs
- Before releases

**Commands:**
```bash
# Run performance tests
pytest tests/performance

# Or using test runner
python run_tests.py performance -v
```

## Running Tests

### Using pytest directly

```bash
# Run all tests
pytest tests/

# Run specific test category
pytest tests/smoke -m smoke
pytest tests/unit -m unit
pytest tests/integration -m integration

# Run with coverage
pytest tests/ --cov=api --cov=src --cov-report=html

# Run in parallel (faster)
pytest tests/unit -n auto

# Run specific test file
pytest tests/smoke/test_imports.py

# Run specific test function
pytest tests/smoke/test_imports.py::test_api_imports -v
```

### Using test runner script

The `run_tests.py` script provides convenient test execution modes:

```bash
# Quick smoke tests (< 1 min)
python run_tests.py smoke

# Unit tests only
python run_tests.py unit

# Unit tests with coverage
python run_tests.py unit --coverage

# CI/CD mode (smoke + unit)
python run_tests.py ci

# Full test suite
python run_tests.py all --coverage

# Verbose output
python run_tests.py smoke -v
```

### Available modes:
- `smoke` - Quick validation tests
- `unit` - Fast isolated tests
- `integration` - Database and service tests
- `functional` - End-to-end tests
- `sql` - PostgreSQL function tests
- `performance` - Load and performance tests
- `ci` - CI/CD suitable tests (smoke + unit)
- `all` - Complete test suite (default)

## CI/CD Integration

The test suite integrates with Azure Pipelines for automated testing.

### Pipeline Stages

1. **Smoke Tests** (< 1 min)
   - Fast validation
   - Fails fast on critical issues
   - Runs on every commit

2. **Unit Tests** (< 5 min)
   - Comprehensive unit testing
   - Code coverage reporting
   - Parallel execution

3. **Integration Tests** (< 15 min)
   - Database integration
   - Service communication
   - PostgreSQL container

4. **SQL Tests** (< 15 min)
   - Function validation
   - Schema integrity

5. **Functional Tests** (< 30 min)
   - End-to-end workflows
   - Feature validation

6. **Performance Tests** (Manual Trigger)
   - Load testing
   - Benchmark tracking
   - Only on explicit request

### Pipeline Configuration

See `azure-pipelines.yml` for complete pipeline configuration.

**Trigger branches:**
- `main`
- `develop`
- `feature/*`

**Pull Request validation:**
- Runs smoke, unit, integration, and SQL tests
- Requires all tests to pass before merge

## Test Markers

Tests are categorized using pytest markers:

```python
@pytest.mark.smoke      # Quick validation tests
@pytest.mark.unit       # Unit tests
@pytest.mark.integration # Integration tests
@pytest.mark.functional # Functional tests
@pytest.mark.sql        # SQL tests
@pytest.mark.slow       # Slow-running tests
@pytest.mark.asyncio    # Async tests
@pytest.mark.gguf       # GGUF-related tests
@pytest.mark.safetensors # SafeTensors tests
@pytest.mark.spatial    # Spatial positioning tests
@pytest.mark.compression # Compression tests
@pytest.mark.gpu        # GPU-required tests
@pytest.mark.cicd       # CI/CD specific tests
```

### Running tests by marker

```bash
# Run only smoke tests
pytest -m smoke

# Run integration or functional tests
pytest -m "integration or functional"

# Skip slow tests
pytest -m "not slow"

# Run GGUF-related tests only
pytest -m gguf
```

## Coverage Reports

Coverage reports are generated in HTML format:

```bash
# Generate coverage report
pytest tests/ --cov=api --cov=src --cov-report=html

# Open coverage report
# Location: htmlcov/index.html
```

**Coverage targets:**
- Smoke tests: N/A (validation only)
- Unit tests: > 80% coverage
- Integration tests: > 70% coverage
- Overall: > 75% coverage

## Writing New Tests

### Test File Naming
- Prefix all test files with `test_`
- Use descriptive names: `test_hilbert_curve.py`

### Test Function Naming
- Prefix all test functions with `test_`
- Use descriptive names: `test_compress_large_model()`

### Example Test Structure

```python
"""Tests for Hilbert curve calculations."""
import pytest
from src.hilbert import HilbertCurve


@pytest.mark.unit
def test_hilbert_curve_2d():
    """Test 2D Hilbert curve calculation."""
    curve = HilbertCurve(n=2, dimensions=2)
    point = curve.point_from_distance(5)
    assert len(point) == 2
    assert all(0 <= p < 4 for p in point)


@pytest.mark.integration
@pytest.mark.asyncio
async def test_hilbert_database_storage(db_session):
    """Test storing Hilbert coordinates in database."""
    curve = HilbertCurve(n=3, dimensions=3)
    point = curve.point_from_distance(10)
    
    # Store in database
    result = await store_hilbert_point(db_session, point)
    assert result.id is not None
```

## Best Practices

1. **Keep tests fast**: Unit tests should run in milliseconds
2. **Isolate tests**: No dependencies between tests
3. **Use fixtures**: Share setup code via conftest.py
4. **Clear assertions**: Use descriptive assertion messages
5. **Mock external services**: Don't call real APIs in tests
6. **Test edge cases**: Include boundary conditions
7. **Document complex tests**: Add docstrings explaining "why"

## Fixtures

Common fixtures are defined in `tests/conftest.py`:

- `db_session` - Database session for integration tests
- `test_client` - FastAPI test client
- `mock_model_data` - Sample model data for testing
- `temp_dir` - Temporary directory for file operations

## Troubleshooting

### Tests fail locally but pass in CI
- Check environment variables
- Verify Python version matches CI (3.11)
- Ensure database is running for integration tests

### Tests are slow
- Run unit tests only: `python run_tests.py unit`
- Use parallel execution: `pytest -n auto`
- Skip slow tests: `pytest -m "not slow"`

### Import errors
- Run smoke tests first: `python run_tests.py smoke`
- Check PYTHONPATH includes project root
- Verify all dependencies installed

### Database connection errors
- Ensure PostgreSQL is running
- Check DATABASE_URL environment variable
- Verify database credentials

## Contributing

When adding new features:

1. Write tests first (TDD approach)
2. Ensure tests pass locally before pushing
3. Add appropriate test markers
4. Update this README if adding new test categories
5. Aim for > 80% code coverage

## Support

For questions or issues with the test suite:
- Check existing test examples
- Review pytest documentation: https://docs.pytest.org/
- Contact: Anthony Hart

---

**Copyright (c) 2025 Anthony Hart. All Rights Reserved.**
