EXTENSION = s3
MODULE_big = s3

# Source files (PGXS will compile them automatically)
SRCS = src/pg/s3_pg_shim.cpp \
       src/pg/s3_pg_geom.cpp \
       src/pg/s3_pg_gist.cpp

# SQL flattening
SQL_SOURCES := $(shell find sql -type f -name '*.sql')
DATA = dist/s3--0.1.0.sql

dist/s3--0.1.0.sql: $(SQL_SOURCES)
    mkdir -p dist
    ../../scripts/linux/build_extension_sql.sh sql/s3--0.1.0.sql dist/s3--0.1.0.sql

# Include paths
PG_CPPFLAGS += -I$(shell pg_config --includedir-server)
PG_CPPFLAGS += -I$(shell pwd)/../../Engine/include
PG_CPPFLAGS += -I$(shell pwd)/../../Engine/external/eigen
PG_CPPFLAGS += -I$(shell pwd)/../../Engine/external/postgis/liblwgeom
PG_CPPFLAGS += -I$(shell pwd)/../../Engine/external/postgis/libpgcommon
PG_CPPFLAGS += -I$(shell pwd)/../../Engine/external/postgis/postgis
PG_CPPFLAGS += -DS3_USE_MKL

# Compiler flags
CXXFLAGS += -O3 -march=native -mtune=native -std=c++20

# Linkage
POSTGIS_DIR = $(shell pwd)/../../Engine/external/postgis

SHLIB_LINK += -L$(shell pwd)/../../build/linux-release-max-perf/Engine -lengine -lmkl_rt
SHLIB_LINK += -lgeos_c -lproj -ljson-c
SHLIB_LINK += -Wl,--start-group \
    $(POSTGIS_DIR)/libpgcommon/libpgcommon.a \
    $(POSTGIS_DIR)/liblwgeom/.libs/liblwgeom.a \
    $(POSTGIS_DIR)/deps/ryu/.libs/libryu.a \
    -Wl,--end-group
SHLIB_LINK += -Wl,-rpath,'$ORIGIN/../../Engine'

PGXS := $(shell pg_config --pgxs)
include $(PGXS)
