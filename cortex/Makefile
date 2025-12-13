# Cortex PostgreSQL Extension Makefile

MODULE_big = cortex
OBJS = cortex.o lmds_projector.o

# Extension metadata
EXTENSION = cortex
DATA = cortex--1.0.sql

# C++ compilation flags
CXX = g++
CXXFLAGS = -std=c++17 -O2 -fPIC -I/usr/include/eigen3

# Link against C++ standard library
SHLIB_LINK = -lstdc++

# PostgreSQL extension build framework
PG_CONFIG = pg_config
PGXS := $(shell $(PG_CONFIG) --pgxs)
include $(PGXS)

# Compile C++ module
lmds_projector.o: lmds_projector.cpp lmds_projector.h
	$(CXX) $(CXXFLAGS) -c $< -o $@
