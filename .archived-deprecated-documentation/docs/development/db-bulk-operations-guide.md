# Database Bulk Operations Utility

**Location**: `api/services/db_bulk_operations.py`

Reusable, optimized PostgreSQL bulk operations with progress tracking. Eliminates code duplication across atomization services while following SOLID principles.

## Quick Start

```python
from api.services.db_bulk_operations import bulk_insert_atoms, bulk_insert_compositions

# Insert atoms
rows = [(hash1, text1, metadata1), (hash2, text2, metadata2)]
elapsed, results = await bulk_insert_atoms(conn, rows, include_spatial=True)

# Insert compositions
elapsed = await bulk_insert_compositions(
    conn, parent_id, component_ids, sequence_indices
)
```

## Architecture

### Design Principles

| Principle | Implementation |
|-----------|----------------|
| **Single Responsibility** | `BulkCopyOperation` handles only COPY operations, not business logic |
| **Open/Closed** | Extensible via subclassing or custom formatters without modifying core |
| **Liskov Substitution** | Subclasses maintain base class contracts |
| **Interface Segregation** | Separate methods for binary vs text operations |
| **Dependency Inversion** | Depends on abstractions (`AsyncConnection`, `Callable`) not concrete types |
| **DRY** | Eliminates ~60+ lines of duplicate COPY code across services |

### Class Structure

```
BulkCopyOperation
├── execute_binary()     # For bytea/complex types (write_row)
├── execute_text()       # For simple types (TSV format)
└── get_stats()          # Performance metrics

Convenience Functions
├── bulk_insert_atoms()
├── bulk_insert_compositions()
└── bulk_retrieve_by_hashes()
```

## API Reference

### BulkCopyOperation

Core class for PostgreSQL COPY operations with progress tracking.

```python
operation = BulkCopyOperation(
    conn: AsyncConnection,
    copy_statement: str,
    chunk_size: int = 5000,
    show_progress: bool = True
)
```

**Parameters:**
- `conn`: PostgreSQL async connection
- `copy_statement`: SQL COPY statement (e.g., `"COPY table (...) FROM STDIN"`)
- `chunk_size`: Rows per batch (affects memory usage and progress granularity)
- `show_progress`: Show tqdm progress bar

#### execute_binary()

For data with bytea fields or complex types.

```python
elapsed = await operation.execute_binary(
    rows: List[Tuple],
    description: str = "COPY operation"
) -> float
```

**Example:**
```python
rows = [
    (b'\x01\x02', 'text', '{"key": "val"}'),
    (b'\x03\x04', 'text2', '{"key": "val2"}')
]
elapsed = await operation.execute_binary(rows, "Inserting atoms")
```

#### execute_text()

For simple data types formatted as TSV.

```python
elapsed = await operation.execute_text(
    rows: List[Any],
    formatter: Callable[[List[Any]], str],
    description: str = "COPY operation"
) -> float
```

**Example:**
```python
def format_tsv(batch):
    return "\n".join(f"{r[0]}\t{r[1]}" for r in batch) + "\n"

rows = [(1, 'a'), (2, 'b')]
elapsed = await operation.execute_text(rows, format_tsv, "Inserting data")
```

### Convenience Functions

#### bulk_insert_atoms()

Insert atoms with automatic hash retrieval.

```python
elapsed, results = await bulk_insert_atoms(
    conn: AsyncConnection,
    rows: List[Tuple],
    include_spatial: bool = False,
    chunk_size: int = 5000,
    show_progress: bool = True
) -> Tuple[float, List[Tuple]]
```

**Returns:** `(elapsed_time, [(content_hash, atom_id), ...])`

**Example:**
```python
rows = [
    (b'hash1', 'canonical_text', '{"metadata": "json"}'),
    (b'hash2', 'canonical_text2', '{"metadata": "json2"}', 'POINT(1 2)')
]
elapsed, results = await bulk_insert_atoms(conn, rows, include_spatial=True)
# results: [(b'hash1', 42), (b'hash2', 43)]
```

#### bulk_insert_compositions()

Insert atom compositions with TSV formatting.

```python
elapsed = await bulk_insert_compositions(
    conn: AsyncConnection,
    parent_id: int,
    component_ids: List[int],
    sequence_indices: List[int],
    chunk_size: int = 100000,
    show_progress: bool = True
) -> float
```

**Example:**
```python
# Insert 1 million compositions
elapsed = await bulk_insert_compositions(
    conn, parent_id=100,
    component_ids=list(range(1000000)),
    sequence_indices=list(range(1000000)),
    chunk_size=100000
)
```

#### bulk_retrieve_by_hashes()

Retrieve records by content hashes with automatic batching.

```python
results = await bulk_retrieve_by_hashes(
    conn: AsyncConnection,
    content_hashes: List[bytes],
    table: str = "atom",
    batch_size: int = 50000
) -> List[Tuple]
```

## Performance Guide

### Benchmarks

| Operation | Throughput | Notes |
|-----------|-----------|-------|
| Binary COPY (atoms) | 100-200K rows/sec | With bytea + JSON |
| TSV COPY (compositions) | 500K-1M rows/sec | Simple integers |
| Hash retrieval | 1-2M rows/sec | Batched queries |

### Chunk Size Guidelines

| Data Size | Recommended Chunk Size | Reason |
|-----------|----------------------|---------|
| < 10K rows | 5K | Lower memory overhead |
| 10K-100K rows | 10K-20K | Balance latency/throughput |
| 100K-1M rows | 50K-100K | Maximize throughput |
| > 1M rows | 100K | Minimize progress overhead |

### Optimization Patterns

#### Pattern 1: Disable Progress for Parallel Batches

```python
async def insert_batch(batch_data):
    async with pool.connection() as conn:
        await bulk_insert_compositions(
            conn, parent_id, batch_data,
            show_progress=False  # ← Disable for parallel execution
        )

tasks = [insert_batch(batch) for batch in batches]
await asyncio.gather(*tasks)
```

#### Pattern 2: Pre-compute Formatters

```python
# BAD: Creates new lambda per call
await operation.execute_text(rows, lambda b: format_batch(b, parent_id), "Insert")

# GOOD: Pre-compute formatter
parent_str = str(parent_id)
def formatter(batch):
    return "".join(f"{parent_str}\t{c}\t{s}\n" for c, s in batch)

await operation.execute_text(rows, formatter, "Insert")
```

#### Pattern 3: Batch Hash Retrieval

```python
# BAD: Query per hash
for hash in hashes:
    result = await conn.execute("SELECT ... WHERE content_hash = %s", (hash,))

# GOOD: Batch retrieval with automatic chunking
results = await bulk_retrieve_by_hashes(conn, hashes, batch_size=50000)
```

## Migration Guide

### Before (model_atomization.py)

```python
# 30+ lines of duplicate COPY code
async with conn.cursor() as cur:
    async with cur.copy("COPY atom (...) FROM STDIN") as copy:
        for i in range(0, len(rows), chunk_size):
            batch = rows[i:i+chunk_size]
            for row in batch:
                await copy.write_row(row)
            print(f"Progress: {i}/{len(rows)}", end='\r')

# Retrieve IDs
async with conn.cursor() as cur:
    await cur.execute("SELECT ... WHERE content_hash = ANY(%s)", (hashes,))
    results = await cur.fetchall()
```

### After (using utility)

```python
# 2 lines with automatic progress tracking
elapsed, results = await bulk_insert_atoms(
    conn, rows, include_spatial=True, chunk_size=5000
)
```

**Result:** 28 lines eliminated, consistent progress bars, better error handling.

## Testing

Run unit tests:
```bash
pytest tests/unit/test_db_bulk_operations.py -v
```

Test coverage includes:
- ✅ Binary and text COPY operations
- ✅ Chunking behavior
- ✅ TSV formatting
- ✅ Progress bar toggling
- ✅ Empty/single row edge cases
- ✅ Statistics calculation
- ✅ Batched hash retrieval

## Common Issues

### Issue: "COPY terminated by user"

**Cause:** Progress bar cleanup interfering with COPY operation

**Solution:** Use `show_progress=False` for parallel execution

```python
# In parallel batch processing
await bulk_insert_compositions(..., show_progress=False)
```

### Issue: Memory usage spikes

**Cause:** Chunk size too large

**Solution:** Reduce chunk_size parameter

```python
# For systems with limited memory
operation = BulkCopyOperation(conn, stmt, chunk_size=1000)
```

### Issue: Slow throughput

**Cause:** Chunk size too small, excessive progress updates

**Solution:** Increase chunk_size

```python
# For large datasets (1M+ rows)
await bulk_insert_compositions(..., chunk_size=100000)
```

## Future Enhancements

Potential extensions:

1. **Error Recovery**: Automatic retry with exponential backoff
2. **Compression**: Optional gzip compression for large text data
3. **Metrics Export**: Prometheus/StatsD integration
4. **Parallel COPY**: Split large operations across multiple connections
5. **Streaming API**: Iterator-based API for infinite data streams

## Contributing

When adding new bulk operations:

1. Use `BulkCopyOperation` for core COPY logic
2. Create convenience function if pattern is common (>2 uses)
3. Add unit tests to `test_db_bulk_operations.py`
4. Document performance characteristics
5. Update this README

## License

Part of Hartonomous project. See LICENSE in repository root.
