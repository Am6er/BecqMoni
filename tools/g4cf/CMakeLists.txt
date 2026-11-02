cmake_minimum_required(VERSION 3.16)
project(g4cf CXX)
find_package(Geant4 REQUIRED)
include(${Geant4_USE_FILE})
add_executable(g4cf g4cf.cc)
target_link_libraries(g4cf ${Geant4_LIBRARIES})
# (AMBER274, физика 29) опора обратного рассеяния электронов, шесть физлистов
add_executable(g4eta g4eta.cc)
target_link_libraries(g4eta ${Geant4_LIBRARIES})
