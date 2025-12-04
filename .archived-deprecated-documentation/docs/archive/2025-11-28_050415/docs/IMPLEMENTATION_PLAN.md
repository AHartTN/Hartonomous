# Hartonomous Ingestion Implementation Plan
**From Architecture to Working System**

---

## 🎯 Executive Summary

**Goal:** Build complete extreme-granular ingestion system for ALL data types  
**Timeline:** 3-4 weeks full implementation  
**Status:** Core infrastructure DONE, parsers 40% complete, need fleshing out

---

## ✅ What's Already Working

### Core Infrastructure (100% Complete)
- ✅ PostgreSQL 16 + PostGIS 3.6.1 + PG-Strom 6.0
- ✅ 950+ database functions installed
- ✅ GPU access confirmed (GTX 1080 Ti)
- ✅ Spatial R-Tree indexes operational
- ✅ Content-addressable atomization working
- ✅ Hierarchical composition tested
- ✅ Reference counting automatic

### Basic Atomization (80% Complete)
- ✅ `atomize_text()` - Character-level working
- ✅ `atomize_numeric()` - Float/int working
- ✅ `atomize_image()` - Pixel-level defined
- ✅ `atomize_audio()` - Sample-level defined
- ⚠️ Need: GPU batch optimization
- ⚠️ Need: Spatial positioning integration

### API Endpoints (60% Complete)
- ✅ `/v1/ingest/text` - Working
- ✅ `/v1/ingest/image` - Skeleton done
- ✅ `/v1/ingest/audio` - Skeleton done
- ⚠️ Need: Document parsers integration
- ⚠️ Need: Model atomization completion
- ⚠️ Need: Code atomizer C# bridge

---

## 🚧 Implementation Phases

### **Phase 1: Core Parsers (Week 1-2)**

#### **Priority 1.1: Document Parsers**
**Files to Complete:**
1. `api/services/document_parser.py` ✅ (Created - needs testing)
2. `api/routes/documents.py` (NEW - needs creation)
3. `api/models/documents.py` (NEW - request/response models)

**Tasks:**
```python
# api/routes/documents.py
@router.post("/document")
async def ingest_document(
    file: UploadFile = File(...),
    format: str = Form(...),  # pdf, docx, md, html, txt
    extract_images: bool = Form(True),
    ocr_enabled: bool = Form(False),
    metadata: Optional[str] = Form(None)
):
    # Detect format
    if format == "pdf":
        result = await DocumentParserService.parse_and_atomize_pdf(...)
    elif format == "docx":
        result = await DocumentParserService.parse_and_atomize_docx(...)
    elif format == "md":
        result = await DocumentParserService.parse_and_atomize_markdown(...)
    
    return IngestResponse(atom_count=result['atom_count'], ...)
```

**Dependencies to Install:**
```bash
pip install pdfplumber python-docx markdown-it-py beautifulsoup4 pytesseract
apt-get install tesseract-ocr  # For OCR
```

**Testing:**
```bash
# Test PDF ingestion
curl -X POST http://localhost:8000/v1/ingest/document \
  -F "file=@test.pdf" \
  -F "format=pdf" \
  -F "extract_images=true"

# Expected: 
# {
#   "atom_count": 1847,
#   "root_atom_id": 12345,
#   "page_ids": [12346, 12347, ...],
#   "processing_time_ms": 543.2
# }
```

---

#### **Priority 1.2: Image/Audio Enhancement**
**Current Status:** Basic implementations exist, need GPU integration

**Tasks:**
1. Add GPU batch processing to `atomize_image()`
2. Add sparse encoding to `atomize_audio()`
3. Add Hilbert curve compression for images
4. Test with large files (10MB+ images, 1-hour audio)

**GPU Optimization:**
```python
# api/services/atomization.py - enhance existing
@staticmethod
async def atomize_image_gpu(
    conn: AsyncConnection,
    image_data: bytes,
    width: int,
    height: int,
    use_gpu: bool = True
):
    # Convert to pixel array
    pixels = np.array(Image.open(io.BytesIO(image_data)))
    
    # GPU batch processing
    async with conn.cursor() as cur:
        # Call GPU batch atomization
        await cur.execute(
            """
            SELECT gpu_batch_atomize_pixels(
                %s::pixel_array,
                %s::boolean
            )
            """,
            (pixels.flatten().tolist(), use_gpu)
        )
        atom_ids = await cur.fetchall()
    
    return atom_ids
```

---

### **Phase 2: AI Model Ingestion (Week 2-3)**

#### **Priority 2.1: GGUF Atomization Completion**
**Current Status:** `model_atomization.py` 50% complete

**Tasks:**
1. Complete GGUF parser (finish `GGUFAtomizer` class)
2. Add streaming for large models (7B+ parameters)
3. GPU-accelerated weight hashing
4. Sparse encoding (threshold-based skipping)
5. Bulk insert optimization

**Streaming Implementation:**
```python
# api/services/model_atomization.py
async def atomize_model_streaming(
    self,
    file_path: Path,
    conn: AsyncConnection,
    batch_size: int = 10000
):
    """
    Stream model weights in batches to avoid memory issues.
    """
    async with conn.cursor() as cur:
        # Create model root atom
        model_atom_id = await self._create_model_atom(...)
        
        # Stream tensors
        async for tensor_batch in self._stream_tensors(file_path):
            # GPU batch atomization (10,000 weights at once)
            weight_atoms = await self._gpu_batch_atomize_weights(
                cur, tensor_batch.weights, batch_size
            )
            
            # Bulk composition insert
            await self._bulk_insert_compositions(
                cur, tensor_batch.tensor_atom_id, weight_atoms
            )
            
            # Progress logging
            self.stats['weights_atomized'] += len(weight_atoms)
            if self.stats['weights_atomized'] % 1000000 == 0:
                logger.info(f"Atomized {self.stats['weights_atomized']/1e6:.1f}M weights")
```

---

#### **Priority 2.2: SafeTensors/PyTorch/ONNX**
**Current Status:** 0% (stub endpoints exist)

**Tasks:**
1. Implement SafeTensors parser
2. Implement PyTorch checkpoint parser
3. Implement ONNX graph parser
4. Unified interface for all formats

**SafeTensors Implementation:**
```python
# api/services/model_parsers/safetensors_parser.py
class SafeTensorsParser:
    @staticmethod
    async def parse_and_atomize(
        file_path: Path,
        conn: AsyncConnection,
        threshold: float = 0.01
    ):
        from safetensors import safe_open
        
        with safe_open(file_path, framework="pt") as f:
            for tensor_name in f.keys():
                tensor = f.get_tensor(tensor_name)
                
                # Atomize tensor weights
                await atomize_tensor(
                    conn, 
                    tensor_name, 
                    tensor.numpy(),
                    threshold
                )
```

---

### **Phase 3: Code Atomization Bridge (Week 3)**

#### **Priority 3.1: C# Microservice Integration**
**Current Status:** C# service exists, Python bridge missing

**Architecture:**
```
User → Python FastAPI → C# Microservice → Roslyn/Tree-sitter → Python API → PostgreSQL
         POST /v1/atomize/code
                 ↓
         HTTP POST to code-atomizer:8080
                 ↓
         C# analyzes code (AST + metrics)
                 ↓
         Returns JSON (nodes, tokens, metrics)
                 ↓
         Python atomizes AST → PostgreSQL
```

**Tasks:**
1. Create Python → C# HTTP client
2. Create C# → Python response mapping
3. Atomize AST nodes into PostgreSQL
4. Add spatial positioning for code elements
5. Create relations (calls, inherits, references)

**Implementation:**
```python
# api/services/code_integration.py (NEW FILE)
import httpx
from typing import Dict, Any

class CodeAtomizerBridge:
    """Bridge between Python API and C# code atomizer microservice."""
    
    def __init__(self, code_atomizer_url: str = "http://code-atomizer:8080"):
        self.url = code_atomizer_url
        self.client = httpx.AsyncClient()
    
    async def atomize_code(
        self,
        code: str,
        language: str,
        conn: AsyncConnection,
        integration_mode: str = "standalone"
    ) -> Dict[str, Any]:
        """
        Send code to C# microservice for analysis, optionally atomize results.
        
        Args:
            code: Source code
            language: Programming language (csharp, python, javascript, ...)
            conn: Database connection (for hartonomous mode)
            integration_mode: "standalone" or "hartonomous"
        
        Returns:
            Standalone: AST + metrics JSON
            Hartonomous: atoms_created, root_atom_id, relations
        """
        # Call C# microservice
        response = await self.client.post(
            f"{self.url}/api/atomize",
            json={
                "code": code,
                "language": language,
                "analysis_level": "semantic"
            }
        )
        response.raise_for_status()
        
        ast_result = response.json()
        
        if integration_mode == "standalone":
            # Return AST directly (billable without Hartonomous)
            return ast_result
        
        elif integration_mode == "hartonomous":
            # Atomize AST into PostgreSQL (premium integration)
            return await self._atomize_ast_to_db(conn, ast_result, language)
    
    async def _atomize_ast_to_db(
        self,
        conn: AsyncConnection,
        ast_result: Dict[str, Any],
        language: str
    ) -> Dict[str, Any]:
        """
        Convert AST JSON from C# into atoms + compositions + relations.
        """
        async with conn.cursor() as cur:
            # Create file root atom
            file_metadata = {
                "modality": "code",
                "language": language,
                "node_count": len(ast_result['ast_nodes']),
                "metrics": ast_result['metrics']
            }
            
            # ... atomize AST nodes recursively
            # ... create compositions (parent → children)
            # ... create relations (calls, inherits, etc.)
            
            return {
                "atoms_created": 1847,
                "root_atom_id": 54321,
                "compositions": 934,
                "relations": 112
            }
```

**Endpoint:**
```python
# api/routes/code.py (ENHANCE EXISTING)
@router.post("/code")
async def atomize_code(
    request: CodeAtomizationRequest,
    conn: AsyncConnection = Depends(get_db_connection)
):
    """
    Atomize code via C# microservice.
    
    Integration modes:
    - standalone: Return AST + metrics JSON (no PostgreSQL)
    - hartonomous: Atomize into knowledge substrate (premium)
    """
    bridge = CodeAtomizerBridge(
        code_atomizer_url=os.getenv("CODE_ATOMIZER_URL", "http://code-atomizer:8080")
    )
    
    result = await bridge.atomize_code(
        code=request.code,
        language=request.language,
        conn=conn,
        integration_mode=request.integration_mode
    )
    
    if request.integration_mode == "standalone":
        return {
            "mode": "standalone",
            "ast_nodes": result['ast_nodes'],
            "metrics": result['metrics'],
            "message": "AST analysis complete (standalone mode)"
        }
    else:
        return {
            "mode": "hartonomous",
            "atoms_created": result['atoms_created'],
            "root_atom_id": result['root_atom_id'],
            "message": "Code atomized into knowledge substrate"
        }
```

---

### **Phase 4: Background Workers (Week 4)**

#### **Priority 4.1: Spatial Positioning Worker**
**Task:** Auto-position new atoms in semantic space

```python
# api/workers/spatial_positioning.py (NEW FILE)
import asyncio
import asyncpg
import logging

logger = logging.getLogger(__name__)

async def spatial_positioning_worker(db_pool: asyncpg.Pool):
    """
    Background worker to position unpositioned atoms.
    
    Runs continuously, checking for atoms without spatial_key.
    Uses GPU-accelerated batch positioning.
    """
    logger.info("Starting spatial positioning worker...")
    
    while True:
        try:
            async with db_pool.acquire() as conn:
                # Find unpositioned atoms (batch of 1000)
                unpositioned = await conn.fetch("""
                    SELECT atom_id, canonical_text, metadata
                    FROM atom
                    WHERE spatial_key IS NULL
                      AND canonical_text IS NOT NULL
                    LIMIT 1000
                """)
                
                if not unpositioned:
                    await asyncio.sleep(5)
                    continue
                
                # GPU batch positioning
                atom_ids = [row['atom_id'] for row in unpositioned]
                texts = [row['canonical_text'] for row in unpositioned]
                
                await conn.execute("""
                    SELECT gpu_batch_position($1::bigint[], true)
                """, atom_ids)
                
                logger.info(f"✅ Positioned {len(atom_ids)} atoms")
        
        except Exception as e:
            logger.error(f"Positioning worker error: {e}", exc_info=True)
            await asyncio.sleep(10)
```

#### **Priority 4.2: Relation Discovery Worker**
**Task:** Auto-create semantic_similar relations based on spatial proximity

```python
# api/workers/relation_discovery.py (NEW FILE)
async def relation_discovery_worker(db_pool: asyncpg.Pool):
    """
    Background worker to discover semantic relations.
    
    Creates "semantic_similar" relations for spatially close atoms.
    """
    logger.info("Starting relation discovery worker...")
    
    while True:
        try:
            async with db_pool.acquire() as conn:
                # Find atoms without relations
                lonely_atoms = await conn.fetch("""
                    SELECT a.atom_id, a.spatial_key
                    FROM atom a
                    LEFT JOIN atom_relation ar ON ar.source_atom_id = a.atom_id
                    WHERE a.spatial_key IS NOT NULL
                      AND ar.relation_id IS NULL
                    LIMIT 100
                """)
                
                if not lonely_atoms:
                    await asyncio.sleep(30)
                    continue
                
                # For each atom, find spatial neighbors
                for atom in lonely_atoms:
                    await conn.execute("""
                        INSERT INTO atom_relation 
                            (source_atom_id, target_atom_id, relation_type_id, weight)
                        SELECT 
                            $1,
                            a2.atom_id,
                            (SELECT atom_id FROM atom WHERE canonical_text = 'semantic_similar'),
                            1.0 / (1.0 + ST_3DDistance($2, a2.spatial_key))
                        FROM atom a2
                        WHERE a2.spatial_key IS NOT NULL
                          AND a2.atom_id != $1
                          AND ST_3DDWithin($2, a2.spatial_key, 2.0)
                        LIMIT 10
                        ON CONFLICT DO NOTHING
                    """, atom['atom_id'], atom['spatial_key'])
                
                logger.info(f"✅ Created relations for {len(lonely_atoms)} atoms")
        
        except Exception as e:
            logger.error(f"Relation worker error: {e}", exc_info=True)
            await asyncio.sleep(30)
```

#### **Priority 4.3: Neo4j Provenance Sync**
**Task:** Stream atoms to Neo4j for lineage tracking

```python
# api/workers/neo4j_sync.py (ENHANCE EXISTING)
async def neo4j_provenance_worker(db_pool: asyncpg.Pool, neo4j_driver):
    """
    Sync atom creations to Neo4j for provenance tracking.
    """
    logger.info("Starting Neo4j provenance sync worker...")
    
    # Listen to PostgreSQL logical replication
    async with db_pool.acquire() as conn:
        await conn.add_listener('atom_created', handle_atom_created)
    
    async def handle_atom_created(connection, pid, channel, payload):
        """Handle atom creation notification."""
        import json
        atom_data = json.loads(payload)
        
        # Create node in Neo4j
        async with neo4j_driver.session() as session:
            await session.run("""
                CREATE (a:Atom {
                    atom_id: $atom_id,
                    content_hash: $content_hash,
                    canonical_text: $canonical_text,
                    created_at: datetime($created_at)
                })
            """, **atom_data)
```

---

## 📋 Implementation Checklist

### Week 1: Core Parsers
- [ ] Test PDF parser with real documents
- [ ] Test DOCX parser with real documents
- [ ] Implement Markdown parser
- [ ] Implement HTML parser
- [ ] Add OCR support (Tesseract)
- [ ] GPU optimize image atomization
- [ ] GPU optimize audio atomization
- [ ] Create `/v1/ingest/document` endpoint
- [ ] Create `/v1/ingest/video` endpoint skeleton

### Week 2: AI Models
- [ ] Complete GGUF streaming parser
- [ ] Add GPU batch weight atomization
- [ ] Add sparse encoding (threshold skipping)
- [ ] Test with Qwen2.5-coder:7b model
- [ ] Implement SafeTensors parser
- [ ] Implement PyTorch parser
- [ ] Implement ONNX parser
- [ ] Benchmark deduplication ratios

### Week 3: Code Integration
- [ ] Create C# → Python HTTP bridge
- [ ] Test standalone mode (AST only)
- [ ] Implement hartonomous mode (atomization)
- [ ] Create code composition hierarchy
- [ ] Create code relations (calls, inherits)
- [ ] Add spatial positioning for code
- [ ] Test with C#, Python, JavaScript files
- [ ] Document pricing model (standalone vs premium)

### Week 4: Workers & Polish
- [ ] Implement spatial positioning worker
- [ ] Implement relation discovery worker
- [ ] Implement Neo4j sync worker
- [ ] Add worker health monitoring
- [ ] Add Prometheus metrics
- [ ] Create Grafana dashboards
- [ ] Load test (1M atoms ingestion)
- [ ] Document API with OpenAPI examples
- [ ] Create client SDKs (Python, C#, JS)

---

## 🎯 Success Metrics

### Completeness
- [ ] All modalities supported (text, docs, images, audio, video, models, code, DB)
- [ ] All parsers working (PDF, DOCX, MD, HTML, GGUF, SafeTensors, PyTorch, ONNX)
- [ ] All workers running (positioning, relations, provenance)

### Performance
- [ ] Text: 50K chars/sec (GPU)
- [ ] Images: 20 images/sec (1080p)
- [ ] Models: 100K weights/sec (7B model in 20 seconds)
- [ ] Code: 10K LOC/sec
- [ ] Spatial queries: <50ms at 1M atoms

### Quality
- [ ] Deduplication: 3-100× space savings
- [ ] Atomization: 100% reversible (perfect reconstruction)
- [ ] Relations: Automatic discovery, 90%+ accuracy
- [ ] Provenance: Complete lineage tracking

---

## 💰 Monetization Strategy

### Tier 1: Core Ingestion (Free/Open Source)
- Text, images, audio atomization
- Document parsing (PDF, DOCX, MD)
- Basic spatial queries
- Limited to 100K atoms

### Tier 2: Professional ($99/month)
- Unlimited atoms
- AI model atomization (GGUF, SafeTensors)
- Video atomization
- Database atomization
- GPU acceleration
- Background workers

### Tier 3: Enterprise ($999/month)
- Code atomization (standalone mode)
- Multi-tenant deployment
- Custom parsers
- SLA guarantees
- Priority support

### Tier 4: Code Integration Premium (+ $499/month)
- Hartonomous integration for code
- Semantic code search
- Cross-modal queries (code + docs + models)
- Custom Roslyn analyzers
- Private deployment

---

## 📞 Next Steps

### Immediate (Today)
1. ✅ Architecture documented
2. ✅ Implementation plan created
3. ⏳ Start Week 1 tasks (document parsers)

### This Week
1. Complete PDF/DOCX parsers
2. Test with real documents
3. Add GPU optimization
4. Begin model atomization

### Next Week
1. Finish model parsers
2. Begin C# bridge
3. Test end-to-end

### Week 4
1. Deploy workers
2. Load testing
3. Documentation
4. Launch beta!

---

**Status:** Architecture Complete, Implementation Roadmap Clear  
**Timeline:** 3-4 weeks to production-ready  
**Confidence:** HIGH (core infrastructure proven working)

Let's build this! 🚀
