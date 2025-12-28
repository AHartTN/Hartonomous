# Hartonomous Roadmap

**Goal**: Replace traditional AI inference with deterministic spatial-semantic graph traversal.

## Current State Summary

| Layer | Status | Confidence |
|-------|--------|------------|
| Atom (Unicode → 4D → Hilbert) | ✅ Complete | High |
| Composition (CPE, Merkle) | ✅ Complete | High |
| Storage (PostgreSQL + PostGIS) | ✅ Complete | High |
| Model Ingestion (SafeTensor) | ⚠️ Exists, Untested | Medium |
| MLOps (Inference/Generation) | ⚠️ Exists, Newly Tested | Medium |
| CLI Interface | ✅ Complete | High |
| C# Services | ✅ Complete | High |
| E2E Demo | ❌ Missing | N/A |

---

## Phase 1: Prove the Substrate (COMPLETE)

**Duration**: Completed  
**Goal**: Demonstrate that deterministic spatial encoding works.

### ✅ Completed Tasks

1. **Atom Layer** - Unicode codepoints mapped to 4D coordinates via CPE
2. **Hilbert Curve** - 4D → 128-bit Hilbert index preserving locality
3. **PostgreSQL Integration** - Atoms and compositions stored with spatial indices
4. **Relationship Graph** - Weighted edges with 5 relationship types
5. **Unit Tests** - 48 test cases, 2765 assertions, all passing

---

## Phase 2: MLOps Foundation (IN PROGRESS)

**Duration**: 1-2 weeks  
**Goal**: Prove inference and generation actually produce sensible output.

### 🔄 In Progress

| Task | Description | Status |
|------|-------------|--------|
| MLOps Tests | test_mlops.cpp with comprehensive coverage | ✅ Created |
| Native API Exports | C API for Complete, Ask, Generate, Infer, Attend | ✅ Created |
| C# P/Invoke | NativeInterop bindings for MLOps functions | ✅ Created |
| CLI Commands | `ask` and `complete` commands | ✅ Created |
| DatabaseService | Complete() and Ask() methods | ✅ Created |

### ⏳ Pending

| Task | Description | Priority |
|------|-------------|----------|
| REPL Chat Mode | Interactive multi-turn conversation | High |
| Model Loading | Actually load and test a small model | High |
| E2E Demo | End-to-end question answering demo | High |
| Benchmarks | Performance comparison vs traditional inference | Medium |

---

## Phase 3: Model Ingestion (Next)

**Duration**: 2-3 weeks  
**Goal**: Load real models and prove weight storage works.

### Tasks

1. **SafeTensor Parser Validation**
   - Load TinyLlama (1.1B params) as test model
   - Verify weight extraction and 4D embedding
   - Store weights as relationships in PostgreSQL

2. **Tokenizer Integration**
   - Validate SentencePiece model loading
   - Build vocabulary → Hilbert mapping
   - Create reverse mapping for decoding

3. **Weight → Relationship Pipeline**
   - Convert attention weights to ModelWeight relationships
   - Convert FFN weights to SemanticLink relationships
   - Verify bidirectional traversal

4. **Integration Tests**
   - Load model → Store → Query → Verify integrity
   - Round-trip encoding/decoding
   - Weight retrieval accuracy

---

## Phase 4: Inference Engine (Core)

**Duration**: 3-4 weeks  
**Goal**: Replace forward pass with graph traversal.

### Tasks

1. **Attention Replacement**
   - Trajectory intersection instead of softmax(QK^T)V
   - Use spatial proximity for attention scores
   - Validate against reference attention outputs

2. **Generation Pipeline**
   - Context → Neighbors → Score → Select
   - Temperature-based sampling from scored candidates
   - Beam search via parallel path exploration

3. **Inference Path**
   - A* search through relationship graph
   - Path cost = accumulated edge weights
   - Early termination at goal or confidence threshold

4. **Caching Strategy**
   - PostgreSQL query caching
   - Hot path memoization
   - Precomputed common sequences

---

## Phase 5: Knowledge Integration

**Duration**: 2-3 weeks  
**Goal**: Extend beyond model weights to knowledge graphs.

### Tasks

1. **Knowledge Ingestion**
   - Wikipedia → composition storage
   - Wikidata → relationship extraction
   - Custom knowledge base loading

2. **RAG-like Retrieval**
   - Query → spatial neighbors → relevant compositions
   - No embedding model needed - pure spatial proximity

3. **Context Window Alternative**
   - Replace token-limited context with graph traversal
   - "Infinite context" via relationship chains
   - Relevance decay via edge weights

---

## Phase 6: Production Hardening

**Duration**: 3-4 weeks  
**Goal**: Make it reliable and performant.

### Tasks

1. **Performance Optimization**
   - Query plan analysis and optimization
   - Index tuning for common access patterns
   - Connection pooling and query batching

2. **Error Handling**
   - Graceful degradation on database failures
   - Input validation and sanitization
   - Comprehensive logging

3. **Scalability**
   - Read replicas for query distribution
   - Sharding strategy for large knowledge bases
   - Horizontal scaling patterns

4. **Monitoring**
   - Query latency metrics
   - Graph traversal depth tracking
   - Memory usage monitoring

---

## Phase 7: User Experience

**Duration**: 2-3 weeks  
**Goal**: Make it usable.

### Tasks

1. **REPL Improvements**
   - Multi-turn conversation with history
   - Streaming output for long responses
   - Command history and editing

2. **Web Interface**
   - Blazor chat interface
   - Real-time generation streaming
   - Conversation history

3. **API**
   - REST endpoints for inference
   - WebSocket for streaming
   - OpenAI-compatible API wrapper

---

## Key Milestones

| Milestone | Target | Success Criteria |
|-----------|--------|------------------|
| M1: Tests Pass | Week 1 | All MLOps tests green |
| M2: Model Loads | Week 4 | TinyLlama stored in PostgreSQL |
| M3: First Answer | Week 6 | System answers simple factual question |
| M4: Coherent Generation | Week 8 | 50-token generation makes sense |
| M5: Benchmark Ready | Week 12 | Performance comparison published |
| M6: Demo Day | Week 14 | Interactive demo with audience |

---

## Technical Debt

| Item | Risk | Mitigation |
|------|------|------------|
| Untested MLOps | High | test_mlops.cpp added |
| No real model tested | High | TinyLlama integration planned |
| Memory management | Medium | Smart pointers everywhere |
| Error propagation | Medium | Result types vs exceptions |
| PostgreSQL single point | Medium | Replica support planned |

---

## Open Questions

1. **Attention Accuracy**: Does trajectory intersection approximate softmax well enough?
2. **Generation Quality**: Will spatial sampling produce coherent text?
3. **Performance**: Can PostgreSQL queries match GPU tensor ops?
4. **Scale**: How many atoms/relationships before performance degrades?
5. **Multi-model**: Can different models coexist in same database?

---

## Success Definition

The project succeeds when:

1. **Functional**: Given a question, returns a relevant answer
2. **Deterministic**: Same input always produces same output
3. **Explainable**: Can show the inference path (graph traversal)
4. **Efficient**: Response time < 5 seconds for typical queries
5. **Maintainable**: No GPU required, standard PostgreSQL

---

## Risk Assessment

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| Spatial encoding loses semantic info | Medium | Critical | Extensive testing |
| Graph traversal too slow | Medium | High | Query optimization |
| PostgreSQL bottleneck | Low | High | Caching, replicas |
| Model weights don't translate | Medium | Critical | Start with small model |
| Team bandwidth | Low | Medium | Incremental milestones |

---

## Next Actions (This Week)

1. ✅ Create test_mlops.cpp with comprehensive MLOps tests
2. ✅ Add native API exports for inference functions
3. ✅ Add C# P/Invoke declarations
4. ✅ Create CLI ask/complete commands
5. ⏳ Build and run MLOps tests to verify they pass
6. ⏳ Create REPL chat mode for interactive testing
7. ⏳ Download and test TinyLlama model ingestion
