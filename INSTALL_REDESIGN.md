# Installation System Redesign

## Current Problem
- Mixed concerns: build, staging (/opt/hartonomous), final install
- Redundant copying via scripts
- Permission chaos (sudo/root ownership issues)
- Not idempotent or production-ready

## Root Cause
CMake ALREADY installs directly to PostgreSQL directories, but we added:
1. Staging to /opt/hartonomous (unnecessary)
2. 02-install.sh script (redundant)
3. Mixed sudo/non-sudo operations

## Proper Solution

### Phase 1: Build (No sudo)
```bash
cmake --preset linux-release-max-perf
cmake --build build/linux-release-max-perf
```
**Output:** `build/linux-release-max-perf/` with all artifacts

### Phase 2: Install (Requires sudo, idempotent)
```bash
sudo cmake --install build/linux-release-max-perf
```
**CMake installs:**
- `libengine_core.so`, `libengine_io.so`, `libengine.so` → `/usr/local/lib/`
- `hartonomous.so` → `/usr/lib/postgresql/18/lib/` AND `/usr/local/lib/`
- `s3.so` → `/usr/lib/postgresql/18/lib/` AND `/usr/local/lib/`
- Headers → `/usr/local/include/hartonomous/`
- Extension files → `/usr/share/postgresql/18/extension/`

### Phase 3: Register Libraries (Requires sudo)
```bash
sudo ldconfig
```

## What to Change

1. **Delete redundant scripts:**
   - `01a-install-local.sh` (use cmake --install directly)
   - `02-install.sh` (CMake already does this)
   - `/opt/hartonomous` (not needed)

2. **Update full-send.sh:**
   ```bash
   # Build
   cmake --build build/linux-release-max-perf
   
   # Install (idempotent, safe to re-run)
   sudo cmake --install build/linux-release-max-perf
   sudo ldconfig
   
   # Database setup
   ./scripts/linux/03-setup-database.sh
   ```

3. **Development workflow:**
   ```bash
   # Rebuild only
   cmake --build build/linux-release-max-perf
   
   # Reinstall (only when .so changed)
   sudo cmake --install build/linux-release-max-perf --component libraries
   sudo ldconfig
   ```

## Benefits
- ✅ Idempotent (safe to re-run)
- ✅ Standard CMake workflow
- ✅ Clear separation: build vs install
- ✅ No permission chaos
- ✅ Production-ready
- ✅ Could be packaged as .deb/.rpm

## Migration Path
1. User reviews this design
2. Implement changes if approved
3. Test full-send.sh end-to-end
4. Update documentation
