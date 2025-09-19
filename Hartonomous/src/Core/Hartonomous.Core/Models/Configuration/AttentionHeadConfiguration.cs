using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hartonomous.Core.Models.Configuration;

public class AttentionHeadConfiguration : IEntityTypeConfiguration<AttentionHead>
{
    public void Configure(EntityTypeBuilder<AttentionHead> builder)
    {
        builder.HasKey(a => a.AttentionHeadId);

        builder.Property(a => a.AttentionPatternType)
            .HasMaxLength(100);

        builder.Property(a => a.FunctionalDescription)
            .HasMaxLength(1000);

        builder.Property(a => a.UserId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(a => a.AttentionStatistics)
            .HasDefaultValue("{}")
            .HasColumnType("nvarchar(max)");

        builder.Property(a => a.ExamplePatterns)
            .HasDefaultValue("[]")
            .HasColumnType("nvarchar(max)");

        builder.HasIndex(a => a.ModelId);
        builder.HasIndex(a => a.LayerId);
        builder.HasIndex(a => a.UserId);
        builder.HasIndex(a => a.AttentionPatternType);
        builder.HasIndex(a => a.ImportanceScore);

        builder.Property<string>("UserId").HasMaxLength(128);

        builder.HasOne(a => a.Model)
            .WithMany()
            .HasForeignKey(a => a.ModelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Layer)
            .WithMany(l => l.AttentionHeads)
            .HasForeignKey(a => a.LayerId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}