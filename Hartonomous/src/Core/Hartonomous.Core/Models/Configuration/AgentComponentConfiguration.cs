using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hartonomous.Core.Models.Configuration;

public class AgentComponentConfiguration : IEntityTypeConfiguration<AgentComponent>
{
    public void Configure(EntityTypeBuilder<AgentComponent> builder)
    {
        builder.HasKey(ac => ac.AgentComponentId);

        builder.Property(ac => ac.ComponentRole)
            .HasMaxLength(100);

        builder.Property(ac => ac.UserId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(ac => ac.TransformationMetadata)
            .HasDefaultValue("{}")
            .HasColumnType("nvarchar(max)");

        builder.Property(ac => ac.ComponentMetrics)
            .HasDefaultValue("{}")
            .HasColumnType("nvarchar(max)");

        builder.HasIndex(ac => ac.AgentId);
        builder.HasIndex(ac => ac.ModelComponentId);
        builder.HasIndex(ac => ac.ModelId);
        builder.HasIndex(ac => ac.UserId);
        builder.HasIndex(ac => ac.ComponentWeight);
        builder.HasIndex(ac => ac.IsActive);

        builder.Property<string>("UserId").HasMaxLength(128);

        builder.HasOne(ac => ac.Agent)
            .WithMany(a => a.AgentComponents)
            .HasForeignKey(ac => ac.AgentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ac => ac.ModelComponent)
            .WithMany(mc => mc.AgentComponents)
            .HasForeignKey(ac => ac.ModelComponentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ac => ac.Model)
            .WithMany()
            .HasForeignKey(ac => ac.ModelId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}