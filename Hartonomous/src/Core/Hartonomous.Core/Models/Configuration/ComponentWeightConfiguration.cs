using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hartonomous.Core.Models.Configuration;

public class ComponentWeightConfiguration : IEntityTypeConfiguration<ComponentWeight>
{
    public void Configure(EntityTypeBuilder<ComponentWeight> builder)
    {
        builder.HasKey(w => w.WeightId);

        builder.Property(w => w.UserId)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(w => w.ComponentId);
        builder.HasIndex(w => w.ModelId);
        builder.HasIndex(w => w.UserId);
        builder.HasIndex(w => w.ImportanceScore);
        builder.HasIndex(w => w.IsCritical);

        builder.Property<string>("UserId").HasMaxLength(128);

        builder.HasOne(w => w.Component)
            .WithMany(c => c.Weights)
            .HasForeignKey(w => w.ComponentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(w => w.Model)
            .WithMany()
            .HasForeignKey(w => w.ModelId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}