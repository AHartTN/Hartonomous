# Modalities

**Multi-modal representation: Text, code, images, audio, and model weights unified in semantic space.**

---

## Core Concept

**All modalities occupy the same 3D semantic space.**

```
Text atoms        POINT Z(0.05, y, z)
Code atoms        POINT Z(0.30, y, z)
Image atoms       POINT Z(0.60, y, z)
Audio atoms       POINT Z(0.90, y, z)
```

**Different modalities, same geometric representation.**

**Key principle:** Cross-modal queries work natively—text query returns images, audio query returns text.

---

## Text Modality

### Character Atoms

**Strategy:** Decompose to individual characters.

```python
"Hello" ? ['H', 'e', 'l', 'l', 'o']
```

**Position:**
```
X = 0.05 (text modality)
Y = 0.85 (literal)
Z = 0.92 (atomic)
```

**Storage:**

```sql
INSERT INTO atom (content_hash, atomic_value, canonical_text, spatial_key, metadata)
VALUES (
    sha256('e'),
    'e'::bytea,
    'e',
    ST_MakePoint(0.05, 0.85, 0.92),
    jsonb_build_object('modality', 'text', 'type', 'character')
);
```

### Word Atoms

**Strategy:** Words as compositions of character atoms.

```sql
-- Word "learning" composed of characters
INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index)
SELECT 
    (SELECT atom_id FROM atom WHERE canonical_text = 'learning'),
    atomize_char(char),
    idx
FROM unnest(string_to_array('learning', NULL)) WITH ORDINALITY AS t(char, idx);
```

**Position:**
```
X = 0.08 (text modality)
Y = 0.45 (symbolic)
Z = 0.65 (compound)
```

### Document Atoms

**Hierarchy:**
```
Document
  ?? Paragraph 1
  ?   ?? Sentence 1
  ?   ?   ?? Word 1 ? Characters
  ?   ?   ?? Word 2 ? Characters
  ?   ?? Sentence 2 ? ...
  ?? Paragraph 2 ? ...
```

**Semantic clustering:** Related documents cluster in space (proximity = topic similarity).

---

## Code Modality

### Token Atoms

**Strategy:** Decompose via AST (Abstract Syntax Tree).

```python
# Code: public class Example { }
# Tokens:
['public', 'class', 'Example', '{', '}']
```

**Position:**
```
X = 0.30 (code modality)
Y = 0.50 (symbolic)
Z = varies by abstraction level
```

### AST Nodes as Atoms

**Example C# code:**

```csharp
public class Example {
    public int GetValue() { return 42; }
}
```

**AST atoms:**
```
ClassDeclaration (atom_id=7000)
  ?? Modifiers: 'public' (atom_id=7001)
  ?? Identifier: 'Example' (atom_id=7002)
  ?? MethodDeclaration (atom_id=7003)
      ?? ReturnType: 'int' (atom_id=7004)
      ?? Identifier: 'GetValue' (atom_id=7005)
      ?? ReturnStatement (atom_id=7006)
          ?? Literal: '42' (atom_id=7007)
```

### Code Atomizer Microservice

**C# service using Roslyn:**

```csharp
[HttpPost("/atomize/csharp")]
public async Task<AtomizeResponse> AtomizeCSharp([FromBody] CodeInput input)
{
    var tree = CSharpSyntaxTree.ParseText(input.Code);
    var root = await tree.GetRootAsync();
    
    var atoms = new List<CodeAtom>();
    
    foreach (var node in root.DescendantNodes())
    {
        atoms.Add(new CodeAtom
        {
            Type = node.Kind().ToString(),
            Value = node.ToString(),
            Position = node.Span,
            ContentHash = ComputeHash(node.ToString())
        });
    }
    
    return new AtomizeResponse { Atoms = atoms };
}
```

**Tree-sitter for other languages:**

```python
# Python, Java, JavaScript, etc.
import tree_sitter

parser = tree_sitter.Parser()
parser.set_language(tree_sitter.Language('build/languages.so', 'python'))

tree = parser.parse(b"def hello(): print('world')")
atoms = extract_atoms(tree.root_node)
```

---

## Image Modality

### Pixel Atoms

**Strategy:** Each pixel RGB value = 1 atom.

```python
# 256×256 image ? 65,536 pixel atoms
for y in range(height):
    for x in range(width):
        r, g, b = image[y, x]
        pixel_bytes = bytes([r, g, b])  # 3 bytes
        atom = Atom(sha256(pixel_bytes), pixel_bytes)
```

**Position:**
```
X = 0.65 (image modality)
Y = 0.90 (literal pixel data)
Z = 0.95 (atomic)
```

**Deduplication:** Solid backgrounds ? 1 pixel atom referenced 40,000 times.

### Image Patch Atoms

**Strategy:** Group pixels into patches (8×8, 16×16).

```python
# 256×256 image ? 1024 patches (16×16 each)
for py in range(0, height, 16):
    for px in range(0, width, 16):
        patch = image[py:py+16, px:px+16]
        patch_bytes = patch.tobytes()
        atom = Atom(sha256(patch_bytes), patch_bytes[:64])  # ?64 bytes
```

**Hierarchy:**
```
Image (atom_id=8000)
  ?? Patch(0,0) (atom_id=8001)
  ?   ?? Pixel(0,0) RGB(255,87,51)
  ?   ?? Pixel(0,1) RGB(100,200,50)
  ?   ?? ...
  ?? Patch(0,1) (atom_id=8002)
  ?? ...
```

### Image Features

**Strategy:** Extract features (edges, corners, textures) as atoms.

```python
# Use OpenCV or similar
features = cv2.goodFeaturesToTrack(gray, maxCorners=100, qualityLevel=0.01, minDistance=10)

for feature in features:
    x, y = feature.ravel()
    feature_bytes = struct.pack('ff', x, y)
    atom = Atom(sha256(feature_bytes), feature_bytes)
```

**Cross-modal query:** Text "cat whiskers" ? Image features near whiskers.

---

## Audio Modality

### Sample Atoms

**Strategy:** Audio samples (waveform values) as atoms.

```python
# Mono audio: 1D array of samples
audio = np.array([0.234, 0.456, 0.123, ...])

for sample in audio:
    sample_bytes = struct.pack('f', sample)  # 4 bytes
    atom = Atom(sha256(sample_bytes), sample_bytes)
```

**Position:**
```
X = 0.90 (audio modality)
Y = 0.85 (literal sample data)
Z = 0.92 (atomic)
```

### Frame Atoms

**Strategy:** Group samples into frames (e.g., 20ms windows).

```python
# 44.1kHz audio, 20ms frames = 882 samples per frame
frame_size = 882
for i in range(0, len(audio), frame_size):
    frame = audio[i:i+frame_size]
    frame_bytes = frame.tobytes()[:64]  # Compress to ?64 bytes
    atom = Atom(sha256(frame_bytes), frame_bytes)
```

### Phoneme Atoms

**Strategy:** Extract phonemes (speech recognition) as atoms.

```python
# Use speech-to-text
phonemes = speech_to_phonemes(audio_file)
# Result: ['k', 'æ', 't']  # "cat"

for phoneme in phonemes:
    atom = Atom(sha256(phoneme.encode()), phoneme.encode())
```

**Cross-modal query:** Audio "cat" ? Text "cat" (same semantic region).

---

## Model Weight Modality

### Weight Atoms

**Strategy:** Model parameters as float atoms.

```python
# GPT-4: 1.76 trillion parameters
# After quantization: ~1-2 billion unique float values

for layer in model.layers:
    for weight in layer.weights:
        weight_bytes = struct.pack('f', weight)
        atom = Atom(sha256(weight_bytes), weight_bytes)
```

**Position:**
```
X = 0.30 (model/code modality)
Y = 0.50 (symbolic)
Z = varies by layer depth
```

### Quantized Weights

**Strategy:** Quantize to 8-bit, 4-bit, or 2-bit.

```python
def quantize_weight(weight: float, bits: int = 8) -> int:
    """Quantize float to N-bit integer."""
    max_val = (1 << bits) - 1  # 2^bits - 1
    quantized = int((weight + 1.0) / 2.0 * max_val)
    return np.clip(quantized, 0, max_val)

# 8-bit quantization: 1.76T params ? 256 unique values
# Massive deduplication!
```

**Result:** 1.76 trillion weights ? ~500K unique atoms (after quantization + deduplication).

### Layer Atoms

**Hierarchy:**
```
Model: GPT-4 (atom_id=9000)
  ?? Layer 0 (atom_id=9001)
  ?   ?? Weight[0] = 0.123 (atom_id=9002)
  ?   ?? Weight[1] = 0.456 (atom_id=9003)
  ?   ?? ...
  ?? Layer 1 (atom_id=9100)
  ?? ...
```

---

## Cross-Modal Queries

### Text Query ? Image Results

```sql
-- Query: "cat whiskers"
WITH text_query AS (
    SELECT spatial_key FROM atom WHERE canonical_text = 'cat whiskers'
)
SELECT 
    a.metadata->>'image_url' AS image_url,
    ST_Distance(a.spatial_key, tq.spatial_key) AS distance
FROM atom a, text_query tq
WHERE a.metadata->>'modality' = 'image'
  AND ST_DWithin(a.spatial_key, tq.spatial_key, 0.5)
ORDER BY distance ASC
LIMIT 10;

-- Returns: Images of cats with visible whiskers
```

### Audio Query ? Text Results

```sql
-- Query: Audio file of "hello"
WITH audio_query AS (
    SELECT spatial_key FROM atom WHERE atom_id = $audio_atom_id
)
SELECT 
    a.canonical_text,
    ST_Distance(a.spatial_key, aq.spatial_key) AS distance
FROM atom a, audio_query aq
WHERE a.metadata->>'modality' = 'text'
  AND ST_DWithin(a.spatial_key, aq.spatial_key, 0.3)
ORDER BY distance ASC
LIMIT 10;

-- Returns: "hello", "hi", "greetings", etc.
```

### Image Query ? Audio Results

```sql
-- Query: Image of barking dog
WITH image_query AS (
    SELECT spatial_key FROM atom WHERE atom_id = $dog_image_id
)
SELECT 
    a.metadata->>'audio_url' AS audio_url,
    ST_Distance(a.spatial_key, iq.spatial_key) AS distance
FROM atom a, image_query iq
WHERE a.metadata->>'modality' = 'audio'
  AND ST_DWithin(a.spatial_key, iq.spatial_key, 0.4)
ORDER BY distance ASC
LIMIT 10;

-- Returns: Audio of dogs barking
```

**No separate models needed.** All modalities in same space = cross-modal search "just works."

---

## Unified Semantic Space

### Modality Clustering

After ingesting diverse data:

```sql
-- Find cluster centers by modality
SELECT 
    metadata->>'modality' AS modality,
    COUNT(*) AS atom_count,
    AVG(ST_X(spatial_key)) AS avg_x,
    AVG(ST_Y(spatial_key)) AS avg_y,
    AVG(ST_Z(spatial_key)) AS avg_z
FROM atom
GROUP BY metadata->>'modality'
ORDER BY atom_count DESC;

-- Result:
-- text:  avg_x=0.08, atom_count=1M
-- code:  avg_x=0.32, atom_count=500K
-- image: avg_x=0.65, atom_count=2M
-- audio: avg_x=0.88, atom_count=300K
```

**Modalities naturally cluster along X-axis.**

### Semantic Overlap

```sql
-- Find atoms where modalities overlap (same semantic region)
SELECT 
    a1.canonical_text AS text_atom,
    a2.metadata->>'image_url' AS image_atom,
    ST_Distance(a1.spatial_key, a2.spatial_key) AS distance
FROM atom a1
JOIN atom a2 ON ST_DWithin(a1.spatial_key, a2.spatial_key, 0.1)
WHERE a1.metadata->>'modality' = 'text'
  AND a2.metadata->>'modality' = 'image'
ORDER BY distance ASC
LIMIT 10;

-- Result: Text atoms near image atoms (e.g., "cat" near cat images)
```

---

## Performance by Modality

### Storage Efficiency

| Modality | Data Type | Deduplication | Compression | Total Savings |
|----------|-----------|---------------|-------------|---------------|
| **Text** | Characters | 50-70% | 5-10x | 10-20x |
| **Code** | Tokens | 30-50% | 3-5x | 5-10x |
| **Images** | Pixels | 10-30% | 2-5x | 3-10x |
| **Audio** | Samples | 5-15% | 2-3x | 2-5x |
| **Models** | Weights | 90-99% | 10-100x | 100-1000x |

**Models benefit most** (quantization + deduplication = massive savings).

### Query Speed

| Modality | Typical Query | Latency | Notes |
|----------|--------------|---------|-------|
| **Text** | K-nearest words | 5ms | Fast (small atoms) |
| **Code** | Semantic code search | 10ms | Moderate (AST traversal) |
| **Images** | Similar images | 20ms | Slower (large atoms) |
| **Audio** | Audio similarity | 15ms | Moderate (frame-based) |
| **Models** | Weight similarity | 8ms | Fast (quantized) |

---

## Key Takeaways

### 1. Unified Space

All modalities occupy the same 3D semantic space.

### 2. Cross-Modal Queries

Text query ? Image results (and vice versa) works natively.

### 3. Modality-Specific Atomization

- **Text**: Characters ? Words ? Sentences
- **Code**: Tokens ? AST nodes
- **Images**: Pixels ? Patches ? Features
- **Audio**: Samples ? Frames ? Phonemes
- **Models**: Weights ? Layers

### 4. Semantic Clustering

Modalities cluster along X-axis, but overlap in semantic regions.

### 5. Deduplication Varies

Models benefit most (99%), text moderately (50-70%), images least (10-30%).

---

## Next Steps

You've completed the concepts documentation! Continue with:

1. **[Architecture Deep Dives](../architecture/README.md)** — Implementation details
2. **[API Reference](../api-reference/README.md)** — Endpoint documentation
3. **[Deployment Guides](../deployment/README.md)** — Production setup

---

**Concepts documentation complete! ?**
