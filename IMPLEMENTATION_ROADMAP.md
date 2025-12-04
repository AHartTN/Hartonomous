# Implementation Roadmap

## Completed Work (Tasks 1-2)

### ✅ Task 1: BPE Learning Algorithm
**Status:** COMPLETE  
**Files Modified:**
- `Hartonomous.Infrastructure/Services/BPEService.cs` - Full production implementation (230 lines)
- `Hartonomous.Core/Domain/ValueObjects/SpatialCoordinate.cs` - Added `Interpolate` method

**Implementation Details:**
- Classical Byte Pair Encoding with spatial awareness
- Pair frequency counting with `Dictionary<(Guid, Guid), long>`
- Iterative merging until `minFrequency` threshold or `maxVocabularySize`
- Spatial interpolation via `SpatialCoordinate.Interpolate` for merged tokens
- Transaction batching (commit every 100 iterations)
- Hash-based deduplication before creating new tokens
- Post-learning vocabulary ranking by frequency
- Comprehensive error handling with cancellation support

**Key Algorithm Features:**
1. **Frequency Counting:** Counts all adjacent token pairs across all sequences
2. **Pair Selection:** Selects most frequent pair for merging
3. **Merge Operation:** Creates new token, updates all sequences, replaces pair occurrences
4. **Spatial Representation:** New token positioned at centroid of constituent tokens (Cartesian average → Hilbert re-encode)
5. **Convergence:** Stops when most frequent pair falls below threshold or vocabulary size reached
6. **Persistence:** Batch commits prevent long-running transactions

### ✅ Task 2: Content Ingestion Pipeline
**Status:** COMPLETE  
**Files Created:**
- `Hartonomous.Core/Application/Interfaces/IContentDecomposer.cs` - Strategy interface
- `Hartonomous.Core/Application/Interfaces/IContentDecomposerFactory.cs` - Factory interface
- `Hartonomous.Infrastructure/Services/Decomposers/BinaryDecomposer.cs` - Byte-level fallback
- `Hartonomous.Infrastructure/Services/Decomposers/TextDecomposer.cs` - Multi-granularity text decomposition
- `Hartonomous.Infrastructure/Services/ContentDecomposerFactory.cs` - Chain of responsibility factory

**Files Modified:**
- `Hartonomous.Infrastructure/Extensions/InfrastructureServicesExtensions.cs` - Registered decomposers
- `Hartonomous.Core/Application/Commands/ContentIngestion/IngestContentCommandHandler.cs` - Complete rewrite

**Implementation Details:**

**Decomposer Architecture:**
- Strategy pattern with content-type specific decomposers
- Factory with three-tier selection: declared type → auto-detect → binary fallback
- Auto-detection: 70% printable character threshold for text classification

**BinaryDecomposer:**
- Universal fallback for unknown/binary content
- Byte-level atomic decomposition
- Always returns `true` for `CanDecompose`

**TextDecomposer:**
- Multi-granularity decomposition: bytes → UTF-8 chars → words → sentences
- UTF-8 aware character extraction with proper encoding
- Word extraction: splits on punctuation/whitespace
- Sentence extraction: Only for text > 100 chars, min sentence length 10
- Binary fallback for invalid UTF-8

**IngestContentCommandHandler:**
- Uses factory for content-type aware decomposition
- Batch deduplication: groups by hash, processes in 100-constant batches
- Bulk operations: `AddRangeAsync` for new constants
- Efficient lookups: batched hash queries minimize database round-trips
- Proper statistics: tracks total vs unique, calculates deduplication ratio
- Duplicate ingestion detection via content hash

---

## Pending Work (Tasks 3-5)

### 🔄 Task 3: Query Engine Implementation
**Priority:** HIGH  
**Estimated Effort:** 2-3 days

**Objectives:**
1. **Spatial Queries** - Leverage PostGIS + Hilbert B-tree indexes
   - k-NN proximity search (find nearest N constants)
   - Bounded region queries (constants within geometric bounds)
   - Radius search (constants within distance threshold)

2. **Graph Traversal** - Content reconstruction from constants
   - Recursive parent lookup (navigate BPE token hierarchy)
   - Full content reconstruction from root tokens
   - Partial content extraction by position/range

3. **Performance Optimization**
   - Redis result caching (sub-second repeat queries)
   - Query specifications pattern (composable query logic)
   - Materialized view utilization
   - Query plan optimization (EXPLAIN ANALYZE)

**Files to Create/Modify:**
- `Hartonomous.Core/Application/Interfaces/IQueryService.cs` - Query service interface
- `Hartonomous.Infrastructure/Services/QueryService.cs` - Implementation
- `Hartonomous.Data/Extensions/SpatialQueryExtensions.cs` - PostGIS query helpers
- `Hartonomous.Core/Application/Specifications/` - Query specification implementations
- `Hartonomous.Infrastructure/Caching/QueryCacheService.cs` - Redis-backed query cache

**Technical Requirements:**
- PostGIS spatial functions: `ST_DWithin`, `ST_Distance`, `ST_Within`, `<->` (k-NN operator)
- Hilbert curve range queries for efficient spatial scans
- Cache key design: hash of query parameters + spatial bounds
- Cache invalidation: time-based (5-15 min TTL) + event-driven (on new ingestions)
- Target performance: < 100ms for cached queries, < 500ms for uncached spatial queries

**Query Types to Implement:**
1. `FindNearestConstants(coordinate, count, filter)` - k-NN search
2. `FindConstantsInRegion(bounds, filter)` - Bounded region
3. `FindConstantsWithinRadius(coordinate, radius, filter)` - Radius search
4. `ReconstructContent(rootTokenIds)` - Graph traversal
5. `FindSimilarContent(contentHash, threshold)` - Similarity search

---

### 🔄 Task 4: Background Workers
**Priority:** MEDIUM  
**Estimated Effort:** 1-2 days

**Status:** 1 of 5 workers complete (`MaterializedViewRefreshJob`)

**Remaining Workers:**

#### 4.1 ContentProcessingWorker
**Purpose:** Polls for pending content ingestions, triggers processing  
**Implementation:**
- Query `ContentIngestion` table for `IsComplete = false AND ProcessingStartedAt IS NULL`
- Batch processing (10-20 ingestions per iteration)
- Send `ProcessIngestionCommand` via MediatR
- Update `ProcessingStartedAt` timestamp to prevent duplicate processing
- Retry logic for failed ingestions (exponential backoff)
- Metrics: processing rate, success/failure counts, average processing time

**File:** `Hartonomous.Worker/Jobs/ContentProcessingWorker.cs`

#### 4.2 BPELearningScheduler
**Purpose:** Periodic vocabulary learning from accumulated constants  
**Implementation:**
- Scheduled execution (e.g., daily at 2 AM, configurable)
- Check for new constants since last learning run
- Trigger `LearnVocabularyAsync` with appropriate parameters
- Track vocabulary growth over time
- Metrics: vocabulary size, compression ratio improvements, learning duration

**File:** `Hartonomous.Worker/Jobs/BPELearningScheduler.cs`

#### 4.3 ConstantIndexingWorker
**Purpose:** Maintain spatial indexes and statistics  
**Implementation:**
- Rebuild spatial indexes when fragmentation > threshold
- Update frequency statistics for constants
- Prune unused constants (ReferenceCount = 0, older than retention period)
- Vacuum/analyze database tables
- Metrics: index health, table sizes, vacuum stats

**File:** `Hartonomous.Worker/Jobs/ConstantIndexingWorker.cs`

#### 4.4 LandmarkDetectionWorker
**Purpose:** Identify high-frequency patterns as spatial landmarks  
**Implementation:**
- Query constants with `Frequency > threshold`
- Mark as landmarks in database (`IsLandmark = true`)
- Used for spatial navigation and compression optimization
- Track landmark stability over time
- Metrics: landmark count, frequency distribution, stability scores

**File:** `Hartonomous.Worker/Jobs/LandmarkDetectionWorker.cs`

**Shared Infrastructure:**
- All workers inherit from `BackgroundService`
- Configurable execution intervals via `appsettings.json`
- Health check endpoints for worker status
- Structured logging with correlation IDs
- Graceful shutdown on cancellation

---

### 🔄 Task 5: REST API Endpoints
**Priority:** HIGH  
**Estimated Effort:** 2-3 days

**Controllers to Implement:**

#### 5.1 ContentController
**Endpoints:**
- `POST /api/v1/content` - Ingest new content
- `GET /api/v1/content/{hash}` - Retrieve content by hash
- `GET /api/v1/content/{hash}/metadata` - Get content metadata
- `GET /api/v1/content/{hash}/decomposition` - View constant breakdown
- `DELETE /api/v1/content/{hash}` - Soft delete content

**Features:**
- Request validation (FluentValidation)
- Content-Type negotiation (application/json, application/octet-stream)
- Rate limiting (10 ingestions per minute per user)
- Authentication required (JWT/Entra ID)
- Response compression (gzip/brotli)

**File:** `Hartonomous.API/Controllers/ContentController.cs`

#### 5.2 QueryController
**Endpoints:**
- `POST /api/v1/query/spatial` - Spatial queries (k-NN, region, radius)
- `POST /api/v1/query/reconstruct` - Content reconstruction
- `POST /api/v1/query/similar` - Similarity search
- `GET /api/v1/query/{id}` - Get query results (for async queries)
- `DELETE /api/v1/query/{id}/cache` - Clear cached query result

**Features:**
- Query specification pattern for composable queries
- Async processing for expensive queries (return job ID, poll for results)
- Result pagination (cursor-based)
- Authorization policies (read scope required)

**File:** `Hartonomous.API/Controllers/QueryController.cs`

#### 5.3 VocabularyController
**Endpoints:**
- `GET /api/v1/vocabulary` - List BPE tokens
- `GET /api/v1/vocabulary/{id}` - Get token details
- `GET /api/v1/vocabulary/stats` - Vocabulary statistics
- `POST /api/v1/vocabulary/learn` - Trigger learning (admin only)
- `DELETE /api/v1/vocabulary/{id}` - Delete token (admin only)

**Features:**
- Admin policy enforcement (`[Authorize(Policy = "AdminPolicy")]`)
- Pagination and filtering
- Rate limiting for learning endpoint (1 request per hour)

**File:** `Hartonomous.API/Controllers/VocabularyController.cs`

#### 5.4 HealthController
**Endpoints:**
- `GET /health` - All health checks
- `GET /health/live` - Liveness probe
- `GET /health/ready` - Readiness probe
- `GET /health/database` - Database connectivity
- `GET /health/cache` - Redis connectivity
- `GET /health/workers` - Background worker status

**Features:**
- No authentication required (except detailed checks)
- Prometheus-compatible metrics endpoint
- Response format: JSON with component status

**File:** `Hartonomous.API/Controllers/HealthController.cs`

**Shared API Infrastructure:**
- OpenAPI/Swagger documentation with examples
- Global exception handling middleware
- Request/response logging
- Correlation ID propagation
- Security headers (already configured)
- CORS policy configuration
- API versioning (URL-based: `/api/v1/`)

---

## Testing Strategy

### Current State
- 71 tests passing (from initial setup)
- No tests for new implementations (BPE, decomposers, ingestion)
- Missing `Hartonomous.Infrastructure.Tests` project

### Testing Requirements

#### Unit Tests (Next Priority)
**Hartonomous.Infrastructure.Tests** (CREATE PROJECT)
- `BPEServiceTests.cs` - Test vocabulary learning, pair merging, spatial interpolation, convergence
- `BinaryDecomposerTests.cs` - Byte-level decomposition correctness
- `TextDecomposerTests.cs` - Multi-granularity decomposition, UTF-8 handling, auto-detection
- `ContentDecomposerFactoryTests.cs` - Strategy selection, fallback behavior

**Hartonomous.Core.Tests** (ADD TESTS)
- `IngestContentCommandHandlerTests.cs` - Batch deduplication, statistics calculation
- `SpatialCoordinateTests.cs` - Interpolate method correctness

#### Integration Tests
**Hartonomous.Data.Tests** (ADD TESTS)
- `BPERepositoryTests.cs` - Token persistence, hash lookups
- `ConstantRepositoryTests.cs` - Batch operations, deduplication
- `SpatialQueryTests.cs` - PostGIS queries, k-NN, region searches

**Hartonomous.API.Tests** (ADD TESTS)
- `ContentControllerTests.cs` - Ingestion workflow end-to-end
- `QueryControllerTests.cs` - Query execution, caching
- `AuthenticationTests.cs` - Entra ID token validation, policy enforcement

#### Performance Tests
**Hartonomous.Benchmarks** (EXPAND)
- BPE learning performance (1K, 10K, 100K constants)
- Ingestion throughput (MB/s)
- Query latency (p50, p95, p99)
- Deduplication efficiency

### Test Infrastructure Requirements
- xUnit test framework (already configured)
- Moq for mocking dependencies
- FluentAssertions for readable assertions
- Testcontainers for PostgreSQL/PostGIS integration tests
- Bogus for test data generation
- In-memory cache provider for unit tests (no Redis dependency)

---

## Next Steps (Immediate)

1. **Commit Current Work**
   - Commit message: "feat: Implement BPE learning algorithm and content ingestion pipeline"
   - Include all Task 1 and Task 2 changes

2. **Create Infrastructure.Tests Project**
   - Add project reference to Infrastructure
   - Configure xUnit, Moq, FluentAssertions

3. **Write Comprehensive Tests**
   - BPEService full coverage (happy path, edge cases, errors)
   - Decomposer strategy pattern coverage
   - Ingestion handler batch processing validation

4. **Run Tests and Fix Issues**
   - Ensure 100% test pass rate
   - Achieve >80% code coverage for new implementations

5. **Proceed to Task 3 (Query Engine)**
   - Begin with TDD approach for query service
   - Write tests first, implement to make them pass

---

## Success Criteria

### Task 1-2 (Completed)
- ✅ BPE learning algorithm fully functional
- ✅ Multi-granularity content decomposition
- ✅ Batch deduplication with performance optimization
- ✅ All code compiles without errors
- ⏳ Comprehensive test coverage (NEXT)

### Task 3 (Query Engine)
- Sub-second query performance (cached)
- < 500ms spatial query latency (uncached)
- Successful content reconstruction from tokens
- Redis cache hit ratio > 70%

### Task 4 (Background Workers)
- All 5 workers operational
- Configurable execution schedules
- Health checks reporting worker status
- Metrics exported for monitoring

### Task 5 (REST API)
- Complete CRUD operations for content
- Query API with pagination
- Admin-only vocabulary management
- OpenAPI documentation complete
- Rate limiting enforced
- Authentication required (Zero Trust)

### Overall System
- > 90% test coverage
- All integration tests passing
- Performance benchmarks meet targets
- Production deployment ready

---

## Technical Debt & Future Enhancements

### Known Limitations
1. **Deduplication:** Still queries database for each hash batch (could optimize with Bloom filter)
2. **BPE Learning:** Single-threaded (could parallelize pair frequency counting)
3. **Spatial Interpolation:** Simple Cartesian average (could use weighted centroid based on frequency)
4. **Caching:** No distributed cache invalidation (could use pub/sub for multi-instance deployments)

### Future Enhancements
1. **GPU-Accelerated BPE:** Use PL/Python GPU functions for large-scale vocabulary learning
2. **Adaptive Decomposition:** Machine learning-based content type detection
3. **Compression Metrics:** Track compression ratios over time, optimize vocabulary for better compression
4. **Distributed Query:** Shard constants across multiple databases for horizontal scaling
5. **Real-time Indexing:** Stream-based constant indexing instead of batch workers
6. **Advanced Similarity:** Deep learning embeddings for semantic similarity search

---

**Document Version:** 1.0  
**Last Updated:** December 4, 2025  
**Status:** Tasks 1-2 Complete, Tasks 3-5 Pending, Testing Phase Next
