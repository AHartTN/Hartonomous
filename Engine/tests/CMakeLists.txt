# Hartonomous Engine Tests

# Google Test integration (optional, falls back to basic tests if not available)
find_package(GTest QUIET)

if(GTest_FOUND)
    message(STATUS "Google Test found, using for unit tests")
    set(USE_GTEST ON)
else()
    message(STATUS "Google Test not found, using basic test framework")
    set(USE_GTEST OFF)
endif()

# Test helper library
add_library(test_helpers INTERFACE)
target_include_directories(test_helpers INTERFACE ${CMAKE_CURRENT_SOURCE_DIR})

# Common test dependencies
set(TEST_LINK_LIBS
    engine
    Eigen3::Eigen
    MKL::MKL
    blake3_impl
)

if(USE_GTEST)
    list(APPEND TEST_LINK_LIBS GTest::GTest GTest::Main)
endif()

# Macro to add a test with category label
macro(add_hartonomous_test REL_PATH CATEGORY)
    # Convert path to valid target name (e.g. unit/test_hashing -> unit_test_hashing)
    string(REPLACE "/" "_" TEST_NAME ${REL_PATH})
    
    add_executable(${TEST_NAME} ${REL_PATH}.cpp)
    target_link_libraries(${TEST_NAME} PRIVATE ${TEST_LINK_LIBS} test_helpers)

    if(USE_GTEST)
        # Google Test integration
        gtest_discover_tests(
            ${TEST_NAME}
            PROPERTIES 
                LABELS "${CATEGORY}"
                WORKING_DIRECTORY ${CMAKE_CURRENT_BINARY_DIR}
                ENVIRONMENT "LD_LIBRARY_PATH=$<TARGET_FILE_DIR:engine>:$ENV{LD_LIBRARY_PATH}"
            DISCOVERY_MODE PRE_TEST
        )
    else()
        # Basic CTest integration
        add_test(NAME ${TEST_NAME} COMMAND ${TEST_NAME})
        set_tests_properties(${TEST_NAME} PROPERTIES
            LABELS "${CATEGORY}"
            WORKING_DIRECTORY ${CMAKE_CURRENT_BINARY_DIR}
        )
    endif()
endmacro()

# ==============================================================================
# Unit Tests (No external dependencies - pure logic)
# ==============================================================================
add_hartonomous_test(unit/test_hashing "unit")
add_hartonomous_test(unit/test_geometry_core "unit")
add_hartonomous_test(unit/test_projections "unit")
add_hartonomous_test(unit/test_spatial_index "unit")
add_hartonomous_test(unit/test_database_marshal "unit")

# ==============================================================================
# Integration Tests (Require database and/or external services)
# ==============================================================================
add_hartonomous_test(integration/test_interop_functionality "integration")

# ==============================================================================
# End-to-End Tests (Full pipeline validation)
# ==============================================================================
add_hartonomous_test(e2e/test_end_to_end_pipeline "e2e")

# Benchmark executables (not part of CTest)

# Example programs

add_executable(example_unicode_projection example_unicode_projection.cpp)
target_link_libraries(example_unicode_projection PRIVATE ${TEST_LINK_LIBS})
