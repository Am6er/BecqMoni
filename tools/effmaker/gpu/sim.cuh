// sim.cuh — состояние одной нити-истории (`struct Sim`) и глобальные данные устройства.
// Полоса П221 (`AMBER160`). Соглашения — README.md.
//
// Поля и объявления методов `Sim` приходят из файлов `decl_<модуль>.inc` — у каждого
// модуля свой, чтобы полосы переноса не правили один файл. Определения — в
// `sim_<модуль>.cuh`, все они включаются в `kernel.cu` ПОСЛЕ этого файла.
#pragma once
#include "common.cuh"
#include "cfg.h"
#include "data.cuh"

// Настройки прогона (= открытые поля `EfficiencySimulator`, cfg.h) и указатели на
// данные — одинаковы для всех нитей, поэтому в константной памяти.
// В переносе: `this.XrayEscape` → `C.XrayEscape`; таблицы — `D.<таблица>[i]`.
// Определение — в единственной единице трансляции (`api.cu` ставит RM_MAIN_TU): без
// `-rdc` пара «extern + определение» в одном файле — C2086 у хостовой половины nvcc.
#ifdef RM_MAIN_TU
__constant__ Cfg C;
__constant__ DevData D;
#else
extern __constant__ Cfg C;
extern __constant__ DevData D;
#endif

struct Sim
{
    RngState rng;

    // = EfficiencySimulator.Uniform (EfficiencySimulator.cs:10571); ResetStream
    // на устройстве не зовётся — состояние ставит ядро.
    // ⚠ Во float число 1 − 2⁻⁵⁴ округляется в РОВНО 1.0f, и `−log(1 − u)` дал бы
    // бесконечность: верх зажат последним float меньше единицы.
    RM_DEV real Uniform()
    {
#ifdef RM_REAL_FLOAT
        // (`AMBER161`) Рабочий режим (Philox) — float из одного слова (`RngUniformF`);
        // xorshift (сверка с CPU, `BQ_GPU_CHECK_FLOAT`) — прежним путём через double.
        if (rng.mode != 0)
        {
            float f = RngUniformF(rng);
            return f > 0.99999994f ? 0.99999994f : f;
        }
#endif
        double u = RngUniform(rng);
#ifdef RM_REAL_FLOAT
        if (u > 0.99999994) u = 0.99999994;
#endif
        return (real)u;
    }

#include "decl_geom.inc"
#include "decl_photon.inc"
#include "decl_electron.inc"
#include "decl_branch.inc"
};
