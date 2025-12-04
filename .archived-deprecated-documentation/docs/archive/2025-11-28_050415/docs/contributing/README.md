# Contributing to Hartonomous

Thank you for your interest in contributing to Hartonomous! This document provides guidelines and instructions for contributing.

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Getting Started](#getting-started)
- [Development Workflow](#development-workflow)
- [Coding Standards](#coding-standards)
- [Testing](#testing)
- [Documentation](#documentation)
- [Submitting Changes](#submitting-changes)

## Code of Conduct

### Our Pledge

We are committed to making participation in this project a harassment-free experience for everyone, regardless of:
- Level of experience
- Gender, gender identity and expression
- Sexual orientation
- Disability
- Personal appearance
- Body size
- Race, ethnicity
- Age
- Religion
- Nationality

### Our Standards

**Positive behaviors:**
- Using welcoming and inclusive language
- Being respectful of differing viewpoints
- Gracefully accepting constructive criticism
- Focusing on what is best for the community
- Showing empathy towards other community members

**Unacceptable behaviors:**
- Trolling, insulting/derogatory comments, and personal attacks
- Public or private harassment
- Publishing others' private information
- Other conduct which could reasonably be considered inappropriate

## Getting Started

### Prerequisites

- Docker & Docker Compose
- PostgreSQL 15+ (for local development)
- Git
- Python 3.10+ (optional, for utility scripts)

### Setting Up Development Environment

1. **Fork the repository**
```bash
git clone https://github.com/YOUR_USERNAME/Hartonomous.git
cd Hartonomous
```

2. **Start the development database**
```bash
cd docker
docker-compose up -d
```

3. **Connect and verify**
```bash
psql -h localhost -U postgres -d hartonomous
hartonomous=# SELECT atomize_text('Hello');
```

## Development Workflow

### Branch Naming

- `feature/description` - New features
- `bugfix/description` - Bug fixes
- `docs/description` - Documentation updates
- `refactor/description` - Code refactoring
- `test/description` - Test additions/modifications

### Commit Messages

Follow conventional commits format:

```
type(scope): brief description

Longer description if needed

Fixes #123
```

**Types:**
- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation only
- `style`: Formatting, no code change
- `refactor`: Code restructuring
- `test`: Adding tests
- `chore`: Maintenance tasks

**Examples:**
```
feat(atomization): add sparse vector compression
fix(spatial): correct KNN distance calculation
docs(architecture): update spatial indexing guide
```

## Coding Standards

### SQL

```sql
-- Use clear, descriptive names
CREATE OR REPLACE FUNCTION atomize_value(
    p_value BYTEA,
    p_canonical_text TEXT DEFAULT NULL
)
RETURNS BIGINT AS $$
DECLARE
    v_hash BYTEA;  -- Use v_ prefix for variables
BEGIN
    -- Comment complex logic
    v_hash := digest(p_value, 'sha256');
    
    -- Use proper error handling
    IF v_hash IS NULL THEN
        RAISE EXCEPTION 'Failed to compute hash';
    END IF;
    
    RETURN v_atom_id;
END;
$$ LANGUAGE plpgsql;

-- Add comments to tables and columns
COMMENT ON FUNCTION atomize_value(BYTEA, TEXT) IS 
'Content-addressable storage with SHA-256 deduplication';
```

### Python (when applicable)

```python
# Follow PEP 8
# Use type hints
from typing import Optional, List

def atomize_text(text: str, metadata: Optional[dict] = None) -> List[int]:
    """
    Atomize text into character-level atoms.
    
    Args:
        text: Input text to atomize
        metadata: Optional metadata dictionary
        
    Returns:
        List of atom IDs
    """
    pass
```

### File Organization

```
schema/
??? tables/        # 00X_*.sql (numbered for order)
??? indexes/       # 00X_*.sql
??? functions/     # 00X_*.sql
??? triggers/      # 00X_*.sql
??? views/         # 00X_*.sql
```

## Testing

### SQL Tests

Create test files in `tests/sql/`:

```sql
-- tests/sql/test_atomization.sql
BEGIN;

-- Test: atomize_value creates atom
SELECT atomize_value('\x48'::bytea, 'H');
SELECT COUNT(*) = 1 FROM atom WHERE canonical_text = 'H';

-- Test: deduplication works
SELECT atomize_value('\x48'::bytea, 'H');
SELECT COUNT(*) = 1 FROM atom WHERE canonical_text = 'H';
SELECT reference_count = 2 FROM atom WHERE canonical_text = 'H';

ROLLBACK;
```

Run tests:
```bash
psql -h localhost -U postgres -d hartonomous -f tests/sql/test_atomization.sql
```

## Documentation

### Code Documentation

- All functions must have comments explaining purpose, parameters, and return values
- Complex algorithms need inline comments
- Add examples in comments where helpful

### User Documentation

Update relevant docs in `docs/`:
- `01-VISION.md` - Conceptual changes
- `02-ARCHITECTURE.md` - Technical changes
- `10-API-REFERENCE.md` - Function changes

### Docstring Format

```sql
-- ============================================================================
-- FUNCTION: atomize_value
-- 
-- Purpose: Content-addressable storage with SHA-256 deduplication
--
-- Parameters:
--   p_value: BYTEA - Value to atomize (?64 bytes)
--   p_canonical_text: TEXT - Optional text representation
--   p_metadata: JSONB - Optional metadata
--
-- Returns: BIGINT - atom_id of created or existing atom
--
-- Example:
--   SELECT atomize_value('\x48'::bytea, 'H', '{"modality": "char"}');
--
-- Notes:
--   - Automatically deduplicates via content hash
--   - Increments reference_count for existing atoms
-- ============================================================================
```

## Submitting Changes

### Pull Request Process

1. **Update your fork**
```bash
git checkout main
git pull upstream main
git checkout -b feature/my-feature
```

2. **Make your changes**
- Follow coding standards
- Add tests
- Update documentation
- Ensure all tests pass

3. **Commit your changes**
```bash
git add .
git commit -m "feat(scope): description"
```

4. **Push to your fork**
```bash
git push origin feature/my-feature
```

5. **Create Pull Request**
- Go to GitHub
- Click "New Pull Request"
- Select your branch
- Fill out the PR template

### Pull Request Template

```markdown
## Description
Brief description of changes

## Motivation and Context
Why is this change needed? What problem does it solve?

## Type of Change
- [ ] Bug fix (non-breaking change)
- [ ] New feature (non-breaking change)
- [ ] Breaking change (fix or feature that would cause existing functionality to not work as expected)
- [ ] Documentation update

## How Has This Been Tested?
Describe the tests you ran

## Checklist
- [ ] My code follows the project's style guidelines
- [ ] I have performed a self-review of my own code
- [ ] I have commented my code, particularly in hard-to-understand areas
- [ ] I have made corresponding changes to the documentation
- [ ] My changes generate no new warnings
- [ ] I have added tests that prove my fix is effective or that my feature works
- [ ] New and existing unit tests pass locally with my changes
```

### Review Process

1. Maintainers will review your PR
2. Address any requested changes
3. Once approved, a maintainer will merge

## Questions?

- **Email**: aharttn@gmail.com
- **GitHub Issues**: [Open an issue](https://github.com/AHartTN/Hartonomous/issues)
- **Discord**: Coming soon

## License

By contributing, you agree that your contributions will be licensed under the MIT License.

---

**Thank you for contributing to Hartonomous!**
