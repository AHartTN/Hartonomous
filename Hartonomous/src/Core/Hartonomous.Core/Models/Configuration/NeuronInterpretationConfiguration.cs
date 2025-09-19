using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hartonomous.Core.Models.Configuration;

public class NeuronInterpretationConfiguration : IEntityTypeConfiguration<NeuronInterpretation>
{
    public void Configure(EntityTypeBuilder<NeuronInterpretation> builder)
    {
        builder.HasKey(i => i.InterpretationId);

        builder.Property(i => i.FunctionalInterpretation)
            .HasMaxLength(1000);

        builder.Property(i => i.InterpretationMethod)
            .HasMaxLength(100);

        builder.Property(i => i.UserId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(i => i.DetectedConcepts)
            .HasDefaultValue("[]")
            .HasColumnType("nvarchar(max)");

        builder.Property(i => i.ActivationStatistics)
            .HasDefaultValue("{}")
            .HasColumnType("nvarchar(max)");

        builder.Property(i => i.MaximalActivationExamples)
            .HasDefaultValue("[]")
            .HasColumnType("nvarchar(max)");

        builder.HasIndex(i => i.ModelId);
        builder.HasIndex(i => i.LayerId);
        builder.HasIndex(i => i.ComponentId);
        builder.HasIndex(i => i.UserId);
        builder.HasIndex(i => i.InterpretationConfidence);

        builder.Property<string>("UserId").HasMaxLength(128);

        builder.HasOne(i => i.Model)
            .WithMany()
            .HasForeignKey(i => i.ModelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.Layer)
            .WithMany(l => l.NeuronInterpretations)
            .HasForeignKey(i => i.LayerId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(i => i.Component)
            .WithMany()
            .HasForeignKey(i => i.ComponentId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}