# Phase 1: Core Geometric Foundation

**Duration**: 3-4 days  
**Dependencies**: None (greenfield refactoring)  
**Critical Path**: Yes - all other phases depend on this

---

## Overview

Transform the system from PointZ (3D) to POINTZM (4D) geometry with universal 21-bit quantization for all metadata dimensions. This is the foundation for the entire architectural transformation.

---

## Objectives

1. Create universal quantization service for Y/Z/M dimensions
2. Refactor `SpatialCoordinate` to support 4D geometry
3. Update all EF Core configurations to use POINTZM
4. Create database migration from PointZ to POINTZM
5. Add support for LINESTRINGZM and MULTIPOINTZM geometries

---

## Task Breakdown

### Task 1.1: Create QuantizationService (8 hours)

**Files to Create**:
- `Hartonomous.Core/Application/Interfaces/IQuantizationService.cs`
- `Hartonomous.Infrastructure/Services/QuantizationService.cs`
- `Hartonomous.Infrastructure.Tests/Services/QuantizationServiceTests.cs`

**Implementation Details**:

```csharp
// IQuantizationService.cs
public interface IQuantizationService
{
    /// <summary>
    /// Quantize Shannon entropy to 21-bit integer [0, 2,097,151]
    /// </summary>
    int QuantizeEntropy(byte[] data);
    
    /// <summary>
    /// Quantize Kolmogorov complexity (compressibility) to 21-bit integer
    /// </summary>
    int QuantizeCompressibility(byte[] data);
    
    /// <summary>
    /// Quantize graph connectivity (reference count) to 21-bit integer
    /// </summary>
    int QuantizeConnectivity(long referenceCount);
    
    /// <summary>
    /// Compute Shannon entropy H(X) = -Σ p(x) log₂ p(x)
    /// Returns value in [0, 8] for byte data
    /// </summary>
    double ComputeEntropy(byte[] data);
    
    /// <summary>
    /// Compute compressibility ratio: compressed_size / original_size
    /// Returns value in [0, 1] where lower = more compressible
    /// </summary>
    double ComputeCompressibility(byte[] data);
    
    /// <summary>
    /// Dequantize back to continuous value for analysis
    /// </summary>
    double DequantizeValue(int quantizedValue, double minValue, double maxValue);
}
```

**Quantization Algorithm**:
- **Entropy**: Map [0, 8 bits] → [0, 2^21-1] using linear scaling
- **Compressibility**: Map [0, 1] → [0, 2^21-1] using linear scaling
- **Connectivity**: Map log₂(count + 1) → [0, 2^21-1] using logarithmic scaling

**Test Cases**:
- Zero entropy data (all same byte) → quantized_y = 0
- Maximum entropy data (uniform random) → quantized_y ≈ 2,097,151
- Incompressible data (already compressed) → quantized_z ≈ 2,097,151
- Highly compressible data (repeated patterns) → quantized_z ≈ 0
- Reference count: 0 → 0, 1 → ~100K, 1000 → ~1M, 1M → ~2M

**Acceptance Criteria**:
- ✅ All quantization methods return values in [0, 2,097,151]
- ✅ Entropy calculation matches Shannon formula
- ✅ Compressibility uses gzip for approximation
- ✅ Connectivity uses logarithmic scaling
- ✅ >90% test coverage
- ✅ Performance: <1ms per quantization operation

---

### Task 1.2: Refactor SpatialCoordinate to 4D (10 hours)

**Files to Modify**:
- `Hartonomous.Core/Domain/ValueObjects/SpatialCoordinate.cs`
- `Hartonomous.Core.Tests/Domain/ValueObjects/SpatialCoordinateTests.cs`
- `Hartonomous.Core.Tests/Domain/ValueObjects/SpatialCoordinateInterpolateTests.cs`

**Changes to SpatialCoordinate**:

```csharp
public sealed class SpatialCoordinate : ValueObject
{
    // PRIMARY: Hilbert index is source of truth (X dimension)
    public ulong HilbertIndex { get; private init; }
    public int Precision { get; private init; }
    
    // NEW: Quantized metadata dimensions (Y, Z, M)
    public int QuantizedY { get; private init; } // Entropy
    public int QuantizedZ { get; private init; } // Compressibility
    public int QuantizedM { get; private init; } // Connectivity
    
    // Decoded X coordinate (from HilbertIndex)
    public double X => /* existing decode logic */
    
    // Decoded Y coordinate (from QuantizedY)
    public double Y => DequantizeValue(QuantizedY, 0, 8);
    
    // Decoded Z coordinate (from QuantizedZ)
    public double Z => DequantizeValue(QuantizedZ, 0, 1);
    
    // NEW: M coordinate (from QuantizedM)
    public double M => DequantizeValue(QuantizedM, 0, 20); // log scale
    
    // NEW: Create from universal properties
    public static SpatialCoordinate FromUniversalProperties(
        Hash256 hash,
        byte[] data,
        long referenceCount,
        IQuantizationService quantization)
    {
        var hilbertIndex = MapHashToHilbert(hash);
        return new SpatialCoordinate
        {
            HilbertIndex = hilbertIndex,
            Precision = 21,
            QuantizedY = quantization.QuantizeEntropy(data),
            QuantizedZ = quantization.QuantizeCompressibility(data),
            QuantizedM = quantization.QuantizeConnectivity(referenceCount)
        };
    }
    
    // NEW: Return 4D cartesian coordinates
    public (double X, double Y, double Z, double M) ToCartesian()
    {
        return (X, Y, Z, M);
    }
    
    // UPDATED: Create PostGIS Point with M dimension
    public Point ToPoint()
    {
        var (x, y, z, m) = ToCartesian();
        return new Point(x, y, z, m) { SRID = 0 };
    }
    
    // Keep existing methods: FromHilbert, HilbertDistanceTo, Interpolate, etc.
}
```

**Test Updates**:
- Update all existing tests to handle 4D coordinates
- Add tests for Y/Z/M dimension setters
- Add tests for `FromUniversalProperties` factory
- Add tests for `ToCartesian()` returning 4-tuple
- Verify PostGIS Point creation includes M dimension

**Acceptance Criteria**:
- ✅ SpatialCoordinate stores 4 dimensions (X via Hilbert, Y/Z/M quantized)
- ✅ All existing tests pass with 4D updates
- ✅ `FromUniversalProperties` creates valid coordinates
- ✅ `ToPoint()` creates POINTZM geometry
- ✅ >85% test coverage maintained

---

### Task 1.3: Update EF Core Configurations (6 hours)

**Files to Modify**:
- `Hartonomous.Data/Configurations/ConstantConfiguration.cs`
- `Hartonomous.Data/Configurations/LandmarkConfiguration.cs`
- `Hartonomous.Data/Configurations/BPETokenConfiguration.cs` (add CompositionGeometry)

**Configuration Changes**:

```csharp
// ConstantConfiguration.cs
public void Configure(EntityTypeBuilder<Constant> builder)
{
    // ... existing configuration ...
    
    // CHANGE: geometry(PointZ) → geometry(PointZM)
    builder.Property(c => c.Location)
        .HasColumnName("location")
        .HasColumnType("geometry(PointZM)")
        .IsRequired();
    
    // NEW: Add quantized dimension columns
    builder.Property("_quantizedY")
        .HasColumnName("quantized_y")
        .HasColumnType("integer")
        .IsRequired();
    
    builder.Property("_quantizedZ")
        .HasColumnName("quantized_z")
        .HasColumnType("integer")
        .IsRequired();
    
    builder.Property("_quantizedM")
        .HasColumnName("quantized_m")
        .HasColumnType("integer")
        .IsRequired();
    
    // UPDATE: Spatial index on 4D geometry
    builder.HasIndex(c => c.Location)
        .HasMethod("gist")
        .HasDatabaseName("idx_constant_location_gist");
    
    // KEEP: B-tree index on HilbertIndex for range queries
    builder.HasIndex("Coordinate.HilbertIndex")
        .HasDatabaseName("idx_constant_hilbert_btree");
}
```

**Acceptance Criteria**:
- ✅ All geometry columns changed to POINTZM
- ✅ Quantized Y/Z/M columns added to schema
- ✅ GIST index on 4D geometry
- ✅ B-tree index on HilbertIndex retained
- ✅ Configuration builds without errors

---

### Task 1.4: Create Database Migration (8 hours)

**Migration Steps**:

1. **Generate Migration**:
```powershell
cd Hartonomous.Data
dotnet ef migrations add RefactorToPointZM --startup-project ../Hartonomous.API
```

2. **Customize Migration**:

```csharp
// 20251204_RefactorToPointZM.cs
protected override void Up(MigrationBuilder migrationBuilder)
{
    // Step 1: Add new quantized columns (nullable initially)
    migrationBuilder.AddColumn<int>(
        name: "quantized_y",
        table: "constant",
        type: "integer",
        nullable: true);
    
    migrationBuilder.AddColumn<int>(
        name: "quantized_z",
        table: "constant",
        type: "integer",
        nullable: true);
    
    migrationBuilder.AddColumn<int>(
        name: "quantized_m",
        table: "constant",
        type: "integer",
        nullable: true);
    
    // Step 2: Compute quantized values for existing data
    migrationBuilder.Sql(@"
        -- Default values for migration (will be recalculated properly later)
        UPDATE constant 
        SET 
            quantized_y = 1048576,  -- Mid-range entropy
            quantized_z = 1048576,  -- Mid-range compressibility
            quantized_m = CASE 
                WHEN reference_count = 0 THEN 0
                ELSE LEAST(2097151, FLOOR(LOG(2, reference_count + 1) * 100000))
            END
        WHERE quantized_m IS NULL;
    ");
    
    // Step 3: Make columns non-nullable
    migrationBuilder.AlterColumn<int>(
        name: "quantized_y",
        table: "constant",
        type: "integer",
        nullable: false);
    
    migrationBuilder.AlterColumn<int>(
        name: "quantized_z",
        table: "constant",
        type: "integer",
        nullable: false);
    
    migrationBuilder.AlterColumn<int>(
        name: "quantized_m",
        table: "constant",
        type: "integer",
        nullable: false);
    
    // Step 4: Convert geometry columns to POINTZM
    migrationBuilder.Sql(@"
        -- Constant table
        ALTER TABLE constant 
        ALTER COLUMN location TYPE geometry(PointZM) 
        USING ST_SetSRID(
            ST_MakePoint(
                ST_X(location), 
                ST_Y(location), 
                ST_Z(location),
                quantized_m::double precision  -- Use M from connectivity
            ), 
            ST_SRID(location)
        );
        
        -- Landmark table
        ALTER TABLE landmark 
        ALTER COLUMN location TYPE geometry(PointZM)
        USING ST_SetSRID(
            ST_MakePoint(
                ST_X(location), 
                ST_Y(location), 
                ST_Z(location),
                0  -- Landmarks get M=0 by default
            ), 
            ST_SRID(location)
        );
    ");
    
    // Step 5: Recreate GIST indexes for 4D
    migrationBuilder.Sql(@"
        DROP INDEX IF EXISTS idx_constant_location_gist;
        CREATE INDEX idx_constant_location_gist 
        ON constant USING GIST (location);
        
        DROP INDEX IF EXISTS idx_landmark_location_gist;
        CREATE INDEX idx_landmark_location_gist 
        ON landmark USING GIST (location);
    ");
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    // Rollback: Convert POINTZM back to PointZ
    migrationBuilder.Sql(@"
        ALTER TABLE constant 
        ALTER COLUMN location TYPE geometry(PointZ)
        USING ST_Force3D(location);
        
        ALTER TABLE landmark 
        ALTER COLUMN location TYPE geometry(PointZ)
        USING ST_Force3D(location);
    ");
    
    // Drop quantized columns
    migrationBuilder.DropColumn(name: "quantized_y", table: "constant");
    migrationBuilder.DropColumn(name: "quantized_z", table: "constant");
    migrationBuilder.DropColumn(name: "quantized_m", table: "constant");
    
    // Recreate 3D indexes
    migrationBuilder.Sql(@"
        DROP INDEX IF EXISTS idx_constant_location_gist;
        CREATE INDEX idx_constant_location_gist 
        ON constant USING GIST (location);
    ");
}
```

3. **Test Migration**:
```powershell
# Apply migration to test database
dotnet ef database update --startup-project ../Hartonomous.API --connection "Host=localhost;Database=hartonomous_test;Username=postgres;Password=postgres"

# Verify schema
psql -U postgres -d hartonomous_test -c "\d constant"
```

**Acceptance Criteria**:
- ✅ Migration applies successfully to empty database
- ✅ Migration applies successfully to database with existing data
- ✅ All geometry columns are POINTZM
- ✅ Quantized columns exist and have valid values
- ✅ Down migration successfully reverts to PointZ
- ✅ No data loss during up/down migrations

---

### Task 1.5: Add LINESTRINGZM Support (6 hours)

**Files to Create**:
- `Hartonomous.Core/Domain/ValueObjects/SequenceGeometry.cs`
- `Hartonomous.Core.Tests/Domain/ValueObjects/SequenceGeometryTests.cs`

**Implementation**:

```csharp
// SequenceGeometry.cs
public sealed class SequenceGeometry : ValueObject
{
    public LineString Geometry { get; private init; }
    public IReadOnlyList<Guid> ConstantIds { get; private init; }
    
    public static SequenceGeometry FromConstants(
        IEnumerable<(Guid Id, SpatialCoordinate Coordinate, int Position)> constants)
    {
        var orderedConstants = constants.OrderBy(c => c.Position).ToList();
        
        var coordinates = orderedConstants
            .Select(c =>
            {
                var (x, y, z, m) = c.Coordinate.ToCartesian();
                return new CoordinateZM(x, y, z, c.Position);
            })
            .ToArray();
        
        var lineString = new LineString(coordinates) { SRID = 0 };
        var ids = orderedConstants.Select(c => c.Id).ToList();
        
        return new SequenceGeometry
        {
            Geometry = lineString,
            ConstantIds = ids
        };
    }
    
    /// <summary>
    /// Detect gaps in Hilbert sequence (compression opportunities)
    /// </summary>
    public IEnumerable<(int StartIndex, int EndIndex, ulong GapSize)> DetectGaps(
        ulong gapThreshold = 1000)
    {
        var gaps = new List<(int, int, ulong)>();
        
        for (int i = 0; i < ConstantIds.Count - 1; i++)
        {
            var currentX = Geometry.Coordinates[i].X;
            var nextX = Geometry.Coordinates[i + 1].X;
            var gap = (ulong)(nextX - currentX);
            
            if (gap > gapThreshold)
            {
                gaps.Add((i, i + 1, gap));
            }
        }
        
        return gaps;
    }
    
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Geometry.EqualsExact(Geometry);
        foreach (var id in ConstantIds)
            yield return id;
    }
}
```

**BPEToken Configuration Update**:

```csharp
// BPETokenConfiguration.cs
public void Configure(EntityTypeBuilder<BPEToken> builder)
{
    // ... existing configuration ...
    
    // NEW: Add composition geometry (LINESTRINGZM)
    builder.Property<LineString>("_compositionGeometry")
        .HasColumnName("composition_geometry")
        .HasColumnType("geometry(LineStringZM)")
        .IsRequired(false); // Nullable for backward compatibility
    
    builder.HasIndex("_compositionGeometry")
        .HasMethod("gist")
        .HasDatabaseName("idx_bpetoken_composition_gist");
}
```

**Acceptance Criteria**:
- ✅ SequenceGeometry creates valid LINESTRINGZM
- ✅ Gap detection identifies sparse regions
- ✅ BPEToken can store composition geometry
- ✅ GIST index on LINESTRINGZM works
- ✅ >80% test coverage

---

## Integration Points

### With Phase 2 (BPE Redesign)
- `SequenceGeometry` will be used to store BPE token compositions
- Gap detection will drive vocabulary learning

### With Phase 3 (Universal Properties)
- `QuantizationService` will be injected into atomization services
- `SpatialCoordinate.FromUniversalProperties` will be used during ingestion

### With Phase 4 (Math Algorithms)
- 4D coordinates enable Voronoi/Delaunay tessellation
- LINESTRINGZM enables A* pathfinding for reconstruction

---

## Testing Strategy

### Unit Tests
- QuantizationService: 15 tests covering all quantization methods
- SpatialCoordinate: Update 28 existing tests + 10 new tests for 4D
- SequenceGeometry: 12 tests for LINESTRINGZM creation and gap detection

### Integration Tests
- End-to-end: Create Constant with POINTZM, save to DB, retrieve, verify
- Migration: Apply up/down migrations, verify data integrity

### Performance Tests
- Quantization: <1ms per operation
- 4D GIST query: <100ms for k-NN (k=10)
- Migration: <5 minutes for 1M constants

---

## Acceptance Criteria (Phase Exit)

- ✅ All unit tests passing (>80% coverage)
- ✅ Integration tests passing
- ✅ Migration tested on database with 10K+ existing constants
- ✅ Performance benchmarks documented
- ✅ Code review completed
- ✅ Documentation updated (this file)
- ✅ No critical or high-priority bugs

---

## Rollback Plan

If critical issues discovered:
1. Revert EF configurations to PointZ
2. Run down migration to revert schema
3. Revert SpatialCoordinate changes via git
4. Remove QuantizationService registration from DI

---

## Timeline

| Task | Duration | Assignee | Status |
|------|----------|----------|--------|
| 1.1 QuantizationService | 8h | Copilot | ✅ Complete |
| 1.2 SpatialCoordinate 4D | 10h | TBD | ✅ Complete |
| 1.3 EF Configurations | 6h | TBD | ✅ Complete |
| 1.4 Database Migration | 8h | TBD | ✅ Complete |
| 1.5 LINESTRINGZM Support | 6h | TBD | Not Started |
| **Total** | **38h (4.75 days)** | | |

---

**Status**: ✅ Phase 1 Core Tasks Complete (except 1.5 LINESTRINGZM)

**Last Updated**: December 4, 2025
