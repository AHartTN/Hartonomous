# UNIVERSAL ATOMIZATION PATTERN

**The Same Three-Phase Order for ALL Data Types**

---

## The Universal Pattern

**ALL digital content follows the same atomization order:**

1. **Pre-populate structural atoms** (KNOWN constants from format/schema)
2. **Stream relations** (connections between pre-populated atoms)
3. **Crystallize patterns** (BPE on topology, find repeated structures)

This pattern applies to:
- Text/Code
- AI Models
- Images/Video
- Databases
- File Systems
- Network Packets
- Audio
- 3D Models
- EVERYTHING

---

## Pattern by Data Type

### Text/Code

```python
# Phase 1: Pre-populate character/token atoms (FAST)
char_atoms = {}
for char in unique_characters:  # ASCII: 256 chars, Unicode: ~150K
    spatial_key = calculate_char_spatial_key(ord(char))
    atom_id = await insert_atom(char, spatial_key)
    char_atoms[char] = atom_id

# Phase 2: Stream text as relations (sequences)
for position, char in enumerate(text_stream):
    if position == 0:
        continue  # First char has no predecessor
    
    # Each character pair = relation (sequence edge)
    await insert_relation(
        source=char_atoms[text_stream[position-1]],
        target=char_atoms[char],
        type='sequence',
        weight=1.0,
        metadata={'position': position}
    )

# Phase 3: BPE on character sequences (find repeated substrings)
common_bigrams = find_frequent_relations(min_count=1000)
# "th", "he", "in", "er" → create composition atoms for common pairs
```

**Result**: All text stored as graph traversal through character atoms.

---

### AI Models (Neural Networks)

```python
# Phase 1: Pre-populate vocabulary + neurons (FAST)
vocab_atoms = await atomize_vocabulary(model.vocab)  # 151K tokens
neuron_atoms = await atomize_neurons(model.layers)  # 131K neurons

# Phase 2: Stream weight matrix as relations
for i, j, value in nonzero_weights(tensor):
    await insert_relation(
        source=neuron_atoms[(layer, i)],
        target=neuron_atoms[(layer, j)],
        type='neural_connection',
        weight=value
    )

# Phase 3: BPE on topology (find repeated attention head patterns)
attention_patterns = find_common_subgraphs(relations)
```

**Result**: Model stored as directed weighted graph of neuron connections.

---

### Images

```python
# Phase 1: Pre-populate color atoms (FAST)
color_atoms = {}
for r in range(256):
    for g in range(256):
        for b in range(256):
            # Only create atoms for colors that actually appear
            if (r, g, b) in image_histogram:
                spatial_key = calculate_color_spatial_key(r, g, b)
                atom_id = await insert_atom(f"RGB({r},{g},{b})", spatial_key)
                color_atoms[(r, g, b)] = atom_id

# Phase 2: Stream pixel adjacencies as relations
for y in range(height):
    for x in range(width):
        pixel_color = image[y, x]
        pixel_atom = color_atoms[pixel_color]
        
        # Create spatial relations to neighbors
        if x < width - 1:
            neighbor_color = image[y, x+1]
            await insert_relation(
                source=pixel_atom,
                target=color_atoms[neighbor_color],
                type='horizontal_adjacency',
                metadata={'position': (x, y)}
            )
        
        if y < height - 1:
            neighbor_color = image[y+1, x]
            await insert_relation(
                source=pixel_atom,
                target=color_atoms[neighbor_color],
                type='vertical_adjacency',
                metadata={'position': (x, y)}
            )

# Phase 3: BPE on color patterns (find repeated texture patches)
texture_patterns = find_common_subgraphs(color_relations)
# Repeated 3×3 patches → composition atoms
```

**Result**: Image stored as graph of color adjacencies with spatial positions.

---

### Video

```python
# Phase 1: Pre-populate frame atoms + color atoms
frame_atoms = await atomize_frames(video.frame_count)  # Frame identifiers
color_atoms = await atomize_colors(unique_colors)      # Color palette

# Phase 2: Stream temporal + spatial relations
for frame_idx, frame in enumerate(video):
    # Spatial relations (within frame)
    await insert_pixel_adjacencies(frame, color_atoms)
    
    # Temporal relations (between frames)
    if frame_idx > 0:
        await insert_motion_vectors(
            prev_frame=video[frame_idx-1],
            curr_frame=frame,
            frame_atoms=frame_atoms
        )

# Phase 3: BPE on motion patterns (find repeated movements)
motion_patterns = find_common_temporal_subgraphs(temporal_relations)
```

**Result**: Video stored as 4D graph (space + time) of color/motion relations.

---

### Databases

```python
# Phase 1: Pre-populate column atoms + table atoms
table_atoms = await atomize_tables(db.schema)
column_atoms = await atomize_columns(db.schema)

# Phase 2: Stream rows as relations (foreign keys, joins)
for row in table.rows:
    row_atom = await insert_atom(row.primary_key)
    
    # Each column value = relation
    for column, value in row.items():
        value_atom = await atomize_value(value)
        await insert_relation(
            source=row_atom,
            target=value_atom,
            type=column_atoms[column],
            metadata={'table': table.name}
        )
    
    # Foreign keys = direct relations between rows
    if row.foreign_key:
        await insert_relation(
            source=row_atom,
            target=foreign_row_atom,
            type='foreign_key',
            weight=1.0
        )

# Phase 3: BPE on query patterns (find repeated join patterns)
join_patterns = find_common_query_subgraphs(relations)
```

**Result**: Database stored as graph of row/column/table relations.

---

### File Systems

```python
# Phase 1: Pre-populate path atoms
path_atoms = {}
for path in filesystem.walk():
    spatial_key = calculate_path_spatial_key(path)
    atom_id = await insert_atom(path, spatial_key)
    path_atoms[path] = atom_id

# Phase 2: Stream directory structure as relations
for parent_path, children in filesystem.tree():
    parent_atom = path_atoms[parent_path]
    
    for child_path in children:
        child_atom = path_atoms[child_path]
        await insert_relation(
            source=parent_atom,
            target=child_atom,
            type='contains',
            metadata={'is_directory': child_path.is_dir()}
        )

# Phase 3: BPE on directory patterns (find repeated project structures)
project_patterns = find_common_subtrees(directory_relations)
# node_modules/, src/, dist/ → composition atoms for project templates
```

**Result**: File system stored as tree graph of containment relations.

---

### Audio

```python
# Phase 1: Pre-populate frequency atoms (FFT bins)
freq_atoms = {}
for freq_bin in range(fft_size):
    spatial_key = calculate_frequency_spatial_key(freq_bin)
    atom_id = await insert_atom(f"freq_{freq_bin}Hz", spatial_key)
    freq_atoms[freq_bin] = atom_id

# Phase 2: Stream audio as spectral relations
for time_window, spectrum in stft(audio):
    # Each time window = relations to active frequencies
    for freq_bin, amplitude in enumerate(spectrum):
        if amplitude > threshold:
            await insert_relation(
                source=time_window_atom,
                target=freq_atoms[freq_bin],
                type='spectral_component',
                weight=amplitude,
                metadata={'time': time_window}
            )

# Phase 3: BPE on harmonic patterns (find chords, melodies)
harmonic_patterns = find_common_frequency_clusters(spectral_relations)
```

**Result**: Audio stored as time-frequency graph of spectral relations.

---

### Network Packets

```python
# Phase 1: Pre-populate IP atoms + port atoms
ip_atoms = await atomize_ip_addresses(network.ips)
port_atoms = await atomize_ports(range(65536))
protocol_atoms = await atomize_protocols(['TCP', 'UDP', 'ICMP'])

# Phase 2: Stream packets as relations (flows)
for packet in packet_stream:
    await insert_relation(
        source=ip_atoms[packet.src_ip],
        target=ip_atoms[packet.dst_ip],
        type=protocol_atoms[packet.protocol],
        weight=packet.size,
        metadata={
            'src_port': packet.src_port,
            'dst_port': packet.dst_port,
            'timestamp': packet.timestamp
        }
    )

# Phase 3: BPE on traffic patterns (find repeated communication flows)
flow_patterns = find_common_connection_patterns(packet_relations)
```

**Result**: Network traffic stored as directed graph of IP relations.

---

## The Universal Schema

**Every data type maps to the same three tables:**

### 1. `atom` Table
Stores:
- **Primitives**: Smallest indivisible constants (characters, colors, frequencies, IPs)
- **Compositions**: Repeated patterns (via `composition_ids` array)

### 2. `atom_relation` Table
Stores:
- **Sequences**: Order/adjacency (text, audio, video)
- **Connections**: Graph edges (neural networks, databases, file systems)
- **Flows**: Temporal/causal relationships (video motion, network packets)

### 3. Spatial Indexing (`spatial_key` column)
- **X, Y, Z**: Semantic coordinates (meaning-based position)
- **M**: Hilbert index (computed from X/Y/Z, enables spatial queries + RLE)

---

## Key Principles (Universal)

### 1. **Structure is KNOWN**
Every format has fixed structure:
- Text: Character set (ASCII, Unicode)
- Models: Vocabulary + architecture
- Images: Color space (RGB, HSV)
- Databases: Schema (tables, columns)
- File Systems: Path hierarchy
- Audio: Frequency bins
- Network: IP addresses, ports

**Atomize this structure FIRST**, don't discover it.

---

### 2. **Relations Preserve Context**
Flattening loses information:
- Text: "cat" → [c, a, t] loses word boundaries
- Models: weights → flat array loses neuron connections
- Images: pixels → flat array loses spatial adjacency
- Databases: rows → flat values loses foreign keys

**Store as relations** to preserve structure.

---

### 3. **Stream, Don't Load**
Never build massive arrays in memory:
- Text: Stream character pairs → relations
- Models: Stream non-zero weights → relations
- Images: Stream pixel adjacencies → relations
- Video: Stream frame differences → relations

**Batch insert** relations (10K at a time).

---

### 4. **Sparse is Free**
Only create relations for meaningful connections:
- Text: BPE tokens (not all character pairs)
- Models: Non-zero weights (skip zeros)
- Images: Similar adjacent colors (skip gradients)
- Network: Active connections (skip idle time)

**Gaps in M coordinate** implicitly encode sparsity.

---

### 5. **Patterns Emerge**
BPE on topology finds repeated structures:
- Text: Common words, phrases
- Models: Attention head patterns
- Images: Texture patches
- Databases: Join patterns
- File Systems: Project templates
- Audio: Chords, melodies

**Create composition atoms** for high-frequency patterns.

---

## Implementation Template

```python
async def atomize_anything(data_source, data_type):
    """
    Universal atomization pipeline for ANY data type.
    """
    # Phase 1: Pre-populate structural atoms (FAST)
    structural_atoms = await extract_and_atomize_structure(
        data_source=data_source,
        data_type=data_type
    )
    # Result: Lookup table for O(1) atom resolution
    
    # Phase 2: Stream relations (NO MEMORY EXPLOSION)
    relation_batch = []
    async for source, target, strength in stream_relations(data_source):
        # Resolve atoms (O(1) lookup)
        source_atom_id = structural_atoms[source]
        target_atom_id = structural_atoms[target]
        
        # Create relation
        relation = {
            'source_atom_id': source_atom_id,
            'target_atom_id': target_atom_id,
            'relation_type_id': get_relation_type(data_type),
            'weight': strength,
            'metadata': get_relation_metadata(source, target)
        }
        relation_batch.append(relation)
        
        # Batch insert
        if len(relation_batch) >= 10000:
            await db.batch_insert_relations(relation_batch)
            relation_batch.clear()
    
    # Insert remaining
    if relation_batch:
        await db.batch_insert_relations(relation_batch)
    
    # Phase 3: Crystallize patterns (OPTIONAL)
    patterns = await find_common_subgraphs(
        relations=db.query_relations(data_type),
        min_frequency=threshold
    )
    
    for pattern in patterns:
        # Create composition atom for repeated pattern
        await db.insert_atom({
            'composition_ids': pattern.atom_ids,
            'metadata': {
                'type': f'{data_type}_pattern',
                'frequency': pattern.frequency
            }
        })
    
    return {
        'structural_atoms': len(structural_atoms),
        'relations': relation_count,
        'patterns': len(patterns)
    }
```

---

## Performance Expectations (Universal)

**Phase 1: Pre-populate** (parallel, batch insert)
- Text: 256-150K chars → seconds
- Models: 151K tokens + 131K neurons → seconds
- Images: 16M colors (RGB) → minutes (only used colors)
- Databases: 100s-1000s tables/columns → seconds
- File Systems: 100Ks paths → minutes
- Audio: 4096 FFT bins → milliseconds
- Network: 1M IPs + 65K ports → minutes

**Phase 2: Stream relations** (batched, no memory explosion)
- Throughput: 10K-100K relations/second
- Memory: Constant (batch size = 10K relations)
- Storage: ~100 bytes per relation

**Phase 3: Crystallize patterns** (graph queries)
- Depends on: Relation count, pattern complexity
- Typical: Minutes to hours for large datasets
- Optional: Can skip for simple use cases

---

## Validation Queries (Universal)

### Check Structure Atomization
```sql
-- Verify all structural atoms exist
SELECT 
    metadata->>'modality' as modality,
    COUNT(*) as atom_count
FROM atom
WHERE metadata->>'modality' IN ('vocabulary', 'neuron-id', 'color', 'path', 'frequency', 'ip-address')
GROUP BY metadata->>'modality';
```

### Check Relations
```sql
-- Verify relations created
SELECT 
    rt.relation_name,
    COUNT(*) as relation_count,
    AVG(ar.weight) as avg_strength
FROM atom_relation ar
JOIN atom rt ON ar.relation_type_id = rt.atom_id
GROUP BY rt.relation_name;
```

### Check Sparsity
```sql
-- Measure sparsity via M-coordinate gaps
SELECT 
    metadata->>'modality',
    AVG(ST_M(spatial_key) - LAG(ST_M(spatial_key)) OVER (ORDER BY ST_M(spatial_key))) as avg_gap
FROM atom
WHERE spatial_key IS NOT NULL
GROUP BY metadata->>'modality';
```

### Check Patterns
```sql
-- Find crystallized patterns
SELECT 
    metadata->>'type' as pattern_type,
    metadata->>'frequency' as frequency,
    array_length(composition_ids, 1) as pattern_size
FROM atom
WHERE composition_ids IS NOT NULL
ORDER BY (metadata->>'frequency')::int DESC
LIMIT 20;
```

---

## Summary

**The Universal Pattern**:
1. Extract structure (KNOWN from format)
2. Pre-populate structural atoms (parallel, deterministic positions)
3. Stream relations (batched, no memory explosion)
4. Crystallize patterns (BPE on topology)

**Applies to ALL data**:
- Text, code, AI models, images, video, audio, databases, file systems, network packets, 3D models, sensor data, genomic sequences, chemical structures, etc.

**Benefits**:
- ✅ No memory explosion (stream relations)
- ✅ O(1) lookups (pre-populated atoms)
- ✅ Spatial queries (all atoms positioned)
- ✅ Sparse automatic (only meaningful relations)
- ✅ Pattern discovery (BPE on topology)
- ✅ Universal (same schema for everything)

**The key insight**: ALL digital content is a **GRAPH OF RELATIONS** between **STRUCTURAL ATOMS**.
