#include <gtest/gtest.h>
#include <catch2/catch.hpp>

// A simple GTest
TEST(SanityCheck, GTestWorks) {
    EXPECT_EQ(1, 1);
}

// Just checking if Catch2 header is found (no main needed here for this check)
