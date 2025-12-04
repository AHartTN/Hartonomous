# Quick Reference: RBAR → Bulk Patterns

## Python Patterns

### ❌ RBAR (Row-By-Agonizing-Row)
```python
for item in items:
    await cursor.execute("SELECT func(%s)", (item,))
    result = await cursor.fetchone()
    results.append(result[0])
```

### ✅ Bulk (UNNEST)
```python
await cursor.execute(
    "SELECT func(val) FROM UNNEST(%s::type[]) AS val",
    (items,)
)
results = [row[0] for row in await cursor.fetchall()]
```

---

## SQL Patterns

### ❌ FOR Loop
```sql
FOR i IN 1..array_length(arr, 1) LOOP
    result := array_append(result, process(arr[i]));
END LOOP;
```

### ✅ Set-Based (unnest)
```sql
SELECT array_agg(process(val))
FROM unnest(arr) AS val;
```

### ❌ FOR Loop with WHERE
```sql
FOR i IN 1..array_length(arr, 1) LOOP
    IF arr[i] > threshold THEN
        result := array_append(result, arr[i]);
    END IF;
END LOOP;
```

### ✅ Set-Based (WHERE clause)
```sql
SELECT array_agg(val)
FROM unnest(arr) WITH ORDINALITY AS t(val, idx)
WHERE val > threshold;
```

### ❌ Cumulative Calculation
```sql
FOR i IN 2..array_length(arr, 1) LOOP
    deltas := array_append(deltas, arr[i] - arr[i-1]);
END LOOP;
```

### ✅ Window Function (LAG)
```sql
SELECT array_agg(val - prev_val)
FROM (
    SELECT val, LAG(val) OVER (ORDER BY idx) AS prev_val
    FROM unnest(arr) WITH ORDINALITY AS t(val, idx)
) WHERE prev_val IS NOT NULL;
```

### ❌ Run-Length Encoding Loop
```sql
FOR i IN 1..array_length(arr, 1) LOOP
    IF arr[i] = current_val THEN
        count := count + 1;
    ELSE
        -- Store run, start new run
    END IF;
END LOOP;
```

### ✅ Window Function (Boundary Detection)
```sql
WITH boundaries AS (
    SELECT val, 
        CASE WHEN val <> LAG(val) OVER (ORDER BY idx) THEN 1 ELSE 0 END AS is_new_run
    FROM unnest(arr) WITH ORDINALITY AS t(val, idx)
),
runs AS (
    SELECT val, SUM(is_new_run) OVER (ORDER BY idx) AS run_id
    FROM boundaries
)
SELECT jsonb_agg(jsonb_build_object('value', MIN(val), 'count', COUNT(*)))
FROM runs GROUP BY run_id;
```

---

## Key Takeaways

1. **UNNEST is your friend**: Convert arrays to sets for processing
2. **Window functions replace loops**: LAG, LEAD, SUM() OVER for cumulative operations
3. **Bulk INSERT**: `INSERT ... SELECT FROM UNNEST()` instead of loop INSERT
4. **List comprehensions with await are RBAR**: Use batch methods instead
5. **Method names lie**: "batch" in name doesn't mean it's truly batched - verify implementation

---

## Performance Impact

| Pattern | Before | After | Improvement |
|---------|--------|-------|-------------|
| Image patches (4096) | 8,192 calls | 2 calls | 4,096x |
| Pixels (1M) | ~2M calls | ~8K calls | 256x |
| Relations (1000) | 1000 queries | 1 query | 1000x |
| Sparse encoding | O(N) loop | O(N) set scan | 2-5x |

---

## When Loops Are OK

1. **Algorithmic necessity**: Hilbert curves, Gram-Schmidt (each step depends on previous)
2. **In-memory operations**: Python list comprehensions without `await`
3. **Small fixed-size loops**: `for i in range(3)` with no DB calls

---

**Rule of Thumb**: If it touches the database, batch it. If it processes arrays in SQL, use sets.
