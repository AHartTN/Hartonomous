using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hartonomous.Core.Models.Configuration;

public class SafetyConstraintConfiguration : IEntityTypeConfiguration<SafetyConstraint>
{
    public void Configure(EntityTypeBuilder<SafetyConstraint> builder)
    {
        builder.HasKey(sc => sc.ConstraintId);

        builder.Property(sc => sc.ConstraintName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(sc => sc.ConstraintType)
            .HasMaxLength(100);

        builder.Property(sc => sc.UserId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(sc => sc.ConstraintDefinition)
            .HasDefaultValue("{}")
            .HasColumnType("nvarchar(max)");

        builder.Property(sc => sc.TriggerConditions)
            .HasDefaultValue("{}")
            .HasColumnType("nvarchar(max)");

        builder.Property(sc => sc.ConstraintActions)
            .HasDefaultValue("[]")
            .HasColumnType("nvarchar(max)");

        builder.Property(sc => sc.EnforcementStatistics)
            .HasDefaultValue("{}")
            .HasColumnType("nvarchar(max)");

        builder.HasIndex(sc => sc.ConstitutionalRuleId);
        builder.HasIndex(sc => sc.AgentId);
        builder.HasIndex(sc => sc.ModelId);
        builder.HasIndex(sc => sc.UserId);
        builder.HasIndex(sc => sc.ConstraintType);
        builder.HasIndex(sc => sc.SeverityLevel);
        builder.HasIndex(sc => sc.IsEnforced);

        builder.Property<string>("UserId").HasMaxLength(128);

        builder.HasOne(sc => sc.ConstitutionalRule)
            .WithMany(cr => cr.SafetyConstraints)
            .HasForeignKey(sc => sc.ConstitutionalRuleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(sc => sc.Agent)
            .WithMany()
            .HasForeignKey(sc => sc.AgentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(sc => sc.Model)
            .WithMany()
            .HasForeignKey(sc => sc.ModelId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}