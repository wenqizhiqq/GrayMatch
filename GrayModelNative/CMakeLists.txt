cmake_minimum_required(VERSION 3.15)
project(GrayModelNative LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Point to the prebuilt opencv_world package produced by the Windows installer.
# (For vcpkg / system installs, replace these with find_package(OpenCV REQUIRED).)
set(_ocv "${CMAKE_CURRENT_SOURCE_DIR}/../ext/opencv/opencv")
set(OpenCV_INCLUDE_DIRS "${_ocv}/build/include")
set(OpenCV_LIBS "${_ocv}/build/x64/vc16/lib/opencv_world480.lib")

add_definitions(-DGRAYMODEL_EXPORTS)

add_library(GrayModelNative SHARED
    gray_model_native.cpp
    gray_model_native.h
)

target_include_directories(GrayModelNative PRIVATE ${OpenCV_INCLUDE_DIRS})
target_link_libraries(GrayModelNative PRIVATE ${OpenCV_LIBS})

# OpenMP for a parallel angle sweep (big speedup on multi-core).
# vcomp.lib is the MSVC OpenMP import library.
if(MSVC)
    add_compile_options(/openmp /utf-8)
    target_link_libraries(GrayModelNative PRIVATE vcomp.lib)
endif()
