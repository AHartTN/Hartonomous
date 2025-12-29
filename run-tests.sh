#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"
export HARTONOMOUS_DB_URL="${HARTONOMOUS_DB_URL:-postgresql://hartonomous:hartonomous@localhost:5433/hartonomous}"
./artifacts/native/build/linux-clang-debug/bin/hartonomous-tests "$@"
