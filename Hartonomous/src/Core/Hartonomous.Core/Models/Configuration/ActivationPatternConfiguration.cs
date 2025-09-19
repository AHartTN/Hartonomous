using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hartonomous.Core.Models.Configuration;

public class ActivationPatternConfiguration : IEntityTypeConfiguration<ActivationPattern>
{
    public void Configure(EntityTypeBuilder<ActivationPattern> builder)
    {
        builder.HasKey(p => p.PatternId);

        builder.Property(p => p.PatternType)
            .HasMaxLength(100);

        builder.Property(p => p.TriggerContext)
            .HasMaxLength(500);

        builder.Property(p => p.UserId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(p => p.ActivationData)
            .HasDefaultValue("{}")
            .HasColumnType("nvarchar(max)");

        builder.Property(p => p.PatternStatistics)
            .HasDefaultValue("{}")
            .HasColumnType("nvarchar(max)");

        builder.HasIndex(p => p.ModelId);
        builder.HasIndex(p => p.ComponentId);
        builder.HasIndex(p => p.AttentionHeadId);
        builder.HasIndex(p => p.UserId);
        builder.HasIndex(p => p.PatternType);
        builder.HasIndex(p => p.PatternStrength);

        builder.Property<string>("UserId").HasMaxLength(128);

        builder.HasOne(p => p.Model)
            .WithMany()
            .HasForeignKey(p => p.ModelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.Component)
            .WithMany(c => c.ActivationPatterns)
            .HasForeignKey(p => p.ComponentId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(p => p.AttentionHead)
            .WithMany(a => a.ActivationPatterns)
            .HasForeignKey(p => p.AttentionHeadId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}