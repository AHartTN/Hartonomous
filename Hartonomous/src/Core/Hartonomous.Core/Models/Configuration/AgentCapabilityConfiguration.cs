using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hartonomous.Core.Models.Configuration;

public class AgentCapabilityConfiguration : IEntityTypeConfiguration<AgentCapability>
{
    public void Configure(EntityTypeBuilder<AgentCapability> builder)
    {
        builder.HasKey(ac => ac.CapabilityId);

        builder.Property(ac => ac.CapabilityName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(ac => ac.Description)
            .HasMaxLength(1000);

        builder.Property(ac => ac.Category)
            .HasMaxLength(100);

        builder.Property(ac => ac.UserId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(ac => ac.Evidence)
            .HasDefaultValue("[]")
            .HasColumnType("nvarchar(max)");

        builder.Property(ac => ac.BenchmarkResults)
            .HasDefaultValue("{}")
            .HasColumnType("nvarchar(max)");

        builder.Property(ac => ac.UsageStatistics)
            .HasDefaultValue("{}")
            .HasColumnType("nvarchar(max)");

        builder.HasIndex(ac => ac.AgentId);
        builder.HasIndex(ac => ac.UserId);
        builder.HasIndex(ac => ac.CapabilityName);
        builder.HasIndex(ac => ac.Category);
        builder.HasIndex(ac => ac.ProficiencyScore);
        builder.HasIndex(ac => ac.IsEnabled);

        builder.Property<string>("UserId").HasMaxLength(128);

        builder.HasOne(ac => ac.Agent)
            .WithMany(a => a.AgentCapabilities)
            .HasForeignKey(ac => ac.AgentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}