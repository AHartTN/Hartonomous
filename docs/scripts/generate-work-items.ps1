param(
    [Parameter(Mandatory=$true)]
    [string]$Organization,
    
    [Parameter(Mandatory=$true)]
    [string]$Project,
    
    [Parameter(Mandatory=$true)]
    [string]$PAT
)

Write-Host "Azure DevOps Work Item Generator" -ForegroundColor Cyan
Write-Host "Generating work items from documentation structure..." -ForegroundColor Yellow
Write-Host ""

# Set PAT
$env:AZURE_DEVOPS_EXT_PAT = $PAT

# Epic definitions based on ARCHITECTURE.md sections
$epics = @(
    @{
        Title = "Core Infrastructure"
        Description = "Landmark projection, Hilbert encoding, database schema"
        Features = @(
            @{
                Title = "Landmark Projection System"
                Description = "Hash-based constant projection to 3D coordinates"
                Tasks = @(
                    "Implement XXHash64 constant hashing",
                    "Implement coordinate extraction (X,Y,Z split)",
                    "SIMD/AVX vectorization for batch hashing",
                    "GPU CUDA kernel for parallel hashing (optional)",
                    "Unit tests for deterministic projection"
                )
            },
            @{
                Title = "Hilbert Curve Encoding"
                Description = "3D coordinate to integer mapping"
                Tasks = @(
                    "Implement Hilbert encoding algorithm",
                    "Implement Hilbert decoding (inverse)",
                    "SIMD/AVX optimization for batch encoding",
                    "GPU acceleration for large batches",
                    "Benchmark encoding performance"
                )
            },
            @{
                Title = "Database Schema with PostGIS"
                Description = "PostgreSQL tables with spatial geometry"
                Tasks = @(
                    "Create atoms table with POINTZ geometry",
                    "Create atom_edges table with LINESTRINGZ",
                    "Implement spatial indexes (GIST)",
                    "Create triggers for ref_count management",
                    "Create functions (reconstruct_atom, find_orphaned)"
                )
            }
        )
    },
    @{
        Title = "Ingestion Pipeline"
        Description = "Content decomposition, BPE processing, batch operations"
        Features = @(
            @{
                Title = "Content Decomposition"
                Description = "Break content into atomic constants"
                Tasks = @(
                    "Text decomposer (bytes, chars, words)",
                    "Image decomposer (pixels, patches)",
                    "Audio decomposer (samples, frames)",
                    "Video decomposer (frames to image decomposition)",
                    "Modality detection from content type"
                )
            },
            @{
                Title = "BPE Processing"
                Description = "Byte Pair Encoding composition graph"
                Tasks = @(
                    "Set-based SQL pair counting",
                    "SIMD pair detection in C#",
                    "Composite atom creation with interpolation",
                    "Edge creation with geometric lines",
                    "Iterative BPE until convergence"
                )
            },
            @{
                Title = "Batch Operations"
                Description = "Eliminate row-by-row processing"
                Tasks = @(
                    "Bulk insert with ON CONFLICT deduplication",
                    "Parallel hash computation (SIMD)",
                    "Staging tables for batch processing",
                    "Transaction batching (1000s per commit)",
                    "Performance benchmarks vs RBAR"
                )
            }
        )
    },
    @{
        Title = "Query Engine"
        Description = "Geometric queries, graph traversal, reconstruction"
        Features = @(
            @{
                Title = "Geometric Queries"
                Description = "PostGIS spatial operations"
                Tasks = @(
                    "k-NN proximity search (semantic similarity)",
                    "Bounded region search (3D bounding box)",
                    "Convex hull similarity (document shapes)",
                    "Density clustering (DBSCAN hot topics)",
                    "Cross-modal proximity queries"
                )
            },
            @{
                Title = "Graph Traversal"
                Description = "Navigate atom composition graph"
                Tasks = @(
                    "Recursive parent lookup (find all uses)",
                    "Content reconstruction with caching",
                    "Shortest path between atoms",
                    "Materialized views for common traversals",
                    "Parallel child fetching"
                )
            },
            @{
                Title = "Caching Layer"
                Description = "Hot atom optimization"
                Tasks = @(
                    "LRU cache implementation",
                    "Materialized view: hot_atoms",
                    "Query result caching (Redis)",
                    "Cache invalidation strategy",
                    "Cache hit rate monitoring"
                )
            }
        )
    },
    @{
        Title = "API Layer"
        Description = "REST API, real-time communication, authentication"
        Features = @(
            @{
                Title = "REST API Endpoints"
                Description = "ASP.NET Core Web API"
                Tasks = @(
                    "POST /api/atoms/ingest (content ingestion)",
                    "GET /api/atoms/{id} (atom retrieval)",
                    "GET /api/atoms/search/similar (range queries)",
                    "POST /api/atoms/reconstruct (content rebuild)",
                    "GET /api/stats/* (system statistics)"
                )
            },
            @{
                Title = "Real-time Communication"
                Description = "SignalR and WebSocket support"
                Tasks = @(
                    "SignalR hub for live updates",
                    "WebSocket endpoint for streaming",
                    "Server-Sent Events for statistics",
                    "Subscribe to ingestion events",
                    "Subscribe to statistics updates"
                )
            }
        )
    },
    @{
        Title = "UI and Visualization"
        Description = "Blazor web UI, 3D visualization, dashboards"
        Features = @(
            @{
                Title = "Blazor Web Pages"
                Description = "Interactive web interface"
                Tasks = @(
                    "Dashboard page (statistics, metrics)",
                    "Atom explorer (search, details)",
                    "Query interface (build queries visually)",
                    "Statistics page (charts, trends)",
                    "Real-time updates via SignalR"
                )
            },
            @{
                Title = "3D Visualization"
                Description = "Hilbert space rendering"
                Tasks = @(
                    "Three.js or Babylon.js integration",
                    "Render atoms as points in 3D space",
                    "Render edges as lines",
                    "Interactive camera controls",
                    "Atom selection and details on click"
                )
            }
        )
    },
    @{
        Title = "Performance Optimization"
        Description = "SIMD/AVX, GPU acceleration, database tuning"
        Features = @(
            @{
                Title = "SIMD/AVX Vectorization"
                Description = "CPU parallel processing"
                Tasks = @(
                    "AVX2 hash computation (4-8x speedup)",
                    "AVX2 coordinate extraction",
                    "AVX2 Hilbert encoding batch",
                    "Benchmark SIMD vs sequential",
                    "Document SIMD requirements (CPU features)"
                )
            },
            @{
                Title = "GPU Acceleration (Optional)"
                Description = "CUDA for massive batches"
                Tasks = @(
                    "CUDA kernel for Hilbert encoding",
                    "CUDA kernel for BPE pair counting",
                    "GPU memory management",
                    "Benchmark GPU vs CPU",
                    "Fallback to CPU if GPU unavailable"
                )
            },
            @{
                Title = "Database Optimization"
                Description = "PostgreSQL performance tuning"
                Tasks = @(
                    "Set-based operations (eliminate cursors)",
                    "Parallel query execution configuration",
                    "Partitioning by Hilbert range",
                    "Spatial index tuning (GIST parameters)",
                    "Connection pooling and async I/O"
                )
            }
        )
    }
)

# Create work items
foreach ($epic in $epics) {
    Write-Host "Creating Epic: $($epic.Title)" -ForegroundColor Cyan
    
    $epicId = az boards work-item create `
        --type Epic `
        --title $epic.Title `
        --description $epic.Description `
        --org $Organization `
        --project $Project `
        --query "id" `
        --output tsv
    
    Write-Host "  Created Epic ID: $epicId" -ForegroundColor Green
    
    foreach ($feature in $epic.Features) {
        Write-Host "  Creating Feature: $($feature.Title)" -ForegroundColor Yellow
        
        $featureId = az boards work-item create `
            --type Feature `
            --title $feature.Title `
            --description $feature.Description `
            --org $Organization `
            --project $Project `
            --query "id" `
            --output tsv
        
        # Link feature to epic
        az boards work-item relation add `
            --id $featureId `
            --relation-type "Parent" `
            --target-id $epicId `
            --org $Organization `
            --project $Project `
            --output none
        
        Write-Host "    Created Feature ID: $featureId (linked to Epic $epicId)" -ForegroundColor Green
        
        foreach ($task in $feature.Tasks) {
            Write-Host "    Creating Task: $task" -ForegroundColor Gray
            
            $taskId = az boards work-item create `
                --type Task `
                --title $task `
                --org $Organization `
                --project $Project `
                --query "id" `
                --output tsv
            
            # Link task to feature
            az boards work-item relation add `
                --id $taskId `
                --relation-type "Parent" `
                --target-id $featureId `
                --org $Organization `
                --project $Project `
                --output none
            
            Write-Host "      Created Task ID: $taskId (linked to Feature $featureId)" -ForegroundColor Green
        }
    }
    
    Write-Host ""
}

Write-Host ""
Write-Host "Work item structure created successfully!" -ForegroundColor Green
Write-Host "View in Azure DevOps Boards: $Organization/$Project/_boards" -ForegroundColor Cyan
