using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hartonomous.Data.Configurations;

/// <summary>
/// EF Core configuration for BPEToken entity
/// </summary>
public class BPETokenConfiguration : IEntityTypeConfiguration<BPEToken>
{
    public void Configure(EntityTypeBuilder<BPEToken> builder)
    {
        builder.ToTable("bpe_tokens");

        // Primary key
        builder.HasKey(t => t.Id);

        // Token ID
        builder.Property(t => t.TokenId)
            .HasColumnName("token_id")
            .IsRequired();

        // Create unique index on token ID
        builder.HasIndex(t => t.TokenId)
            .IsUnique()
            .HasDatabaseName("ix_bpe_tokens_token_id");

        // Hash256 value object
        builder.Property(t => t.Hash)
            .HasColumnName("hash")
            .HasColumnType("bytea")
            .IsRequired()
            .HasConversion(
                hash => hash.Bytes,
                bytes => Hash256.FromBytes(bytes));

        builder.HasIndex(t => t.Hash)
            .IsUnique()
            .HasDatabaseName("ix_bpe_tokens_hash");

        // Constant sequence - store as array of GUIDs
        builder.Property(t => t.ConstantSequence)
            .HasColumnName("constant_sequence")
            .HasColumnType("uuid[]")
            .IsRequired();
        
        // NEW: Composition geometry (LINESTRINGZM)
        builder.Property(t => t.CompositionGeometry)
            .HasColumnName("composition_geometry")
            .HasColumnType("geometry(LineStringZM, 4326)")
            .IsRequired(false); // Nullable for backward compatibility
        
        // Spatial index for geometric queries
        builder.HasIndex(t => t.CompositionGeometry)
            .HasMethod("gist")
            .HasDatabaseName("ix_bpe_tokens_composition_gist")
            .HasFilter("composition_geometry IS NOT NULL"); // Partial index
        
        // NEW: Path length
        builder.Property(t => t.PathLength)
            .HasColumnName("path_length")
            .HasPrecision(18, 6)
            .IsRequired(false);
        
        builder.HasIndex(t => t.PathLength)
            .HasDatabaseName("ix_bpe_tokens_path_length")
            .HasFilter("path_length IS NOT NULL"); // Partial index

        builder.Property(t => t.SequenceLength)
            .HasColumnName("sequence_length")
            .IsRequired();

        builder.HasIndex(t => t.SequenceLength)
            .HasDatabaseName("ix_bpe_tokens_sequence_length");

        builder.Property(t => t.MergeLevel)
            .HasColumnName("merge_level")
            .IsRequired();

        builder.HasIndex(t => t.MergeLevel)
            .HasDatabaseName("ix_bpe_tokens_merge_level");

        // Usage statistics
        builder.Property(t => t.Frequency)
            .HasColumnName("frequency")
            .IsRequired();

        builder.HasIndex(t => t.Frequency)
            .HasDatabaseName("ix_bpe_tokens_frequency");

        builder.Property(t => t.VocabularyRank)
            .HasColumnName("vocabulary_rank")
            .IsRequired();

        builder.HasIndex(t => t.VocabularyRank)
            .HasDatabaseName("ix_bpe_tokens_vocabulary_rank");

        builder.Property(t => t.LastUsedAt)
            .HasColumnName("last_used_at")
            .IsRequired();

        builder.HasIndex(t => t.LastUsedAt)
            .HasDatabaseName("ix_bpe_tokens_last_used");

        builder.Property(t => t.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.HasIndex(t => t.IsActive)
            .HasDatabaseName("ix_bpe_tokens_is_active");

        // Relationships - many-to-many with Constants
        builder.HasMany(t => t.Constants)
            .WithMany(c => c.ComposingTokens)
            .UsingEntity(j => j.ToTable("constant_tokens"));

        // Audit fields from BaseEntity
        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(t => t.CreatedBy)
            .HasColumnName("created_by")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(t => t.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired(false);

        builder.Property(t => t.UpdatedBy)
            .HasColumnName("updated_by")
            .HasMaxLength(256)
            .IsRequired(false);

        builder.Property(t => t.IsDeleted)
            .HasColumnName("is_deleted")
            .IsRequired();

        builder.Property(t => t.DeletedAt)
            .HasColumnName("deleted_at")
            .IsRequired(false);

        builder.Property(t => t.DeletedBy)
            .HasColumnName("deleted_by")
            .HasMaxLength(256)
            .IsRequired(false);

        builder.HasIndex(t => t.IsDeleted)
            .HasDatabaseName("ix_bpe_tokens_is_deleted");
        
        // Composite indexes for common queries
        builder.HasIndex(t => new { t.IsActive, t.Frequency, t.IsDeleted })
            .HasDatabaseName("ix_bpe_tokens_active_frequency_deleted");
        
        builder.HasIndex(t => new { t.MergeLevel, t.SequenceLength, t.IsDeleted })
            .HasDatabaseName("ix_bpe_tokens_merge_sequence_deleted");
    }
}
