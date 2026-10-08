cmake_minimum_required(VERSION 3.20)
project(LoudifyLimiter CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

if(MSVC)
    # Static CRT: the DLL is loaded by audiodg.exe, which must not depend on a
    # Visual C++ redistributable being installed.
    set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
    add_compile_options(/W4 /permissive-)
    add_compile_definitions(_CRT_SECURE_NO_WARNINGS)
else()
    add_compile_options(-Wall -Wextra)
endif()

add_library(LoudifyLimiter SHARED src/VstPlugin.cpp)
set_target_properties(LoudifyLimiter PROPERTIES PREFIX "")
if(MINGW)
    target_link_options(LoudifyLimiter PRIVATE -static -static-libgcc -static-libstdc++)
endif()

include(CTest)
if(BUILD_TESTING)
    add_executable(LimiterTests tests/LimiterTests.cpp src/VstPlugin.cpp)
    add_test(NAME LimiterTests COMMAND LimiterTests)
endif()
