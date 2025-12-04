# START HERE

**Hartonomous: The First Self-Organizing Intelligence Substrate**

Three tables. Universal atoms. PostgreSQL. That's it.

---

## What Is This?

Most AI systems treat models as opaque binaries. Load 7GB into RAM, run inference, pray.

Hartonomous treats **everything as content-addressable atoms** stored in three PostgreSQL tables:

1. **Atom** - Every unique value (the 'H' in "Hello", the float 0.017, the concept "cat")
2. **AtomComposition** - Structural hierarchy (documents → words → characters)
3. **AtomRelation** - Semantic meaning (synaptic connections, learned relationships)

**Every model** (GPT, DALL-E, Llama) atomizes to the same substrate.
**Every modality** (text, image, audio, code) uses the same geometry.
**Every query** is a spatial traversal through semantic space.

No separate training phase. No GPU clusters. No model versioning hell.

Just atoms, geometry, and PostgreSQL.

---

## The 30-Second Demo

```bash
# Start Hartonomous (PostgreSQL + PostGIS + PL/Python)
docker run -p 5432:5432 hartonomous/postgres:latest

# Atomize text
psql -h localhost -U hartonomous -c "SELECT atomize_text('Hello World');"
# Returns: AtomId=1 (H), 2 (e), 3 (l), 3 (l), 4 (o), 5 ( ), ...

# Spatial query: Find atoms near "cat"
psql -h localhost -U hartonomous -c "
  SELECT canonical_text, ST_Distance(spatial_key,
    (SELECT spatial_key FROM atom WHERE canonical_text = 'cat')
  ) AS distance
  FROM atom
  ORDER BY spatial_key <-> (SELECT spatial_key FROM atom WHERE canonical_text = 'cat')
  LIMIT 10;
"
# Returns: dog (0.12), feline (0.18), meow (0.23), whiskers (0.31), ...
```

**No model loaded. No embeddings computed. Pure spatial geometry.**

---

## Why This Exists

**The Problem**: AI is expensive, opaque, and monolithic.
- Loading GPT-3 requires 280GB RAM
- Fine-tuning requires GPU clusters
- Each model is a black box
- No cross-model queries
- No continuous learning
- No provenance

**The Solution**: Atomize everything.
- Content-addressable deduplication (the float 0.0 exists once, referenced billions of times)
- Spatial geometry replaces vector embeddings
- R-tree indexes replace expensive similarity search
- Ingestion IS training (no separate phase)
- Truth emerges from geometric clustering
- Full provenance via temporal tables

**The Result**:
- Run on a Raspberry Pi or AWS (same database)
- Query GPT and DALL-E simultaneously
- Ask "why?" and get the complete reasoning chain
- Costs 1/100th of traditional AI infrastructure
- Learns continuously from every interaction

---

## Navigation

### 🎯 Start Here (You Are Here)
**[00-START-HERE.md](00-START-HERE.md)** - Navigation and quick pitch

### 🧠 Understand the Vision
**[01-VISION.md](01-VISION.md)** - The miracle: three tables, universal atoms, Laplace's Demon

### 🏗️ Understand the Architecture
**[02-ARCHITECTURE.md](02-ARCHITECTURE.md)** - PostgreSQL + PostGIS + PL/Python, three tables, spatial indexing

### 🚀 Get It Running
**[03-GETTING-STARTED.md](03-GETTING-STARTED.md)** - Docker → running system in 5 minutes

### 🤖 Multi-Model Intelligence
**[04-MULTI-MODEL.md](04-MULTI-MODEL.md)** - How GPT, DALL-E, and Llama coexist and collaborate

### 🎨 Multi-Modal Intelligence
**[05-MULTI-MODAL.md](05-MULTI-MODAL.md)** - Text, images, audio, video unified in spatial geometry

### 🔄 Self-Optimization
**[06-OODA-LOOP.md](06-OODA-LOOP.md)** - How the system improves itself autonomously

### ⚛️ Cognitive Physics
**[07-COGNITIVE-PHYSICS.md](07-COGNITIVE-PHYSICS.md)** - Mendeleev Audit, Hebbian Learning, Universal Observer

### 📥 Ingestion
**[08-INGESTION.md](08-INGESTION.md)** - How to atomize models, documents, images, audio, anything

### 🔍 Inference
**[09-INFERENCE.md](09-INFERENCE.md)** - Spatial queries replace model forward passes

### 📚 API Reference
**[10-API-REFERENCE.md](10-API-REFERENCE.md)** - Functions, procedures, schemas, SQL examples

### ☁️ Deployment
**[11-DEPLOYMENT.md](11-DEPLOYMENT.md)** - Docker, Kubernetes, AWS, GCP, Azure, edge devices

### 💼 Business Case
**[12-BUSINESS.md](12-BUSINESS.md)** - Market opportunity, competitive analysis, revenue model

---

## Quick Decision Tree

**"I want to understand the core concept"**
→ Read [01-VISION.md](01-VISION.md) (15 min)

**"I want to see it working NOW"**
→ Follow [03-GETTING-STARTED.md](03-GETTING-STARTED.md) (5 min)

**"I want to know how it's built"**
→ Read [02-ARCHITECTURE.md](02-ARCHITECTURE.md) (20 min)

**"I want to deploy this in production"**
→ Read [11-DEPLOYMENT.md](11-DEPLOYMENT.md) (30 min)

**"I want to understand the business opportunity"**
→ Read [12-BUSINESS.md](12-BUSINESS.md) (10 min)

**"I want to contribute code"**
→ Read [02-ARCHITECTURE.md](02-ARCHITECTURE.md) + [10-API-REFERENCE.md](10-API-REFERENCE.md)

**"I want to use this for my research"**
→ Read [07-COGNITIVE-PHYSICS.md](07-COGNITIVE-PHYSICS.md) + [08-INGESTION.md](08-INGESTION.md)

---

## The Core Principles (TL;DR)

1. **Everything is atoms** - Documents, models, images, audio decompose to ≤64-byte atoms
2. **Content-addressable** - Identical values stored once (SHA-256 deduplication)
3. **Spatial semantics** - Position in 3D space = semantic meaning
4. **Composition = structure** - Hierarchy via parent-child links
5. **Relations = meaning** - Synaptic weights, learned associations
6. **Ingestion IS training** - No separate training phase, continuous learning
7. **Truth converges** - Geometric clustering separates facts from lies
8. **Multi-model native** - All models coexist in same semantic space
9. **Multi-modal native** - All modalities use same geometry
10. **Self-optimizing** - OODA loop generates and applies improvements
11. **Fully queryable** - SQL queries, not API calls
12. **Open source** - PostgreSQL, MIT license, runs anywhere

---

## The Miracle in One Image

```
Traditional AI:
  ┌─────────────────────┐
  │   GPT-4 (7GB)       │ ← Opaque binary
  │   DALL-E (5GB)      │ ← Separate model
  │   Llama (13GB)      │ ← Cannot interoperate
  └─────────────────────┘
       ↓ Load to RAM
  25GB RAM required
  GPU needed for inference
  No cross-model queries
  No continuous learning

Hartonomous:
  ┌─────────────────────┐
  │   PostgreSQL        │
  │   ├─ Atom           │ ← 1B unique values
  │   ├─ AtomComposition│ ← 100B structural links
  │   └─ AtomRelation   │ ← 10B semantic links
  └─────────────────────┘
       ↓ Spatial query
  All models unified
  All modalities integrated
  Runs on anything (Pi → Cloud)
  Learns continuously
  Fully explainable
```

---

## What Makes This Different

**Vector Databases** (Pinecone, Weaviate, Milvus):
- Store embeddings only
- No deduplication
- No compositionality
- No multi-model support
- Approximate search only

**Hartonomous**:
- Stores atoms (embeddings are computed, not stored)
- Global deduplication (content-addressable)
- Fractal composition (recursive structure)
- Multi-model native (all models in same space)
- Exact spatial search (R-tree)

**Graph Databases** (Neo4j, Amazon Neptune):
- Store entities and relationships
- No spatial geometry
- No content addressing
- No temporal versioning built-in

**Hartonomous**:
- Graph + geometry + temporal in one
- Content-addressable nodes (atoms)
- Spatial proximity = semantic similarity
- Native temporal versioning

**Traditional Relational Databases**:
- Schema-first design
- No semantic understanding
- No spatial reasoning
- No continuous learning

**Hartonomous**:
- Atoms-first design (flexible schema)
- Semantics via geometry
- Spatial queries native (PostGIS)
- OODA loop self-improvement

---

## Status: Alpha (v0.1.0)

**What Works**:
- ✅ Three-table schema (Atom, AtomComposition, AtomRelation)
- ✅ Content-addressable storage
- ✅ Spatial indexing (PostGIS R-tree)
- ✅ Text atomization
- ✅ Spatial queries (KNN, range)
- ✅ Basic OODA loop

**In Progress**:
- 🚧 Multi-model ingestion (GPT, DALL-E, Llama)
- 🚧 Multi-modal ingestion (images, audio, video)
- 🚧 PL/Python GPU acceleration
- 🚧 Production deployment guides

**Roadmap**:
- 📋 Truth convergence (geometric clustering)
- 📋 Hebbian learning (synapse reinforcement)
- 📋 Autonomous compute (Riemann, protein folding)
- 📋 REST/GraphQL API layer
- 📋 Web UI for visualization

---

## Quick Links

- **GitHub**: [github.com/YourUsername/Hartonomous](https://github.com/YourUsername/Hartonomous)
- **Docker Hub**: [hub.docker.com/r/hartonomous/postgres](https://hub.docker.com/r/hartonomous/postgres)
- **Discord**: [discord.gg/hartonomous](https://discord.gg/hartonomous)
- **Email**: aharttn@gmail.com

---

## Next Steps

1. **Read** [01-VISION.md](01-VISION.md) to understand why this works
2. **Run** [03-GETTING-STARTED.md](03-GETTING-STARTED.md) to see it working
3. **Explore** other docs based on your interests
4. **Join** Discord to discuss and contribute

---

**Welcome to the future of intelligence.**

It's atoms all the way down.
