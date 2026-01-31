# cmake/treesitter-config.cmake

# 1. Calculate Root Relative to This File
get_filename_component(PROJECT_ROOT "${CMAKE_CURRENT_LIST_DIR}/.." ABSOLUTE)
set(TS_SUBMODULE_PATH "${PROJECT_ROOT}/Engine/external/tree-sitter")

# 2. Header (tree_sitter/api.h)
#    Tree-sitter keeps headers in 'lib/include'
find_path(TreeSitter_INCLUDE_DIR
    NAMES tree_sitter/api.h
    PATHS "${TS_SUBMODULE_PATH}/lib/include"
    NO_DEFAULT_PATH
    NO_CMAKE_ENVIRONMENT_PATH
    NO_CMAKE_SYSTEM_PATH
)

# 3. Library (libtree-sitter.a)
#    The standard Makefile usually outputs the static lib to the root of the repo
find_library(TreeSitter_LIBRARY
    NAMES tree-sitter libtree-sitter
    PATHS 
        "${TS_SUBMODULE_PATH}"      # Makefile output
        "${TS_SUBMODULE_PATH}/lib"  # Potential other layouts
    NO_DEFAULT_PATH
    NO_CMAKE_ENVIRONMENT_PATH
    NO_CMAKE_SYSTEM_PATH
)

# 4. Create Target
if(TreeSitter_INCLUDE_DIR AND TreeSitter_LIBRARY)
    if(NOT TARGET tree-sitter::tree-sitter)
        add_library(tree-sitter::tree-sitter UNKNOWN IMPORTED)
        set_target_properties(tree-sitter::tree-sitter PROPERTIES
            IMPORTED_LOCATION "${TreeSitter_LIBRARY}"
            INTERFACE_INCLUDE_DIRECTORIES "${TreeSitter_INCLUDE_DIR}"
        )
        message(STATUS "Tree-sitter: Found at ${TS_SUBMODULE_PATH}")
    endif()
else()
    message(FATAL_ERROR "Tree-sitter not found in submodule: ${TS_SUBMODULE_PATH}\nDid you run 'make' inside Engine/external/tree-sitter?")
endif()