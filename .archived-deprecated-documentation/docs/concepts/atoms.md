# Atoms

**The fundamental unit of Hartonomous: ?64 bytes, content-addressed, globally deduplicated.**

---

## What Is an Atom?

An **atom** is any unique value ?64 bytes, stored exactly once, identified by its content hash.

```python
# Examples of atoms:
'H'              # Character (1 byte)
0.017            # Float (4 bytes)
0xFF5733         # RGB color (3 bytes)
b'\x00\x01...'   # Binary data (?64 bytes)
```

**Key principle**: **Same value anywhere = same atom.**

---

## The ?64 Byte Constraint

### Why 64 Bytes?

**Forcing function**: If data doesn't fit in 64 bytes, you must decompose it.

```python
# Too large ? Must decompose
"Hello World"  # 11 bytes, but treated as word
               # ? Decomposes to 11 character atoms

# Fits ? Single atom
'A'            # 1 byte ?
0.017          # 4 bytes ?
RGB(255,87,51) # 3 bytes ?
```

**The constraint enforces atomicity.** You cannot cheat.

### What Fits in 64 Bytes?

| Data Type | Example | Bytes | Atomic? |
|-----------|---------|-------|---------|
| **Character** | `'A'` | 1 | ? Yes |
| **Integer** | `42` | 4-8 | ? Yes |
| **Float** | `0.017` | 4 | ? Yes |
| **RGB Pixel** | `(255, 87, 51)` | 3 | ? Yes |
| **UUID** | `550e8400-...` | 16 | ? Yes |
| **IPv6** | `2001:db8::1` | 16 | ? Yes |
| **Audio Sample** | `0x3F800000` | 4 | ? Yes |
| **Short String** | `"Machine"` | 7 | ? No (decompose to chars) |
| **Vector** | `[0.23, 0.87]` | 8 | ? No (decompose to floats) |
| **Image** | 256�256 PNG | 65KB | ? No (decompose to pixels) |

---

## Content Addressing

### SHA-256 Hash as Identity

Every atom identified by `content_hash = SHA-256(atomic_value)`:

```sql
-- Insert atom
INSERT INTO atom (content_hash, atomic_value, canonical_text)
VALUES (
    sha256('A'),          -- content_hash
    'A'::bytea,           -- atomic_value
    'A'                   -- canonical_text (cached)
);

-- Deduplication: ON CONFLICT DO NOTHING
-- If hash exists ? atom exists ? no insert
```

**Benefits:**

1. **Global deduplication** � same value = same hash = same atom
2. **Deterministic** � same input always produces same hash
3. **Collision-resistant** � SHA-256 practically collision-free
4. **Cryptographically secure** � cannot reverse hash to value

### Example: Character 'e'

```sql
-- First insertion
INSERT INTO atom (content_hash, atomic_value, canonical_text)
VALUES (sha256('e'), 'e', 'e');
-- Result: atom_id=101, reference_count=1

-- Second insertion (from different document)
INSERT INTO atom (content_hash, atomic_value, canonical_text)
VALUES (sha256('e'), 'e', 'e')
ON CONFLICT (content_hash) DO UPDATE SET reference_count = atom.reference_count + 1;
-- Result: atom_id=101, reference_count=2 (reused!)
```

**1 billion occurrences of 'e' ? 1 atom, 1B references.**

---

## Atom Table Schema

### Complete DDL

```sql
CREATE TABLE atom (
    -- Identity
    atom_id BIGSERIAL PRIMARY KEY,
    
    -- Content addressing (global deduplication via SHA-256)
    content_hash BYTEA UNIQUE NOT NULL,
    
    -- The actual atomic value (?64 bytes enforced)
    atomic_value BYTEA CHECK (length(atomic_value) <= 64),
    
    -- Cached text representation for text atoms
    canonical_text TEXT,
    
    -- Spatial semantics - position in 3D semantic space
    spatial_key GEOMETRY(POINTZ, 0),
    
    -- Importance / atomic mass (how often referenced)
    reference_count BIGINT NOT NULL DEFAULT 1,
    
    -- Flexible metadata (modality, tenant, model_name, etc.)
    metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    
    -- Temporal versioning
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    valid_from TIMESTAMPTZ NOT NULL DEFAULT now(),
    valid_to TIMESTAMPTZ NOT NULL DEFAULT 'infinity'::timestamptz
);

-- Critical indexes
CREATE UNIQUE INDEX idx_atom_hash ON atom (content_hash);
CREATE INDEX idx_atom_spatial ON atom USING GIST (spatial_key);
CREATE INDEX idx_atom_reference_count ON atom (reference_count DESC);
CREATE INDEX idx_atom_metadata ON atom USING GIN (metadata);
```

### Field Descriptions

| Field | Type | Purpose |
|-------|------|---------|
| `atom_id` | BIGSERIAL | Unique identifier (auto-increment) |
| `content_hash` | BYTEA | SHA-256 hash (deduplication key) |
| `atomic_value` | BYTEA | Raw bytes (?64) |
| `canonical_text` | TEXT | Cached text representation (performance) |
| `spatial_key` | GEOMETRY(POINTZ) | 3D position in semantic space |
| `reference_count` | BIGINT | "Atomic mass" (usage frequency) |
| `metadata` | JSONB | Modality, tenant, model, etc. |
| `created_at` | TIMESTAMPTZ | When atom first created |
| `valid_from` / `valid_to` | TIMESTAMPTZ | Temporal versioning |

---

## Atomic Mass (Reference Count)

### What Is Reference Count?

**`reference_count`** = number of times atom is referenced/composed.

```sql
-- Common character
SELECT canonical_text, reference_count
FROM atom
WHERE canonical_text = 'e';
-- Result: reference_count=1,000,000 (very common)

-- Rare word
SELECT canonical_text, reference_count
FROM atom
WHERE canonical_text = 'xenophobia';
-- Result: reference_count=1 (rare)
```

**High reference count = high "atomic mass" = important atom.**

### Usage

**Ranking queries:**

```sql
-- Most important atoms
SELECT canonical_text, reference_count
FROM atom
ORDER BY reference_count DESC
LIMIT 10;

-- Result: 'e', 't', 'a', 'o', 'i', 'n', 's', 'h', 'r', 'd'
-- (Most common English letters)
```

**Filtering:**

```sql
-- Only common atoms (reference_count > 1000)
SELECT canonical_text FROM atom
WHERE reference_count > 1000;

-- Only unique atoms (reference_count = 1)
SELECT canonical_text FROM atom
WHERE reference_count = 1;
```

---

## Spatial Positioning

### Position = Meaning

Every atom has a position in **3D semantic space**:

```sql
atom.spatial_key = POINT Z(X, Y, Z)
```

**Where:**
- **X-axis**: Modality (text, image, audio)
- **Y-axis**: Category (animals, technology, etc.)
- **Z-axis**: Specificity (general ? specific)

**Close in space = similar in meaning.**

```sql
-- Find atoms near "cat"
SELECT canonical_text, ST_Distance(spatial_key, $cat_position)
FROM atom
ORDER BY distance ASC;

-- Result: cat (0.00), kitten (0.08), feline (0.12), dog (0.15), ...
```

**See [Spatial Semantics](spatial-semantics.md) for full explanation.**

---

## Metadata (JSONB)

### Flexible Attributes

`metadata` field stores modality, model, tenant, etc.:

```json
{
  "modality": "text",
  "language": "en",
  "model_name": "gpt-4",
  "tenant_id": "customer-123",
  "confidence": 0.95,
  "source": "wikipedia",
  "ingestion_timestamp": "2025-11-28T12:00:00Z"
}
```

### Query by Metadata

```sql
-- Find all text atoms
SELECT * FROM atom
WHERE metadata->>'modality' = 'text';

-- Find atoms from specific model
SELECT * FROM atom
WHERE metadata->>'model_name' = 'gpt-4';

-- Find high-confidence atoms
SELECT * FROM atom
WHERE (metadata->>'confidence')::numeric > 0.90;
```

**GIN index on `metadata` enables fast queries.**

---

## Temporal Versioning

### Bitemporal Tracking

`valid_from` and `valid_to` track atom versions:

```sql
-- Current atoms
SELECT * FROM atom
WHERE valid_to = 'infinity';

-- Historical atoms
SELECT * FROM atom
WHERE valid_to < now();

-- Atom history
SELECT atom_id, canonical_text, valid_from, valid_to
FROM atom
WHERE atom_id = 12345
ORDER BY valid_from DESC;
```

**Use case:** Time-travel queries, audit trails, rollback.

---

## Atomization Strategies

### Text Atomization

**Strategy**: Decompose to character atoms.

```python
def atomize_text(text: str) -> list[Atom]:
    atoms = []
    for char in text:
        hash = sha256(char.encode())
        atoms.append(Atom(hash, char.encode(), char))
    return atoms
```

**Example:**

```
"Machine" ? ['M', 'a', 'c', 'h', 'i', 'n', 'e']
          ? 7 atoms
```

### Numeric Atomization

**Strategy**: Store as bytes.

```python
def atomize_float(value: float) -> Atom:
    bytes_data = struct.pack('f', value)  # IEEE 754
    hash = sha256(bytes_data)
    return Atom(hash, bytes_data, str(value))
```

**Example:**

```
0.017 ? 0x3C8B4396 (4 bytes)
      ? 1 atom
```

### Image Atomization

**Strategy**: Decompose to pixel RGB atoms.

```python
def atomize_image(image: np.ndarray) -> list[Atom]:
    atoms = []
    for y in range(image.shape[0]):
        for x in range(image.shape[1]):
            r, g, b = image[y, x]
            pixel_bytes = bytes([r, g, b])
            hash = sha256(pixel_bytes)
            atoms.append(Atom(hash, pixel_bytes, f"RGB({r},{g},{b})"))
    return atoms
```

**Example:**

```
256�256 image ? 65,536 pixel atoms
```

### Vector Atomization

**Strategy**: Each float is an atom (sparse encoding).

```python
def atomize_vector(vector: np.ndarray, threshold=0.01) -> list[Atom]:
    atoms = []
    for i, value in enumerate(vector):
        if abs(value) > threshold:  # Sparse
            bytes_data = struct.pack('f', value)
            hash = sha256(bytes_data)
            atoms.append(Atom(hash, bytes_data, str(value), sequence_index=i))
    return atoms
```

**Example:**

```
1998D embedding (mostly zeros)
? ~50 non-zero floats
? 50 atoms (not 1998)
```

---

## Performance Characteristics

### Storage Efficiency

**Deduplication:**

```
1 billion 'e' characters
Traditional: 1B � 1 byte = 1 GB
Hartonomous: 1 atom + 1B references = ~8 MB
Savings: 99.2%
```

**Sparse encoding:**

```
1998D vector (5% non-zero)
Traditional: 1998 � 4 bytes = 7992 bytes
Hartonomous: 100 � 12 bytes = 1200 bytes (15% of original)
```

### Query Performance

**Lookup by hash** (B-tree):
```sql
SELECT * FROM atom WHERE content_hash = $hash;
-- O(log N) � sub-1ms
```

**Spatial query** (GiST):
```sql
SELECT * FROM atom WHERE ST_DWithin(spatial_key, $target, 0.15);
-- O(log N) � sub-10ms
```

**Count references**:
```sql
SELECT COUNT(*) FROM atom WHERE reference_count > 1000;
-- O(log N) with index on reference_count
```

---

## Common Patterns

### Upsert Atom

```sql
INSERT INTO atom (content_hash, atomic_value, canonical_text, metadata)
VALUES ($hash, $value, $text, $metadata)
ON CONFLICT (content_hash) DO UPDATE SET
    reference_count = atom.reference_count + 1,
    metadata = atom.metadata || EXCLUDED.metadata
RETURNING atom_id, reference_count;
```

### Batch Insert

```sql
INSERT INTO atom (content_hash, atomic_value, canonical_text)
SELECT 
    sha256(char::bytea),
    char::bytea,
    char
FROM unnest(ARRAY['a','b','c']) AS char
ON CONFLICT (content_hash) DO NOTHING;
```

### Query by Modality

```sql
SELECT canonical_text, reference_count
FROM atom
WHERE metadata->>'modality' = 'text'
ORDER BY reference_count DESC
LIMIT 100;
```

---

## Atom Lifecycle

### 1. Creation

```
Input ? Hash ? Check Exists ? Insert (or increment reference_count)
```

### 2. Composition

```
Atom ? Part of Parent ? atom_composition entry created
```

### 3. Relations

```
Atom ? Semantic Connection ? atom_relation entry created
```

### 4. Archival

```
Atom ? Unused ? valid_to set ? Archived (not deleted)
```

### 5. Deletion (Rare)

```
Atom ? All references removed ? Soft delete (valid_to = now())
```

---

## Key Takeaways

### 1. Content Addressing Works

- Same value = same hash = same atom
- Global deduplication
- Storage efficiency

### 2. ?64 Bytes Enforces Decomposition

- Forces hierarchical structure
- Natural sparsity
- Compositional by design

### 3. Reference Count = Importance

- High count = common/important
- Query weighting
- Ranking signals

### 4. Position = Meaning

- 3D spatial coordinates
- Semantic similarity
- No embedding model needed

### 5. Metadata = Flexibility

- JSONB for arbitrary attributes
- Modality, model, tenant, etc.
- Indexed for fast queries

---

## Next Steps

Now that you understand atoms, continue with:

1. **[Compositions](compositions.md)** � How atoms combine into structures
2. **[Relations](relations.md)** — How atoms connect semantically
3. **[Spatial Semantics](spatial-semantics.md)** — How positions are computed

---

## Advanced Atom Patterns

### Atom Pooling for High-Frequency Values

Pre-allocate commonly used atoms to reduce lookup overhead:

```python
from typing import Dict
import hashlib

class AtomPool:
    """Pre-computed pool of frequently used atoms."""
    
    def __init__(self):
        self.pool: Dict[bytes, int] = {}
        self._preload_common_atoms()
    
    def _preload_common_atoms(self):
        """Preload common single-byte atoms (ASCII printable)."""
        for i in range(32, 127):  # Printable ASCII
            char = chr(i)
            content_hash = hashlib.sha256(char.encode()).digest()
            self.pool[content_hash] = i  # Store atom_id
        
        # Common punctuation and whitespace
        for char in [' ', '\n', '\t', '.', ',', '!', '?']:
            content_hash = hashlib.sha256(char.encode()).digest()
            self.pool[content_hash] = len(self.pool)
    
    def get_atom_id(self, content: bytes) -> int | None:
        """Get atom_id from pool if exists."""
        content_hash = hashlib.sha256(content).digest()
        return self.pool.get(content_hash)

# Usage
pool = AtomPool()
atom_id = pool.get_atom_id(b'A')  # Fast lookup, no DB query
```

### Atom Compression for Storage Efficiency

Compress atoms when content has redundancy:

```python
import zlib
from dataclasses import dataclass

@dataclass
class CompressedAtom:
    """Atom with optional compression."""
    content_hash: bytes
    content: bytes
    is_compressed: bool = False
    original_size: int = 0
    
    @classmethod
    def create(cls, content: bytes, compression_threshold: int = 16) -> 'CompressedAtom':
        """Create atom with compression if beneficial."""
        content_hash = hashlib.sha256(content).digest()
        
        # Only compress if content is above threshold
        if len(content) > compression_threshold:
            compressed = zlib.compress(content, level=9)
            
            # Only store compressed if it's actually smaller
            if len(compressed) < len(content):
                return cls(
                    content_hash=content_hash,
                    content=compressed,
                    is_compressed=True,
                    original_size=len(content)
                )
        
        # Store uncompressed
        return cls(
            content_hash=content_hash,
            content=content,
            is_compressed=False,
            original_size=len(content)
        )
    
    def decompress(self) -> bytes:
        """Get original content."""
        if self.is_compressed:
            return zlib.decompress(self.content)
        return self.content

# Usage
atom = CompressedAtom.create(b'A' * 50)  # Repetitive content
print(f"Original: {atom.original_size} bytes")
print(f"Stored: {len(atom.content)} bytes")
print(f"Compression ratio: {atom.original_size / len(atom.content):.2f}x")
```

### Atom Validation Rules

Enforce constraints at atom creation:

```python
from typing import Protocol
from enum import Enum

class AtomValidationError(Exception):
    """Raised when atom validation fails."""
    pass

class Modality(Enum):
    """Supported atom modalities."""
    TEXT = "text"
    CODE = "code"
    IMAGE = "image"
    AUDIO = "audio"
    VIDEO = "video"
    MODEL = "model"

class AtomValidator:
    """Validate atom creation constraints."""
    
    MAX_SIZE = 64  # bytes
    
    @classmethod
    def validate(cls, content: bytes, modality: Modality) -> None:
        """Validate atom meets all constraints."""
        # Size constraint
        if len(content) > cls.MAX_SIZE:
            raise AtomValidationError(
                f"Atom exceeds maximum size: {len(content)} > {cls.MAX_SIZE}"
            )
        
        # Non-empty constraint
        if len(content) == 0:
            raise AtomValidationError("Atom cannot be empty")
        
        # Modality-specific validation
        if modality == Modality.TEXT:
            cls._validate_text(content)
        elif modality == Modality.CODE:
            cls._validate_code(content)
        # ... other modalities
    
    @staticmethod
    def _validate_text(content: bytes) -> None:
        """Validate text atom."""
        try:
            text = content.decode('utf-8')
        except UnicodeDecodeError:
            raise AtomValidationError("Text atom must be valid UTF-8")
        
        # No null bytes in text
        if '\x00' in text:
            raise AtomValidationError("Text atom cannot contain null bytes")
    
    @staticmethod
    def _validate_code(content: bytes) -> None:
        """Validate code atom."""
        # Code must be valid UTF-8
        try:
            content.decode('utf-8')
        except UnicodeDecodeError:
            raise AtomValidationError("Code atom must be valid UTF-8")

# Usage
try:
    AtomValidator.validate(b'A' * 100, Modality.TEXT)
except AtomValidationError as e:
    print(f"Validation failed: {e}")
```

### Atom Versioning for Temporal Queries

Track atom versions over time:

```python
from datetime import datetime
from dataclasses import dataclass

@dataclass
class VersionedAtom:
    """Atom with temporal versioning."""
    atom_id: int
    content_hash: bytes
    content: bytes
    version: int
    valid_from: datetime
    valid_to: datetime
    
    def is_valid_at(self, timestamp: datetime) -> bool:
        """Check if atom version was valid at given time."""
        return self.valid_from <= timestamp < self.valid_to

class AtomVersionManager:
    """Manage atom versions over time."""
    
    def __init__(self, db_pool):
        self.pool = db_pool
    
    async def get_atom_at_time(
        self,
        content_hash: bytes,
        timestamp: datetime
    ) -> VersionedAtom | None:
        """Retrieve atom version valid at specific time."""
        query = """
            SELECT atom_id, content_hash, content, version, valid_from, valid_to
            FROM atom
            WHERE content_hash = $1
              AND valid_from <= $2
              AND valid_to > $2
            ORDER BY version DESC
            LIMIT 1
        """
        
        async with self.pool.connection() as conn:
            async with conn.cursor() as cursor:
                await cursor.execute(query, (content_hash, timestamp))
                row = await cursor.fetchone()
                
                if row:
                    return VersionedAtom(*row)
                return None
    
    async def get_version_history(
        self,
        content_hash: bytes
    ) -> list[VersionedAtom]:
        """Get all versions of an atom."""
        query = """
            SELECT atom_id, content_hash, content, version, valid_from, valid_to
            FROM atom
            WHERE content_hash = $1
            ORDER BY version ASC
        """
        
        async with self.pool.connection() as conn:
            async with conn.cursor() as cursor:
                await cursor.execute(query, (content_hash,))
                rows = await cursor.fetchall()
                return [VersionedAtom(*row) for row in rows]

# Usage
manager = AtomVersionManager(db_pool)
atom = await manager.get_atom_at_time(content_hash, datetime(2024, 1, 1))
print(f"Atom version at 2024-01-01: {atom.version}")
```

### Atom Metadata Enrichment

Add semantic metadata to atoms:

```python
from typing import Dict, Any
import json

class AtomMetadataEnricher:
    """Enrich atoms with semantic metadata."""
    
    @staticmethod
    def enrich_text_atom(content: str, language: str = "en") -> Dict[str, Any]:
        """Add metadata for text atoms."""
        return {
            "type": "text",
            "language": language,
            "char_count": len(content),
            "is_ascii": content.isascii(),
            "is_alphabetic": content.isalpha(),
            "is_numeric": content.isnumeric(),
            "is_whitespace": content.isspace()
        }
    
    @staticmethod
    def enrich_code_atom(
        content: str,
        language: str,
        token_type: str
    ) -> Dict[str, Any]:
        """Add metadata for code atoms."""
        return {
            "type": "code",
            "language": language,
            "token_type": token_type,  # keyword, identifier, literal, etc.
            "char_count": len(content),
            "has_special_chars": any(c in content for c in "{}[]();,.")
        }
    
    @staticmethod
    def enrich_image_atom(
        rgb: tuple[int, int, int]
    ) -> Dict[str, Any]:
        """Add metadata for image atoms (RGB pixels)."""
        r, g, b = rgb
        brightness = (r + g + b) / 3
        
        return {
            "type": "image",
            "format": "RGB",
            "r": r, "g": g, "b": b,
            "brightness": brightness,
            "is_grayscale": (r == g == b),
            "dominant_channel": max(
                [("r", r), ("g", g), ("b", b)],
                key=lambda x: x[1]
            )[0]
        }

# Usage
metadata = AtomMetadataEnricher.enrich_text_atom("Hello", language="en")
print(json.dumps(metadata, indent=2))
```

---

**Next: [Compositions →](compositions.md)**
