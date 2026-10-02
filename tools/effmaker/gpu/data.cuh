// data.cuh — неизменяемые данные устройства: три арены и таблицы структур.
// Полоса П221 (`AMBER160`). Правила зеркалирования C# — README.md, «Данные устройства».
//
// ⚠ МАССИВЫ В СТРУКТУРАХ — НЕ УКАЗАТЕЛИ, А СМЕЩЕНИЯ в арены (`int`): `R` — числа
// `real`, `I` — целые, `B` — байты-признаки. Так структура одинакова на хосте и на
// устройстве и не требует перебивки указателей после копирования. Доступ:
//   RR(e.EnergyKev)[i]   — элемент массива чисел,
//   II(f.Z)[k]           — элемент массива целых,
//   BB(r.Flags)[k]       — элемент массива байтов.
// Длина массива — соседнее поле `<Имя>Len`. Зубчатый — плоский массив `<Имя>` и
// смещения строк `<Имя>Off` (в арене `I`, n + 1 чисел, ОТНОСИТЕЛЬНО начала `<Имя>`).
#pragma once
#include "common.cuh"

#define RR(off) (D.R + (off))
#define II(off) (D.I + (off))
#define BB(off) (D.B + (off))

#include "data_tables.cuh"   // сечения, рассеяние, релаксация, электроны, тормозное, свет
#include "data_scene.cuh"    // вещества сцены, области, источник, сцена

struct DevData
{
    const real* R;
    const int* I;
    const unsigned char* B;

    const ElementG* elements;             int nElements;
    const FluorescenceG* fluor;           int nFluor;
    const PhotoShellModelG* photoShell;   int nPhotoShell;
    const RelaxationG* relax;             int nRelax;
    const TransitionsG* transitions;      int nTransitions;
    const AtomG* atoms;                   int nAtoms;
    const ElectronMaterialG* electronMats; int nElectronMats;
    const ThickTargetBremG* brems;        int nBrems;
    const LightYieldCurveG* lightYields;  int nLightYields;

    // Поиск по Z (−1 — нет), длина 128.
    const int* elementsByZ;
    const int* fluorByZ;
    const int* photoShellByZ;
    const int* relaxByZ;
    const int* atomsByZ;

    const MaterialG* materials;           int nMaterials;
    const FluorescersG* fluorescers;      int nFluorescers;
    const ScatterersG* scatterers;        int nScatterers;
    const ScatterElementG* scatterElements; int nScatterElements;
    const RegionG* regions;               int nRegions;
    const SceneG* scene;                  // ровно одна
};
