# Deployment Fix Analysis

## Root Cause
1. **docker/Dockerfile** (PostgreSQL image) uses `postgis/postgis:15-3.4` → Has Python 3.9
2. **markdown-it-py 4.0.0** dropped Python 3.9 support (requires 3.10+)
3. **C# Dockerfile** references .NET 10.0 which doesn't exist yet

## Solution
Use `markdown-it-py>=3.0.0,<4.0.0` for Python 3.9 compatibility
OR upgrade to postgis:16 with newer Python

Checking current state after revert...
