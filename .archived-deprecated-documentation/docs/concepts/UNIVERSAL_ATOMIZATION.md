# Universal Atomization Pattern

**"All Digital Content is Relations Between Constants"**

---

## The Core Insight

Traditional systems treat different data types completely differently:
- Text → strings of characters
- Tensors → arrays of floats
- Images → grids of pixels
- Audio → streams of samples

**Hartonomous unifies them all:** Every structured data is a **graph of relationships** between **immutable constants**.

---

## The Universal Pattern

### Step 1: Identify the Dimensions

Any n-dimensional data has:
1. **Structural Dimensions:** Define positions/roles (row, column, time, x, y)
2. **Value Dimension:** The actual data at that position

### Step 2: Atomize the Constants

```python
# The dimensions themselves are constants
dimension_atoms = [atomize(dim_value) for dim_value in dimension_values]

# The values are constants
value_atoms = {v: atomize(v) for v in unique_values}
```

### Step 3: Create Relation Compositions

```python
# Each data point = composition of its coordinates + value
for position in data_positions:
    dims = [dimension_atoms[i] for i in position.indices]
    val = value_atoms[data[position]]
    
    relation_atom = create_composition([*dims, val])
    # This composition encodes the RELATIONSHIP
```

### Step 4: Build LINESTRING Trajectory

```python
# Trace path through relation atoms in 3D semantic space
coordinates = [get_spatial_key(atom) for atom in relation_atoms]
m_values = [compute_logical_index(pos) for pos in positions]

trajectory_wkt = build_linestring_zm(coordinates, m_values)
# Gaps in M = sparse regions (zeros cost zero bytes)
```

### Step 5: Apply BPE Crystallization

```python
# BPE finds patterns in the STRUCTURE
compressed_sequence = bpe.crystallize(relation_atoms)
# Discovers: "This 10-relation subgraph repeats 1000 times"
```

---

## Examples Across Data Types

### Text: Sequential Relations

```python
text = "Cat"

# Old approach (WRONG):
atoms = [atomize('C'), atomize('a'), atomize('t')]
# Problem: Lost positional information

# Correct approach:
position_atoms = [atomize(f"pos_{i}") for i in range(3)]
char_atoms = [atomize('C'), atomize('a'), atomize('t')]

# Create positional relations
relations = [
    composition([position_atoms[0], char_atoms[0]]),  # (pos_0, 'C')
    composition([position_atoms[1], char_atoms[1]]),  # (pos_1, 'a')
    composition([position_atoms[2], char_atoms[2]]),  # (pos_2, 't')
]

# Or as bigrams (character transitions):
relations = [
    composition([char_atoms[0], char_atoms[1]]),  # ('C', 'a')
    composition([char_atoms[1], char_atoms[2]]),  # ('a', 't')
]

# LINESTRING traces through these relation atoms
trajectory = build_linestring_zm(
    [get_position(r) for r in relations],
    m_values=[0, 1, 2]  # Sequence indices
)
```

### Weight Matrix: Connection Graph

```python
tensor = np.array([[0.5, 0.3], [0.2, 0.5]])  # shape [2, 2]

# Atomize structural components
row_atoms = [atomize("layer0_n0"), atomize("layer0_n1")]
col_atoms = [atomize("layer0_n0"), atomize("layer0_n1")]
value_atoms = {0.2: atomize(0.2), 0.3: atomize(0.3), 0.5: atomize(0.5)}

# Create connection relations
connections = []
m_values = []

for i in range(2):
    for j in range(2):
        value = tensor[i, j]
        
        # Each connection is a triple
        connection = composition([
            row_atoms[i],      # Source neuron
            col_atoms[j],      # Target neuron
            value_atoms[value] # Weight value
        ])
        
        connections.append(connection)
        m_values.append(i * 2 + j)  # Matrix position

# LINESTRING traces through connection atoms
trajectory = build_linestring_zm(
    [get_position(c) for c in connections],
    m_values=m_values  # [0, 1, 2, 3]
)

# BPE discovers:
# - Connection (n0 → n0, 0.5) and (n1 → n1, 0.5) have same structure
# - Diagonal pattern with 0.5 weights
```

### Image: Pixel Grid

```python
image = load_image("cat.png")  # shape [256, 256, 3]

# Atomize coordinates
x_atoms = [atomize(f"x_{x}") for x in range(256)]
y_atoms = [atomize(f"y_{y}") for y in range(256)]

# Atomize unique RGB values (typically 10K-50K unique colors)
unique_colors = get_unique_colors(image)
color_atoms = {color: atomize(color) for color in unique_colors}

# Create pixel relations
pixels = []
m_values = []

for y in range(256):
    for x in range(256):
        rgb = image[y, x]
        
        # Skip transparent/background pixels (sparse optimization)
        if is_background(rgb):
            continue
        
        # Each pixel is a spatial relation
        pixel = composition([
            x_atoms[x],
            y_atoms[y],
            color_atoms[rgb]
        ])
        
        pixels.append(pixel)
        m_values.append(y * 256 + x)  # Raster-scan position

# LINESTRING traces through pixel atoms
trajectory = build_linestring_zm(
    [get_position(p) for p in pixels],
    m_values=m_values
)
# Gaps in M = background pixels (cost zero bytes)

# BPE discovers:
# - Repeated color patterns (textures)
# - Edge structures
# - Shape motifs
```

### Audio: Time-Series

```python
audio = load_audio("speech.wav")  # shape [44100, 2] (44.1kHz stereo)

# Atomize time positions
time_atoms = [atomize(f"t_{t}") for t in range(44100)]

# Atomize channels
channel_atoms = [atomize("left"), atomize("right")]

# Atomize sample values (quantized)
unique_samples = np.unique(audio)  # Typically 256 for 8-bit
sample_atoms = {s: atomize(s) for s in unique_samples}

# Create temporal relations
samples = []
m_values = []

for t in range(44100):
    for ch in range(2):
        value = audio[t, ch]
        
        # Skip silence (sparse optimization)
        if abs(value) < silence_threshold:
            continue
        
        # Each sample is a temporal relation
        sample = composition([
            time_atoms[t],
            channel_atoms[ch],
            sample_atoms[value]
        ])
        
        samples.append(sample)
        m_values.append(t * 2 + ch)

# LINESTRING traces through sample atoms
trajectory = build_linestring_zm(
    [get_position(s) for s in samples],
    m_values=m_values
)
# Gaps in M = silence (cost zero bytes)

# BPE discovers:
# - Repeated phonemes
# - Musical patterns
# - Stereo correlations
```

---

## Benefits of Universal Atomization

### 1. Structural Deduplication

Same relation across different contexts = same composition atom.

```python
# If two layers have identical connection patterns:
layer0_connection = composition([n0, n5, 0.017])
layer1_connection = composition([n0, n5, 0.017])
# They're the SAME atom! Stored once, referenced twice.
```

### 2. Cross-Modal Pattern Discovery

BPE finds patterns **across data types**:
- Image edge structure ≈ Weight matrix sparsity pattern
- Text rhythm ≈ Audio temporal pattern
- Database row structure ≈ Tensor dimension structure

### 3. Semantic Spatial Queries

PostGIS queries work universally:

```sql
-- Find connections similar to this attention pattern
SELECT * FROM atom
WHERE ST_DWithin(
    spatial_key,
    (SELECT spatial_key FROM atom WHERE atom_id = $attention_connection),
    0.1
);
-- Returns: Similar connections from ANY model, ANY layer!

-- Find images with similar color distributions
SELECT image_a.atom_id, image_b.atom_id,
       ST_HausdorffDistance(
           image_a.spatial_expression,
           image_b.spatial_expression
       ) AS similarity
FROM atom image_a, atom image_b
WHERE image_a.metadata->>'modality' = 'image'
  AND image_b.metadata->>'modality' = 'image'
  AND ST_HausdorffDistance(
      image_a.spatial_expression, 
      image_b.spatial_expression
  ) < 0.5;
```

### 4. Infinite Compression

- **Sparse Encoding:** Gaps in M coordinate = infinite zeros cost zero bytes
- **Value Deduplication:** Each unique value stored once
- **Structure Deduplication:** Same relations deduplicated
- **Pattern Compression:** BPE collapses repeated structures

### 5. Unified Operations

Same operations work on ANY data:

```python
# Generic atomization
def atomize_structured(data, dimensions):
    """Works for text, tensors, images, audio, databases..."""
    
    # Atomize dimension constants
    dim_atoms = atomize_dimensions(dimensions)
    
    # Atomize value constants
    value_atoms = atomize_unique_values(data)
    
    # Create relation compositions
    relations = []
    for position in iterate_positions(data):
        dims = [dim_atoms[d][position[d]] for d in dimensions]
        val = value_atoms[data[position]]
        relations.append(composition([*dims, val]))
    
    # Build trajectory
    return build_trajectory(relations)
```

---

## Implementation Guidelines

### For New Data Types

1. **Identify dimensions:** What are the structural coordinates?
2. **Identify values:** What's stored at those coordinates?
3. **Create relation triples:** (dim1, dim2, ..., dimN, value)
4. **Build LINESTRING:** Trace through relations with M coordinate
5. **Apply BPE:** Let crystallization find patterns

### Performance Considerations

- **Chunking:** Process large datasets in chunks to avoid memory explosion
- **Streaming:** Stream relation creation to BPE incrementally
- **Sparse Optimization:** Skip near-zero/background/silence
- **Batch Operations:** Use vectorized numpy/SQL operations

### Composition Positioning

Compositions are positioned via **compositional gravity:**
- **Centroid:** Average of component positions (default)
- **Semantic Projection:** Project based on role (optional)
- Result: Relations cluster by semantic similarity

---

## Key Takeaways

1. **All data is relations** between immutable constants
2. **Compositions encode the relationships** (not just values)
3. **LINESTRING traces through composition atoms** in 3D semantic space
4. **M coordinate encodes logical position** (with gaps = sparse)
5. **BPE discovers structural patterns** across all data types
6. **PostGIS enables universal spatial queries** on any atomized data

**The paradigm shift:** Stop thinking about "arrays of data" — think about "graphs of relationships in geometric space."

---

## Next Steps

- **[Read about Fractal Deduplication](FRACTAL_DEDUPLICATION.md)** — How BPE finds patterns
- **[Read about Geometric Compression](GEOMETRIC_COMPRESSION.md)** — How sparse encoding works
- **[Read about Spatial Semantics](spatial-semantics.md)** — How positions are computed
- **[See Model Atomization](MODEL_ATOMIZATION.md)** — Neural networks as relation graphs

---

**"Data is not a blob. Data is a constellation of relationships in semantic space."**
