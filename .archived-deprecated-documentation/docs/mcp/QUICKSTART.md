# Hartonomous MCP Server - Quick Start

**Get Claude Desktop connected to Hartonomous in 3 minutes**

---

## Prerequisites

- ✅ Hartonomous installed and PostgreSQL running
- ✅ Claude Desktop installed
- ✅ Python 3.11+

---

## Step 1: Find Your Config File

**macOS**:
```bash
open ~/Library/Application\ Support/Claude/
# Edit: claude_desktop_config.json
```

**Windows**:
```powershell
explorer %APPDATA%\Claude\
# Edit: claude_desktop_config.json
```

**Linux**:
```bash
nano ~/.config/Claude/claude_desktop_config.json
```

---

## Step 2: Add Hartonomous MCP Server

Copy this into your `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "hartonomous": {
      "command": "python",
      "args": ["-m", "api.mcp"],
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

**⚠️ IMPORTANT**: Update `POSTGRES_PASSWORD` with your actual password!

---

## Step 3: Restart Claude Desktop

Close and reopen Claude Desktop completely.

---

## Step 4: Test It

In Claude Desktop, type:

```
List the Hartonomous tools available
```

You should see:
- ✅ search_atoms
- ✅ generate_text
- ✅ answer_question
- ✅ atomize_content
- ✅ train_relationships
- ✅ find_similar
- ✅ get_atom
- ✅ query_region

---

## Step 5: Try It Out

```
Search Hartonomous for "machine learning"
```

If you see atoms returned, **you're connected!** 🎉

---

## Troubleshooting

### "Server failed to start"

1. Check PostgreSQL is running:
   ```bash
   psql -U postgres -d hartonomous -c "SELECT 1"
   ```

2. Verify Python path:
   ```bash
   which python  # macOS/Linux
   where python  # Windows
   ```

3. Check Claude logs:
   - macOS: `~/Library/Logs/Claude/mcp*.log`
   - Windows: `%APPDATA%\Claude\logs\mcp*.log`

### "No tools listed"

Edit config and use **full Python path**:

```json
{
  "mcpServers": {
    "hartonomous": {
      "command": "/usr/local/bin/python3",  # Your full path
      "args": ["-m", "api.mcp"],
      ...
    }
  }
}
```

### "Database connection failed"

Check credentials in config match your PostgreSQL setup:

```bash
# Test connection
psql -h localhost -U postgres -d hartonomous
```

---

## What's Next?

Read the [full documentation](README.md) to learn about:
- All 8 available tools
- Advanced usage examples
- Integration patterns
- Architecture details

---

## Quick Examples

### Atomize Content
```
Atomize this text into Hartonomous:
"Transformers use self-attention to process sequences in parallel."
```

### Search Knowledge
```
Search Hartonomous for "neural networks"
```

### Answer Questions
```
What does my Hartonomous knowledge base say about attention mechanisms?
```

### Find Similar
```
Find atoms similar to #1234
```

---

**Welcome to AGI in SQL!** 🚀
