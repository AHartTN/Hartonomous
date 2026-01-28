# Hartonomous Engine Tests

enable_testing()

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

# Macro to add a test
macro(add_hartonomous_test TEST_NAME)
    add_executable(${TEST_NAME} ${TEST_NAME}.cpp)
    target_link_libraries(${TEST_NAME} PRIVATE ${TEST_LINK_LIBS} test_helpers)

    if(USE_GTEST)
        # Google Test integration
        gtest_discover_tests(${TEST_NAME})
    else()
        # Basic CTest integration
        add_test(NAME ${TEST_NAME} COMMAND ${TEST_NAME})
    endif()

    # Set working directory for tests
    set_tests_properties(${TEST_NAME} PROPERTIES
        WORKING_DIRECTORY ${CMAKE_CURRENT_BINARY_DIR}
    )
endmacro()

# Geometry tests
add_hartonomous_test(test_hopf_fibration)
add_hartonomous_test(test_super_fibonacci)
add_hartonomous_test(test_hilbert_curve_4d)

# Integration test (full pipeline)
add_hartonomous_test(test_codepoint_projection)

# Benchmark executables (not part of CTest)
add_executable(benchmark_blake3 benchmark_blake3.cpp)
target_link_libraries(benchmark_blake3 PRIVATE ${TEST_LINK_LIBS})

add_executable(benchmark_hilbert benchmark_hilbert.cpp)
target_link_libraries(benchmark_hilbert PRIVATE ${TEST_LINK_LIBS})

add_executable(benchmark_geometry benchmark_geometry.cpp)
target_link_libraries(benchmark_geometry PRIVATE ${TEST_LINK_LIBS})

# Example programs
add_executable(example_hopf_visualization example_hopf_visualization.cpp)
target_link_libraries(example_hopf_visualization PRIVATE ${TEST_LINK_LIBS})

add_executable(example_unicode_projection example_unicode_projection.cpp)
target_link_libraries(example_unicode_projection PRIVATE ${TEST_LINK_LIBS})
