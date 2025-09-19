using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hartonomous.Core.Models.Configuration;

public class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.HasKey(p => p.ProjectId);

        builder.Property(p => p.ProjectName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(p => p.Description)
            .HasMaxLength(1000);

        builder.Property(p => p.Category)
            .HasMaxLength(100);

        builder.Property(p => p.UserId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(p => p.Configuration)
            .HasDefaultValue("{}")
            .HasColumnType("nvarchar(max)");

        builder.Property(p => p.Metadata)
            .HasDefaultValue("{}")
            .HasColumnType("nvarchar(max)");

        builder.Property(p => p.CollaborationSettings)
            .HasDefaultValue("{}")
            .HasColumnType("nvarchar(max)");

        builder.HasIndex(p => p.UserId);
        builder.HasIndex(p => p.ProjectName);
        builder.HasIndex(p => p.Category);
        builder.HasIndex(p => p.Status);
        builder.HasIndex(p => p.IsPublic);
        builder.HasIndex(p => new { p.UserId, p.Status });

        builder.Property<string>("UserId").HasMaxLength(128);

        builder.HasMany(p => p.ProjectModels)
            .WithOne(pm => pm.Project)
            .HasForeignKey(pm => pm.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}