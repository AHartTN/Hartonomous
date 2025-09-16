using Bogus;
using Hartonomous.Core.DTOs;
using System.Text.Json;

namespace Hartonomous.IntegrationTests.Infrastructure;

/// <summary>
/// Generates realistic test data for integration tests
/// </summary>
public static class TestDataGenerator
{
    private static readonly Faker _faker = new Faker();

    public static CreateProjectRequest GenerateCreateProjectRequest()
    {
        return new CreateProjectRequest(_faker.Company.CompanyName() + " ML Project");
    }

    public static CreateModelRequest GenerateCreateModelRequest()
    {
        var modelTypes = new[] { "CNN", "RNN", "Transformer", "LSTM", "GAN", "VAE", "ResNet", "BERT" };
        var licenses = new[] { "MIT", "Apache-2.0", "GPL-3.0", "BSD-3-Clause", "CC-BY-4.0" };

        var metadata = new
        {
            author = _faker.Person.FullName,
            description = _faker.Lorem.Sentence(),
            framework = _faker.PickRandom("TensorFlow", "PyTorch", "Keras", "Scikit-learn"),
            architecture = _faker.PickRandom(modelTypes),
            parameters = _faker.Random.Int(1000000, 175000000),
            accuracy = _faker.Random.Float(0.85f, 0.99f),
            trainingTime = _faker.Random.Int(1, 168) + " hours",
            dataset = _faker.Lorem.Word() + " Dataset",
            tags = _faker.Lorem.Words(3).ToArray()
        };

        return new CreateModelRequest(
            ModelName: $"{_faker.PickRandom(modelTypes)}-{_faker.Random.Word()}",
            Version: $"{_faker.Random.Int(1, 3)}.{_faker.Random.Int(0, 9)}.{_faker.Random.Int(0, 9)}",
            License: _faker.PickRandom(licenses),
            MetadataJson: JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true })
        );
    }

    public static List<CreateProjectRequest> GenerateMultipleProjects(int count)
    {
        return Enumerable.Range(1, count)
            .Select(_ => GenerateCreateProjectRequest())
            .ToList();
    }

    public static List<CreateModelRequest> GenerateMultipleModels(int count)
    {
        return Enumerable.Range(1, count)
            .Select(_ => GenerateCreateModelRequest())
            .ToList();
    }

    public static string GenerateTestUserId() => $"test-user-{_faker.Random.Guid()}";

    public static class Scenarios
    {
        /// <summary>
        /// Generate a complete project with models for end-to-end testing
        /// </summary>
        public static ProjectWithModels GenerateCompleteProject()
        {
            var project = GenerateCreateProjectRequest();
            var models = GenerateMultipleModels(_faker.Random.Int(2, 5));

            return new ProjectWithModels(project, models);
        }

        /// <summary>
        /// Generate data for testing project management workflows
        /// </summary>
        public static ProjectManagementScenario GenerateProjectManagementScenario()
        {
            return new ProjectManagementScenario
            {
                InitialProjects = GenerateMultipleProjects(3),
                ProjectToUpdate = GenerateCreateProjectRequest(),
                ModelsToAdd = GenerateMultipleModels(2),
                ProjectsToDelete = new List<int> { 0, 2 } // Indices of projects to delete
            };
        }

        /// <summary>
        /// Generate data for testing model versioning
        /// </summary>
        public static ModelVersioningScenario GenerateModelVersioningScenario()
        {
            var baseModel = GenerateCreateModelRequest();
            var versions = new List<CreateModelRequest>();

            for (int i = 1; i <= 3; i++)
            {
                var versionedModel = new CreateModelRequest(
                    ModelName: baseModel.ModelName,
                    Version: $"1.{i}.0",
                    License: baseModel.License,
                    MetadataJson: UpdateMetadataVersion(baseModel.MetadataJson, i)
                );
                versions.Add(versionedModel);
            }

            return new ModelVersioningScenario(baseModel, versions);
        }

        private static string? UpdateMetadataVersion(string? originalJson, int versionNumber)
        {
            if (string.IsNullOrEmpty(originalJson))
                return originalJson;

            try
            {
                var metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(originalJson);
                if (metadata != null)
                {
                    metadata["version"] = versionNumber;
                    metadata["accuracy"] = _faker.Random.Float(0.85f + (versionNumber * 0.02f), 0.99f);
                    return JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
                }
            }
            catch
            {
                // Return original if parsing fails
            }

            return originalJson;
        }

        /// <summary>
        /// Generate data for performance testing
        /// </summary>
        public static PerformanceTestData GeneratePerformanceTestData(int projectCount, int modelsPerProject)
        {
            var projects = GenerateMultipleProjects(projectCount);
            var projectModels = new Dictionary<CreateProjectRequest, List<CreateModelRequest>>();

            foreach (var project in projects)
            {
                projectModels[project] = GenerateMultipleModels(modelsPerProject);
            }

            return new PerformanceTestData(projects, projectModels);
        }
    }
}

public record ProjectWithModels(CreateProjectRequest Project, List<CreateModelRequest> Models);

public class ProjectManagementScenario
{
    public List<CreateProjectRequest> InitialProjects { get; set; } = new();
    public CreateProjectRequest ProjectToUpdate { get; set; } = null!;
    public List<CreateModelRequest> ModelsToAdd { get; set; } = new();
    public List<int> ProjectsToDelete { get; set; } = new();
}

public record ModelVersioningScenario(CreateModelRequest BaseModel, List<CreateModelRequest> Versions);

public record PerformanceTestData(
    List<CreateProjectRequest> Projects,
    Dictionary<CreateProjectRequest, List<CreateModelRequest>> ProjectModels);

public record CreateModelRequest(
    string ModelName,
    string Version,
    string License,
    string? MetadataJson = null);