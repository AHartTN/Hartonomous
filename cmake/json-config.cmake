# ==============================================================================
#  NLOHMANN JSON CONFIGURATION (HEADER-ONLY)
# ==============================================================================
#
#  nlohmann/json is a modern C++ JSON library.
#  Header-only library, no compilation required.
#
# ==============================================================================

get_filename_component(JSON_ROOT "${CMAKE_CURRENT_LIST_DIR}/../Engine/external/json" ABSOLUTE)

if(NOT EXISTS "${JSON_ROOT}/include/nlohmann/json.hpp")
    message(FATAL_ERROR "nlohmann/json not found at ${JSON_ROOT}")
endif()

if(NOT TARGET nlohmann_json::nlohmann_json)
    add_library(nlohmann_json_interface INTERFACE IMPORTED GLOBAL)

    target_include_directories(nlohmann_json_interface INTERFACE
        "${JSON_ROOT}/include"
    )

    # Alias to the standard namespace
    add_library(nlohmann_json::nlohmann_json ALIAS nlohmann_json_interface)

    message(STATUS "nlohmann/json: Header-only JSON library configured")
endif()
