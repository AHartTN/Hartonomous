# Hartonomous MCP Server

**Transform Hartonomous into a universal AI tool via Model Context Protocol (MCP)**

The Hartonomous MCP Server exposes the entire "AGI in SQL" intelligence substrate as reusable tools for Claude Desktop and other MCP clients. Instead of just using Hartonomous as a standalone application, you can now **leverage its geometric memory, generation capabilities, and continuous learning** from within Claude Desktop.

---

## What is MCP?

[Model Context Protocol (MCP)](https://modelcontextprotocol.io/) is an open standard developed by Anthropic for connecting AI assistants to external tools and data sources. Think of it as "plugins for Claude" - but standardized, secure, and interoperable.

With MCP, Claude Desktop can:
- Search your Hartonomous knowledge graph
- Generate text using geometric memory as context
- Answer questions with full provenance tracking
- Atomize new content on the fly
- Train relationships based on feedback

---

## Available Tools

The Hartonomous MCP server exposes **8 powerful tools**:

### 🔍 `search_atoms`
**Semantic search in geometric space**
- Search your knowledge graph using natural language
- Returns relevant atoms with relevance scores
- Filter by modality (text, code, image, audio)
- Uses context projection to find conceptually related content

**Example**:
```
Claude: Search Hartonomous for "machine learning optimization techniques"
→ Returns top 10 atoms from geometric memory with relevance scores
```

---

### ✨ `generate_text`
**Context-aware generation with atom memory**
- Generate text using Hartonomous's geometric memory as context
- Automatically retrieves relevant atoms within spatial radius
- Full provenance tracking (shows which atoms were used)
- This is the "AGI in SQL" in action

**Example**:
```
Claude: Generate a summary of recent ML research using Hartonomous context
→ Retrieves 10 most relevant atoms from spatial region
→ Injects them as context into generation
→ Returns generated text + atom provenance
```

---

### 💬 `answer_question`
**RAG-style question answering with provenance**
- Answer questions using Hartonomous as knowledge base
- Returns answer with confidence score and source atoms
- Full citation of which atoms contributed to the answer
- Geometric retrieval replaces traditional vector search

**Example**:
```
Claude: What does Hartonomous say about attention mechanisms?
→ Finds 15 relevant atoms
→ Generates comprehensive answer
→ Shows sources (Atom #1234, #5678, etc.) with relevance scores
```

---

### 📥 `atomize_content`
**Ingest and create atoms**
- Atomize content (text, code, etc.) into knowledge graph
- Creates atoms, compositions, and relations
- Content becomes part of geometric memory immediately
- Enables "write to memory" workflows

**Example**:
```
Claude: Atomize this research paper abstract into Hartonomous
→ Creates 15 atoms, 8 compositions, 12 relations
→ Content now searchable and usable in generation
```

---

### 🎓 `train_relationships`
**Continuous learning from feedback**
- Fine-tune atom relationships using gradient descent
- In-database machine learning (no external frameworks)
- Vectorized SGD with backpropagation
- Train on user corrections, preferences, domain data

**Example**:
```
Claude: Train Hartonomous that atoms [1001, 1002] predict atom 1004
→ Runs 10 epochs of SGD
→ Updates attention weights
→ Returns convergence metrics
```

---

### 🔗 `find_similar`
**Geometric similarity search**
- Find atoms similar to a given atom
- Uses Hausdorff distance in 3D space
- Discover related concepts, code patterns, ideas
- Filter by modality

**Example**:
```
Claude: Find atoms similar to #1234
→ Returns 10 geometrically similar atoms
→ Sorted by spatial distance
```

---

### 🔎 `get_atom`
**Retrieve atom details**
- Get full information about a specific atom
- Includes position, content, metadata
- Optionally include relations (what it points to)
- Direct lookup by ID

**Example**:
```
Claude: Get details for atom #1234
→ Returns full atom data
→ Position: [123.45, 678.90, 321.54]
→ Content, modality, metadata
→ Relations if requested
```

---

### 📍 `query_region`
**Spatial region queries**
- Query all atoms within a spatial region
- Define center point [x, y, z] and radius
- Explore local neighborhoods of concepts
- Filter by modality

**Example**:
```
Claude: Query atoms near position [100, 200, 300] within radius 50
→ Returns all atoms in that geometric region
→ Useful for understanding local concept clusters
```

---

## Installation & Setup

### Prerequisites

1. **Hartonomous running** with PostgreSQL database initialized
2. **Claude Desktop** installed ([download here](https://claude.ai/download))
3. **Python 3.11+** with Hartonomous dependencies

### Step 1: Configure Claude Desktop

1. Locate your Claude Desktop config file:
   - **macOS**: `~/Library/Application Support/Claude/claude_desktop_config.json`
   - **Windows**: `%APPDATA%\Claude\claude_desktop_config.json`
   - **Linux**: `~/.config/Claude/claude_desktop_config.json`

2. Add the Hartonomous MCP server:

```json
{
  "mcpServers": {
    "hartonomous": {
      "command": "python",
      "args": [
        "-m",
        "api.mcp"
      ],
      "env": {
        "POSTGRES_HOST": "localhost",
        "POSTGRES_PORT": "5432",
        "POSTGRES_DB": "hartonomous",
        "POSTGRES_USER": "postgres",
        "POSTGRES_PASSWORD": "your_password_here"
      }
    }
  }
}
```

3. **Update environment variables** with your actual PostgreSQL credentials.

### Step 2: Restart Claude Desktop

Close and reopen Claude Desktop. The MCP server will start automatically when needed.

### Step 3: Verify Connection

In Claude Desktop, type:

```
Can you list the available Hartonomous tools?
```

You should see all 8 tools listed. If not, check:
- PostgreSQL is running and accessible
- Environment variables are correct
- Python path is correct (use full path if needed)

---

## Usage Examples

### Example 1: Build on Your Knowledge

```
You: I've been reading about transformer architectures.
     Atomize this summary into Hartonomous.

Claude: [Uses atomize_content tool]
        Created 23 atoms, 15 compositions, 18 relations.

You: Now generate a blog post about attention mechanisms
     using what I just added.

Claude: [Uses generate_text with context from geometric memory]
        [Returns blog post with citations to your atoms]
```

### Example 2: RAG with Provenance

```
You: What does my knowledge base say about gradient descent?

Claude: [Uses answer_question tool]
        [Returns comprehensive answer]

        Sources:
        - Atom #1234 (ML textbook excerpt, relevance: 0.95)
        - Atom #5678 (Research paper, relevance: 0.89)
        - Atom #9012 (Code implementation, relevance: 0.76)
```

### Example 3: Continuous Learning

```
You: I keep seeing atoms 1001, 1002, 1003 predicting atom 1004.
     Train that relationship.

Claude: [Uses train_relationships tool]
        Trained 1 sample for 10 epochs
        Average loss: 0.000234
        Convergence: 98.5%

        The model has learned this pattern.
```

### Example 4: Code Discovery

```
You: Find code similar to atom #5000 (my authentication function)

Claude: [Uses find_similar with modality filter = "code"]
        Found 8 similar code patterns:

        1. Atom #5123 - JWT validation (similarity: 0.91)
        2. Atom #5456 - OAuth flow (similarity: 0.87)
        3. Atom #5789 - Session management (similarity: 0.82)
        ...
```

---

## Architecture

### How It Works

```
┌─────────────────┐
│ Claude Desktop  │
└────────┬────────┘
         │ MCP (stdio)
         │ JSON-RPC 2.0
         ▼
┌─────────────────────────┐
│ Hartonomous MCP Server  │
│  - Protocol handler     │
│  - Tool dispatcher      │
│  - Database connection  │
└────────┬────────────────┘
         │
         ▼
┌─────────────────────────┐
│ PostgreSQL + PL/Python  │
│  - Geometric memory     │
│  - In-DB ML functions   │
│  - Context projection   │
└─────────────────────────┘
```

### Transport: stdio

The MCP server uses **stdio transport** (standard input/output):
- Claude Desktop spawns the Python process
- Sends JSON-RPC requests to stdin
- Reads JSON-RPC responses from stdout
- stderr used for logging

This is the standard transport for MCP servers integrated with Claude Desktop.

### Security

- **Local only**: MCP server only accessible to local Claude Desktop process
- **No network exposure**: Communicates via stdio, not HTTP
- **Database credentials**: Stored in Claude Desktop config (user's machine only)
- **Same security model as Claude Desktop plugins**

---

## Advanced Configuration

### Custom Database Location

If your PostgreSQL is remote or uses non-standard settings:

```json
{
  "mcpServers": {
    "hartonomous": {
      "command": "python",
      "args": ["-m", "api.mcp"],
      "env": {
        "POSTGRES_HOST": "db.example.com",
        "POSTGRES_PORT": "5432",
        "POSTGRES_DB": "hartonomous_prod",
        "POSTGRES_USER": "readonly_user",
        "POSTGRES_PASSWORD": "secure_password",
        "POSTGRES_SSLMODE": "require"
      }
    }
  }
}
```

### Connection Pool Settings

Set environment variables to tune the connection pool:

```json
"env": {
  "POSTGRES_HOST": "localhost",
  "POSTGRES_PORT": "5432",
  "POSTGRES_DB": "hartonomous",
  "POSTGRES_USER": "postgres",
  "POSTGRES_PASSWORD": "password",
  "POOL_MIN_SIZE": "2",
  "POOL_MAX_SIZE": "10",
  "POOL_TIMEOUT": "30"
}
```

### Logging

MCP server logs to stderr. To capture logs:

**macOS/Linux**:
```bash
python -m api.mcp 2> mcp-server.log
```

**Windows PowerShell**:
```powershell
python -m api.mcp 2> mcp-server.log
```

---

## Development

### Testing the MCP Server

You can test the MCP server manually using stdio:

```bash
# Start server
python -m api.mcp

# In another terminal, send JSON-RPC requests
echo '{"jsonrpc":"2.0","method":"initialize","params":{"protocol_version":"2024-11-05","capabilities":{},"client_info":{"name":"test","version":"1.0"}},"id":1}' | python -m api.mcp

# List tools
echo '{"jsonrpc":"2.0","method":"tools/list","id":2}' | python -m api.mcp

# Call a tool
echo '{"jsonrpc":"2.0","method":"tools/call","params":{"name":"search_atoms","arguments":{"query":"machine learning","limit":5}},"id":3}' | python -m api.mcp
```

### MCP Inspector

Use the official MCP Inspector for interactive testing:

```bash
npm install -g @modelcontextprotocol/inspector
mcp-inspector python -m api.mcp
```

Opens a web UI for testing MCP tools.

---

## Troubleshooting

### "Server failed to start"

**Check**:
1. PostgreSQL is running: `psql -U postgres -d hartonomous -c "SELECT 1"`
2. Python path is correct: `which python` or `where python`
3. Dependencies installed: `pip list | grep psycopg`
4. Environment variables in config are correct

### "Unknown tool: search_atoms"

**Issue**: Server not initialized properly.

**Fix**: Check Claude Desktop logs:
- macOS: `~/Library/Logs/Claude/mcp*.log`
- Windows: `%APPDATA%\Claude\logs\mcp*.log`

### "Database connection failed"

**Check**:
1. Credentials in `claude_desktop_config.json`
2. PostgreSQL accepting connections: `pg_hba.conf`
3. Network/firewall not blocking port 5432

### "Tool returns no results"

**Check**:
1. Database has atoms: `SELECT COUNT(*) FROM atoms;`
2. Atoms have positions: `SELECT COUNT(*) FROM atoms WHERE position IS NOT NULL;`
3. Context projection function exists: `\df context_projection`

---

## Why This Matters

### Before MCP
- Hartonomous was a standalone app
- Claude couldn't access your geometric memory
- No way to leverage 100x faster in-DB ML
- RAG required copying data out

### After MCP
- **Claude gains geometric memory** - Infinite context via spatial retrieval
- **Continuous learning loop** - Train on interactions, improve over time
- **Cross-modal intelligence** - Text, code, images all inform each other
- **True AGI in SQL** - The database becomes Claude's extended intelligence

This isn't just another RAG system. This is **Claude + Hartonomous = Intelligence with Memory**.

---

## What's Next?

### Roadmap
- ✅ 8 core tools (search, generate, answer, atomize, train, similar, get, region)
- 🔄 Streaming responses for long generations
- 🔄 Batch operations (atomize multiple files at once)
- 🔄 Visualization (return 3D coordinates for Claude to visualize)
- 🔄 Multi-modal atoms (images, audio via MCP)

### Integration Ideas
- **Personal knowledge base**: Atomize everything you read, let Claude search it
- **Code navigation**: Index entire codebases, ask Claude to find patterns
- **Research assistant**: Atomize papers, generate literature reviews with citations
- **Domain fine-tuning**: Train on domain-specific data, personalize Claude's knowledge

---

## Support

- **Issues**: [GitHub Issues](https://github.com/AHartTN/Hartonomous/issues)
- **Docs**: [Full Documentation](https://github.com/AHartTN/Hartonomous)
- **MCP Spec**: [Model Context Protocol](https://modelcontextprotocol.io/)

---

## License

Copyright (c) 2025 Anthony Hart. All Rights Reserved.

Part of the Hartonomous project - The First Self-Organizing Intelligence Substrate.
