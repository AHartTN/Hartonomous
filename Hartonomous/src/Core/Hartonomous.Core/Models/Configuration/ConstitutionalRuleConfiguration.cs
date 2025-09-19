using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hartonomous.Core.Models.Configuration;

public class ConstitutionalRuleConfiguration : IEntityTypeConfiguration<ConstitutionalRule>
{
    public void Configure(EntityTypeBuilder<ConstitutionalRule> builder)
    {
        builder.HasKey(cr => cr.RuleId);

        builder.Property(cr => cr.RuleName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(cr => cr.Description)
            .HasMaxLength(1000);

        builder.Property(cr => cr.RuleCategory)
            .HasMaxLength(100);

        builder.Property(cr => cr.UserId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(cr => cr.RuleDefinition)
            .HasDefaultValue("{}")
            .HasColumnType("nvarchar(max)");

        builder.Property(cr => cr.ApplicabilityConditions)
            .HasDefaultValue("{}")
            .HasColumnType("nvarchar(max)");

        builder.Property(cr => cr.ViolationExamples)
            .HasDefaultValue("[]")
            .HasColumnType("nvarchar(max)");

        builder.Property(cr => cr.EnforcementActions)
            .HasDefaultValue("[]")
            .HasColumnType("nvarchar(max)");

        builder.HasIndex(cr => cr.UserId);
        builder.HasIndex(cr => cr.RuleName);
        builder.HasIndex(cr => cr.RuleCategory);
        builder.HasIndex(cr => cr.Priority);
        builder.HasIndex(cr => cr.IsMandatory);
        builder.HasIndex(cr => cr.IsActive);

        builder.Property<string>("UserId").HasMaxLength(128);

        builder.HasMany(cr => cr.SafetyConstraints)
            .WithOne(sc => sc.ConstitutionalRule)
            .HasForeignKey(sc => sc.ConstitutionalRuleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}