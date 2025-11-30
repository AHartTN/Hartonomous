"""Hartonomous Enterprise Test Suite.

Copyright (c) 2025 Anthony Hart. All Rights Reserved.

Structure:
- smoke/: Quick validation tests (imports, connections) - < 1 min
- unit/: Fast isolated unit tests - < 5 min
- integration/: Database and service integration tests - < 15 min
- functional/: End-to-end functional tests - < 30 min
- performance/: Load and performance tests - variable duration
- sql/: PostgreSQL function and schema tests

Usage:
- All tests: pytest tests/
- Smoke only: pytest tests/smoke -m smoke
- CI/CD: pytest tests/smoke tests/unit -m "smoke or unit"
- Coverage: pytest tests/ --cov=api --cov=src --cov-report=html
"""
