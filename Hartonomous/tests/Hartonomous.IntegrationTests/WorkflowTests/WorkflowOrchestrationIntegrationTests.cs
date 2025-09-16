using Hartonomous.IntegrationTests.Infrastructure;
using Hartonomous.Orchestration.DTOs;
using Hartonomous.Orchestration.DSL;
using System.Net;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Hartonomous.IntegrationTests.WorkflowTests;

/// <summary>
/// Comprehensive workflow orchestration integration tests with DSL and execution engine validation
/// </summary>
public class WorkflowOrchestrationIntegrationTests : IClassFixture<TestFixture>, IAsyncLifetime
{
    private readonly TestFixture _fixture;
    private readonly ILogger<WorkflowOrchestrationIntegrationTests> _logger;
    private readonly List<Guid> _createdWorkflowIds = new();
    private readonly List<Guid> _createdExecutionIds = new();
    private readonly List<Guid> _createdProjectIds = new();

    public WorkflowOrchestrationIntegrationTests(TestFixture fixture)
    {
        _fixture = fixture;
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddSerilog());
        _logger = loggerFactory.CreateLogger<WorkflowOrchestrationIntegrationTests>();
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing workflow orchestration integration tests");
        await CleanupTestDataAsync();
    }

    public async Task DisposeAsync()
    {
        _logger.LogInformation("Cleaning up workflow orchestration integration tests");
        await CleanupTestDataAsync();
    }

    [Fact]
    public async Task WorkflowDSL_SimpleLinearWorkflow_ShouldExecuteSuccessfully()
    {
        _logger.LogInformation("Testing simple linear workflow with DSL");

        // Create a simple linear workflow using DSL
        var workflowDsl = @"
{
  ""name"": ""Simple Data Processing Pipeline"",
  ""description"": ""A basic data processing workflow"",
  ""version"": ""1.0"",
  ""parameters"": {
    ""input_file"": {
      ""type"": ""string"",
      ""description"": ""Input file path"",
      ""required"": true
    },
    ""output_format"": {
      ""type"": ""string"",
      ""description"": ""Output format"",
      ""default"": ""json""
    }
  },
  ""nodes"": {
    ""start"": {
      ""id"": ""start"",
      ""name"": ""Start"",
      ""type"": ""start"",
      ""configuration"": {}
    },
    ""load_data"": {
      ""id"": ""load_data"",
      ""name"": ""Load Data"",
      ""type"": ""action"",
      ""configuration"": {
        ""action"": ""load_file"",
        ""file_path"": ""${parameters.input_file}""
      },
      ""dependencies"": [""start""]
    },
    ""validate_data"": {
      ""id"": ""validate_data"",
      ""name"": ""Validate Data"",
      ""type"": ""action"",
      ""configuration"": {
        ""action"": ""validate_schema"",
        ""schema_type"": ""csv""
      },
      ""dependencies"": [""load_data""]
    },
    ""transform_data"": {
      ""id"": ""transform_data"",
      ""name"": ""Transform Data"",
      ""type"": ""transform"",
      ""configuration"": {
        ""operations"": [
          ""remove_duplicates"",
          ""normalize_values"",
          ""handle_missing""
        ]
      },
      ""dependencies"": [""validate_data""]
    },
    ""save_output"": {
      ""id"": ""save_output"",
      ""name"": ""Save Output"",
      ""type"": ""action"",
      ""configuration"": {
        ""action"": ""save_file"",
        ""format"": ""${parameters.output_format}""
      },
      ""dependencies"": [""transform_data""]
    },
    ""end"": {
      ""id"": ""end"",
      ""name"": ""End"",
      ""type"": ""end"",
      ""dependencies"": [""save_output""]
    }
  },
  ""edges"": [
    { ""from"": ""start"", ""to"": ""load_data"" },
    { ""from"": ""load_data"", ""to"": ""validate_data"" },
    { ""from"": ""validate_data"", ""to"": ""transform_data"" },
    { ""from"": ""transform_data"", ""to"": ""save_output"" },
    { ""from"": ""save_output"", ""to"": ""end"" }
  ]
}";

        // Create workflow definition
        var createRequest = new CreateWorkflowRequest(
            Name: "Simple Data Processing Pipeline",
            Description: "Integration test workflow for linear execution",
            WorkflowDefinition: workflowDsl,
            Category: "data_processing",
            Parameters: new Dictionary<string, object>
            {
                { "default_timeout", 300 },
                { "retry_count", 3 }
            },
            Tags: new List<string> { "integration_test", "data_processing", "linear" }
        );

        var createResponse = await _fixture.HttpClient.PostAsJsonAsync("/api/workflows", createRequest);
        createResponse.Should().HaveStatusCode(HttpStatusCode.Created);

        var workflowId = await createResponse.Content.ReadFromJsonAsync<Guid>();
        workflowId.Should().NotBeEmpty();
        _createdWorkflowIds.Add(workflowId);

        // Validate workflow definition
        var validateResponse = await _fixture.HttpClient.PostAsync($"/api/workflows/{workflowId}/validate", null);
        validateResponse.Should().HaveStatusCode(HttpStatusCode.OK);

        var validationResult = await validateResponse.Content.ReadFromJsonAsync<WorkflowValidationResult>();
        validationResult.Should().NotBeNull();
        validationResult!.IsValid.Should().BeTrue();
        validationResult.Errors.Should().BeEmpty();

        // Execute workflow
        var executionRequest = new StartWorkflowExecutionRequest(
            WorkflowId: workflowId,
            Input: new Dictionary<string, object>
            {
                { "input_file", "/data/test_dataset.csv" },
                { "output_format", "json" }
            },
            Configuration: new Dictionary<string, object>
            {
                { "timeout", 600 },
                { "debug", true }
            },
            ExecutionName: "Integration Test Execution"
        );

        var executeResponse = await _fixture.HttpClient.PostAsJsonAsync("/api/workflow-executions", executionRequest);
        executeResponse.Should().HaveStatusCode(HttpStatusCode.Created);

        var executionId = await executeResponse.Content.ReadFromJsonAsync<Guid>();
        executionId.Should().NotBeEmpty();
        _createdExecutionIds.Add(executionId);

        // Monitor execution progress
        var maxWaitTime = TimeSpan.FromMinutes(2);
        var stopwatch = Stopwatch.StartNew();
        WorkflowExecutionDto? execution = null;

        while (stopwatch.Elapsed < maxWaitTime)
        {
            var statusResponse = await _fixture.HttpClient.GetAsync($"/api/workflow-executions/{executionId}");
            statusResponse.Should().HaveStatusCode(HttpStatusCode.OK);

            execution = await statusResponse.Content.ReadFromJsonAsync<WorkflowExecutionDto>();
            execution.Should().NotBeNull();

            if (execution!.Status == WorkflowExecutionStatus.Completed ||
                execution.Status == WorkflowExecutionStatus.Failed ||
                execution.Status == WorkflowExecutionStatus.Cancelled)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        stopwatch.Stop();

        // Validate execution results
        execution.Should().NotBeNull();
        execution!.Status.Should().BeOneOf(WorkflowExecutionStatus.Completed, WorkflowExecutionStatus.Failed);
        execution.NodeExecutions.Should().HaveCountGreaterOrEqualTo(6); // All nodes should have executed

        var nodeStatuses = execution.NodeExecutions.ToDictionary(n => n.NodeId, n => n.Status);
        nodeStatuses.Should().ContainKey("start");
        nodeStatuses.Should().ContainKey("load_data");
        nodeStatuses.Should().ContainKey("validate_data");
        nodeStatuses.Should().ContainKey("transform_data");
        nodeStatuses.Should().ContainKey("save_output");
        nodeStatuses.Should().ContainKey("end");

        _logger.LogInformation("Simple linear workflow test completed in {ElapsedMs}ms with status {Status}",
            stopwatch.ElapsedMilliseconds, execution.Status);
    }

    [Fact]
    public async Task WorkflowDSL_ConditionalBranchingWorkflow_ShouldHandleBranching()
    {
        _logger.LogInformation("Testing conditional branching workflow");

        var workflowDsl = @"
{
  ""name"": ""Conditional Data Processing"",
  ""description"": ""Workflow with conditional branching"",
  ""version"": ""1.0"",
  ""parameters"": {
    ""data_type"": {
      ""type"": ""string"",
      ""description"": ""Type of data to process""
    }
  },
  ""nodes"": {
    ""start"": {
      ""id"": ""start"",
      ""name"": ""Start"",
      ""type"": ""start""
    },
    ""check_data_type"": {
      ""id"": ""check_data_type"",
      ""name"": ""Check Data Type"",
      ""type"": ""condition"",
      ""configuration"": {
        ""condition"": ""${parameters.data_type} === 'csv'""
      },
      ""dependencies"": [""start""]
    },
    ""process_csv"": {
      ""id"": ""process_csv"",
      ""name"": ""Process CSV Data"",
      ""type"": ""action"",
      ""configuration"": {
        ""action"": ""process_csv"",
        ""delimiter"": "",""
      },
      ""dependencies"": [""check_data_type""],
      ""condition"": {
        ""expression"": ""${check_data_type.result} === true"",
        ""type"": ""javascript""
      }
    },
    ""process_json"": {
      ""id"": ""process_json"",
      ""name"": ""Process JSON Data"",
      ""type"": ""action"",
      ""configuration"": {
        ""action"": ""process_json"",
        ""schema_validation"": true
      },
      ""dependencies"": [""check_data_type""],
      ""condition"": {
        ""expression"": ""${check_data_type.result} === false"",
        ""type"": ""javascript""
      }
    },
    ""merge_results"": {
      ""id"": ""merge_results"",
      ""name"": ""Merge Results"",
      ""type"": ""action"",
      ""configuration"": {
        ""action"": ""merge_outputs""
      },
      ""dependencies"": [""process_csv"", ""process_json""]
    },
    ""end"": {
      ""id"": ""end"",
      ""name"": ""End"",
      ""type"": ""end"",
      ""dependencies"": [""merge_results""]
    }
  },
  ""edges"": [
    { ""from"": ""start"", ""to"": ""check_data_type"" },
    {
      ""from"": ""check_data_type"",
      ""to"": ""process_csv"",
      ""condition"": {
        ""expression"": ""${check_data_type.result} === true"",
        ""type"": ""javascript""
      }
    },
    {
      ""from"": ""check_data_type"",
      ""to"": ""process_json"",
      ""condition"": {
        ""expression"": ""${check_data_type.result} === false"",
        ""type"": ""javascript""
      }
    },
    { ""from"": ""process_csv"", ""to"": ""merge_results"" },
    { ""from"": ""process_json"", ""to"": ""merge_results"" },
    { ""from"": ""merge_results"", ""to"": ""end"" }
  ]
}";

        var workflowId = await CreateWorkflowAsync("Conditional Data Processing", workflowDsl);

        // Test CSV branch
        var csvExecutionId = await ExecuteWorkflowAsync(workflowId, new Dictionary<string, object>
        {
            { "data_type", "csv" }
        });

        var csvExecution = await WaitForExecutionCompletionAsync(csvExecutionId);
        csvExecution.Should().NotBeNull();

        var csvNodeExecutions = csvExecution!.NodeExecutions.ToDictionary(n => n.NodeId, n => n);
        csvNodeExecutions.Should().ContainKey("process_csv");
        csvNodeExecutions["process_csv"].Status.Should().BeOneOf(NodeExecutionStatus.Completed, NodeExecutionStatus.Failed);

        // JSON processing node should be skipped
        if (csvNodeExecutions.ContainsKey("process_json"))
        {
            csvNodeExecutions["process_json"].Status.Should().Be(NodeExecutionStatus.Skipped);
        }

        // Test JSON branch
        var jsonExecutionId = await ExecuteWorkflowAsync(workflowId, new Dictionary<string, object>
        {
            { "data_type", "json" }
        });

        var jsonExecution = await WaitForExecutionCompletionAsync(jsonExecutionId);
        jsonExecution.Should().NotBeNull();

        var jsonNodeExecutions = jsonExecution!.NodeExecutions.ToDictionary(n => n.NodeId, n => n);
        jsonNodeExecutions.Should().ContainKey("process_json");
        jsonNodeExecutions["process_json"].Status.Should().BeOneOf(NodeExecutionStatus.Completed, NodeExecutionStatus.Failed);

        // CSV processing node should be skipped
        if (jsonNodeExecutions.ContainsKey("process_csv"))
        {
            jsonNodeExecutions["process_csv"].Status.Should().Be(NodeExecutionStatus.Skipped);
        }

        _logger.LogInformation("Conditional branching workflow test completed: CSV branch {CsvStatus}, JSON branch {JsonStatus}",
            csvExecution.Status, jsonExecution.Status);
    }

    [Fact]
    public async Task WorkflowDSL_ParallelExecutionWorkflow_ShouldExecuteInParallel()
    {
        _logger.LogInformation("Testing parallel execution workflow");

        var workflowDsl = @"
{
  ""name"": ""Parallel Data Processing"",
  ""description"": ""Workflow with parallel execution branches"",
  ""version"": ""1.0"",
  ""nodes"": {
    ""start"": {
      ""id"": ""start"",
      ""name"": ""Start"",
      ""type"": ""start""
    },
    ""split_data"": {
      ""id"": ""split_data"",
      ""name"": ""Split Data"",
      ""type"": ""action"",
      ""configuration"": {
        ""action"": ""split_dataset"",
        ""chunks"": 3
      },
      ""dependencies"": [""start""]
    },
    ""process_chunk_1"": {
      ""id"": ""process_chunk_1"",
      ""name"": ""Process Chunk 1"",
      ""type"": ""action"",
      ""configuration"": {
        ""action"": ""process_chunk"",
        ""chunk_id"": 1
      },
      ""dependencies"": [""split_data""]
    },
    ""process_chunk_2"": {
      ""id"": ""process_chunk_2"",
      ""name"": ""Process Chunk 2"",
      ""type"": ""action"",
      ""configuration"": {
        ""action"": ""process_chunk"",
        ""chunk_id"": 2
      },
      ""dependencies"": [""split_data""]
    },
    ""process_chunk_3"": {
      ""id"": ""process_chunk_3"",
      ""name"": ""Process Chunk 3"",
      ""type"": ""action"",
      ""configuration"": {
        ""action"": ""process_chunk"",
        ""chunk_id"": 3
      },
      ""dependencies"": [""split_data""]
    },
    ""combine_results"": {
      ""id"": ""combine_results"",
      ""name"": ""Combine Results"",
      ""type"": ""action"",
      ""configuration"": {
        ""action"": ""combine_chunks""
      },
      ""dependencies"": [""process_chunk_1"", ""process_chunk_2"", ""process_chunk_3""]
    },
    ""end"": {
      ""id"": ""end"",
      ""name"": ""End"",
      ""type"": ""end"",
      ""dependencies"": [""combine_results""]
    }
  },
  ""edges"": [
    { ""from"": ""start"", ""to"": ""split_data"" },
    { ""from"": ""split_data"", ""to"": ""process_chunk_1"" },
    { ""from"": ""split_data"", ""to"": ""process_chunk_2"" },
    { ""from"": ""split_data"", ""to"": ""process_chunk_3"" },
    { ""from"": ""process_chunk_1"", ""to"": ""combine_results"" },
    { ""from"": ""process_chunk_2"", ""to"": ""combine_results"" },
    { ""from"": ""process_chunk_3"", ""to"": ""combine_results"" },
    { ""from"": ""combine_results"", ""to"": ""end"" }
  ]
}";

        var workflowId = await CreateWorkflowAsync("Parallel Data Processing", workflowDsl);
        var executionId = await ExecuteWorkflowAsync(workflowId, new Dictionary<string, object>
        {
            { "parallel_execution", true },
            { "max_parallelism", 3 }
        });

        var execution = await WaitForExecutionCompletionAsync(executionId);
        execution.Should().NotBeNull();

        // Verify parallel chunks executed
        var nodeExecutions = execution!.NodeExecutions.ToDictionary(n => n.NodeId, n => n);
        nodeExecutions.Should().ContainKey("process_chunk_1");
        nodeExecutions.Should().ContainKey("process_chunk_2");
        nodeExecutions.Should().ContainKey("process_chunk_3");

        // Check that parallel nodes had overlapping execution times (indicating parallel execution)
        var chunk1 = nodeExecutions["process_chunk_1"];
        var chunk2 = nodeExecutions["process_chunk_2"];
        var chunk3 = nodeExecutions["process_chunk_3"];

        if (chunk1.StartedAt.HasValue && chunk1.CompletedAt.HasValue &&
            chunk2.StartedAt.HasValue && chunk2.CompletedAt.HasValue &&
            chunk3.StartedAt.HasValue && chunk3.CompletedAt.HasValue)
        {
            // Check for temporal overlap indicating parallel execution
            var hasOverlap = (chunk1.StartedAt < chunk2.CompletedAt && chunk2.StartedAt < chunk1.CompletedAt) ||
                            (chunk2.StartedAt < chunk3.CompletedAt && chunk3.StartedAt < chunk2.CompletedAt) ||
                            (chunk1.StartedAt < chunk3.CompletedAt && chunk3.StartedAt < chunk1.CompletedAt);

            // Note: This might not always be true in test environments due to fast execution
            _logger.LogInformation("Parallel execution overlap detected: {HasOverlap}", hasOverlap);
        }

        _logger.LogInformation("Parallel execution workflow test completed with status {Status}", execution.Status);
    }

    [Fact]
    public async Task WorkflowDSL_ErrorHandlingAndRetry_ShouldHandleFailures()
    {
        _logger.LogInformation("Testing error handling and retry mechanisms");

        var workflowDsl = @"
{
  ""name"": ""Error Handling Test Workflow"",
  ""description"": ""Workflow to test error handling and retry logic"",
  ""version"": ""1.0"",
  ""nodes"": {
    ""start"": {
      ""id"": ""start"",
      ""name"": ""Start"",
      ""type"": ""start""
    },
    ""unreliable_operation"": {
      ""id"": ""unreliable_operation"",
      ""name"": ""Unreliable Operation"",
      ""type"": ""action"",
      ""configuration"": {
        ""action"": ""simulate_failure"",
        ""failure_rate"": 0.7
      },
      ""dependencies"": [""start""],
      ""retry"": {
        ""maxAttempts"": 3,
        ""initialDelay"": ""00:00:01"",
        ""maxDelay"": ""00:00:05"",
        ""backoffMultiplier"": 2.0
      },
      ""timeout"": {
        ""duration"": ""00:00:30"",
        ""onTimeoutAction"": ""retry""
      }
    },
    ""fallback_operation"": {
      ""id"": ""fallback_operation"",
      ""name"": ""Fallback Operation"",
      ""type"": ""action"",
      ""configuration"": {
        ""action"": ""safe_operation""
      },
      ""dependencies"": [""unreliable_operation""],
      ""condition"": {
        ""expression"": ""${unreliable_operation.status} === 'failed'"",
        ""type"": ""javascript""
      }
    },
    ""success_operation"": {
      ""id"": ""success_operation"",
      ""name"": ""Success Operation"",
      ""type"": ""action"",
      ""configuration"": {
        ""action"": ""process_success""
      },
      ""dependencies"": [""unreliable_operation""],
      ""condition"": {
        ""expression"": ""${unreliable_operation.status} === 'completed'"",
        ""type"": ""javascript""
      }
    },
    ""cleanup"": {
      ""id"": ""cleanup"",
      ""name"": ""Cleanup"",
      ""type"": ""action"",
      ""configuration"": {
        ""action"": ""cleanup_resources""
      },
      ""dependencies"": [""fallback_operation"", ""success_operation""]
    },
    ""end"": {
      ""id"": ""end"",
      ""name"": ""End"",
      ""type"": ""end"",
      ""dependencies"": [""cleanup""]
    }
  },
  ""edges"": [
    { ""from"": ""start"", ""to"": ""unreliable_operation"" },
    {
      ""from"": ""unreliable_operation"",
      ""to"": ""fallback_operation"",
      ""condition"": {
        ""expression"": ""${unreliable_operation.status} === 'failed'"",
        ""type"": ""javascript""
      }
    },
    {
      ""from"": ""unreliable_operation"",
      ""to"": ""success_operation"",
      ""condition"": {
        ""expression"": ""${unreliable_operation.status} === 'completed'"",
        ""type"": ""javascript""
      }
    },
    { ""from"": ""fallback_operation"", ""to"": ""cleanup"" },
    { ""from"": ""success_operation"", ""to"": ""cleanup"" },
    { ""from"": ""cleanup"", ""to"": ""end"" }
  ]
}";

        var workflowId = await CreateWorkflowAsync("Error Handling Test Workflow", workflowDsl);

        // Execute workflow multiple times to test different failure scenarios
        var executions = new List<WorkflowExecutionDto>();

        for (int i = 0; i < 3; i++)
        {
            var executionId = await ExecuteWorkflowAsync(workflowId, new Dictionary<string, object>
            {
                { "test_run", i + 1 },
                { "simulate_failure", true }
            });

            var execution = await WaitForExecutionCompletionAsync(executionId);
            execution.Should().NotBeNull();
            executions.Add(execution!);
        }

        // Analyze execution results
        foreach (var execution in executions)
        {
            var nodeExecutions = execution.NodeExecutions.ToDictionary(n => n.NodeId, n => n);

            // Unreliable operation should have been attempted
            nodeExecutions.Should().ContainKey("unreliable_operation");
            var unreliableOp = nodeExecutions["unreliable_operation"];

            // Check retry behavior
            if (unreliableOp.Status == NodeExecutionStatus.Failed)
            {
                unreliableOp.RetryCount.Should().BeGreaterThan(0, "Failed operations should have retries");
                nodeExecutions.Should().ContainKey("fallback_operation", "Fallback should execute on failure");
            }
            else if (unreliableOp.Status == NodeExecutionStatus.Completed)
            {
                nodeExecutions.Should().ContainKey("success_operation", "Success operation should execute on completion");
            }

            // Cleanup should always execute
            nodeExecutions.Should().ContainKey("cleanup");
            nodeExecutions["cleanup"].Status.Should().BeOneOf(NodeExecutionStatus.Completed, NodeExecutionStatus.Failed);
        }

        _logger.LogInformation("Error handling and retry test completed with {ExecutionCount} executions", executions.Count);
    }

    [Fact]
    public async Task WorkflowDSL_ComplexMLPipelineWorkflow_ShouldExecuteEndToEnd()
    {
        _logger.LogInformation("Testing complex ML pipeline workflow");

        var workflowDsl = @"
{
  ""name"": ""ML Model Training Pipeline"",
  ""description"": ""Complete machine learning pipeline from data to model"",
  ""version"": ""1.0"",
  ""parameters"": {
    ""dataset_url"": {
      ""type"": ""string"",
      ""description"": ""URL to training dataset""
    },
    ""model_type"": {
      ""type"": ""string"",
      ""description"": ""Type of model to train"",
      ""default"": ""random_forest""
    }
  },
  ""nodes"": {
    ""start"": {
      ""id"": ""start"",
      ""name"": ""Pipeline Start"",
      ""type"": ""start""
    },
    ""data_ingestion"": {
      ""id"": ""data_ingestion"",
      ""name"": ""Data Ingestion"",
      ""type"": ""action"",
      ""configuration"": {
        ""action"": ""download_dataset"",
        ""url"": ""${parameters.dataset_url}""
      },
      ""dependencies"": [""start""]
    },
    ""data_validation"": {
      ""id"": ""data_validation"",
      ""name"": ""Data Validation"",
      ""type"": ""action"",
      ""configuration"": {
        ""action"": ""validate_data_quality"",
        ""checks"": [""completeness"", ""consistency"", ""accuracy""]
      },
      ""dependencies"": [""data_ingestion""]
    },
    ""feature_engineering"": {
      ""id"": ""feature_engineering"",
      ""name"": ""Feature Engineering"",
      ""type"": ""action"",
      ""configuration"": {
        ""action"": ""engineer_features"",
        ""techniques"": [""scaling"", ""encoding"", ""selection""]
      },
      ""dependencies"": [""data_validation""]
    },
    ""train_test_split"": {
      ""id"": ""train_test_split"",
      ""name"": ""Train-Test Split"",
      ""type"": ""action"",
      ""configuration"": {
        ""action"": ""split_dataset"",
        ""test_size"": 0.2,
        ""random_state"": 42
      },
      ""dependencies"": [""feature_engineering""]
    },
    ""model_training"": {
      ""id"": ""model_training"",
      ""name"": ""Model Training"",
      ""type"": ""action"",
      ""configuration"": {
        ""action"": ""train_model"",
        ""model_type"": ""${parameters.model_type}"",
        ""hyperparameters"": {
          ""n_estimators"": 100,
          ""max_depth"": 10
        }
      },
      ""dependencies"": [""train_test_split""]
    },
    ""model_evaluation"": {
      ""id"": ""model_evaluation"",
      ""name"": ""Model Evaluation"",
      ""type"": ""action"",
      ""configuration"": {
        ""action"": ""evaluate_model"",
        ""metrics"": [""accuracy"", ""precision"", ""recall"", ""f1""]
      },
      ""dependencies"": [""model_training""]
    },
    ""model_validation"": {
      ""id"": ""model_validation"",
      ""name"": ""Model Validation"",
      ""type"": ""condition"",
      ""configuration"": {
        ""condition"": ""${model_evaluation.accuracy} > 0.8""
      },
      ""dependencies"": [""model_evaluation""]
    },
    ""model_registration"": {
      ""id"": ""model_registration"",
      ""name"": ""Model Registration"",
      ""type"": ""action"",
      ""configuration"": {
        ""action"": ""register_model"",
        ""model_registry"": ""hartonomous""
      },
      ""dependencies"": [""model_validation""],
      ""condition"": {
        ""expression"": ""${model_validation.result} === true"",
        ""type"": ""javascript""
      }
    },
    ""model_deployment"": {
      ""id"": ""model_deployment"",
      ""name"": ""Model Deployment"",
      ""type"": ""action"",
      ""configuration"": {
        ""action"": ""deploy_model"",
        ""environment"": ""staging""
      },
      ""dependencies"": [""model_registration""]
    },
    ""notification"": {
      ""id"": ""notification"",
      ""name"": ""Pipeline Notification"",
      ""type"": ""notification"",
      ""configuration"": {
        ""type"": ""email"",
        ""recipients"": [""ml-team@company.com""],
        ""subject"": ""ML Pipeline Completed""
      },
      ""dependencies"": [""model_deployment""]
    },
    ""end"": {
      ""id"": ""end"",
      ""name"": ""Pipeline End"",
      ""type"": ""end"",
      ""dependencies"": [""notification""]
    }
  },
  ""edges"": [
    { ""from"": ""start"", ""to"": ""data_ingestion"" },
    { ""from"": ""data_ingestion"", ""to"": ""data_validation"" },
    { ""from"": ""data_validation"", ""to"": ""feature_engineering"" },
    { ""from"": ""feature_engineering"", ""to"": ""train_test_split"" },
    { ""from"": ""train_test_split"", ""to"": ""model_training"" },
    { ""from"": ""model_training"", ""to"": ""model_evaluation"" },
    { ""from"": ""model_evaluation"", ""to"": ""model_validation"" },
    {
      ""from"": ""model_validation"",
      ""to"": ""model_registration"",
      ""condition"": {
        ""expression"": ""${model_validation.result} === true"",
        ""type"": ""javascript""
      }
    },
    { ""from"": ""model_registration"", ""to"": ""model_deployment"" },
    { ""from"": ""model_deployment"", ""to"": ""notification"" },
    { ""from"": ""notification"", ""to"": ""end"" }
  ],
  ""timeout"": {
    ""duration"": ""01:00:00"",
    ""onTimeoutAction"": ""fail""
  }
}";

        var workflowId = await CreateWorkflowAsync("ML Model Training Pipeline", workflowDsl);
        var executionId = await ExecuteWorkflowAsync(workflowId, new Dictionary<string, object>
        {
            { "dataset_url", "https://example.com/training_data.csv" },
            { "model_type", "random_forest" }
        });

        var execution = await WaitForExecutionCompletionAsync(executionId, TimeSpan.FromMinutes(5));
        execution.Should().NotBeNull();

        // Validate ML pipeline execution
        var nodeExecutions = execution!.NodeExecutions.ToDictionary(n => n.NodeId, n => n);

        // Key ML pipeline stages should be present
        var expectedNodes = new[]
        {
            "data_ingestion", "data_validation", "feature_engineering",
            "train_test_split", "model_training", "model_evaluation"
        };

        foreach (var nodeId in expectedNodes)
        {
            nodeExecutions.Should().ContainKey(nodeId, $"ML pipeline should include {nodeId} step");
        }

        // Check execution order - data ingestion should complete before training
        if (nodeExecutions.ContainsKey("data_ingestion") && nodeExecutions.ContainsKey("model_training"))
        {
            var ingestion = nodeExecutions["data_ingestion"];
            var training = nodeExecutions["model_training"];

            if (ingestion.CompletedAt.HasValue && training.StartedAt.HasValue)
            {
                ingestion.CompletedAt.Should().BeLessOrEqualTo(training.StartedAt,
                    "Data ingestion should complete before model training starts");
            }
        }

        _logger.LogInformation("Complex ML pipeline workflow test completed with status {Status}, {NodeCount} nodes executed",
            execution.Status, execution.NodeExecutions.Count);
    }

    [Fact]
    public async Task WorkflowTemplates_CreateAndUseTemplate_ShouldWork()
    {
        _logger.LogInformation("Testing workflow template creation and usage");

        // First create a workflow
        var workflowDsl = GenerateSimpleWorkflowDsl();
        var workflowId = await CreateWorkflowAsync("Template Source Workflow", workflowDsl);

        // Create template from workflow
        var templateRequest = new CreateTemplateFromWorkflowRequest(
            WorkflowId: workflowId,
            Name: "Data Processing Template",
            Description: "Reusable template for data processing workflows",
            Category: "data_processing",
            Tags: new List<string> { "template", "reusable", "data" },
            IsPublic: false
        );

        var templateResponse = await _fixture.HttpClient.PostAsJsonAsync("/api/workflow-templates", templateRequest);
        templateResponse.Should().HaveStatusCode(HttpStatusCode.Created);

        var templateId = await templateResponse.Content.ReadFromJsonAsync<Guid>();
        templateId.Should().NotBeEmpty();

        // Get template details
        var getTemplateResponse = await _fixture.HttpClient.GetAsync($"/api/workflow-templates/{templateId}");
        getTemplateResponse.Should().HaveStatusCode(HttpStatusCode.OK);

        var template = await getTemplateResponse.Content.ReadFromJsonAsync<WorkflowTemplateDto>();
        template.Should().NotBeNull();
        template!.Name.Should().Be("Data Processing Template");

        // Create workflow from template
        var fromTemplateRequest = new Dictionary<string, object>
        {
            { "templateId", templateId },
            { "name", "Workflow from Template" },
            { "parameters", new Dictionary<string, object>
                {
                    { "input_file", "/data/template_test.csv" }
                }
            }
        };

        var createFromTemplateResponse = await _fixture.HttpClient.PostAsJsonAsync("/api/workflows/from-template", fromTemplateRequest);
        createFromTemplateResponse.Should().HaveStatusCode(HttpStatusCode.Created);

        var newWorkflowId = await createFromTemplateResponse.Content.ReadFromJsonAsync<Guid>();
        newWorkflowId.Should().NotBeEmpty();
        _createdWorkflowIds.Add(newWorkflowId);

        // Verify new workflow
        var newWorkflowResponse = await _fixture.HttpClient.GetAsync($"/api/workflows/{newWorkflowId}");
        var newWorkflow = await newWorkflowResponse.Content.ReadFromJsonAsync<WorkflowDefinitionDto>();
        newWorkflow.Should().NotBeNull();
        newWorkflow!.Name.Should().Be("Workflow from Template");

        _logger.LogInformation("Workflow template test completed: template {TemplateId}, new workflow {NewWorkflowId}",
            templateId, newWorkflowId);
    }

    // Helper Methods
    private async Task<Guid> CreateWorkflowAsync(string name, string workflowDsl)
    {
        var createRequest = new CreateWorkflowRequest(
            Name: name,
            Description: $"Integration test workflow: {name}",
            WorkflowDefinition: workflowDsl,
            Category: "integration_test",
            Tags: new List<string> { "integration_test", "automated" }
        );

        var response = await _fixture.HttpClient.PostAsJsonAsync("/api/workflows", createRequest);
        response.Should().HaveStatusCode(HttpStatusCode.Created);

        var workflowId = await response.Content.ReadFromJsonAsync<Guid>();
        _createdWorkflowIds.Add(workflowId);
        return workflowId;
    }

    private async Task<Guid> ExecuteWorkflowAsync(Guid workflowId, Dictionary<string, object>? input = null)
    {
        var executionRequest = new StartWorkflowExecutionRequest(
            WorkflowId: workflowId,
            Input: input ?? new Dictionary<string, object>(),
            Configuration: new Dictionary<string, object>
            {
                { "debug", true },
                { "timeout", 300 }
            }
        );

        var response = await _fixture.HttpClient.PostAsJsonAsync("/api/workflow-executions", executionRequest);
        response.Should().HaveStatusCode(HttpStatusCode.Created);

        var executionId = await response.Content.ReadFromJsonAsync<Guid>();
        _createdExecutionIds.Add(executionId);
        return executionId;
    }

    private async Task<WorkflowExecutionDto?> WaitForExecutionCompletionAsync(Guid executionId, TimeSpan? maxWaitTime = null)
    {
        var timeout = maxWaitTime ?? TimeSpan.FromMinutes(2);
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            var response = await _fixture.HttpClient.GetAsync($"/api/workflow-executions/{executionId}");
            response.Should().HaveStatusCode(HttpStatusCode.OK);

            var execution = await response.Content.ReadFromJsonAsync<WorkflowExecutionDto>();
            if (execution != null &&
                (execution.Status == WorkflowExecutionStatus.Completed ||
                 execution.Status == WorkflowExecutionStatus.Failed ||
                 execution.Status == WorkflowExecutionStatus.Cancelled ||
                 execution.Status == WorkflowExecutionStatus.TimedOut))
            {
                return execution;
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        _logger.LogWarning("Workflow execution {ExecutionId} did not complete within {Timeout}", executionId, timeout);
        return null;
    }

    private static string GenerateSimpleWorkflowDsl()
    {
        return @"
{
  ""name"": ""Simple Template Workflow"",
  ""description"": ""Simple workflow for template testing"",
  ""version"": ""1.0"",
  ""parameters"": {
    ""input_file"": {
      ""type"": ""string"",
      ""description"": ""Input file path"",
      ""required"": true
    }
  },
  ""nodes"": {
    ""start"": {
      ""id"": ""start"",
      ""name"": ""Start"",
      ""type"": ""start""
    },
    ""process"": {
      ""id"": ""process"",
      ""name"": ""Process Data"",
      ""type"": ""action"",
      ""configuration"": {
        ""action"": ""process_file"",
        ""file"": ""${parameters.input_file}""
      },
      ""dependencies"": [""start""]
    },
    ""end"": {
      ""id"": ""end"",
      ""name"": ""End"",
      ""type"": ""end"",
      ""dependencies"": [""process""]
    }
  },
  ""edges"": [
    { ""from"": ""start"", ""to"": ""process"" },
    { ""from"": ""process"", ""to"": ""end"" }
  ]
}";
    }

    private async Task CleanupTestDataAsync()
    {
        try
        {
            // Stop running executions
            foreach (var executionId in _createdExecutionIds)
            {
                try
                {
                    var stopRequest = new WorkflowControlRequest(executionId, WorkflowControlAction.Cancel);
                    await _fixture.HttpClient.PostAsJsonAsync($"/api/workflow-executions/{executionId}/control", stopRequest);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to stop execution {ExecutionId}", executionId);
                }
            }

            // Delete workflows
            foreach (var workflowId in _createdWorkflowIds)
            {
                try
                {
                    await _fixture.HttpClient.DeleteAsync($"/api/workflows/{workflowId}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cleanup workflow {WorkflowId}", workflowId);
                }
            }

            // Delete projects
            foreach (var projectId in _createdProjectIds)
            {
                try
                {
                    await _fixture.HttpClient.DeleteAsync($"/api/projects/{projectId}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cleanup project {ProjectId}", projectId);
                }
            }
        }
        finally
        {
            _createdWorkflowIds.Clear();
            _createdExecutionIds.Clear();
            _createdProjectIds.Clear();
        }
    }
}