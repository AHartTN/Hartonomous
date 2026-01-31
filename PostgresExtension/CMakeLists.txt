# PostgresExtension/CMakeLists.txt

# This directory contains PostgreSQL extensions. 
# We use custom targets to build them via their respective Makefiles (PGXS).

set(S3_EXT_DIR "${CMAKE_CURRENT_SOURCE_DIR}/s3")
set(HART_EXT_DIR "${CMAKE_CURRENT_SOURCE_DIR}/hartonomous")

# Custom target for s3 extension
add_custom_target(s3_extension ALL
    COMMAND make
    WORKING_DIRECTORY "${S3_EXT_DIR}"
    COMMENT "Building s3 PostgreSQL extension"
    DEPENDS engine
)

# Custom target for hartonomous extension
add_custom_target(hartonomous_extension ALL
    COMMAND make
    WORKING_DIRECTORY "${HART_EXT_DIR}"
    COMMENT "Building hartonomous PostgreSQL extension"
    DEPENDS s3_extension
)