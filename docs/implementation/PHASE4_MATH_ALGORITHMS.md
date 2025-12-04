# Phase 4: Mathematical Algorithm Integration

**Duration**: 5-6 days  
**Dependencies**: Phase 1 (POINTZM geometry), Phase 2 (MST/Voronoi), Phase 3 (Universal properties)  
**Critical Path**: Yes - core algorithms for content reconstruction, deduplication, importance scoring

---

## Overview

Integrate six foundational mathematical algorithms that leverage POINTZM geometry: A* pathfinding for content reconstruction, PageRank for atom importance, Laplace operator for information diffusion, Blossom/Hungarian matching for deduplication, Voronoi/Delaunay neighborhood analysis, and MST for minimal spanning structures.

---

## Objectives

1. Implement A* pathfinding for optimal content reconstruction paths
2. Implement PageRank algorithm for constant importance scoring
3. Implement Laplace operator for information diffusion analysis
4. Implement Blossom/Hungarian matching for atom deduplication
5. Integrate Voronoi/Delaunay neighborhood analysis
6. Integrate MST computation for compositional structures
7. Create `GraphAlgorithmsService` for unified algorithm access

---

## Task Breakdown

### Task 4.1: A* Pathfinding for Content Reconstruction (10 hours)

**Purpose**: Given a target composite (e.g., document), find optimal path through POINTZM space to reconstruct it from atoms.

**File**: `Hartonomous.Core/Application/Services/PathfindingService.cs` (NEW)

```csharp
public interface IPathfindingService
{
    Task<ReconstructionPath?> FindOptimalPathAsync(
        Guid targetCompositeId,
        CancellationToken cancellationToken = default);
    
    Task<IEnumerable<Constant>> ReconstructContentAsync(
        ReconstructionPath path,
        CancellationToken cancellationToken = default);
}

public sealed class PathfindingService : IPathfindingService
{
    private readonly IConstantRepository _constantRepository;
    private readonly IBPETokenRepository _bpeTokenRepository;
    private readonly ILogger<PathfindingService> _logger;
    
    public async Task<ReconstructionPath?> FindOptimalPathAsync(
        Guid targetCompositeId,
        CancellationToken cancellationToken = default)
    {
        // Get target composite (landmark)
        var target = await _constantRepository.GetByIdAsync(targetCompositeId);
        if (target == null)
            return null;
        
        _logger.LogInformation("Finding optimal reconstruction path for {Target}", targetCompositeId);
        
        // A* algorithm: f(n) = g(n) + h(n)
        // g(n) = actual cost from start to n (Hilbert distance traversed)
        // h(n) = heuristic cost from n to target (Euclidean distance in XYZM space)
        
        var openSet = new PriorityQueue<PathNode, double>();
        var closedSet = new HashSet<Guid>();
        var cameFrom = new Dictionary<Guid, Guid>();
        var gScore = new Dictionary<Guid, double> { [target.Id] = 0 };
        var fScore = new Dictionary<Guid, double> { [target.Id] = 0 };
        
        // Start from target, work backwards to atoms
        openSet.Enqueue(new PathNode(target), 0);
        
        while (openSet.Count > 0)
        {
            var current = openSet.Dequeue();
            
            // If we reached an atom (MergeLevel = 0), reconstruct path
            if (current.Constant.MergeLevel == 0)
            {
                return ReconstructPath(cameFrom, current.Constant.Id, target.Id);
            }
            
            closedSet.Add(current.Constant.Id);
            
            // Get neighbors (decomposition: find atoms that compose this constant)
            var neighbors = await GetDecompositionNeighborsAsync(
                current.Constant.Id,
                cancellationToken);
            
            foreach (var neighbor in neighbors)
            {
                if (closedSet.Contains(neighbor.Id))
                    continue;
                
                // Compute tentative g score
                var tentativeGScore = gScore[current.Constant.Id] + 
                    HilbertDistance(current.Constant, neighbor);
                
                if (!gScore.ContainsKey(neighbor.Id) || tentativeGScore < gScore[neighbor.Id])
                {
                    // This path is better
                    cameFrom[neighbor.Id] = current.Constant.Id;
                    gScore[neighbor.Id] = tentativeGScore;
                    
                    // Heuristic: Euclidean distance in XYZM space to "atom space" (origin)
                    var heuristic = EuclideanDistanceToAtomSpace(neighbor);
                    fScore[neighbor.Id] = tentativeGScore + heuristic;
                    
                    openSet.Enqueue(new PathNode(neighbor), fScore[neighbor.Id]);
                }
            }
        }
        
        _logger.LogWarning("No reconstruction path found for {Target}", targetCompositeId);
        return null;
    }
    
    private async Task<IEnumerable<Constant>> GetDecompositionNeighborsAsync(
        Guid compositeId,
        CancellationToken cancellationToken)
    {
        // Find BPE token that created this composite
        var token = await _bpeTokenRepository
            .Query()
            .FirstOrDefaultAsync(t => t.CompositeId == compositeId, cancellationToken);
        
        if (token == null || token.ConstantSequence.Count == 0)
            return Enumerable.Empty<Constant>();
        
        // Return constituent atoms
        var constants = new List<Constant>();
        foreach (var atomId in token.ConstantSequence)
        {
            var atom = await _constantRepository.GetByIdAsync(atomId);
            if (atom != null)
                constants.Add(atom);
        }
        
        return constants;
    }
    
    private double HilbertDistance(Constant a, Constant b)
    {
        return Math.Abs((long)a.Coordinate.HilbertIndex - (long)b.Coordinate.HilbertIndex);
    }
    
    private double EuclideanDistanceToAtomSpace(Constant constant)
    {
        // Heuristic: distance to "atom space" (low MergeLevel, origin-like coordinates)
        var x = constant.Coordinate.HilbertIndex;
        var y = constant.Coordinate.QuantizedY;
        var z = constant.Coordinate.QuantizedZ;
        var m = constant.Coordinate.QuantizedM;
        
        // Normalize to [0, 1]
        var xNorm = x / (double)ulong.MaxValue;
        var yNorm = y / 2_097_151.0;
        var zNorm = z / 2_097_151.0;
        var mNorm = m / 2_097_151.0;
        
        // Euclidean distance to origin
        return Math.Sqrt(xNorm * xNorm + yNorm * yNorm + zNorm * zNorm + mNorm * mNorm);
    }
    
    private ReconstructionPath ReconstructPath(
        Dictionary<Guid, Guid> cameFrom,
        Guid atomId,
        Guid targetId)
    {
        var path = new List<Guid> { atomId };
        var current = atomId;
        
        while (cameFrom.ContainsKey(current))
        {
            current = cameFrom[current];
            path.Add(current);
        }
        
        path.Reverse(); // Start from target, end at atom
        
        return new ReconstructionPath
        {
            TargetId = targetId,
            AtomId = atomId,
            Path = path,
            PathLength = path.Count
        };
    }
    
    public async Task<IEnumerable<Constant>> ReconstructContentAsync(
        ReconstructionPath path,
        CancellationToken cancellationToken = default)
    {
        var constants = new List<Constant>();
        
        foreach (var id in path.Path)
        {
            var constant = await _constantRepository.GetByIdAsync(id);
            if (constant != null)
                constants.Add(constant);
        }
        
        return constants;
    }
}

private record PathNode(Constant Constant);

public record ReconstructionPath
{
    public Guid TargetId { get; init; }
    public Guid AtomId { get; init; }
    public List<Guid> Path { get; init; } = new();
    public int PathLength { get; init; }
}
```

**Complexity**: O((V + E) log V) where V = constants, E = BPE edges

**Acceptance Criteria**:
- ✅ A* correctly finds shortest reconstruction path
- ✅ Heuristic is admissible (never overestimates)
- ✅ Performance: <1s for 10K constant graph
- ✅ Handles disconnected graphs gracefully

---

### Task 4.2: PageRank for Importance Scoring (8 hours)

**Purpose**: Score constants by their "importance" based on reference graph structure.

**File**: `Hartonomous.Core/Application/Services/ImportanceScoringService.cs` (NEW)

```csharp
public interface IImportanceScoringService
{
    Task<Dictionary<Guid, double>> ComputePageRankAsync(
        double dampingFactor = 0.85,
        int maxIterations = 100,
        double convergenceThreshold = 1e-6,
        CancellationToken cancellationToken = default);
    
    Task UpdateConstantImportanceScoresAsync(CancellationToken cancellationToken = default);
}

public sealed class ImportanceScoringService : IImportanceScoringService
{
    private readonly IConstantRepository _constantRepository;
    private readonly IBPETokenRepository _bpeTokenRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ImportanceScoringService> _logger;
    
    public async Task<Dictionary<Guid, double>> ComputePageRankAsync(
        double dampingFactor = 0.85,
        int maxIterations = 100,
        double convergenceThreshold = 1e-6,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Computing PageRank (d={D}, maxIter={MaxIter})",
            dampingFactor, maxIterations);
        
        // Build adjacency graph from BPE tokens
        var constants = await _constantRepository.GetAllAsync().ToListAsync(cancellationToken);
        var n = constants.Count;
        
        if (n == 0)
            return new Dictionary<Guid, double>();
        
        // Initialize PageRank scores
        var pageRank = constants.ToDictionary(c => c.Id, c => 1.0 / n);
        var newPageRank = new Dictionary<Guid, double>();
        
        // Build outlink graph
        var outlinks = await BuildOutlinkGraphAsync(cancellationToken);
        
        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            double maxDelta = 0;
            
            foreach (var constant in constants)
            {
                // PageRank formula: PR(A) = (1-d)/N + d * Σ(PR(Ti)/C(Ti))
                // where Ti = pages linking to A, C(Ti) = number of outlinks from Ti
                
                double sum = 0;
                if (outlinks.TryGetValue(constant.Id, out var inlinks))
                {
                    foreach (var inlink in inlinks)
                    {
                        var outlinkCount = GetOutlinkCount(inlink, outlinks);
                        if (outlinkCount > 0)
                        {
                            sum += pageRank[inlink] / outlinkCount;
                        }
                    }
                }
                
                var newScore = (1 - dampingFactor) / n + dampingFactor * sum;
                newPageRank[constant.Id] = newScore;
                
                var delta = Math.Abs(newScore - pageRank[constant.Id]);
                maxDelta = Math.Max(maxDelta, delta);
            }
            
            // Update scores
            pageRank = new Dictionary<Guid, double>(newPageRank);
            
            // Check convergence
            if (maxDelta < convergenceThreshold)
            {
                _logger.LogInformation("PageRank converged after {Iterations} iterations", iteration + 1);
                break;
            }
        }
        
        // Normalize scores to [0, 1]
        var maxScore = pageRank.Values.Max();
        if (maxScore > 0)
        {
            pageRank = pageRank.ToDictionary(kvp => kvp.Key, kvp => kvp.Value / maxScore);
        }
        
        _logger.LogInformation("PageRank computed for {Count} constants", n);
        return pageRank;
    }
    
    private async Task<Dictionary<Guid, List<Guid>>> BuildOutlinkGraphAsync(
        CancellationToken cancellationToken)
    {
        // Inlinks: composite -> atoms that compose it
        var outlinks = new Dictionary<Guid, List<Guid>>();
        
        var tokens = await _bpeTokenRepository.GetAllAsync().ToListAsync(cancellationToken);
        
        foreach (var token in tokens)
        {
            var compositeId = token.CompositeId;
            
            foreach (var atomId in token.ConstantSequence)
            {
                if (!outlinks.ContainsKey(atomId))
                    outlinks[atomId] = new List<Guid>();
                
                outlinks[atomId].Add(compositeId);
            }
        }
        
        return outlinks;
    }
    
    private int GetOutlinkCount(Guid constantId, Dictionary<Guid, List<Guid>> outlinkGraph)
    {
        return outlinkGraph.TryGetValue(constantId, out var outlinks) ? outlinks.Count : 0;
    }
    
    public async Task UpdateConstantImportanceScoresAsync(
        CancellationToken cancellationToken = default)
    {
        var pageRanks = await ComputePageRankAsync(cancellationToken: cancellationToken);
        
        foreach (var (constantId, score) in pageRanks)
        {
            var constant = await _constantRepository.GetByIdAsync(constantId);
            if (constant != null)
            {
                constant.ImportanceScore = score;
            }
        }
        
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        
        _logger.LogInformation("Updated importance scores for {Count} constants", pageRanks.Count);
    }
}
```

**Complexity**: O(I * (V + E)) where I = iterations, V = constants, E = BPE edges

**Acceptance Criteria**:
- ✅ PageRank converges to stable values
- ✅ Important atoms (high reference count) get high scores
- ✅ Performance: <10s for 100K constant graph
- ✅ Handles disconnected components

---

### Task 4.3: Laplace Operator for Information Diffusion (6 hours)

**Purpose**: Analyze how information "flows" through POINTZM space (smooth vs. discontinuous regions).

**File**: `Hartonomous.Core/Application/Services/DiffusionAnalysisService.cs` (NEW)

```csharp
public interface IDiffusionAnalysisService
{
    Task<DiffusionResult> AnalyzeDiffusionAsync(
        Point sourceLocation,
        double radius,
        CancellationToken cancellationToken = default);
    
    Task<double> ComputeLaplacianAsync(
        Guid constantId,
        CancellationToken cancellationToken = default);
}

public sealed class DiffusionAnalysisService : IDiffusionAnalysisService
{
    private readonly IConstantRepository _constantRepository;
    private readonly ILogger<DiffusionAnalysisService> _logger;
    
    public async Task<DiffusionResult> AnalyzeDiffusionAsync(
        Point sourceLocation,
        double radius,
        CancellationToken cancellationToken = default)
    {
        // Find neighbors within radius
        var neighbors = await _constantRepository
            .WithinRadius(sourceLocation, radius)
            .ToListAsync(cancellationToken);
        
        if (neighbors.Count == 0)
            return DiffusionResult.Empty;
        
        // Compute Laplacian at each neighbor
        var laplacians = new Dictionary<Guid, double>();
        
        foreach (var neighbor in neighbors)
        {
            var laplacian = await ComputeLaplacianAsync(neighbor.Id, cancellationToken);
            laplacians[neighbor.Id] = laplacian;
        }
        
        // Analyze diffusion properties
        var meanLaplacian = laplacians.Values.Average();
        var maxLaplacian = laplacians.Values.Max();
        var minLaplacian = laplacians.Values.Min();
        var variance = laplacians.Values.Select(l => Math.Pow(l - meanLaplacian, 2)).Average();
        
        return new DiffusionResult
        {
            SourceLocation = sourceLocation,
            Radius = radius,
            NeighborCount = neighbors.Count,
            MeanLaplacian = meanLaplacian,
            MaxLaplacian = maxLaplacian,
            MinLaplacian = minLaplacian,
            Variance = variance,
            IsSmooth = variance < 0.1 // Low variance = smooth diffusion
        };
    }
    
    public async Task<double> ComputeLaplacianAsync(
        Guid constantId,
        CancellationToken cancellationToken = default)
    {
        // Laplacian: Δf(x) = Σ(f(neighbor) - f(x)) / degree
        // Measures "difference from neighbors" - high value = discontinuity
        
        var constant = await _constantRepository.GetByIdAsync(constantId);
        if (constant == null)
            return 0;
        
        // Get k nearest neighbors
        var neighbors = await _constantRepository
            .KNearestNeighbors(constant.Location, k: 10)
            .Where(c => c.Id != constantId)
            .ToListAsync(cancellationToken);
        
        if (neighbors.Count == 0)
            return 0;
        
        // Use entropy (Y) as "value" for Laplacian
        var centerValue = constant.Coordinate.QuantizedY;
        var neighborValues = neighbors.Select(n => n.Coordinate.QuantizedY).ToList();
        
        // Discrete Laplacian
        var laplacian = neighborValues.Average() - centerValue;
        
        // Normalize to [-1, 1]
        laplacian /= 2_097_151.0;
        
        return laplacian;
    }
}

public record DiffusionResult
{
    public Point SourceLocation { get; init; } = null!;
    public double Radius { get; init; }
    public int NeighborCount { get; init; }
    public double MeanLaplacian { get; init; }
    public double MaxLaplacian { get; init; }
    public double MinLaplacian { get; init; }
    public double Variance { get; init; }
    public bool IsSmooth { get; init; }
    
    public static DiffusionResult Empty => new();
}
```

**Interpretation**:
- **Low Laplacian**: Constant similar to neighbors (smooth region)
- **High Laplacian**: Constant very different from neighbors (discontinuity, edge)

**Acceptance Criteria**:
- ✅ Laplacian correctly computed using k-NN
- ✅ Smooth regions identified (low variance)
- ✅ Discontinuities identified (high Laplacian values)
- ✅ Performance: <100ms per constant

---

### Task 4.4: Blossom/Hungarian Matching for Deduplication (10 hours)

**Purpose**: Find optimal matching between near-duplicate atoms for deduplication.

**File**: `Hartonomous.Core/Application/Services/DeduplicationService.cs` (NEW)

```csharp
public interface IDeduplicationService
{
    Task<IEnumerable<DuplicatePair>> FindDuplicatesAsync(
        double similarityThreshold = 0.95,
        CancellationToken cancellationToken = default);
    
    Task<int> MergeDuplicatesAsync(
        IEnumerable<DuplicatePair> duplicates,
        CancellationToken cancellationToken = default);
}

public sealed class DeduplicationService : IDeduplicationService
{
    private readonly IConstantRepository _constantRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DeduplicationService> _logger;
    
    public async Task<IEnumerable<DuplicatePair>> FindDuplicatesAsync(
        double similarityThreshold = 0.95,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Finding duplicates (threshold={Threshold})", similarityThreshold);
        
        // Find candidate pairs using k-NN in POINTZM space
        var constants = await _constantRepository.GetAllAsync().ToListAsync(cancellationToken);
        var candidates = new List<(Constant, Constant, double)>();
        
        foreach (var constant in constants)
        {
            var neighbors = await _constantRepository
                .KNearestNeighbors(constant.Location, k: 5)
                .Where(c => c.Id != constant.Id)
                .ToListAsync(cancellationToken);
            
            foreach (var neighbor in neighbors)
            {
                var similarity = ComputeSimilarity(constant, neighbor);
                
                if (similarity >= similarityThreshold)
                {
                    candidates.Add((constant, neighbor, similarity));
                }
            }
        }
        
        // Build bipartite graph for matching
        var duplicatePairs = SolveMaximumWeightMatching(candidates);
        
        _logger.LogInformation("Found {Count} duplicate pairs", duplicatePairs.Count());
        return duplicatePairs;
    }
    
    private double ComputeSimilarity(Constant a, Constant b)
    {
        // Cosine similarity in XYZM space
        var dotProduct = 
            a.Coordinate.HilbertIndex * b.Coordinate.HilbertIndex +
            a.Coordinate.QuantizedY * b.Coordinate.QuantizedY +
            a.Coordinate.QuantizedZ * b.Coordinate.QuantizedZ +
            a.Coordinate.QuantizedM * b.Coordinate.QuantizedM;
        
        var magnitudeA = Math.Sqrt(
            a.Coordinate.HilbertIndex * a.Coordinate.HilbertIndex +
            a.Coordinate.QuantizedY * a.Coordinate.QuantizedY +
            a.Coordinate.QuantizedZ * a.Coordinate.QuantizedZ +
            a.Coordinate.QuantizedM * a.Coordinate.QuantizedM);
        
        var magnitudeB = Math.Sqrt(
            b.Coordinate.HilbertIndex * b.Coordinate.HilbertIndex +
            b.Coordinate.QuantizedY * b.Coordinate.QuantizedY +
            b.Coordinate.QuantizedZ * b.Coordinate.QuantizedZ +
            b.Coordinate.QuantizedM * b.Coordinate.QuantizedM);
        
        return dotProduct / (magnitudeA * magnitudeB);
    }
    
    private IEnumerable<DuplicatePair> SolveMaximumWeightMatching(
        List<(Constant, Constant, double)> candidates)
    {
        // Simplified Hungarian algorithm for maximum weight matching
        // For production: use library like Google OR-Tools
        
        // Group by similarity and select highest weight pairs
        var pairs = new List<DuplicatePair>();
        var matched = new HashSet<Guid>();
        
        foreach (var (left, right, similarity) in candidates.OrderByDescending(c => c.Item3))
        {
            if (matched.Contains(left.Id) || matched.Contains(right.Id))
                continue;
            
            pairs.Add(new DuplicatePair
            {
                LeftId = left.Id,
                RightId = right.Id,
                Similarity = similarity,
                LeftData = left.Data,
                RightData = right.Data
            });
            
            matched.Add(left.Id);
            matched.Add(right.Id);
        }
        
        return pairs;
    }
    
    public async Task<int> MergeDuplicatesAsync(
        IEnumerable<DuplicatePair> duplicates,
        CancellationToken cancellationToken = default)
    {
        int mergedCount = 0;
        
        foreach (var pair in duplicates)
        {
            var left = await _constantRepository.GetByIdAsync(pair.LeftId);
            var right = await _constantRepository.GetByIdAsync(pair.RightId);
            
            if (left == null || right == null)
                continue;
            
            // Keep higher reference count constant, delete other
            var keeper = left.ReferenceCount >= right.ReferenceCount ? left : right;
            var duplicate = left.ReferenceCount >= right.ReferenceCount ? right : left;
            
            // Update references to point to keeper
            keeper.ReferenceCount += duplicate.ReferenceCount;
            
            // Soft delete duplicate
            await _constantRepository.DeleteAsync(duplicate.Id);
            
            mergedCount++;
        }
        
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        
        _logger.LogInformation("Merged {Count} duplicate pairs", mergedCount);
        return mergedCount;
    }
}

public record DuplicatePair
{
    public Guid LeftId { get; init; }
    public Guid RightId { get; init; }
    public double Similarity { get; init; }
    public byte[] LeftData { get; init; } = Array.Empty<byte>();
    public byte[] RightData { get; init; } = Array.Empty<byte>();
}
```

**Complexity**: O(V² log V) for naive matching, O(V³) for Hungarian algorithm

**Acceptance Criteria**:
- ✅ Duplicate pairs correctly identified
- ✅ Maximum weight matching produces optimal pairing
- ✅ Merge operation preserves higher reference count constant
- ✅ Performance: <1min for 10K constants

---

### Task 4.5: GraphAlgorithmsService Unified Interface (4 hours)

**File**: `Hartonomous.Core/Application/Services/GraphAlgorithmsService.cs` (NEW)

```csharp
public interface IGraphAlgorithmsService
{
    // Pathfinding
    Task<ReconstructionPath?> AStarAsync(Guid targetId, CancellationToken cancellationToken = default);
    
    // Importance
    Task<Dictionary<Guid, double>> PageRankAsync(CancellationToken cancellationToken = default);
    
    // Diffusion
    Task<DiffusionResult> LaplacianAsync(Point location, double radius, CancellationToken cancellationToken = default);
    
    // Deduplication
    Task<IEnumerable<DuplicatePair>> FindDuplicatesAsync(double threshold = 0.95, CancellationToken cancellationToken = default);
    
    // Voronoi/Delaunay (from Phase 2)
    Task<List<ConstantPair>> VoronoiNeighborsAsync(List<Constant> segment, CancellationToken cancellationToken = default);
    
    // MST (from Phase 2)
    MinimumSpanningTree ComputeMSTAsync(DelaunayGraph graph);
}

public sealed class GraphAlgorithmsService : IGraphAlgorithmsService
{
    private readonly IPathfindingService _pathfindingService;
    private readonly IImportanceScoringService _importanceScoringService;
    private readonly IDiffusionAnalysisService _diffusionAnalysisService;
    private readonly IDeduplicationService _deduplicationService;
    private readonly BPEService _bpeService; // For Voronoi/MST
    
    public async Task<ReconstructionPath?> AStarAsync(
        Guid targetId,
        CancellationToken cancellationToken = default)
    {
        return await _pathfindingService.FindOptimalPathAsync(targetId, cancellationToken);
    }
    
    public async Task<Dictionary<Guid, double>> PageRankAsync(
        CancellationToken cancellationToken = default)
    {
        return await _importanceScoringService.ComputePageRankAsync(cancellationToken: cancellationToken);
    }
    
    public async Task<DiffusionResult> LaplacianAsync(
        Point location,
        double radius,
        CancellationToken cancellationToken = default)
    {
        return await _diffusionAnalysisService.AnalyzeDiffusionAsync(location, radius, cancellationToken);
    }
    
    public async Task<IEnumerable<DuplicatePair>> FindDuplicatesAsync(
        double threshold = 0.95,
        CancellationToken cancellationToken = default)
    {
        return await _deduplicationService.FindDuplicatesAsync(threshold, cancellationToken);
    }
    
    // Delegate Voronoi/MST to BPEService
    public async Task<List<ConstantPair>> VoronoiNeighborsAsync(
        List<Constant> segment,
        CancellationToken cancellationToken = default)
    {
        return await _bpeService.ComputeVoronoiNeighborsAsync(segment, cancellationToken);
    }
    
    public MinimumSpanningTree ComputeMSTAsync(DelaunayGraph graph)
    {
        return _bpeService.ComputeMinimumSpanningTree(graph);
    }
}
```

**Acceptance Criteria**:
- ✅ Unified interface for all graph algorithms
- ✅ Delegates to specialized services
- ✅ Easy to use from controllers/workflows

---

## Integration Tests

### Test Scenario 1: A* Pathfinding
```csharp
[Fact]
public async Task AStar_FindsShortestReconstructionPath()
{
    // Arrange: Create composite from atoms
    var atom1 = CreateAtom("Hello");
    var atom2 = CreateAtom("World");
    var composite = await MergeAtomsAsync(atom1, atom2);
    
    // Act: Find reconstruction path
    var path = await _pathfindingService.FindOptimalPathAsync(composite.Id);
    
    // Assert: Path goes from composite → atoms
    Assert.NotNull(path);
    Assert.Contains(atom1.Id, path.Path);
    Assert.Contains(atom2.Id, path.Path);
    Assert.True(path.PathLength >= 2);
}
```

### Test Scenario 2: PageRank Converges
```csharp
[Fact]
public async Task PageRank_ConvergesToStableValues()
{
    // Arrange: Create reference graph
    await SeedReferenceGraphAsync();
    
    // Act: Compute PageRank
    var pageRanks = await _importanceScoringService.ComputePageRankAsync();
    
    // Assert: Scores normalized and reasonable
    Assert.All(pageRanks.Values, score => Assert.InRange(score, 0, 1));
    Assert.True(pageRanks.Values.Sum() > 0);
}
```

---

## Performance Benchmarks

| Algorithm | Complexity | Target (10K) | Measured |
|-----------|------------|--------------|----------|
| A* | O((V+E) log V) | <1s | TBD |
| PageRank | O(I*(V+E)) | <10s | TBD |
| Laplacian | O(V*k) | <5s | TBD |
| Hungarian | O(V³) | <1min | TBD |
| Voronoi | O(n log n) | <1s | TBD |
| MST | O(E log E) | <500ms | TBD |

---

## Acceptance Criteria (Phase Exit)

- ✅ A* pathfinding operational
- ✅ PageRank importance scoring working
- ✅ Laplace diffusion analysis working
- ✅ Blossom/Hungarian deduplication working
- ✅ Voronoi/Delaunay from Phase 2 integrated
- ✅ MST from Phase 2 integrated
- ✅ GraphAlgorithmsService provides unified access
- ✅ All tests passing (>80% coverage)
- ✅ Performance benchmarks met

---

**Next Phase**: [PHASE5_ADVANCED_FEATURES.md](./PHASE5_ADVANCED_FEATURES.md) - Embeddings, neural networks, Borsuk-Ulam, topology

**Status**: 📋 Ready for implementation after Phase 3

**Last Updated**: December 4, 2025
