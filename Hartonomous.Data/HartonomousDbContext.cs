using Hartonomous.Core.Primitives;
using Hartonomous.Data.Converters;
using Hartonomous.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hartonomous.Data;

public class HartonomousDbContext : DbContext
{
    public DbSet<Physicality> Physicality { get; set; }
    public DbSet<Atom> Atoms { get; set; }

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
                if (property.ClrType == typeof(HartonomousId))
                {
                    property.SetValueConverter(new HartonomousIdConverter());
                }
            }
        }

        // Configure Physicality
        modelBuilder.Entity<Physicality>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id");
            entity.Property(e => e.HilbertIndex).HasColumnName("Hilbert"); // Assuming column name 'Hilbert'
            entity.Property(e => e.Centroid).HasColumnName("Centroid");
        });
        
        // Configure Atom
        modelBuilder.Entity<Atom>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne<Physicality>().WithMany().HasForeignKey(e => e.PhysicalityId);
        });
    }
}
