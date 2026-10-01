cmake_minimum_required(VERSION 3.16)
project(soapy-vst CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

if(NOT CMAKE_BUILD_TYPE)
  set(CMAKE_BUILD_TYPE Release CACHE STRING "" FORCE)
endif()

# Prefer radioconda SoapySDR (SOAPY_SDR_ROOT or CMAKE_PREFIX_PATH)
if(DEFINED ENV{SOAPY_SDR_ROOT})
  list(APPEND CMAKE_PREFIX_PATH "$ENV{SOAPY_SDR_ROOT}")
endif()
if(DEFINED ENV{CONDA_PREFIX})
  list(APPEND CMAKE_PREFIX_PATH "$ENV{CONDA_PREFIX}/Library" "$ENV{CONDA_PREFIX}")
endif()

find_package(SoapySDR CONFIG REQUIRED)
message(STATUS "SoapySDR ${SoapySDR_VERSION} found")

set(NIRFSA_INCLUDE_CANDIDATES
  "C:/Program Files/IVI Foundation/IVI/Include"
  "C:/Program Files (x86)/IVI Foundation/IVI/Include"
)
set(NIRFSA_LIB_CANDIDATES
  "C:/Program Files/IVI Foundation/IVI/Lib_x64/msc"
  "C:/Program Files/IVI Foundation/IVI/Lib_x64"
  "C:/Program Files (x86)/IVI Foundation/IVI/Lib_x64/msc"
)

find_path(NIRFSA_INCLUDE_DIR niRFSA.h PATHS ${NIRFSA_INCLUDE_CANDIDATES} REQUIRED)
find_library(NIRFSA_LIBRARY NAMES niRFSA_64 niRFSA PATHS ${NIRFSA_LIB_CANDIDATES})

if(NOT NIRFSA_LIBRARY)
  # Link by import name; loader finds niRFSA_64.dll on PATH / IVI Bin
  set(NIRFSA_LIBRARY "niRFSA_64")
  message(WARNING "niRFSA import lib not found; linking by name niRFSA_64")
endif()

message(STATUS "NI-RFSA include: ${NIRFSA_INCLUDE_DIR}")
message(STATUS "NI-RFSA library: ${NIRFSA_LIBRARY}")

set(VISA_INCLUDE_CANDIDATES
  "C:/Program Files/IVI Foundation/VISA/Win64/Include"
  "C:/Program Files (x86)/IVI Foundation/VISA/WinNT/Include"
)
find_path(VISA_INCLUDE_DIR visa.h PATHS ${VISA_INCLUDE_CANDIDATES})
if(VISA_INCLUDE_DIR)
  message(STATUS "NI-VISA include: ${VISA_INCLUDE_DIR}")
else()
  message(WARNING "visa.h not found — IVI headers may fail to compile")
endif()

set(SOURCES
  src/Registration.cpp
  src/Settings.cpp
  src/Streaming.cpp
  src/TxStream.cpp
  src/RfsaFetchBackend.cpp
  src/FpgaDmaBackend.cpp
)

SOAPY_SDR_MODULE_UTIL(
  TARGET vstSupport
  SOURCES ${SOURCES}
  LIBRARIES ${NIRFSA_LIBRARY}
)

target_include_directories(vstSupport PRIVATE
  ${CMAKE_CURRENT_SOURCE_DIR}/include
  ${NIRFSA_INCLUDE_DIR}
  ${VISA_INCLUDE_DIR}
)

target_compile_definitions(vstSupport PRIVATE
  NOMINMAX
  WIN32_LEAN_AND_MEAN
)

# NI-RFSA import lib references legacy sprintf; required on VS2015+ toolchains
if(MSVC)
  target_compile_options(vstSupport PRIVATE /utf-8)
  target_link_libraries(vstSupport PRIVATE legacy_stdio_definitions)
endif()

install(FILES README.md DESTINATION share/doc/soapy-vst)
install(FILES grc/vst_live_source.block.yml DESTINATION share/gnuradio/grc/blocks)
