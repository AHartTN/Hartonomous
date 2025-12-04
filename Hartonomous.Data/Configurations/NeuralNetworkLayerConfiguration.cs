using Hartonomous.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hartonomous.Data.Configurations;

/// <summary>
/// EF Core configuration for NeuralNetworkLayer entity
/// Configures MULTILINESTRINGZM geometry storage for weight matrices
/// </summary>
public class NeuralNetworkLayerConfiguration : IEntityTypeConfiguration<NeuralNetworkLayer>
{
    public void Configure(EntityTypeBuilder<NeuralNetworkLayer> builder)
    {
        builder.ToTable("neural_network_layers");
        
        // Primary key
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id)
            .HasColumnName("id")
            .IsRequired();
        
        // Foreign key to model (will be added when NeuralNetworkModel entity is created)
        builder.Property(l => l.ModelId)
            .HasColumnName("model_id")
            .IsRequired();
        
        builder.HasIndex(l => l.ModelId)
            .HasDatabaseName("ix_neural_network_layers_model_id");
        
        // Layer identification
        builder.Property(l => l.LayerIndex)
            .HasColumnName("layer_index")
            .IsRequired();
        
        builder.Property(l => l.LayerName)
            .HasColumnName("layer_name")
            .HasMaxLength(200)
            .IsRequired();
        
        builder.Property(l => l.LayerType)
            .HasColumnName("layer_type")
            .HasMaxLength(50)
            .IsRequired()
            .HasDefaultValue("Dense");
        
        // Composite index for layer lookup
        builder.HasIndex(l => new { l.ModelId, l.LayerIndex })
            .IsUnique()
            .HasDatabaseName("ix_neural_network_layers_model_index");
        
        builder.HasIndex(l => new { l.ModelId, l.LayerName })
            .HasDatabaseName("ix_neural_network_layers_model_name");
        
        // Weight geometry (MULTILINESTRINGZM)
        builder.Property(l => l.WeightGeometry)
            .HasColumnName("weight_geometry")
            .HasColumnType("geometry(MultiLineStringZM, 4326)")
            .IsRequired();
        
        // Spatial index for geometric weight analysis
        builder.HasIndex(l => l.WeightGeometry)
            .HasMethod("gist")
            .HasDatabaseName("ix_neural_network_layers_weight_gist");
        
        // Layer dimensions
        builder.Property(l => l.NeuronCount)
            .HasColumnName("neuron_count")
            .IsRequired();
        
        builder.HasIndex(l => l.NeuronCount)
            .HasDatabaseName("ix_neural_network_layers_neuron_count");
        
        builder.Property(l => l.InputDim)
            .HasColumnName("input_dim")
            .IsRequired();
        
        builder.HasIndex(l => l.InputDim)
            .HasDatabaseName("ix_neural_network_layers_input_dim");
        
        builder.Property(l => l.ParameterCount)
            .HasColumnName("parameter_count")
            .IsRequired();
        
        builder.HasIndex(l => l.ParameterCount)
            .HasDatabaseName("ix_neural_network_layers_parameter_count");
        
        // Bias geometry (LINESTRINGZM, nullable)
        builder.Property(l => l.BiasGeometry)
            .HasColumnName("bias_geometry")
            .HasColumnType("geometry(LineStringZM, 4326)")
            .IsRequired(false);
        
        // Spatial index on bias geometry
        builder.HasIndex(l => l.BiasGeometry)
            .HasMethod("gist")
            .HasDatabaseName("ix_neural_network_layers_bias_gist");
        
        // Layer configuration
        builder.Property(l => l.ActivationFunction)
            .HasColumnName("activation_function")
            .HasMaxLength(50)
            .IsRequired(false);
        
        builder.HasIndex(l => l.ActivationFunction)
            .HasDatabaseName("ix_neural_network_layers_activation");
        
        builder.Property(l => l.InitializationMethod)
            .HasColumnName("initialization_method")
            .HasMaxLength(50)
            .IsRequired(false);
        
        // Weight statistics
        builder.Property(l => l.WeightNorm)
            .HasColumnName("weight_norm")
            .HasPrecision(18, 10)
            .IsRequired();
        
        builder.HasIndex(l => l.WeightNorm)
            .HasDatabaseName("ix_neural_network_layers_weight_norm");
        
        builder.Property(l => l.IsFrozen)
            .HasColumnName("is_frozen")
            .IsRequired()
            .HasDefaultValue(false);
        
        builder.HasIndex(l => l.IsFrozen)
            .HasDatabaseName("ix_neural_network_layers_is_frozen");
        
        // Training metadata
        builder.Property(l => l.Epoch)
            .HasColumnName("epoch")
            .IsRequired(false);
        
        builder.HasIndex(l => l.Epoch)
            .HasDatabaseName("ix_neural_network_layers_epoch");
        
        // Base entity audit fields
        builder.Property(l => l.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();
        
        builder.Property(l => l.CreatedBy)
            .HasColumnName("created_by")
            .HasMaxLength(256)
            .IsRequired();
        
        builder.Property(l => l.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired(false);
        
        builder.Property(l => l.UpdatedBy)
            .HasColumnName("updated_by")
            .HasMaxLength(256)
            .IsRequired(false);
        
        builder.Property(l => l.IsDeleted)
            .HasColumnName("is_deleted")
            .IsRequired()
            .HasDefaultValue(false);
        
        builder.Property(l => l.DeletedAt)
            .HasColumnName("deleted_at")
            .IsRequired(false);
        
        builder.Property(l => l.DeletedBy)
            .HasColumnName("deleted_by")
            .HasMaxLength(256)
            .IsRequired(false);
        
        // Global query filter for soft delete
        builder.HasQueryFilter(l => !l.IsDeleted);
        
        // Composite indexes for common queries
        builder.HasIndex(l => new { l.ModelId, l.LayerType, l.IsDeleted })
            .HasDatabaseName("ix_neural_network_layers_model_type_deleted");
        
        builder.HasIndex(l => new { l.ModelId, l.Epoch, l.IsDeleted })
            .HasDatabaseName("ix_neural_network_layers_model_epoch_deleted");
        
        builder.HasIndex(l => new { l.LayerType, l.ParameterCount, l.IsDeleted })
            .HasDatabaseName("ix_neural_network_layers_type_params_deleted");
    }
}
