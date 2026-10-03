// common.cuh — тип `real`, обёртки Math.* с семантикой .NET, генераторы случайных чисел.
// Полоса П221 (`AMBER160`). Соглашения — README.md этого каталога.
#pragma once
#include <cuda_runtime.h>
#include <stdint.h>
#include <float.h>
#include <math.h>

#ifdef RM_REAL_FLOAT
typedef float real;
#define RM_REAL_MAX FLT_MAX
#else
typedef double real;
#define RM_REAL_MAX DBL_MAX
#endif

#define RM_DEV __device__ __forceinline__
#define RM_DEVF __device__

// double.MaxValue / double.MinValue C#. ⚠ В режиме float число 1.79e308 не
// представимо: сравнения «step == double.MaxValue» в переносе пишутся через
// RM_REAL_MAX, а не через литерал.
#define CS_DOUBLE_MAX RM_REAL_MAX
#define CS_DOUBLE_MIN (-RM_REAL_MAX)

// --- Math.* с семантикой .NET Framework 4.8 ---------------------------------
// Math.Max/Min(double, double): NaN в любом аргументе даёт NaN, и порядок
// проверок ровно как в справочном коде .NET (`if (a > b) return a; if (IsNaN(a))
// return a; return b;`). fmax/fmin так НЕ делают — возвращают не-NaN.
RM_DEV real M_Max(real a, real b) { if (a > b) return a; if (a != a) return a; return b; }
RM_DEV real M_Min(real a, real b) { if (a < b) return a; if (a != a) return a; return b; }
RM_DEV int M_Max(int a, int b) { return a > b ? a : b; }
RM_DEV int M_Min(int a, int b) { return a < b ? a : b; }
RM_DEV long long M_Max(long long a, long long b) { return a > b ? a : b; }
RM_DEV long long M_Min(long long a, long long b) { return a < b ? a : b; }
RM_DEV real M_Abs(real a) { return fabs(a); }
RM_DEV int M_Abs(int a) { return a < 0 ? -a : a; }
RM_DEV real M_Sqrt(real a) { return sqrt(a); }
RM_DEV real M_Log(real a) { return log(a); }
RM_DEV real M_Log10(real a) { return log10(a); }
RM_DEV real M_Exp(real a) { return exp(a); }
RM_DEV real M_Pow(real a, real b) { return pow(a, b); }
RM_DEV real M_Sin(real a) { return sin(a); }
RM_DEV real M_Cos(real a) { return cos(a); }
RM_DEV real M_Tan(real a) { return tan(a); }
RM_DEV real M_Acos(real a) { return acos(a); }
RM_DEV real M_Asin(real a) { return asin(a); }
RM_DEV real M_Atan(real a) { return atan(a); }
RM_DEV real M_Atan2(real y, real x) { return atan2(y, x); }
RM_DEV real M_Floor(real a) { return floor(a); }
RM_DEV real M_Ceiling(real a) { return ceil(a); }
RM_DEV real M_Sinh(real a) { return sinh(a); }
RM_DEV real M_Cosh(real a) { return cosh(a); }
RM_DEV real M_Tanh(real a) { return tanh(a); }
// Math.Round(double) — к ЧЁТНОМУ (MidpointRounding.ToEven), это rint, а не round.
RM_DEV real M_Round(real a) { return rint(a); }
// Math.Sign(double): -1/0/1 (NaN в .NET бросает — здесь не бывает).
RM_DEV int M_Sign(real a) { return a > 0 ? 1 : (a < 0 ? -1 : 0); }
RM_DEV bool M_IsNaN(real a) { return a != a; }
RM_DEV bool M_IsInfinity(real a) { return isinf(a); }
RM_DEV bool M_IsFinite(real a) { return isfinite(a); }

// --- допуски обхода сцены ------------------------------------------------------
// В double — РОВНО константы C# (ступень 1 сверяет историю побитово): подталкивание
// через границу 1e-7 см (`step + 1e-7`, EfficiencySimulator.cs:4822 и ещё семь мест),
// порог «позади точки» 1e-7 см (`CollectCrossings` :4495, `At` :4031), «позади начала
// луча» −1e-9 см и отклонение² 1e-12 см² (`OnCachedRay` :4424).
//
// ⛔ Во float те же числа вырождаются (map_geometry.md §5.4): шаг представления float на
// 10 см ≈ 9.5e-7 см, подталкивание 1e-7 точку НЕ сдвигает, обход крутится на границе или
// перескакивает тонкий слой (замер П221 02.10.2026: на 6 кэВ у G1S24 истории проходили
// корпус с τ ≈ 44 вместо > 60). Поэтому во float: подталкивание 2e-5 см (0.2 мкм — больше
// 20 шагов float на 30 см и физически ничто), пороги — той же шкалы.
#ifdef RM_REAL_FLOAT
#define RM_NUDGE ((real)2e-5)
#define RM_BEHIND ((real)1e-5)
#define RM_RAY_BACK ((real)1e-5)
#define RM_ON_RAY2 ((real)1e-9)
#else
#define RM_NUDGE ((real)1e-7)
#define RM_BEHIND ((real)1e-7)
#define RM_RAY_BACK ((real)1e-9)
#define RM_ON_RAY2 ((real)1e-12)
#endif

// Константы Math.PI / Math.E с точностью real.
#define M_PI_R ((real)3.14159265358979323846)
#define M_E_R ((real)2.71828182845904523536)

// --- генераторы --------------------------------------------------------------
// Режим 0 — xorshift64*, ПОБИТНО как `EfficiencySimulator.Uniform`
// (EfficiencySimulator.cs:10571): ступень 1 приёмки (сверка по историям)
// получает состояние CPU на начало истории.
// Режим 1 — Philox4x32-10 со счётчиком (узел, история, номер выдачи): рабочий.
struct RngState
{
    uint64_t s;          // xorshift64*: состояние; Philox: не используется
    uint32_t key0, key1; // Philox: ключ (зерно узла)
    uint32_t c0, c1;     // Philox: номер истории (64 бита)
    uint32_t c2;         // Philox: номер блока выдачи
    uint32_t buf[4];
    int left;            // сколько слов осталось в buf
    int mode;            // 0 — xorshift64*, 1 — Philox
};

RM_DEV void PhiloxRound(uint32_t c[4], uint32_t k0, uint32_t k1)
{
    const uint32_t M0 = 0xD2511F53u, M1 = 0xCD9E8D57u;
    uint32_t hi0 = __umulhi(M0, c[0]), lo0 = M0 * c[0];
    uint32_t hi1 = __umulhi(M1, c[2]), lo1 = M1 * c[2];
    uint32_t n0 = hi1 ^ c[1] ^ k0;
    uint32_t n1 = lo1;
    uint32_t n2 = hi0 ^ c[3] ^ k1;
    uint32_t n3 = lo0;
    c[0] = n0; c[1] = n1; c[2] = n2; c[3] = n3;
}

RM_DEV void Philox4x32_10(uint32_t out[4], uint32_t c0, uint32_t c1, uint32_t c2, uint32_t c3,
                          uint32_t k0, uint32_t k1)
{
    uint32_t c[4] = { c0, c1, c2, c3 };
    const uint32_t W0 = 0x9E3779B9u, W1 = 0xBB67AE85u;
#pragma unroll
    for (int i = 0; i < 10; i++)
    {
        PhiloxRound(c, k0, k1);
        k0 += W0; k1 += W1;
    }
    out[0] = c[0]; out[1] = c[1]; out[2] = c[2]; out[3] = c[3];
}

// Равномерное в (0, 1), открытое с обеих сторон, как у CPU: ((r>>11)+0.5)/2^53.
RM_DEV double RngUniform(RngState& g)
{
    if (g.mode == 0)
    {
        g.s ^= g.s >> 12;
        g.s ^= g.s << 25;
        g.s ^= g.s >> 27;
        uint64_t r = g.s * 2685821657736338717ULL;
        return ((double)(r >> 11) + 0.5) * (1.0 / 9007199254740992.0);
    }

    if (g.left < 2)
    {
        Philox4x32_10(g.buf, g.c0, g.c1, g.c2, 0u, g.key0, g.key1);
        g.c2++;
        g.left = 4;
    }

    uint64_t hi = g.buf[4 - g.left];
    uint64_t lo = g.buf[5 - g.left];
    g.left -= 2;
    uint64_t r = (hi << 32) | lo;
    return ((double)(r >> 11) + 0.5) * (1.0 / 9007199254740992.0);
}

// (`AMBER161`, П227) Равномерное в (0, 1) ВО FLOAT из ОДНОГО 32-битного слова Philox:
// w·2⁻³² + 2⁻³³ (низ — 1.2e-10, не ноль). Путь выше тратит два слова и арифметику double,
// а FP64 на GA104 в 64 раза медленнее FP32: генератор занимал ~16 % выборок профиля.
// Только режим 1; верх (округление к 1.0f) зажимает вызывающий. Поток розыгрышей другой,
// чем у RngUniform, — сверка с прежним GPU статистикой, не побитовая.
RM_DEV float RngUniformF(RngState& g)
{
    if (g.left < 1)
    {
        Philox4x32_10(g.buf, g.c0, g.c1, g.c2, 0u, g.key0, g.key1);
        g.c2++;
        g.left = 4;
    }

    uint32_t w = g.buf[4 - g.left];
    g.left -= 1;
    return __uint2float_rn(w) * 2.3283064365386963e-10f + 1.1641532182693481e-10f;
}

// SplitMix64 — `EfficiencySimulator.MixSeed` (EfficiencySimulator.cs:10562).
RM_DEV uint64_t MixSeed(uint64_t seed)
{
    uint64_t z = seed + 0x9E3779B97F4A7C15ULL;
    z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ULL;
    z = (z ^ (z >> 27)) * 0x94D049BB133111EBULL;
    z ^= z >> 31;
    return z | 1ULL;
}
