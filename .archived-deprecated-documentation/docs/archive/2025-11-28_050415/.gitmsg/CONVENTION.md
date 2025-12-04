# Commit Message Convention

**Standardized naming and format for commit messages**

---

## ?? File Naming Convention

### For Multi-Line Commits

**Format:** `.gitmsg/<scope>-<timestamp>.txt`

**Examples:**
```
.gitmsg/docs-restructure-20250125.txt
.gitmsg/feat-vectorization-20250115.txt
.gitmsg/fix-spatial-index-20250110.txt
```

### Directory Structure

```
.gitmsg/
??? docs-restructure-20250125.txt
??? feat-vectorization-20250115.txt
??? archive/
    ??? 2024/
        ??? feat-core-schema-20241115.txt
```

---

## ?? Commit Message Format

### Structure

```
<type>: <subject>

<body>

<footer>
```

### Type Prefixes

| Type | Use Case | Example |
|------|----------|---------|
| `feat` | New feature | `feat: Add vectorized image atomization` |
| `fix` | Bug fix | `fix: Correct spatial index query performance` |
| `docs` | Documentation only | `docs: Update API reference for AGE functions` |
| `style` | Code style (formatting) | `style: Format SQL files with pgFormatter` |
| `refactor` | Code refactoring | `refactor: Extract helper functions` |
| `perf` | Performance improvement | `perf: Optimize Gram-Schmidt for SIMD` |
| `test` | Add/update tests | `test: Add pgTAP tests for atomization` |
| `chore` | Maintenance tasks | `chore: Update dependencies` |
| `ci` | CI/CD changes | `ci: Add GitHub Actions workflow` |

---

## ?? Message Guidelines

### Subject Line (First Line)

**Rules:**
- Maximum 72 characters
- Capitalize first letter
- No period at end
- Imperative mood ("Add" not "Added" or "Adds")

**Examples:**
```
? GOOD: feat: Add GPU acceleration via CuPy
? BAD:  added gpu acceleration.
```

---

### Body (Optional)

**When to include:**
- Complex changes requiring explanation
- Breaking changes
- Context for design decisions

**Format:**
- Wrap at 72 characters
- Separate from subject with blank line
- Use bullet points for multiple items

**Example:**
```
feat: Add vectorized image atomization

Replace FOR loop with bulk UNNEST operation for 100x speedup.

Changes:
- atomize_image_vectorized() function
- Eliminates RBAR (Row-By-Agonizing-Row)
- Enables 8-16 parallel workers per query

Performance: 5000ms ? 50ms for 1M pixels
```

---

### Footer (Optional)

**Breaking changes:**
```
BREAKING CHANGE: atom_relation.weight now REAL instead of INTEGER
```

**Issue references:**
```
Fixes #123
Closes #456
Refs #789
```

---

## ?? Workflow

### 1. Create Message File

```powershell
# Create .gitmsg directory if it doesn't exist
New-Item -Path ".gitmsg" -ItemType Directory -Force

# Create message file with timestamp
$timestamp = Get-Date -Format "yyyyMMdd"
$file = ".gitmsg/docs-restructure-$timestamp.txt"
New-Item -Path $file -ItemType File
```

---

### 2. Write Message

```powershell
# Edit in VS Code
code $file

# Or use here-string
@"
docs: Enterprise-grade documentation transformation

PHASE 1: MIGRATION
- Executed full documentation reorganization
- Created audience-segmented directory structure

PHASE 2: ENHANCED README
- Added shields.io badges
- Added Mermaid diagrams
"@ | Out-File $file
```

---

### 3. Commit with Message File

```powershell
# Stage changes
git add -A

# Commit using message file
git commit -F $file

# Clean up (optional)
Move-Item $file ".gitmsg/archive/"
```

---

## ??? Archive Strategy

### Monthly

```powershell
# Move old messages to archive
$year = (Get-Date).Year
$month = (Get-Date).Month
$archive = ".gitmsg/archive/$year/$month"
New-Item -Path $archive -ItemType Directory -Force
Move-Item ".gitmsg/*.txt" $archive
```

---

## ?? Templates

### Feature Template

```
feat: <Short description>

<Detailed explanation>

Changes:
- <Change 1>
- <Change 2>

Performance: <Before> ? <After>
```

### Documentation Template

```
docs: <Short description>

<What was changed>

Structure:
- <Change 1>
- <Change 2>

Status: <Version>
```

### Fix Template

```
fix: <Short description>

<Problem description>

Root cause: <Explanation>

Solution: <How it was fixed>

Fixes #<issue-number>
```

---

## ? Checklist Before Commit

- [ ] Type prefix is correct
- [ ] Subject line ?72 characters
- [ ] Subject uses imperative mood
- [ ] Body wraps at 72 characters
- [ ] Breaking changes noted in footer
- [ ] Issue references included
- [ ] Message file in `.gitmsg/` directory
- [ ] Staged changes match commit message

---

## ?? Anti-Patterns

### ? BAD

```
# Vague
Update stuff

# Too technical
Refactored compute_spatial_position to use trilateration with
weighted averages based on K-nearest neighbors using R-tree
spatial index for O(log N) performance

# No context
Fixed bug

# All caps
ADDED NEW FEATURE

# Past tense
Added vectorization
```

### ? GOOD

```
# Clear and concise
feat: Add vectorized image atomization

# Right level of detail
refactor: Simplify spatial positioning algorithm

Uses trilateration for 2x speedup.

# Context provided
fix: Correct R-tree index usage in KNN queries

Bug: Queries were using sequential scan
Fix: Added explicit index hint

# Professional
feat: Add GPU acceleration support
```

---

## ?? References

- [Conventional Commits](https://www.conventionalcommits.org/)
- [Git Commit Best Practices](https://chris.beams.io/posts/git-commit/)
- [Angular Commit Guidelines](https://github.com/angular/angular/blob/main/CONTRIBUTING.md#commit)

---

## ?? Automation

### PowerShell Helper Script

```powershell
# scripts/commit-helper.ps1

param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("feat","fix","docs","style","refactor","perf","test","chore","ci")]
    [string]$Type,
    
    [Parameter(Mandatory=$true)]
    [string]$Subject
)

$timestamp = Get-Date -Format "yyyyMMdd"
$scope = $Subject.Split()[0].ToLower()
$file = ".gitmsg/$Type-$scope-$timestamp.txt"

New-Item -Path ".gitmsg" -ItemType Directory -Force
New-Item -Path $file -ItemType File -Value "$Type`: $Subject`n`n"

code $file
```

**Usage:**
```powershell
.\scripts\commit-helper.ps1 -Type "feat" -Subject "Add GPU acceleration"
```

---

**Last Updated:** 2025-01-25  
**Version:** 1.0
