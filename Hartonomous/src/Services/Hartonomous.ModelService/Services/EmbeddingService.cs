using Hartonomous.DataFabric.Abstractions;

namespace Hartonomous.ModelService.Services;

/// <summary>
/// Generates and stores vector embeddings for model components
/// </summary>
public class EmbeddingService
{
    private readonly IVectorService _vectorService;

    public EmbeddingService(IVectorService vectorService)
    {
        _vectorService = vectorService;
    }

    public async Task GenerateEmbeddingsAsync(List<ExtractedComponent> components)
    {
        foreach (var component in components)
        {
            var embedding = GenerateEmbedding(component);

            await _vectorService.InsertEmbeddingAsync(
                component.ComponentId,
                component.ModelId,
                embedding,
                component.Type,
                component.Description,
                component.UserId);
        }
    }

    private float[] GenerateEmbedding(ExtractedComponent component)
    {
        // Create deterministic embedding based on component characteristics
        var text = $"{component.Type} {component.Name} {component.Description} params:{component.ParameterCount}";
        var embedding = new float[1536];

        // Base hash
        var hash = text.GetHashCode();
        var random = new Random(hash);

        for (int i = 0; i < embedding.Length; i++)
        {
            embedding[i] = (float)(random.NextDouble() * 2.0 - 1.0);
        }

        // Add type-specific signature
        var typeHash = component.Type.GetHashCode();
        var typeRandom = new Random(typeHash);
        for (int i = 0; i < 100; i++)
        {
            embedding[i] += (float)(typeRandom.NextDouble() - 0.5) * 0.3f;
        }

        // Add parameter count influence
        var sizeInfluence = Math.Log10(component.ParameterCount) / 10.0f;
        for (int i = 100; i < 200; i++)
        {
            embedding[i] += sizeInfluence * 0.2f;
        }

        // Normalize
        var magnitude = Math.Sqrt(embedding.Sum(x => x * x));
        if (magnitude > 0)
        {
            for (int i = 0; i < embedding.Length; i++)
            {
                embedding[i] /= (float)magnitude;
            }
        }

        return embedding;
    }
}