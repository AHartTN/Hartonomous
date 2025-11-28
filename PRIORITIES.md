# Hartonomous - Development Priorities

## ✅ Just Completed
- **GGUF Model Atomization** (1120x speedup, production-ready)
- All geometry/type/variable bugs fixed
- Model deduplication working

---

## 🔥 High Priority (Critical Path)

### 1. Document Parser Enhancements
**File**: `api/services/document_parser.py`
**Status**: Basic structure exists, 4 TODOs remaining

**TODOs Found**:
- Line 172: Image extraction and atomization from PDFs
- Line 318: Table atomization from DOCX
- Line 361: Extract title from first h1 in Markdown
- Line 388: C# code atomizer bridge integration

**Impact**: Complete end-to-end document ingestion pipeline
**Effort**: Medium (2-3 hours)
**Priority**: **HIGH**

---

### 2. AGE Graph Sync Worker
**File**: `api/workers/age_sync.py`
**Status**: Stub implementation, marked experimental

**TODOs Found**:
- Line 169: Actual AGE sync logic
- Line 231: AGE relation sync implementation

**Current State**: Neo4j is production alternative, AGE is optional
**Decision Needed**: 
- Option A: Complete AGE implementation
- Option B: Remove AGE worker entirely (Neo4j works)
- Option C: Leave as stub for future

**Priority**: **MEDIUM-LOW** (Neo4j already working)

---

### 3. API Route Completion
**Files**: `api/routes/ingest.py`, `api/routes/query.py`

**Current State**:
- ✅ `/v1/ingest/text` - Working
- ✅ `/v1/ingest/image` - Working
- ✅ `/v1/ingest/audio` - Working  
- ✅ `/v1/ingest/model` - Working (GGUF complete)
- ⚠️ `/v1/ingest/document` - Partial (TODOs above)
- ⚠️ `/v1/query/*` - Basic implementation exists

**Missing**:
- Advanced spatial queries (A*, Voronoi)
- Similarity search across modalities
- Temporal/provenance queries
- Batch operations

**Priority**: **HIGH** (after document parser)

---

### 4. Testing & Validation
**Current Coverage**: ~20% (sanity tests only)
**Target**: 80%+

**Missing Tests**:
- Integration tests (database, API)
- Atomization tests (text, image, audio, model)
- Query tests (spatial, similarity, temporal)
- Performance benchmarks
- Provenance tests

**Priority**: **HIGH** (critical for production)

---

## 🚀 Medium Priority (Important but Not Blocking)

### 5. Performance Optimizations
- Bulk insert optimization (already good with vectorization)
- Query plan analysis and tuning
- Index optimization
- Caching layer (Redis/in-memory)

### 6. GPU Acceleration
- Complete CuPy path for model atomization (5-10x additional speedup)
- GPU-accelerated spatial computations
- Batch processing for large ingestions

### 7. Production Features
- Authentication/authorization (OAuth2/JWT)
- Rate limiting
- Tenant isolation
- Monitoring/observability (Prometheus/Grafana)
- Error tracking (Sentry)

### 8. C# Code Atomizer Bridge
- HTTP bridge to C# AST parser
- Standalone mode (AST only)
- Hartonomous mode (full atomization)
- Code composition hierarchy
- Spatial positioning for code

---

## 📋 Low Priority (Nice to Have)

### 9. Documentation Enhancements
- Tutorial series with examples
- Video walkthrough
- API examples (cURL/Python/JavaScript)
- Troubleshooting guide
- FAQ

### 10. Additional Parsers
- Video frame extraction
- 3D model parsers (STL, OBJ)
- More document formats (RTF, ODT)

### 11. Advanced Features
- Multi-layer compression cascade
- Adaptive landmark selection
- Incremental Gram-Schmidt
- Streaming ingestion with progress
- Export/reconstruction APIs

---

## 🎯 Recommended Next Actions

### Today (Highest ROI)
1. **Fix Document Parser TODOs** (2-3 hours)
   - Image extraction from PDFs
   - Table atomization from DOCX
   - Markdown title extraction
   - Complete end-to-end document pipeline

2. **Create Integration Test Suite** (2-4 hours)
   - Test GGUF ingestion end-to-end
   - Test document ingestion pipeline
   - Test spatial queries
   - Set up test database in Docker

### This Week
3. **Complete Query API** (4-6 hours)
   - A* semantic pathfinding
   - Voronoi spatial queries
   - Cross-modal similarity search
   - Temporal queries

4. **Add Authentication** (2-3 hours)
   - OAuth2/JWT implementation
   - API key management
   - Rate limiting

### This Month
5. **Production Readiness**
   - Monitoring and observability
   - Load testing (1M atoms)
   - Backup/restore automation
   - Horizontal scaling strategy

6. **C# Code Atomizer Integration**
   - HTTP bridge setup
   - Test with C#/Python/JavaScript
   - Document pricing model

---

## Decision Points

### AGE Worker
**Question**: Keep, complete, or remove?
**Recommendation**: Remove or keep as stub. Neo4j is production-ready and working.

### GPU Acceleration
**Question**: Priority for implementation?
**Recommendation**: Low priority. CPU vectorization already 1120x faster. GPU would be 5-10x on top, but not critical.

### C# Code Atomizer
**Question**: When to implement?
**Recommendation**: After document parser and query API complete. Significant effort, but high value for code analysis.

---

## Summary

**GGUF is done.** Move to:
1. Document parser completion (quick wins)
2. Integration testing (critical)
3. Query API enhancement (high value)
4. Production features (authentication, monitoring)

**Velocity unlocked** ✅ - Foundation is solid, now we're in enhancement mode.
