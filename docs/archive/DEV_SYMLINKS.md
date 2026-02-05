# Development Symlinks Setup

## Overview

By default, building and installing Hartonomous requires `sudo` to copy libraries to system directories:
- `/usr/local/lib/libengine*.so`
- `/usr/lib/postgresql/18/lib/*.so`
- `/usr/share/postgresql/18/extension/*`

This slows down iteration during development. The **development symlinks** approach replaces these copied files with symlinks pointing directly to your build directory.

## Benefits

✅ **Rebuild without sudo** - Only `ldconfig` needs sudo after building  
✅ **Faster iteration** - Edit → Build → Test cycle with minimal friction  
✅ **No copies** - Symlinks automatically reflect new builds  
✅ **Easy revert** - Can switch back to normal installation anytime  

## One-Time Setup

```bash
# 1. Clean build everything first
./scripts/linux/01-build.sh -c

# 2. Setup development symlinks (requires sudo once)
sudo ./scripts/linux/02-install-dev-symlinks.sh

# 3. You're done! Now iterate without sudo
```

## Development Workflow

After setup, your workflow becomes:

```bash
# 1. Make code changes
vim Engine/src/whatever.cpp

# 2. Quick rebuild (automatic with helper script)
./rebuild.sh

# OR manually:
./scripts/linux/01-build.sh
sudo ldconfig
```

The symlinks point to `build/linux-release-max-perf/`, so rebuilding automatically updates what's "installed"!

## What Gets Symlinked

### Engine Libraries
- `/usr/local/lib/libengine_core.so` → `build/.../Engine/libengine_core.so`
- `/usr/local/lib/libengine_io.so` → `build/.../Engine/libengine_io.so`
- `/usr/local/lib/libengine.so` → `build/.../Engine/libengine.so`

### PostgreSQL Extensions
- `/usr/lib/postgresql/18/lib/s3.so` → `build/.../PostgresExtension/s3/s3.so`
- `/usr/lib/postgresql/18/lib/hartonomous.so` → `build/.../PostgresExtension/hartonomous/hartonomous.so`
- Engine libraries also symlinked to PostgreSQL lib directory

### Extension Configs
- `/usr/share/postgresql/18/extension/s3.control` → `PostgresExtension/s3/s3.control`
- `/usr/share/postgresql/18/extension/s3--0.1.0.sql` → `PostgresExtension/s3/dist/s3--0.1.0.sql`
- Similarly for hartonomous extension

### Headers
- `/usr/local/include/hartonomous` → `Engine/include`

## Checking Symlink Status

```bash
# Check if dev mode is active
ls -l .dev-symlinks-active

# Inspect symlinks
ls -lh /usr/local/lib/libengine*
ls -lh $(pg_config --pkglibdir)/{s3,hartonomous,libengine}*

# You should see -> arrows pointing to your build directory
```

## Reverting to Normal Installation

If you need to go back to the normal copied-files approach:

```bash
# Method 1: Run normal install script (will overwrite symlinks with real files)
sudo ./scripts/linux/02-install.sh

# Method 2: Clean and rebuild everything
./full-send.sh
```

Both will remove the `.dev-symlinks-active` marker file.

## Important Notes

⚠️ **Don't delete build directory** - Symlinks will break if you remove `build/linux-release-max-perf/`  
⚠️ **ldconfig still needs sudo** - Library cache must be updated after rebuilding  
⚠️ **Extension configs rarely change** - `.sql` and `.control` files are symlinked but rarely updated  
⚠️ **Full pipeline uses normal install** - `full-send.sh` runs normal installation (resets dev mode)  

## Troubleshooting

### Symlinks broken after clean build
```bash
# Just re-run the setup script
sudo ./scripts/linux/02-install-dev-symlinks.sh
```

### Library not found errors
```bash
# Update library cache
sudo ldconfig

# Check symlinks are valid
ls -lh /usr/local/lib/libengine*
```

### Want to force normal installation
```bash
# Remove marker file and reinstall
rm -f .dev-symlinks-active
sudo ./scripts/linux/02-install.sh
```

## CMake Refactoring

This setup enables fast iteration while refactoring the CMake build system:

1. Edit CMakeLists.txt files
2. Run `./rebuild.sh`
3. Test immediately
4. Repeat

No sudo password prompts interrupting your flow!
