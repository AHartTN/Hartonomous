/*
 * Hartonomous MCP Service Library
 *
 * This service has been converted from a web application to a service library.
 * All web hosting functionality has been moved to the API Gateway.
 * This file now only contains documentation for the service configuration.
 */

namespace Hartonomous.MCP;

/// <summary>
/// MCP Service Configuration
///
/// This service provides Multi-Agent Context Protocol functionality including:
/// - Agent registration and management
/// - Message routing between agents
/// - Workflow orchestration
/// - Task assignment
/// - Real-time communication via SignalR
///
/// Service Dependencies:
/// - AddHartonomousMcp() - Registers MCP repositories and SignalR
/// - SignalR Hub: McpHub at /mcp-hub
///
/// IMPORTANT: This service is now consumed as a library by the API Gateway.
/// All HTTP endpoints have been moved to the API Gateway controllers.
/// </summary>
public static class McpServiceInfo
{
    public const string ServiceName = "Hartonomous Multi-Agent Context Protocol";
    public const string Version = "1.0.0";

    public static readonly string[] Capabilities =
    {
        "agent-registration",
        "message-routing",
        "workflow-orchestration",
        "task-assignment",
        "real-time-communication"
    };
}