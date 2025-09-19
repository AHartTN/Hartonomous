using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hartonomous.Core.Models.Configuration;

public class DistilledAgentConfiguration : IEntityTypeConfiguration<DistilledAgent>
{
    public void Configure(EntityTypeBuilder<DistilledAgent> builder)
    {
        builder.HasKey(a => a.AgentId);

        builder.Property(a => a.AgentName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(a => a.Description)
            .HasMaxLength(1000);

        builder.Property(a => a.Domain)
            .HasMaxLength(100);

        builder.Property(a => a.Version)
            .HasMaxLength(50);

        builder.Property(a => a.UserId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(a => a.SourceModelIds)
            .HasDefaultValue("[]")
            .HasColumnType("nvarchar(max)");

        builder.Property(a => a.Capabilities)
            .HasDefaultValue("[]")
            .HasColumnType("nvarchar(max)");

        builder.Property(a => a.Configuration)
            .HasDefaultValue("{}")
            .HasColumnType("nvarchar(max)");

        builder.Property(a => a.DeploymentConfig)
            .HasDefaultValue("{}")
            .HasColumnType("nvarchar(max)");

        builder.Property(a => a.PerformanceMetrics)
            .HasDefaultValue("{}")
            .HasColumnType("nvarchar(max)");

        builder.HasIndex(a => a.UserId);
        builder.HasIndex(a => a.AgentName);
        builder.HasIndex(a => a.Domain);
        builder.HasIndex(a => a.Status);
        builder.HasIndex(a => new { a.UserId, a.Status });

        builder.Property<string>("UserId").HasMaxLength(128);

        builder.HasMany(a => a.AgentComponents)
            .WithOne(ac => ac.Agent)
            .HasForeignKey(ac => ac.AgentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(a => a.AgentCapabilities)
            .WithOne(ac => ac.Agent)
            .HasForeignKey(ac => ac.AgentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}