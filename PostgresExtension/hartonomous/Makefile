EXTENSION = hartonomous
MODULE_big = hartonomous

# Enterprise Objects
OBJS = src/uint64_ops.o src/uint128_ops.o src/pg_wrapper.o src/hartonomous_functions.o src/main.o

# Ground Truth SQL
DATA = dist/hartonomous--0.1.0.sql

# Production Paths
ENGINE_INC = $(shell pwd)/../../Engine/include
EIGEN_INC = $(shell pwd)/../../Engine/external/eigen
BLAKE3_INC = $(shell pwd)/../../Engine/external/blake3/c
HILBERT_INC = $(shell pwd)/../../Engine/external/hilbert
PG_SERVER_INC = $(shell pg_config --includedir-server)

# Enterprise PGXS Configuration
PG_CPPFLAGS += -Iinclude -I$(ENGINE_INC) -I$(EIGEN_INC) -I$(BLAKE3_INC) -I$(HILBERT_INC)
PG_CPPFLAGS += -O3 -march=native -mtune=native -fPIC

# C++ Standard Alignment
PG_CXXFLAGS += -std=c++20 -fvisibility=hidden -fPIC

# Production Linkage
SHLIB_LINK += -L$(shell pwd)/../../build/linux-release-max-perf/Engine -lengine -lmkl_rt
SHLIB_LINK += -Wl,-rpath,'$$ORIGIN/../../Engine'

# JIT Support
BITCODE_CXXFLAGS += -std=c++20 -Iinclude -I$(ENGINE_INC) -I$(EIGEN_INC) -I$(BLAKE3_INC) -I$(HILBERT_INC) -I$(PG_SERVER_INC)
BITCODE_CFLAGS += -Iinclude -I$(ENGINE_INC) -I$(EIGEN_INC) -I$(BLAKE3_INC) -I$(HILBERT_INC) -I$(PG_SERVER_INC)

# Ensure SQL distribution is current
all: dist/hartonomous--0.1.0.sql

dist/hartonomous--0.1.0.sql: sql/hartonomous--0.1.0.sql
	mkdir -p dist
	../../scripts/linux/XX-build_extension_sql.sh sql/hartonomous--0.1.0.sql dist/hartonomous--0.1.0.sql

PGXS := $(shell pg_config --pgxs)
include $(PGXS)

# Robust C++ Rule
%.o: %.cpp
	$(CXX) $(PG_CXXFLAGS) $(CXXFLAGS) $(CPPFLAGS) $(PG_CPPFLAGS) -c -o $@ $<

# Robust C Rule
%.o: %.c
	$(CC) $(CFLAGS) $(CPPFLAGS) $(PG_CPPFLAGS) -c -o $@ $<
