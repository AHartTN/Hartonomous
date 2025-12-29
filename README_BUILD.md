# Hartonomous Build Instructions

## Prerequisites

### Linux
```bash
# Install build tools
sudo apt install clang ninja-build cmake libpq-dev pkg-config

# Optional: Install Catch2 for tests
cd /tmp && git clone --depth 1 --branch v3.5.4 https://github.com/catchorg/Catch2.git
cd Catch2 && cmake -B build -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX=$HOME/.local
cmake --build build && cmake --install build
export CMAKE_PREFIX_PATH=$HOME/.local:$CMAKE_PREFIX_PATH
```

### macOS
```bash
brew install llvm ninja cmake postgresql catch2
```

### Windows
```powershell
# Install via winget or chocolatey
winget install LLVM.LLVM Ninja-build.Ninja Kitware.CMake
# PostgreSQL from https://www.postgresql.org/download/windows/
```

## Build

```bash
# Debug build
./build.sh debug

# Release build  
./build.sh release

# Clean build with tests
./build.sh debug --clean --test

# With database seeding
./build.sh release --seed
```

## Run

```bash
# CLI
cd Hartonomous.CLI/bin/Debug/net10.0/linux-x64
./hartonomous info
./hartonomous map 0x1F600

# With database
export HARTONOMOUS_DB_URL="postgresql://user:pass@localhost:5433/hartonomous"
./hartonomous query stats
```

## Docker

```bash
# Start infrastructure
docker compose up -d postgres redis

# Build and run
docker compose up worker
```
