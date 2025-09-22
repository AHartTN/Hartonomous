# Hartonomous MCP Server

## Technical Overview

The Multi-Agent Context Protocol (MCP) server provides real-time agent coordination and workflow orchestration capabilities through SignalR WebSocket connections and REST API endpoints. The service manages agent registration, message routing, task assignment, and workflow execution across distributed AI agents.

## Features

- **Agent Registration & Discovery**: Register agents with capabilities and discover available agents
- **Real-time Communication**: SignalR-based messaging between agents
- **Workflow Orchestration**: Define and execute multi-step workflows across agents
- **Task Assignment**: Assign tasks to agents with priority and scheduling
- **User-scoped Security**: All operations are scoped to authenticated users
- **Message Persistence**: Store and retrieve agent communications
- **Status Monitoring**: Track agent health and availability

## Architecture

The MCP server follows Clean Architecture principles:

- **Controllers**: REST API endpoints for HTTP-based operations
- **Hubs**: SignalR hubs for real-time communication
- **Repositories**: Data access layer using Dapper and SQL Server
- **DTOs**: Data transfer objects for API contracts
- **Interfaces**: Abstractions for dependency injection

## API Endpoints

### Authentication

All endpoints require authentication using JWT tokens. Include the token in the Authorization header:

```
Authorization: Bearer <jwt-token>
```

### Agent Management

#### GET /api/agents
Get all agents for the authenticated user.

**Response:**
```json
[
  {
    "agentId": "guid",
    "agentName": "string",
    "agentType": "string",
    "connectionId": "string",
    "capabilities": ["string"],
    "description": "string",
    "configuration": {},
    "registeredAt": "datetime",
    "lastHeartbeat": "datetime",
    "status": 1
  }
]
```

**Status Values:**
- 0: Connecting
- 1: Online
- 2: Busy
- 3: Idle
- 4: Offline
- 5: Error

#### GET /api/agents/{agentId}
Get a specific agent by ID.

**Parameters:**
- `agentId` (guid): The agent identifier

**Response:** Agent object (same as above) or 404 if not found.

#### POST /api/agents/discover
Discover agents based on criteria.

**Request Body:**
```json
{
  "agentType": "string",
  "requiredCapabilities": ["string"]
}
```

**Response:**
```json
{
  "availableAgents": [/* array of agent objects */]
}
```

#### PUT /api/agents/{agentId}/status
Update agent status.

**Parameters:**
- `agentId` (guid): The agent identifier

**Request Body:**
```json
{
  "status": 1
}
```

**Response:** 204 No Content on success, 404 if agent not found.

#### DELETE /api/agents/{agentId}
Unregister an agent.

**Parameters:**
- `agentId` (guid): The agent identifier

**Response:** 204 No Content on success, 404 if agent not found.

### Workflow Management

#### GET /api/workflows
Get all workflows for the authenticated user.

**Response:**
```json
[
  {
    "workflowId": "guid",
    "workflowName": "string",
    "description": "string",
    "steps": [
      {
        "stepId": "guid",
        "stepName": "string",
        "agentType": "string",
        "input": {},
        "dependsOn": ["string"],
        "configuration": {}
      }
    ],
    "parameters": {}
  }
]
```

#### GET /api/workflows/{workflowId}
Get a specific workflow by ID.

**Parameters:**
- `workflowId` (guid): The workflow identifier

#### POST /api/workflows
Create a new workflow.

**Request Body:** Workflow definition object (see above structure)

**Response:**
```json
{
  "workflowId": "guid"
}
```

#### POST /api/workflows/{workflowId}/execute
Start workflow execution.

**Parameters:**
- `workflowId` (guid): The workflow identifier

**Request Body:**
```json
{
  "projectId": "guid",
  "parameters": {}
}
```

**Response:**
```json
{
  "executionId": "guid"
}
```

#### GET /api/workflows/executions/{executionId}
Get workflow execution status and details.

**Parameters:**
- `executionId` (guid): The execution identifier

**Response:**
```json
{
  "executionId": "guid",
  "workflowId": "guid",
  "projectId": "guid",
  "userId": "string",
  "status": 1,
  "startedAt": "datetime",
  "completedAt": "datetime",
  "stepExecutions": [
    {
      "stepExecutionId": "guid",
      "stepId": "guid",
      "assignedAgentId": "guid",
      "status": 1,
      "input": {},
      "output": {},
      "startedAt": "datetime",
      "completedAt": "datetime",
      "errorMessage": "string"
    }
  ],
  "errorMessage": "string"
}
```

**Execution Status Values:**
- 0: Pending
- 1: Running
- 2: Completed
- 3: Failed
- 4: Cancelled

#### GET /api/workflows/projects/{projectId}/executions
Get workflow executions for a specific project.

**Parameters:**
- `projectId` (guid): The project identifier

#### PUT /api/workflows/executions/{executionId}/status
Update workflow execution status.

**Parameters:**
- `executionId` (guid): The execution identifier

**Request Body:**
```json
{
  "status": 2,
  "errorMessage": "string"
}
```

#### DELETE /api/workflows/{workflowId}
Delete a workflow definition.

**Parameters:**
- `workflowId` (guid): The workflow identifier

### System Endpoints

#### GET /health
Health check endpoint.

**Response:**
```json
{
  "status": "Healthy",
  "timestamp": "datetime",
  "service": "MCP"
}
```

#### GET /mcp/info
Get MCP service information and capabilities.

**Response:**
```json
{
  "service": "Hartonomous Multi-Agent Context Protocol",
  "version": "1.0.0",
  "capabilities": [
    "agent-registration",
    "message-routing",
    "workflow-orchestration",
    "task-assignment",
    "real-time-communication"
  ],
  "endpoints": {
    "hub": "/mcp-hub",
    "agents": "/api/agents",
    "workflows": "/api/workflows"
  }
}
```

## SignalR Hub: /mcp-hub

The SignalR hub provides real-time communication between agents and the MCP server.

### Connection

Connect to the hub with authentication:

```javascript
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/mcp-hub", {
        accessTokenFactory: () => "<jwt-token>"
    })
    .build();
```

### Client-to-Server Methods

#### RegisterAgent
Register an agent with the MCP server.

```javascript
await connection.invoke("RegisterAgent", {
    agentName: "CodeGenerator",
    agentType: "CodeGenerator",
    capabilities: ["generate-code", "refactor-code"],
    description: "Generates and refactors code",
    configuration: { language: "csharp" }
});
```

#### SendMessage
Send a message to another agent.

```javascript
await connection.invoke("SendMessage",
    toAgentId,
    "CodeRequest",
    { request: "Generate a hello world function" },
    { priority: "high" }
);
```

#### BroadcastToAgentType
Broadcast a message to all agents of a specific type.

```javascript
await connection.invoke("BroadcastToAgentType",
    "CodeGenerator",
    "UpdateConfiguration",
    { newConfig: { timeout: 30 } }
);
```

#### Heartbeat
Send agent heartbeat with status and metrics.

```javascript
await connection.invoke("Heartbeat", 1, {
    cpu: 75.5,
    memory: 512,
    activeTasks: 3
});
```

#### SubmitTaskResult
Submit the result of a completed task.

```javascript
await connection.invoke("SubmitTaskResult",
    taskId,
    0, // Success
    { generatedCode: "console.log('Hello World');" },
    null, // No error
    { executionTime: 1500 }
);
```

#### DiscoverAgents
Request agent discovery.

```javascript
await connection.invoke("DiscoverAgents", {
    agentType: "CodeGenerator",
    requiredCapabilities: ["generate-code"]
});
```

### Server-to-Client Events

#### AgentRegistered
Fired when agent registration succeeds.

```javascript
connection.on("AgentRegistered", (data) => {
    console.log("Agent registered with ID:", data.agentId);
});
```

#### AgentJoined
Fired when another agent joins the user's scope.

```javascript
connection.on("AgentJoined", (agent) => {
    console.log("New agent joined:", agent.agentName);
});
```

#### AgentDisconnected
Fired when an agent disconnects.

```javascript
connection.on("AgentDisconnected", (data) => {
    console.log("Agent disconnected:", data.agentId);
});
```

#### AgentStatusChanged
Fired when an agent's status changes.

```javascript
connection.on("AgentStatusChanged", (data) => {
    console.log("Agent status changed:", data.agentId, data.status);
});
```

#### MessageReceived
Fired when a message is received from another agent.

```javascript
connection.on("MessageReceived", (message) => {
    console.log("Message from:", message.fromAgentId);
    console.log("Type:", message.messageType);
    console.log("Payload:", message.payload);
});
```

#### TaskCompleted
Fired when a task is completed by an agent.

```javascript
connection.on("TaskCompleted", (result) => {
    console.log("Task completed:", result.taskId);
    console.log("Status:", result.status);
});
```

#### AgentsDiscovered
Response to agent discovery request.

```javascript
connection.on("AgentsDiscovered", (response) => {
    console.log("Available agents:", response.availableAgents);
});
```

#### Error
Fired when an error occurs.

```javascript
connection.on("Error", (error) => {
    console.error("MCP Error:", error.message);
});
```

## Configuration

### Database

The MCP server requires SQL Server with the following tables:
- Agents
- McpMessages
- WorkflowDefinitions
- WorkflowExecutions
- StepExecutions
- TaskAssignments
- TaskResults

Run the schema creation script: `database/mcp-schema.sql`

### appsettings.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=HartonomousDB;Trusted_Connection=true;MultipleActiveResultSets=true;TrustServerCertificate=true;"
  },
  "MCP": {
    "MaxConcurrentConnections": 1000,
    "MessageQueueSize": 10000,
    "HeartbeatIntervalSeconds": 30,
    "AgentTimeoutMinutes": 5,
    "WorkflowTimeoutMinutes": 60,
    "EnableMetrics": true,
    "EnableDetailedLogging": true
  }
}
```

## Security

- All operations are user-scoped using JWT token claims
- Database operations include UserId filtering
- SignalR connections are authenticated
- CORS is configured for specific origins
- Input validation on all endpoints

## Error Handling

- Structured logging with correlation IDs
- Graceful error responses with appropriate HTTP status codes
- Circuit breaker patterns for external dependencies
- Retry policies for transient failures

## Monitoring

- Health check endpoints
- Application insights integration
- Custom metrics for agent activity
- Performance counters for SignalR connections

## Development

### Running the Service

```bash
cd src/Services/Hartonomous.MCP
dotnet run
```

### Running Tests

```bash
dotnet test tests/Hartonomous.MCP.Tests
```

### Building

```bash
dotnet build src/Services/Hartonomous.MCP
```

## Deployment

The MCP server can be deployed as:
- Azure App Service
- Docker container
- IIS application
- Self-hosted console application

Ensure proper configuration of:
- Connection strings
- Authentication providers
- CORS origins
- Logging providers