# Phase 2: BPE Algorithm Redesign

**Duration**: 4-5 days  
**Dependencies**: Phase 1 (POINTZM geometry must be working)  
**Critical Path**: Yes - foundational for compositional structure

---

## Overview

Completely redesign the Byte Pair Encoding (BPE) algorithm to exploit geometric properties: Hilbert-sorted sequences, gap-based compression detection, Voronoi tessellation for natural neighborhoods, and MST-based vocabulary learning. This transforms BPE from traditional byte-level merging to universal geometric composition.

---

## Current vs. Target Architecture

### Current (WRONG)
```csharp
// Each constant becomes isolated single-element sequence
foreach (var constant in constantsList) {
    var sequence = new List<Guid> { constant.Id };  // ❌ ISOLATED
    sequences.Add(sequence);
}
```

### Target (CORRECT)
```csharp
// Constants sorted by Hilbert index, gaps detected, Voronoi cells merged
var hilbertSorted = await _repository
    .GetAllAsync()
    .OrderBy(c => c.Coordinate.HilbertIndex)
    .ToListAsync();

var gaps = DetectHilbertGaps(hilbertSorted, threshold: 1000);
var voronoiCells = await ComputeVoronoiTessellation(hilbertSorted);
var delaunay = ComputeDelaunayTriangulation(voronoiCells);
var mst = ComputeMinimumSpanningTree(delaunay);

// BPE merges = MST edges
var vocabulary = LearnVocabularyFromMST(mst, minFrequency, maxSize);
```

---

## Objectives

1. Redesign `BPEService.LearnVocabularyAsync` to use Hilbert-sorted sequences
2. Implement gap-based compression detection
3. Integrate PostGIS Voronoi tessellation
4. Implement Delaunay triangulation via PostGIS
5. Compute MST for minimal vocabulary learning
6. Store compositions as LINESTRINGZM geometries
7. Add `CompositionGeometry` property to `BPEToken` entity

---

## Task Breakdown

### Task 2.1: Redesign LearnVocabularyAsync (12 hours)

**Files to Modify**:
- `Hartonomous.Data/Services/BPEService.cs`

**New Algorithm**:

```csharp
public async Task<IEnumerable<BPEToken>> LearnVocabularyAsync(
    ContentType contentType,
    int maxVocabularySize = 10000,
    int minFrequency = 2,
    CancellationToken cancellationToken = default)
{
    _logger.LogInformation(
        "Learning BPE vocabulary (maxSize={MaxSize}, minFreq={MinFreq})",
        maxVocabularySize, minFrequency);
    
    // Step 1: Get all constants sorted by Hilbert index
    var constants = await _constantRepository
        .Query()
        .Where(c => c.ContentType == contentType)
        .OrderBy(c => c.Coordinate.HilbertIndex)
        .ToListAsync(cancellationToken);
    
    if (constants.Count < 2)
    {
        _logger.LogWarning("Insufficient constants for BPE: {Count}", constants.Count);
        return Enumerable.Empty<BPEToken>();
    }
    
    _logger.LogDebug("Processing {Count} constants", constants.Count);
    
    // Step 2: Detect gaps in Hilbert sequence (compression opportunities)
    var gaps = DetectHilbertGaps(constants, gapThreshold: 1000);
    _logger.LogDebug("Detected {GapCount} gaps in Hilbert sequence", gaps.Count);
    
    // Step 3: Build segments between gaps (dense regions)
    var segments = BuildSegmentsFromGaps(constants, gaps);
    _logger.LogDebug("Created {SegmentCount} dense segments", segments.Count);
    
    // Step 4: For each segment, compute Voronoi tessellation
    var allPairs = new List<ConstantPair>();
    foreach (var segment in segments.Where(s => s.Count > 1))
    {
        var pairs = await ComputeVoronoiNeighborsAsync(segment, cancellationToken);
        allPairs.AddRange(pairs);
    }
    _logger.LogDebug("Found {PairCount} Voronoi neighbor pairs", allPairs.Count);
    
    // Step 5: Build Delaunay graph (dual of Voronoi)
    var delaunayGraph = BuildDelaunayGraph(allPairs);
    
    // Step 6: Compute MST on Delaunay edges (weight = Hilbert distance)
    var mst = ComputeMinimumSpanningTree(delaunayGraph);
    _logger.LogDebug("MST has {EdgeCount} edges", mst.Edges.Count);
    
    // Step 7: Select vocabulary from MST edges
    var vocabulary = await SelectVocabularyFromMST(
        mst,
        maxVocabularySize,
        minFrequency,
        cancellationToken);
    
    _logger.LogInformation("Learned {VocabSize} BPE tokens", vocabulary.Count);
    return vocabulary;
}
```

**Acceptance Criteria**:
- ✅ Constants sorted by Hilbert index
- ✅ Gaps detected and logged
- ✅ Voronoi neighbors computed per segment
- ✅ MST computed on Delaunay graph
- ✅ Vocabulary size respects maxVocabularySize
- ✅ Only pairs with frequency >= minFrequency included

---

### Task 2.2: Implement Gap Detection (4 hours)

```csharp
private List<HilbertGap> DetectHilbertGaps(
    List<Constant> sortedConstants,
    ulong gapThreshold)
{
    var gaps = new List<HilbertGap>();
    
    for (int i = 0; i < sortedConstants.Count - 1; i++)
    {
        var current = sortedConstants[i];
        var next = sortedConstants[i + 1];
        
        var gap = next.Coordinate.HilbertIndex - current.Coordinate.HilbertIndex;
        
        if (gap > gapThreshold)
        {
            gaps.Add(new HilbertGap
            {
                StartIndex = i,
                EndIndex = i + 1,
                StartHilbert = current.Coordinate.HilbertIndex,
                EndHilbert = next.Coordinate.HilbertIndex,
                GapSize = gap,
                IsSparse = gap > gapThreshold * 10 // Very sparse
            });
            
            _logger.LogTrace(
                "Gap detected: [{Start}→{End}] size={Size}", 
                current.Coordinate.HilbertIndex,
                next.Coordinate.HilbertIndex,
                gap);
        }
    }
    
    return gaps;
}

private List<List<Constant>> BuildSegmentsFromGaps(
    List<Constant> sortedConstants,
    List<HilbertGap> gaps)
{
    var segments = new List<List<Constant>>();
    var currentSegment = new List<Constant>();
    var nextGapIndex = 0;
    
    for (int i = 0; i < sortedConstants.Count; i++)
    {
        currentSegment.Add(sortedConstants[i]);
        
        // Check if we hit a gap
        if (nextGapIndex < gaps.Count && i == gaps[nextGapIndex].StartIndex)
        {
            if (currentSegment.Count > 1)
            {
                segments.Add(currentSegment);
            }
            currentSegment = new List<Constant>();
            nextGapIndex++;
        }
    }
    
    // Add final segment
    if (currentSegment.Count > 1)
    {
        segments.Add(currentSegment);
    }
    
    return segments;
}

private record HilbertGap
{
    public int StartIndex { get; init; }
    public int EndIndex { get; init; }
    public ulong StartHilbert { get; init; }
    public ulong EndHilbert { get; init; }
    public ulong GapSize { get; init; }
    public bool IsSparse { get; init; }
}
```

**Acceptance Criteria**:
- ✅ Gaps correctly identified using threshold
- ✅ Segments contain only constants within dense regions
- ✅ Sparse gaps (10x threshold) flagged separately
- ✅ Edge cases handled (empty input, single constant, no gaps)

---

### Task 2.3: Integrate PostGIS Voronoi Tessellation (10 hours)

```csharp
private async Task<List<ConstantPair>> ComputeVoronoiNeighborsAsync(
    List<Constant> segment,
    CancellationToken cancellationToken)
{
    // Build MULTIPOINT from segment coordinates
    var points = segment
        .Select(c => c.Location)
        .ToArray();
    
    var multiPoint = _geometryFactory.CreateMultiPoint(points);
    
    // Use PostGIS ST_VoronoiPolygons
    var sql = @"
        WITH voronoi_cells AS (
            SELECT 
                (ST_Dump(ST_VoronoiPolygons(@multipoint::geometry))).geom as cell
        ),
        cell_neighbors AS (
            SELECT 
                c1.cell as cell1,
                c2.cell as cell2
            FROM voronoi_cells c1
            CROSS JOIN voronoi_cells c2
            WHERE ST_Touches(c1.cell, c2.cell)
        )
        SELECT 
            ST_AsText(cell1) as cell1_wkt,
            ST_AsText(cell2) as cell2_wkt
        FROM cell_neighbors;
    ";
    
    var neighbors = await _dbContext.Database
        .SqlQueryRaw<VoronoiNeighbor>(sql, new NpgsqlParameter("@multipoint", multiPoint))
        .ToListAsync(cancellationToken);
    
    // Map Voronoi cells back to constants
    var pairs = new List<ConstantPair>();
    foreach (var neighbor in neighbors)
    {
        var const1 = FindConstantByCell(segment, neighbor.Cell1Wkt);
        var const2 = FindConstantByCell(segment, neighbor.Cell2Wkt);
        
        if (const1 != null && const2 != null)
        {
            pairs.Add(new ConstantPair
            {
                Left = const1,
                Right = const2,
                HilbertDistance = Math.Abs(
                    (long)const1.Coordinate.HilbertIndex - 
                    (long)const2.Coordinate.HilbertIndex),
                SpatialDistance = const1.Location.Distance(const2.Location)
            });
        }
    }
    
    return pairs;
}

private Constant? FindConstantByCell(List<Constant> segment, string cellWkt)
{
    var cell = _wktReader.Read(cellWkt);
    var centroid = cell.Centroid;
    
    // Find constant closest to Voronoi cell centroid
    return segment
        .OrderBy(c => c.Location.Distance(centroid))
        .FirstOrDefault();
}

private record VoronoiNeighbor
{
    public string Cell1Wkt { get; init; } = string.Empty;
    public string Cell2Wkt { get; init; } = string.Empty;
}

private record ConstantPair
{
    public Constant Left { get; init; } = null!;
    public Constant Right { get; init; } = null!;
    public long HilbertDistance { get; init; }
    public double SpatialDistance { get; init; }
}
```

**Alternative: NetTopologySuite Voronoi** (if PostGIS version < 3.3):

```csharp
private List<ConstantPair> ComputeVoronoiNeighborsNTS(List<Constant> segment)
{
    var coordinates = segment
        .Select(c => new Coordinate(c.Location.X, c.Location.Y, c.Location.Z))
        .ToArray();
    
    var builder = new VoronoiDiagramBuilder();
    builder.SetSites(coordinates);
    
    var diagram = builder.GetDiagram(_geometryFactory);
    
    // Extract neighboring cells
    var neighbors = new List<ConstantPair>();
    
    foreach (var polygon in diagram.Geometries.Cast<Polygon>())
    {
        var touchingPolygons = diagram.Geometries
            .Cast<Polygon>()
            .Where(p => p.Touches(polygon))
            .ToList();
        
        foreach (var touching in touchingPolygons)
        {
            var const1 = FindConstantByCell(segment, polygon);
            var const2 = FindConstantByCell(segment, touching);
            
            if (const1 != null && const2 != null)
            {
                neighbors.Add(new ConstantPair
                {
                    Left = const1,
                    Right = const2,
                    HilbertDistance = Math.Abs(
                        (long)const1.Coordinate.HilbertIndex - 
                        (long)const2.Coordinate.HilbertIndex),
                    SpatialDistance = const1.Location.Distance(const2.Location)
                });
            }
        }
    }
    
    return neighbors.DistinctBy(p => new { p.Left.Id, p.Right.Id }).ToList();
}
```

**Acceptance Criteria**:
- ✅ Voronoi tessellation computed for each segment
- ✅ Neighboring cells correctly identified
- ✅ Constant pairs extracted from neighbors
- ✅ Works with both PostGIS and NTS implementations
- ✅ Performance: <1s for 1000 constants

---

### Task 2.4: Compute MST for Vocabulary Learning (8 hours)

```csharp
private DelaunayGraph BuildDelaunayGraph(List<ConstantPair> pairs)
{
    var graph = new DelaunayGraph();
    
    foreach (var pair in pairs)
    {
        graph.AddEdge(
            pair.Left.Id,
            pair.Right.Id,
            weight: pair.HilbertDistance);
    }
    
    _logger.LogDebug("Built Delaunay graph: {Vertices} vertices, {Edges} edges",
        graph.Vertices.Count, graph.Edges.Count);
    
    return graph;
}

private MinimumSpanningTree ComputeMinimumSpanningTree(DelaunayGraph graph)
{
    // Kruskal's algorithm: sort edges by weight, add if no cycle
    var sortedEdges = graph.Edges.OrderBy(e => e.Weight).ToList();
    var mst = new MinimumSpanningTree();
    var unionFind = new UnionFind(graph.Vertices.Count);
    
    foreach (var edge in sortedEdges)
    {
        var leftIndex = graph.GetVertexIndex(edge.LeftId);
        var rightIndex = graph.GetVertexIndex(edge.RightId);
        
        if (unionFind.Find(leftIndex) != unionFind.Find(rightIndex))
        {
            mst.AddEdge(edge);
            unionFind.Union(leftIndex, rightIndex);
        }
        
        // MST complete when we have V-1 edges
        if (mst.Edges.Count == graph.Vertices.Count - 1)
            break;
    }
    
    _logger.LogDebug("MST computed: {EdgeCount} edges, total weight={TotalWeight}",
        mst.Edges.Count, mst.TotalWeight);
    
    return mst;
}

private async Task<List<BPEToken>> SelectVocabularyFromMST(
    MinimumSpanningTree mst,
    int maxVocabularySize,
    int minFrequency,
    CancellationToken cancellationToken)
{
    var vocabulary = new List<BPEToken>();
    
    // MST edges represent most "natural" constant combinations
    // Select top maxVocabularySize edges by frequency/utility
    var edgesWithFrequency = new List<(MSTEdge Edge, int Frequency)>();
    
    foreach (var edge in mst.Edges)
    {
        var frequency = await ComputePairFrequencyAsync(
            edge.LeftId,
            edge.RightId,
            cancellationToken);
        
        if (frequency >= minFrequency)
        {
            edgesWithFrequency.Add((edge, frequency));
        }
    }
    
    // Sort by frequency (most common pairs first)
    var selectedEdges = edgesWithFrequency
        .OrderByDescending(e => e.Frequency)
        .Take(maxVocabularySize)
        .ToList();
    
    // Create BPETokens for selected edges
    foreach (var (edge, frequency) in selectedEdges)
    {
        var leftConstant = await _constantRepository.GetByIdAsync(edge.LeftId);
        var rightConstant = await _constantRepository.GetByIdAsync(edge.RightId);
        
        if (leftConstant == null || rightConstant == null)
            continue;
        
        // Create LINESTRINGZM geometry for composition
        var sequenceGeometry = SequenceGeometry.FromConstants(new[]
        {
            (leftConstant.Id, leftConstant.Coordinate, 0),
            (rightConstant.Id, rightConstant.Coordinate, 1)
        });
        
        var token = BPEToken.CreateFromMerge(
            leftConstant,
            rightConstant,
            mergeLevel: 1,
            frequency: frequency,
            compositionGeometry: sequenceGeometry);
        
        vocabulary.Add(token);
    }
    
    return vocabulary;
}

private async Task<int> ComputePairFrequencyAsync(
    Guid leftId,
    Guid rightId,
    CancellationToken cancellationToken)
{
    // Count how often these two constants appear adjacent in data
    // This would query ingestion logs or composition history
    
    var sql = @"
        SELECT COUNT(*) 
        FROM constant_adjacency
        WHERE (source_id = @left AND target_id = @right)
           OR (source_id = @right AND target_id = @left)
    ";
    
    var count = await _dbContext.Database
        .SqlQueryRaw<int>(sql,
            new NpgsqlParameter("@left", leftId),
            new NpgsqlParameter("@right", rightId))
        .FirstOrDefaultAsync(cancellationToken);
    
    return count;
}

// Helper classes
private class DelaunayGraph
{
    public List<Guid> Vertices { get; } = new();
    public List<GraphEdge> Edges { get; } = new();
    
    private Dictionary<Guid, int> _vertexIndexMap = new();
    
    public void AddEdge(Guid left, Guid right, long weight)
    {
        if (!_vertexIndexMap.ContainsKey(left))
        {
            _vertexIndexMap[left] = Vertices.Count;
            Vertices.Add(left);
        }
        if (!_vertexIndexMap.ContainsKey(right))
        {
            _vertexIndexMap[right] = Vertices.Count;
            Vertices.Add(right);
        }
        
        Edges.Add(new GraphEdge
        {
            LeftId = left,
            RightId = right,
            Weight = weight
        });
    }
    
    public int GetVertexIndex(Guid id) => _vertexIndexMap[id];
}

private record GraphEdge
{
    public Guid LeftId { get; init; }
    public Guid RightId { get; init; }
    public long Weight { get; init; }
}

private class MinimumSpanningTree
{
    public List<MSTEdge> Edges { get; } = new();
    public long TotalWeight => Edges.Sum(e => e.Weight);
    
    public void AddEdge(GraphEdge edge)
    {
        Edges.Add(new MSTEdge
        {
            LeftId = edge.LeftId,
            RightId = edge.RightId,
            Weight = edge.Weight
        });
    }
}

private record MSTEdge
{
    public Guid LeftId { get; init; }
    public Guid RightId { get; init; }
    public long Weight { get; init; }
}

private class UnionFind
{
    private readonly int[] _parent;
    private readonly int[] _rank;
    
    public UnionFind(int size)
    {
        _parent = Enumerable.Range(0, size).ToArray();
        _rank = new int[size];
    }
    
    public int Find(int x)
    {
        if (_parent[x] != x)
            _parent[x] = Find(_parent[x]); // Path compression
        return _parent[x];
    }
    
    public void Union(int x, int y)
    {
        var rootX = Find(x);
        var rootY = Find(y);
        
        if (rootX == rootY) return;
        
        // Union by rank
        if (_rank[rootX] < _rank[rootY])
            _parent[rootX] = rootY;
        else if (_rank[rootX] > _rank[rootY])
            _parent[rootY] = rootX;
        else
        {
            _parent[rootY] = rootX;
            _rank[rootX]++;
        }
    }
}
```

**Acceptance Criteria**:
- ✅ MST correctly computed using Kruskal's algorithm
- ✅ Vocabulary size limited to maxVocabularySize
- ✅ Only edges with frequency >= minFrequency included
- ✅ BPETokens created with LINESTRINGZM geometry
- ✅ Performance: <5s for 10K constants

---

### Task 2.5: Add CompositionGeometry to BPEToken (4 hours)

**Files to Modify**:
- `Hartonomous.Core/Domain/Entities/BPEToken.cs`
- `Hartonomous.Data/Configurations/BPETokenConfiguration.cs`

```csharp
// BPEToken.cs - Add property
public sealed class BPEToken : BaseEntity
{
    // ... existing properties ...
    
    // NEW: Store composition as LINESTRINGZM geometry
    private LineString? _compositionGeometry;
    public LineString? CompositionGeometry
    {
        get => _compositionGeometry;
        private set => _compositionGeometry = value;
    }
    
    // UPDATED: Factory method includes geometry
    public static BPEToken CreateFromMerge(
        Constant left,
        Constant right,
        int mergeLevel,
        int frequency,
        SequenceGeometry compositionGeometry)
    {
        var token = new BPEToken
        {
            Id = Guid.NewGuid(),
            ConstantSequence = new List<Guid> { left.Id, right.Id },
            MergeLevel = mergeLevel,
            Frequency = frequency,
            CompositionGeometry = compositionGeometry.Geometry,
            // ... existing initialization ...
        };
        
        return token;
    }
}
```

**Acceptance Criteria**:
- ✅ BPEToken stores LINESTRINGZM geometry
- ✅ Geometry nullable for backward compatibility
- ✅ Factory method accepts SequenceGeometry
- ✅ EF configuration maps to PostGIS correctly

---

## Integration Tests

### Test Scenario 1: Complete BPE Workflow
```csharp
[Fact]
public async Task LearnVocabulary_WithHilbertSortedConstants_CreatesGeometricTokens()
{
    // Arrange: Create 100 constants with varying Hilbert positions
    var constants = CreateTestConstants(count: 100);
    await SeedDatabaseAsync(constants);
    
    // Act: Learn vocabulary
    var vocabulary = await _bpeService.LearnVocabularyAsync(
        ContentType.Text,
        maxVocabularySize: 20,
        minFrequency: 2);
    
    // Assert: Vocabulary created with LINESTRINGZM geometries
    Assert.NotEmpty(vocabulary);
    Assert.All(vocabulary, token =>
    {
        Assert.NotNull(token.CompositionGeometry);
        Assert.Equal(GeometryType.LineString, token.CompositionGeometry.GeometryType);
        Assert.True(token.CompositionGeometry.Coordinates.Length >= 2);
    });
}
```

### Test Scenario 2: Gap Detection
```csharp
[Fact]
public void DetectHilbertGaps_WithSparseSequence_IdentifiesGaps()
{
    // Arrange: Constants with large gap
    var constants = new List<Constant>
    {
        CreateConstant(hilbertIndex: 100),
        CreateConstant(hilbertIndex: 200),
        CreateConstant(hilbertIndex: 5000), // Large gap
        CreateConstant(hilbertIndex: 5100)
    };
    
    // Act: Detect gaps
    var gaps = DetectHilbertGaps(constants, gapThreshold: 1000);
    
    // Assert: One gap identified
    Assert.Single(gaps);
    Assert.Equal(4800UL, gaps[0].GapSize);
}
```

---

## Performance Benchmarks

| Operation | Target | Measured |
|-----------|--------|----------|
| Sort 10K constants | <100ms | TBD |
| Detect gaps | <50ms | TBD |
| Voronoi tessellation (1K) | <1s | TBD |
| MST computation (1K) | <500ms | TBD |
| Full workflow (10K) | <30s | TBD |

---

## Acceptance Criteria (Phase Exit)

- ✅ BPE algorithm uses Hilbert-sorted sequences
- ✅ Gap detection working correctly
- ✅ Voronoi tessellation integrated
- ✅ MST-based vocabulary learning operational
- ✅ LINESTRINGZM geometries stored in BPEToken
- ✅ All tests passing (>80% coverage)
- ✅ Performance benchmarks met
- ✅ Code review completed

---

**Next Phase**: [PHASE3_UNIVERSAL_PROPERTIES.md](./PHASE3_UNIVERSAL_PROPERTIES.md) - Shannon entropy, Kolmogorov complexity, emergent modality

**Status**: 🔨 In Progress - Core components implemented

**Implementation Status**:
- ✅ Task 2.2: Gap Detection (HilbertGapDetector) - Complete with 10 tests
- ✅ Task 2.4: MST Computation (MinimumSpanningTreeComputer) - Complete with 7 tests
- ✅ Task 2.3: Voronoi Tessellation (VoronoiTessellator) - Basic implementation complete
- ⏸️ Task 2.1: LearnVocabularyAsync redesign - Pending (requires integration of above components)
- ⏸️ Task 2.5: CompositionGeometry - Pending (after 2.1)

**Test Results**:
- ✅ 17 QuantizationService tests passing (Phase 1)
- ✅ 10 HilbertGapDetector tests passing
- ✅ 7 MinimumSpanningTreeComputer tests passing
- ✅ 115 Core tests passing
- ⚠️ 6 legacy BPEService tests failing (expected - old implementation being replaced)

**Last Updated**: December 4, 2025
