using Hartonomous.Core.Primitives;
using Hartonomous.Data.Converters;
using Hartonomous.Data.Entities;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Hartonomous.Data;

public class HartonomousDbContext : DbContext
{
    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<Content> Content { get; set; }
    public DbSet<Physicality> Physicality { get; set; }
    public DbSet<Atom> Atoms { get; set; }
    public DbSet<Composition> Compositions { get; set; }
    public DbSet<CompositionSequence> CompositionSequences { get; set; }
    public DbSet<Relation> Relations { get; set; }
    public DbSet<RelationSequence> RelationSequences { get; set; }
    public DbSet<RelationRating> RelationRatings { get; set; }
    public DbSet<RelationEvidence> RelationEvidence { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }

    public HartonomousDbContext(DbContextOptions<HartonomousDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure HartonomousId globally
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(HartonomousId) || property.ClrType == typeof(HartonomousId?))
                {
                    property.SetValueConverter(new HartonomousIdConverter());
                }
            }
        }

        // Configure Hilbert Index (UInt128) to map to NUMERIC(39,0)
        modelBuilder.Entity<Physicality>()
            .Property(p => p.Hilbert)
            .HasColumnType("numeric(39,0)");

        // Table Mappings to match PostgresExtension schema
        modelBuilder.Entity<Tenant>().ToTable("tenant", "hartonomous");
        modelBuilder.Entity<User>().ToTable("user", "hartonomous");
        modelBuilder.Entity<Content>().ToTable("content", "hartonomous");
        modelBuilder.Entity<Physicality>().ToTable("physicality", "hartonomous");
        modelBuilder.Entity<Atom>().ToTable("atom", "hartonomous");
        modelBuilder.Entity<Composition>().ToTable("composition", "hartonomous");
        modelBuilder.Entity<CompositionSequence>().ToTable("composition_sequence", "hartonomous");
        modelBuilder.Entity<Relation>().ToTable("relation", "hartonomous");
        modelBuilder.Entity<RelationSequence>().ToTable("relation_sequence", "hartonomous");
        modelBuilder.Entity<RelationRating>().ToTable("relation_rating", "hartonomous").HasKey(r => r.RelationId);
        modelBuilder.Entity<RelationEvidence>().ToTable("relation_evidence", "hartonomous");
        modelBuilder.Entity<AuditLog>().ToTable("audit_log", "hartonomous");

        // Force POINTZM for Physicality.Centroid
        modelBuilder.Entity<Physicality>(entity =>
        {
            entity.Property(e => e.Centroid)
                .HasColumnType("geometry(POINTZM, 0)");
            
            entity.Property(e => e.Trajectory)
                .HasColumnType("geometry(GEOMETRYZM, 0)");

            entity.Property(e => e.Id).HasColumnName("Id");
            entity.Property(e => e.Hilbert).HasColumnName("Hilbert");
        });

        // Configure JSONB for AuditLog.ActionDetails
        modelBuilder.Entity<AuditLog>()
            .Property(e => e.ActionDetails)
            .HasColumnType("jsonb");
    }
}