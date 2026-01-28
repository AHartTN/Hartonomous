/**
 * @file main.cpp
 * @brief PostgreSQL extension entry point
 */

// Must include PostgreSQL headers BEFORE C++ headers for proper linkage
extern "C" {
#include <postgres.h>
#include <fmgr.h>

// Extension magic - MUST be in extern "C" block
PG_MODULE_MAGIC;
}

// All function implementations are in hartonomous_functions.cpp
