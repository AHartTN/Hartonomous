# Hartonomous Enterprise Deployment Endpoints

## ?? **PRIMARY APPLICATION ENDPOINTS**

### **API Gateway / Main API**
- **Production**: `https://api.hartonomous.com`
- **Staging**: `https://api-staging.hartonomous.com`
- **Development**: `https://api-dev.hartonomous.com`

### **Web Application (Blazor)**
- **Production**: `https://app.hartonomous.com`
- **Staging**: `https://app-staging.hartonomous.com`
- **Development**: `https://app-dev.hartonomous.com`

### **Worker Service** (Background Processing)
- Internal service - no public endpoint
- Health Check: `https://worker.hartonomous.internal/health`

---

## ?? **AUTHENTICATION & AUTHORIZATION ENDPOINTS**

### **Identity Server / Auth Provider**
- **Authorization Endpoint**: `https://auth.hartonomous.com/authorize`
- **Token Endpoint**: `https://auth.hartonomous.com/token`
- **UserInfo Endpoint**: `https://auth.hartonomous.com/userinfo`
- **JWKS Endpoint**: `https://auth.hartonomous.com/.well-known/jwks.json`
- **OpenID Configuration**: `https://auth.hartonomous.com/.well-known/openid-configuration`
- **Logout**: `https://auth.hartonomous.com/logout`
- **Token Revocation**: `https://auth.hartonomous.com/revoke`

### **Microsoft Entra ID (Azure AD) Integration**
- **Tenant**: `https://login.microsoftonline.com/{tenant-id}`
- **Authority**: `https://login.microsoftonline.com/{tenant-id}/v2.0`
- **Redirect URI**: `https://app.hartonomous.com/signin-oidc`
- **Post Logout Redirect**: `https://app.hartonomous.com/signout-callback-oidc`

---

## ?? **AI & MACHINE LEARNING ENDPOINTS**

### **MLOps / Model Serving**
- **Model Inference API**: `https://ml.hartonomous.com/v1/models/{model-name}/infer`
- **Batch Prediction**: `https://ml.hartonomous.com/v1/batch/predict`
- **Model Registry**: `https://ml.hartonomous.com/v1/models`
- **Model Metadata**: `https://ml.hartonomous.com/v1/models/{model-name}/metadata`
- **Model Health**: `https://ml.hartonomous.com/health`

### **Azure OpenAI / LLM Integration**
- **Chat Completions**: `https://ai.hartonomous.com/v1/chat/completions`
- **Embeddings**: `https://ai.hartonomous.com/v1/embeddings`
- **Fine-tuning**: `https://ai.hartonomous.com/v1/fine-tuning/jobs`
- **Assistants API**: `https://ai.hartonomous.com/v1/assistants`

### **Azure AI Services**
- **Cognitive Search**: `https://search.hartonomous.com/indexes/{index-name}/docs/search`
- **Computer Vision**: `https://vision.hartonomous.com/analyze`
- **Speech Services**: `https://speech.hartonomous.com/v1/recognize`
- **Content Safety**: `https://safety.hartonomous.com/text:analyze`

---

## ?? **MCP (Model Context Protocol) SERVER ENDPOINTS**

### **MCP Gateway**
- **Main Endpoint**: `https://mcp.hartonomous.com`
- **WebSocket**: `wss://mcp.hartonomous.com/ws`
- **SSE (Server-Sent Events)**: `https://mcp.hartonomous.com/sse`

### **MCP Tools**
- **List Tools**: `POST https://mcp.hartonomous.com/tools/list`
- **Execute Tool**: `POST https://mcp.hartonomous.com/tools/execute`
- **Tool Schema**: `GET https://mcp.hartonomous.com/tools/{tool-name}/schema`

### **MCP Resources**
- **List Resources**: `POST https://mcp.hartonomous.com/resources/list`
- **Read Resource**: `POST https://mcp.hartonomous.com/resources/read`
- **Subscribe to Resource**: `POST https://mcp.hartonomous.com/resources/subscribe`

### **MCP Prompts**
- **List Prompts**: `POST https://mcp.hartonomous.com/prompts/list`
- **Get Prompt**: `POST https://mcp.hartonomous.com/prompts/get`

---

## ?? **OBSERVABILITY & MONITORING ENDPOINTS**

### **Application Insights / Telemetry**
- **OTLP Ingest**: `https://telemetry.hartonomous.com/v1/traces`
- **Metrics**: `https://telemetry.hartonomous.com/v1/metrics`
- **Logs**: `https://telemetry.hartonomous.com/v1/logs`

### **Prometheus / Metrics**
- **Scrape Endpoint**: `https://metrics.hartonomous.com/metrics`
- **API Metrics**: `https://api.hartonomous.com/metrics`
- **Worker Metrics**: `https://worker.hartonomous.internal/metrics`

### **Grafana Dashboards**
- **Main Dashboard**: `https://dashboards.hartonomous.com`
- **API Endpoint**: `https://dashboards.hartonomous.com/api/datasources`

### **Health Checks**
- **API Health**: `https://api.hartonomous.com/health`
- **Liveness**: `https://api.hartonomous.com/health/live`
- **Readiness**: `https://api.hartonomous.com/health/ready`
- **Startup**: `https://api.hartonomous.com/health/startup`

---

## ??? **DATA & STORAGE ENDPOINTS**

### **PostgreSQL with PostGIS**
- **Connection String**: `postgresql://user:pass@postgres.hartonomous.internal:5432/hartonomous`
- **Read Replica**: `postgresql://user:pass@postgres-ro.hartonomous.internal:5432/hartonomous`

### **Redis Cache**
- **Primary**: `redis.hartonomous.internal:6379`
- **Sentinel**: `redis-sentinel.hartonomous.internal:26379`

### **Azure Blob Storage**
- **Blob Service**: `https://hartonomous.blob.core.windows.net`
- **File Share**: `https://hartonomous.file.core.windows.net`

### **Azure Cosmos DB**
- **Endpoint**: `https://hartonomous.documents.azure.com:443/`
- **SQL API**: `https://hartonomous.documents.azure.com/dbs/{db}/colls/{collection}/docs`

---

## ?? **SECURITY & SECRETS ENDPOINTS**

### **Azure Key Vault**
- **Vault URI**: `https://hartonomous-kv.vault.azure.net/`
- **Secrets**: `https://hartonomous-kv.vault.azure.net/secrets/{secret-name}`
- **Keys**: `https://hartonomous-kv.vault.azure.net/keys/{key-name}`
- **Certificates**: `https://hartonomous-kv.vault.azure.net/certificates/{cert-name}`

### **Managed Identity**
- **IMDS Endpoint**: `http://169.254.169.254/metadata/identity/oauth2/token`

---

## ?? **CI/CD & DEPLOYMENT ENDPOINTS**

### **Azure DevOps**
- **Organization**: `https://dev.azure.com/aharttn`
- **Project**: `https://dev.azure.com/aharttn/Hartonomous`
- **Pipeline**: `https://dev.azure.com/aharttn/Hartonomous/_build`
- **Artifacts**: `https://pkgs.dev.azure.com/aharttn/Hartonomous/_packaging/hartonomous-feed/nuget/v3/index.json`

### **GitHub Container Registry (GHCR)**
- **Registry**: `ghcr.io/aharttn`
- **API Image**: `ghcr.io/aharttn/hartonomous-api:latest`
- **Worker Image**: `ghcr.io/aharttn/hartonomous-worker:latest`
- **Web Image**: `ghcr.io/aharttn/hartonomous-web:latest`

### **Docker Registry**
- **Login**: `docker login ghcr.io`
- **Pull**: `docker pull ghcr.io/aharttn/hartonomous-api:v1.0.0`

---

## ?? **API SPECIFIC ENDPOINTS**

### **RESTful API Routes**
```
GET    /api/v1/health                    # Health check
GET    /api/v1/ready                     # Readiness probe
GET    /api/v1/atoms                     # List atoms
GET    /api/v1/atoms/{id}                # Get atom by ID
POST   /api/v1/atoms                     # Create atom
PUT    /api/v1/atoms/{id}                # Update atom
DELETE /api/v1/atoms/{id}                # Delete atom
GET    /api/v1/atoms/{id}/spatial        # Spatial queries
POST   /api/v1/search                    # Full-text search
GET    /api/v1/metrics                   # Prometheus metrics
GET    /swagger                          # OpenAPI documentation
```

### **GraphQL Endpoint**
- **Query/Mutation**: `POST https://api.hartonomous.com/graphql`
- **Subscriptions**: `wss://api.hartonomous.com/graphql`
- **Playground**: `https://api.hartonomous.com/graphql/playground`

### **gRPC Services**
- **gRPC Endpoint**: `grpc://api.hartonomous.com:443`
- **Health Check**: `grpc://api.hartonomous.com:443/grpc.health.v1.Health/Check`

---

## ?? **CDN & STATIC ASSETS**
- **CDN Origin**: `https://cdn.hartonomous.com`
- **Static Assets**: `https://cdn.hartonomous.com/assets/{path}`
- **Images**: `https://cdn.hartonomous.com/images/{image}`

---

## ?? **DOCUMENTATION ENDPOINTS**
- **API Docs**: `https://docs.hartonomous.com/api`
- **OpenAPI Spec**: `https://api.hartonomous.com/swagger/v1/swagger.json`
- **Postman Collection**: `https://docs.hartonomous.com/postman/collection.json`

---

## ?? **WEBHOOK & NOTIFICATION ENDPOINTS**
- **Webhook Receiver**: `POST https://api.hartonomous.com/webhooks/{provider}`
- **Event Stream**: `https://api.hartonomous.com/events`
- **SignalR Hub**: `https://api.hartonomous.com/hubs/notifications`

---

## ?? **CONTAINER & KUBERNETES ENDPOINTS**

### **Azure Container Registry (ACR)**
- **Registry**: `hartonomous.azurecr.io`
- **Login**: `az acr login --name hartonomous`

### **Kubernetes Ingress**
- **Ingress Controller**: `https://k8s.hartonomous.com`
- **API Service**: `http://hartonomous-api.default.svc.cluster.local`
- **Worker Service**: `http://hartonomous-worker.default.svc.cluster.local`

---

## ?? **TESTING & SANDBOX ENDPOINTS**
- **Sandbox API**: `https://sandbox.hartonomous.com/api`
- **Mock Server**: `https://mock.hartonomous.com`
- **Test Data Generator**: `https://testdata.hartonomous.com`

---

## ?? **ENVIRONMENT-SPECIFIC CONFIGURATIONS**

### **Development**
```json
{
  "ApiBaseUrl": "https://localhost:7001",
  "AuthAuthority": "https://localhost:5001",
  "MLEndpoint": "http://localhost:8000",
  "MCPEndpoint": "ws://localhost:3000"
}
```

### **Staging**
```json
{
  "ApiBaseUrl": "https://api-staging.hartonomous.com",
  "AuthAuthority": "https://auth-staging.hartonomous.com",
  "MLEndpoint": "https://ml-staging.hartonomous.com",
  "MCPEndpoint": "wss://mcp-staging.hartonomous.com"
}
```

### **Production**
```json
{
  "ApiBaseUrl": "https://api.hartonomous.com",
  "AuthAuthority": "https://auth.hartonomous.com",
  "MLEndpoint": "https://ml.hartonomous.com",
  "MCPEndpoint": "wss://mcp.hartonomous.com"
}
```

---

## ?? **EXTERNAL INTEGRATION ENDPOINTS**

### **Azure Services**
- **Service Bus**: `https://hartonomous.servicebus.windows.net`
- **Event Grid**: `https://hartonomous.eventgrid.azure.net/api/events`
- **Logic Apps**: `https://prod-123.westus.logic.azure.com:443/workflows/{workflow-id}/triggers/manual/paths/invoke`

### **Third-Party APIs**
- **Stripe**: `https://api.stripe.com/v1`
- **SendGrid**: `https://api.sendgrid.com/v3`
- **Twilio**: `https://api.twilio.com/2010-04-01`

---

**Generated**: 2025-12-04  
**Version**: 1.0.0  
**Status**: Production Ready ?
