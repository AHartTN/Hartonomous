# Hartonomous - Working Commands

All commands run from repo root without environment setup.

## CLI Commands

```bash
./hartonomous info
./hartonomous map 0x1F600
./hartonomous query stats
./hartonomous version
```

## Tests

```bash
./run-tests.sh
./run-tests.sh --list-tests
./run-tests.sh "[mlops]"
```

## Native Tools

```bash
./artifacts/native/build/linux-clang-debug/bin/hartonomous-seed
./artifacts/native/build/linux-clang-debug/bin/hartonomous-ingest test-data/moby_dick.txt
```

## Build

```bash
./build.sh debug
./build.sh release --test
./validate.sh
```
