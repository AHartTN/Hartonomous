#!/usr/bin/env bash
# Quick install script - run with sudo

set -e

cp build/linux-release-max-perf/PostgresExtension/hartonomous.so /usr/lib/postgresql/18/lib/
echo "Extension installed. Test with:"
echo "psql -d hypercube -c \"DROP EXTENSION IF EXISTS hartonomous CASCADE; CREATE EXTENSION hartonomous; SELECT hartonomous_version();\""
