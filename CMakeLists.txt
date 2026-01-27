cmake_minimum_required(VERSION 3.25)

project(Hartonomous LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_subdirectory(engine)
add_subdirectory(postgres_ext)
