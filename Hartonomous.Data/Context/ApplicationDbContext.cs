using Hartonomous.Core.Domain.Common;
using Hartonomous.Core.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Reflection;

namespace Hartonomous.Data.Context;

/// <summary>
/// Main application DbContext for EF Core 10 with PostgreSQL/PostGIS support
/// </summary>
public class ApplicationDbContext : DbContext
{
    private readonly IPublisher? _publisher;
    private IDbContextTransaction? _currentTransaction;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }
    
    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        IPublisher publisher)
        : base(options)
    {
        _publisher = publisher;
    }

    // DbSets for domain entities
    public DbSet<Constant> Constants => Set<Constant>();
    public DbSet<Landmark> Landmarks => Set<Landmark>();
    public DbSet<BPEToken> BPETokens => Set<BPEToken>();
    public DbSet<ContentIngestion> ContentIngestions => Set<ContentIngestion>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);

        // TODO: Enable compiled model for production performance (10-50% faster startup)
        // Generate with: dotnet ef dbcontext optimize
        // optionsBuilder.UseModel(CompiledModels.ApplicationDbContextModel.Instance);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all entity configurations from assembly
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // Configure PostgreSQL conventions
        modelBuilder.HasPostgresExtension("postgis");
        modelBuilder.HasPostgresExtension("uuid-ossp");
        modelBuilder.HasPostgresExtension("plpython3u");

        // Global query filter for soft delete
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .HasQueryFilter(BuildSoftDeleteFilter(entityType.ClrType));
            }
        }
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Handle audit fields
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = DateTime.UtcNow;
                    entry.Entity.CreatedBy = GetCurrentUser();
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                    entry.Entity.UpdatedBy = GetCurrentUser();
                    break;

                case EntityState.Deleted:
                    entry.State = EntityState.Modified;
                    entry.Entity.IsDeleted = true;
                    entry.Entity.DeletedAt = DateTime.UtcNow;
                    entry.Entity.DeletedBy = GetCurrentUser();
                    break;
            }
        }

        // Dispatch domain events before saving
        await DispatchDomainEventsAsync(cancellationToken);

        return await base.SaveChangesAsync(cancellationToken);
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        _currentTransaction ??= await Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await SaveChangesAsync(cancellationToken);
            if (_currentTransaction != null)
            {
                await _currentTransaction.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            await RollbackTransactionAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (_currentTransaction != null)
            {
                _currentTransaction.Dispose();
                _currentTransaction = null;
            }
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_currentTransaction != null)
            {
                await _currentTransaction.RollbackAsync(cancellationToken);
            }
        }
        finally
        {
            if (_currentTransaction != null)
            {
                _currentTransaction.Dispose();
                _currentTransaction = null;
            }
        }
    }

    private static string GetCurrentUser()
    {
        // TODO: Implement user context service
        return "System";
    }

    private static System.Linq.Expressions.LambdaExpression BuildSoftDeleteFilter(Type entityType)
    {
        var parameter = System.Linq.Expressions.Expression.Parameter(entityType, "e");
        var property = System.Linq.Expressions.Expression.Property(parameter, nameof(BaseEntity.IsDeleted));
        var filterExpression = System.Linq.Expressions.Expression.Equal(property, System.Linq.Expressions.Expression.Constant(false));
        return System.Linq.Expressions.Expression.Lambda(filterExpression, parameter);
    }

    private async Task DispatchDomainEventsAsync(CancellationToken cancellationToken = default)
    {
        if (_publisher == null)
        {
            return;
        }

        // Collect all domain events from tracked entities
        var domainEntities = ChangeTracker
            .Entries<BaseEntity>()
            .Where(x => x.Entity.DomainEvents.Any())
            .Select(x => x.Entity)
            .ToList();

        var domainEvents = domainEntities
            .SelectMany(x => x.DomainEvents)
            .ToList();

        // Clear events from entities
        domainEntities.ForEach(entity => entity.ClearDomainEvents());

        // Publish all events
        foreach (var domainEvent in domainEvents)
        {
            await _publisher.Publish(domainEvent, cancellationToken);
        }
    }

    /// <summary>
    /// Execute raw SQL for database-specific optimizations (partitioning, triggers, functions)
    /// </summary>
    public async Task ExecuteRawSqlAsync(string sql, CancellationToken cancellationToken = default)
    {
        await Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }

    /// <summary>
    /// Create PostgreSQL table partitioning by Hilbert High index range.
    /// Improves query performance 10-100x for spatial proximity searches.
    /// Partitions based on the upper 42 bits of the Hilbert index.
    /// </summary>
    public async Task CreateTablePartitioningAsync(CancellationToken cancellationToken = default)
    {
        var sql = @"
            -- Convert constants table to partitioned table
            ALTER TABLE constants RENAME TO constants_old;
            
            -- Create partitioned table with new schema
            CREATE TABLE constants (
                LIKE constants_old INCLUDING ALL
            ) PARTITION BY RANGE (hilbert_high);
            
            -- Create partitions for Hilbert curve ranges (2^42 total space in High, 64 partitions)
            DO $$
            DECLARE
                -- Total range of ulong (High) is 0 to 2^64 - 1? No, High is 42 bits?
                -- Wait, SpatialCoordinate.HilbertHigh is ulong (64-bit type) but effectively uses 42 bits.
                -- Range is 0 to 2^42 - 1 = 4,398,046,511,103.
                -- We want 64 partitions.
                -- Partition size = 2^42 / 64 = 2^36 = 68,719,476,736.
                
                partition_size BIGINT := 68719476736; 
                start_val BIGINT := 0;
                end_val BIGINT;
                partition_name TEXT;
            BEGIN
                FOR i IN 0..63 LOOP
                    end_val := start_val + partition_size;
                    partition_name := 'constants_p' || LPAD(i::TEXT, 2, '0');
                    
                    -- Check if end_val exceeds max (just in case of rounding), though exact power of 2 is safe.
                    
                    EXECUTE format(
                        'CREATE TABLE %I PARTITION OF constants FOR VALUES FROM (%L) TO (%L)',
                        partition_name, start_val, end_val
                    );
                    
                    start_val := end_val;
                END LOOP;
                
                -- Catch-all partition for anything outside (should not happen if logic is correct)
                CREATE TABLE constants_p_default PARTITION OF constants DEFAULT;
            END $$;
            
            -- Copy data from old table
            INSERT INTO constants SELECT * FROM constants_old;
            
            -- Drop old table
            DROP TABLE constants_old;
        ";

        await ExecuteRawSqlAsync(sql, cancellationToken);
    }

    /// <summary>
    /// Create materialized view for hot atoms (frequently accessed constants)
    /// Refreshed every 5 minutes via background job
    /// </summary>
    public async Task CreateMaterializedViewForHotAtomsAsync(CancellationToken cancellationToken = default)
    {
        var sql = @"
            CREATE MATERIALIZED VIEW IF NOT EXISTS hot_atoms AS
            SELECT 
                id,
                hash,
                data,
                size,
                content_type,
                hilbert_high,
                hilbert_low,
                hilbert_precision,
                quantized_entropy,
                quantized_compressibility,
                quantized_connectivity,
                location,
                reference_count,
                frequency,
                last_accessed_at
            FROM constants
            WHERE 
                is_deleted = false
                AND status = 'Active'
                AND (
                    frequency >= 10 
                    OR reference_count >= 5
                    OR last_accessed_at >= NOW() - INTERVAL '1 hour'
                )
            ORDER BY frequency DESC, last_accessed_at DESC
            LIMIT 10000;
            
            CREATE UNIQUE INDEX IF NOT EXISTS idx_hot_atoms_id ON hot_atoms(id);
            CREATE INDEX IF NOT EXISTS idx_hot_atoms_hilbert ON hot_atoms USING btree(hilbert_high, hilbert_low);
            CREATE INDEX IF NOT EXISTS idx_hot_atoms_location ON hot_atoms USING gist(location);
        ";

        await ExecuteRawSqlAsync(sql, cancellationToken);
    }

    /// <summary>
    /// Create trigger for automatic reference count management
    /// Updates reference_count when constant_tokens join table changes
    /// </summary>
    public async Task CreateReferenceCountTriggerAsync(CancellationToken cancellationToken = default)
    {
        var sql = @"
            CREATE OR REPLACE FUNCTION update_constant_reference_count()
            RETURNS TRIGGER AS $$
            BEGIN
                IF TG_OP = 'INSERT' THEN
                    UPDATE constants 
                    SET reference_count = reference_count + 1
                    WHERE id = NEW.constants_id;
                ELSIF TG_OP = 'DELETE' THEN
                    UPDATE constants 
                    SET reference_count = GREATEST(0, reference_count - 1)
                    WHERE id = OLD.constants_id;
                END IF;
                RETURN NULL;
            END;
            $$ LANGUAGE plpgsql;
            
            DROP TRIGGER IF EXISTS trg_update_constant_reference_count ON constant_tokens;
            CREATE TRIGGER trg_update_constant_reference_count
            AFTER INSERT OR DELETE ON constant_tokens
            FOR EACH ROW
            EXECUTE FUNCTION update_constant_reference_count();
        ";

        await ExecuteRawSqlAsync(sql, cancellationToken);
    }

    /// <summary>
    /// Refresh materialized view for hot atoms
    /// Call this from background worker every 5 minutes
    /// </summary>
    public async Task RefreshHotAtomsMaterializedViewAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteRawSqlAsync("REFRESH MATERIALIZED VIEW CONCURRENTLY hot_atoms;", cancellationToken);
    }
}
