EXTENSION = hartonomous
MODULE_big = hartonomous

# Pure C Shim (bridges PostgreSQL to Engine C API)
OBJS = src/hartonomous_shim.o src/uint128_ops.o src/uint64_ops.o

# Modular Entry Point
DATA = dist/hartonomous--0.1.0.sql

# Production Paths
ENGINE_INC = $(shell pwd)/../../Engine/include

# PGXS Configuration
PG_CPPFLAGS += -I$(ENGINE_INC)
PG_CPPFLAGS += -O3 -march=native -mtune=native -fPIC

# Linkage: This C shim calls into the C++ Engine shared library
SHLIB_LINK += -L$(shell pwd)/../../build/linux-release-max-perf/Engine -lengine
SHLIB_LINK += -Wl,-rpath,'$$ORIGIN/../../Engine'

# Ensure SQL distribution is current
all: dist/hartonomous--0.1.0.sql

dist/hartonomous--0.1.0.sql: sql/hartonomous--0.1.0.sql
	mkdir -p dist
	../../scripts/linux/XX-build_extension_sql.sh sql/hartonomous--0.1.0.sql dist/hartonomous--0.1.0.sql

PGXS := $(shell pg_config --pgxs)
include $(PGXS)