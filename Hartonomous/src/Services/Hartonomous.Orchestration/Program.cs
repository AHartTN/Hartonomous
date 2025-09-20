/*
 * Hartonomous Orchestration Service Library
 *
 * This service has been converted from a web application to a service library.
 * All web hosting functionality has been moved to the API Gateway.
 * This file now only contains documentation for the service configuration.
 */

namespace Hartonomous.Orchestration;

/// <summary>
/// Orchestration Service Configuration
///
/// This service provides workflow orchestration functionality including:
/// - Workflow definition and management
/// - DSL-based workflow parsing and validation
/// - LangGraph-based execution engine
/// - Workflow template management
/// - Execution state management
/// - Debug and monitoring capabilities
///
/// Service Dependencies:
/// - AddOrchestrationServices(configuration) - Registers all orchestration services
/// - Configuration sections: "Orchestration"
///
/// IMPORTANT: This service is now consumed as a library by the API Gateway.
/// All HTTP endpoints have been moved to the API Gateway controllers.
/// </summary>
public static class OrchestrationServiceInfo
{
    public const string ServiceName = "Hartonomous Workflow Orchestration";
    public const string Version = "1.0.0";

    public static readonly string[] Capabilities =
    {
        "dsl-workflow-definition",
        "langgraph-execution",
        "workflow-templates",
        "state-management",
        "execution-debugging",
        "workflow-validation"
    };
}