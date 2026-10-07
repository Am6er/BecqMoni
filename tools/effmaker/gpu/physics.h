// physics.h — поколение физики переноса, которое несёт эта сборка ядра (`AMBER219`, П245).
//
// Число — копия `ResponseMatrix.PhysicsVersion` (BecquerelMonitor/EfficiencyMaker/ResponseMatrix.cs)
// на момент, когда перенос `sim_*.cuh` сверен с C#-оригиналом. Приложение читает его из
// `rm_build_info()` и при расхождении со своим `PhysicsVersion` гасит галку «Use Nvidia GPU»
// с причиной «библиотека собрана для другой физики»; сторож `tools/check_gpu_dll.py` сверяет
// то же на дереве. Поднимая `PhysicsVersion` в C#, перенеси правку в ядро и подними число здесь.
#pragma once
#define RM_PHYSICS_VERSION 26
