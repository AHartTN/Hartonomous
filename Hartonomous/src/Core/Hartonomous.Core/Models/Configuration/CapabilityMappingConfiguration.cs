using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hartonomous.Core.Models.Configuration;

public class CapabilityMappingConfiguration : IEntityTypeConfiguration<CapabilityMapping>
{
    public void Configure(EntityTypeBuilder<CapabilityMapping> builder)
    {
        builder.HasKey(cm => cm.MappingId);

        builder.Property(cm => cm.CapabilityName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(cm => cm.Description)
            .HasMaxLength(1000);

        builder.Property(cm => cm.Category)
            .HasMaxLength(100);

        builder.Property(cm => cm.MappingMethod)
            .HasMaxLength(100);

        builder.Property(cm => cm.UserId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(cm => cm.Evidence)
            .HasDefaultValue("[]")
            .HasColumnType("nvarchar(max)");

        builder.Property(cm => cm.AnalysisResults)
            .HasDefaultValue("{}")
            .HasColumnType("nvarchar(max)");

        builder.HasIndex(cm => cm.ModelId);
        builder.HasIndex(cm => cm.ComponentId);
        builder.HasIndex(cm => cm.LayerId);
        builder.HasIndex(cm => cm.UserId);
        builder.HasIndex(cm => cm.CapabilityName);
        builder.HasIndex(cm => cm.Category);
        builder.HasIndex(cm => cm.CapabilityStrength);
        builder.HasIndex(cm => cm.MappingConfidence);

        builder.Property<string>("UserId").HasMaxLength(128);

        builder.HasOne(cm => cm.Model)
            .WithMany()
            .HasForeignKey(cm => cm.ModelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(cm => cm.Component)
            .WithMany()
            .HasForeignKey(cm => cm.ComponentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(cm => cm.Layer)
            .WithMany()
            .HasForeignKey(cm => cm.LayerId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}