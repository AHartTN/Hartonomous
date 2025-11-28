# Language Standards - Technical Communication

## ❌ Words to ELIMINATE

### Diminishing Terms:
- **"simple"** / **"simply"** - Nothing in this system is simple. It's elegant, efficient, or direct.
- **"easy"** / **"easily"** - Easy for whom? Use "straightforward", "direct", or be specific.
- **"basic"** / **"just"** - Undermines complexity. Use "foundational", "core", or accurate description.
- **"trivial"** - If it's in the codebase, it's not trivial.
- **"obvious"** / **"obviously"** - Not obvious to everyone. Explain or don't mention.

## ✅ Replacement Language

### Instead of:
- ~~"Simply call the function"~~ → **"Call the function"** (more direct)
- ~~"This is simple"~~ → **"This is efficient"** / **"This is direct"**
- ~~"Just add this"~~ → **"Add this"** / **"This requires"**
- ~~"Basic setup"~~ → **"Core setup"** / **"Foundational setup"**
- ~~"Easy to use"~~ → **"Designed for performance"** / **"Optimized for X"**

### When Describing Complexity:
- **Straightforward** - Clear path, no ambiguity
- **Direct** - No indirection, minimal steps
- **Efficient** - Optimized for performance
- **Precise** - Exact, no approximation
- **Focused** - Single responsibility
- **Foundational** - Core building block
- **Essential** - Cannot be omitted

### When Describing Architecture:
- **Modular** - Composable components
- **Scalable** - Handles growth
- **Resilient** - Handles failures gracefully
- **Deterministic** - Predictable behavior
- **Idempotent** - Safe to repeat
- **Content-addressable** - Hash-based deduplication

## 🎯 Why This Matters

### Technical Debt:
Calling something "simple" creates obligation to keep it simple, which:
- Prevents optimization
- Hides complexity
- Makes maintenance harder
- Misleads users

### Professional Standards:
This is a **production-grade AI infrastructure platform**:
- 954 PostgreSQL functions
- GPU acceleration
- Spatial semantic indexing
- Content-addressable atomization
- Real-time provenance tracking

**Nothing about this is "simple". It's sophisticated, elegant, and powerful.**

## 📝 Code Review Checklist

Before committing, search for:
```bash
grep -rn "simple\|Simple\|easy\|Easy\|just\|Just\|basic\|Basic" --include="*.py" --include="*.md" --include="*.sql"
```

Replace with accurate, respectful language that reflects the engineering effort.

---

**Standard applies to:** Code, docs, comments, commit messages, API responses
