// api.cu — единственная единица трансляции `rmgpu.dll`: всё устройство + хостовое API.
// Полоса П221 (`AMBER160`). Соглашения — README.md.
//
// Порядок работы хоста (C#, `GpuMatrix.cs`):
//   rm_init()                         — устройство, стек нити;
//   rm_cfg_set(имя, значение) × N     — настройки симулятора узла по имени;
//   rm_load(упаковка, длина)          — таблицы и сцена (разделы host.h);
//   rm_run(ветвь, NodeArgs-поля, …)   — ядро ветви, суммы обратно на хост;
//   rm_last_error()                   — текст последней ошибки (каждая функция
//                                       возвращает 0 при успехе).
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>
#include <stdexcept>

#define RM_MAIN_TU
#include "sim.cuh"
#include "tally.cuh"

#include "tables_dev.cuh"
#include "sim_geom.cuh"
#include "sim_photon.cuh"
#include "sim_electron.cuh"
#include "sim_branch.cuh"
#include "kernels.cuh"

#include "host.h"
#include "host_tables.h"
#include "host_scene.h"
#include "physics.h"

#define RM_API extern "C" __declspec(dllexport)

// (`AMBER219`) Отпечаток исходников ядра — ставит `build_gpu.cmd` ключом -DRM_SRC_HASH;
// сборка мимо скрипта несёт «unknown», и сторож `check_gpu_dll.py` её не примет.
#ifndef RM_SRC_HASH
#define RM_SRC_HASH "unknown"
#endif
#ifndef RM_ARCH
#define RM_ARCH "unknown"
#endif

namespace
{
    std::string lastError;
    Cfg hostCfg;
    std::vector<bool> cfgSet;
    bool cfgReady = false;

    // Устройство: арены и таблицы последней загрузки.
    std::vector<void*> deviceAllocs;
    bool loaded = false;

    struct CfgField { const char* name; char kind; size_t offset; };

#define RM_CFG_ENTRY(k, n) { #n, #k[0], offsetof(Cfg, n) },
    const CfgField cfgFields[] = { RM_CFG_FIELDS(RM_CFG_ENTRY) };
#undef RM_CFG_ENTRY
    const int cfgCount = (int)(sizeof(cfgFields) / sizeof(cfgFields[0]));

    void Check(cudaError_t e, const char* where)
    {
        if (e != cudaSuccess)
        {
            throw std::runtime_error(std::string(where) + ": " + cudaGetErrorString(e));
        }
    }

    void FreeDevice()
    {
        for (void* p : deviceAllocs) cudaFree(p);
        deviceAllocs.clear();
        loaded = false;
    }

    template <class T>
    const T* Upload(const std::vector<T>& v, const char* what)
    {
        if (v.empty()) return nullptr;
        void* p = nullptr;
        Check(cudaMalloc(&p, v.size() * sizeof(T)), what);
        deviceAllocs.push_back(p);
        Check(cudaMemcpy(p, v.data(), v.size() * sizeof(T), cudaMemcpyHostToDevice), what);
        return (const T*)p;
    }

    // vector<bool> не хранит байты подряд — отдельная перегрузка не нужна, арена B —
    // vector<unsigned char>.

    int Fail(const std::exception& e)
    {
        lastError = e.what();
        return 1;
    }

    // (`AMBER161`, П227) Буферы взвешенной ветви стадиями (kernels.cuh, `WBuf`): одна
    // порция историй, память держится между запусками и растёт по нужде.
    WBuf stageBuf = {};
    void* stageMem = nullptr;
    size_t stageBytes = 0;

    WBuf StageBuffers(int cap)
    {
        size_t r = sizeof(real), c = (size_t)cap;
        size_t need = c * (sizeof(unsigned int) + sizeof(int) + 1 + r * (6 + 3 + 9 + 3 + 8) + 3 * sizeof(int))
                      + 64 * 16 + 3 * sizeof(int);
        if (stageMem == nullptr || stageBuf.cap < cap)
        {
            if (stageMem != nullptr) cudaFree(stageMem);
            stageMem = nullptr;
            Check(cudaMalloc(&stageMem, need), "буферы стадий");
            stageBytes = need;
            unsigned char* p = (unsigned char*)stageMem;
            auto take = [&](size_t bytes) { void* q = p; p += (bytes + 63) & ~(size_t)63; return q; };
            stageBuf.cap = cap;
            stageBuf.c2 = (unsigned int*)take(c * sizeof(unsigned int));
            stageBuf.left = (int*)take(c * sizeof(int));
            stageBuf.rayValid = (unsigned char*)take(c);
            stageBuf.ray = (real*)take(c * 6 * r);
            stageBuf.src = (real*)take(c * 3 * r);
            stageBuf.f = (real*)take(c * 9 * r);
            stageBuf.score = (real*)take(c * r);
            stageBuf.hd = (real*)take(c * r);
            stageBuf.cosv = (real*)take(c * r);
            stageBuf.sc = (real*)take(c * 8 * r);
            stageBuf.listP = (int*)take(c * sizeof(int));
            stageBuf.listS = (int*)take(c * sizeof(int));
            stageBuf.listC = (int*)take(c * sizeof(int));
            stageBuf.counts = (int*)take(3 * sizeof(int));
        }

        return stageBuf;
    }

    // Взвешенная ветвь стадиями: порции по `cap` историй, стадии 1…5 подряд. Длины
    // списков стадии читают с устройства сами — хост не ждёт между запусками.
    void RunWeightedStaged(const NodeArgs& a, int threads)
    {
        const int cap = 1 << 20;
        int portion = (int)(a.n < cap ? a.n : cap);
        WBuf w = StageBuffers(portion);
        for (long long base = 0; base < a.n; base += portion)
        {
            int count = (int)((a.n - base) < portion ? (a.n - base) : portion);
            int blocks = (count + threads - 1) / threads;
            Check(cudaMemsetAsync(w.counts, 0, 3 * sizeof(int)), "списки стадий");
            WeightedStage1<<<blocks, threads>>>(a, w, base, count);
            WeightedStage2<<<blocks, threads>>>(a, w, base);
            WeightedStage3<<<blocks, threads>>>(a, w, base);
            WeightedStage4<<<blocks, threads>>>(a, w, base);
            WeightedStage5<<<blocks, threads>>>(a, w, count);
            Check(cudaGetLastError(), "запуск стадий");
        }
    }
}

RM_API const char* rm_last_error()
{
    return lastError.c_str();
}

// (`AMBER219`, П245) Паспорт сборки — строка «ключ=значение;»: отпечаток исходников ядра
// (`src`, sha256 файлов gpu/ — его сверяет `check_gpu_dll.py`), поколение физики (`phys`,
// physics.h — его сверяет приложение с `ResponseMatrix.PhysicsVersion`), архитектура
// (`arch`), тип real и версия рантайма CUDA, с которым слинковано ядро.
RM_API const char* rm_build_info()
{
    static std::string info;
    if (info.empty())
    {
        info = std::string("src=") + RM_SRC_HASH + ";phys=" + std::to_string(RM_PHYSICS_VERSION)
             + ";arch=" + RM_ARCH + ";real=" + (sizeof(real) == 4 ? "float" : "double")
             + ";cudart=" + std::to_string(CUDART_VERSION) + ";";
    }

    return info.c_str();
}

// (`AMBER219`) Годность устройства БЕЗ контекста: число устройств CUDA, имя и
// вычислительная способность первого, версии драйвера и рантайма. Возврат — код
// `cudaError_t` первого отказа (0 — годно; 100 — cudaErrorNoDevice, 35 —
// cudaErrorInsufficientDriver: драйвер старее рантайма), текст — `rm_last_error`.
// Ядер не запускает и памяти не трогает: форма приложения зовёт это при каждом открытии.
RM_API int rm_probe(int* count, int* ccMajor, int* ccMinor, int* driverVersion, int* runtimeVersion,
                    char* name, int nameLength)
{
    if (count) *count = 0;
    if (ccMajor) *ccMajor = 0;
    if (ccMinor) *ccMinor = 0;
    if (driverVersion) *driverVersion = 0;
    if (runtimeVersion) *runtimeVersion = CUDART_VERSION;
    if (name && nameLength > 0) name[0] = 0;
    int driver = 0;
    cudaError_t e = cudaDriverGetVersion(&driver);
    if (driverVersion) *driverVersion = driver;
    if (e != cudaSuccess) { lastError = cudaGetErrorString(e); return (int)e; }
    int n = 0;
    e = cudaGetDeviceCount(&n);
    if (e != cudaSuccess) { lastError = cudaGetErrorString(e); return (int)e; }
    if (count) *count = n;
    if (n < 1) { lastError = "no CUDA device"; return (int)cudaErrorNoDevice; }
    cudaDeviceProp prop;
    e = cudaGetDeviceProperties(&prop, 0);
    if (e != cudaSuccess) { lastError = cudaGetErrorString(e); return (int)e; }
    if (ccMajor) *ccMajor = prop.major;
    if (ccMinor) *ccMinor = prop.minor;
    if (name && nameLength > 0)
    {
        std::strncpy(name, prop.name, (size_t)nameLength - 1);
        name[nameLength - 1] = 0;
    }

    lastError.clear();
    return 0;
}

RM_API int rm_init(int device, long long stackBytes)
{
    try
    {
        Check(cudaSetDevice(device), "cudaSetDevice");
        // Рекурсия InCrystal ↔ ElectronLoss ↔ TransportElectron (глубина до 13
        // кадров) держится стеком нити; умолчание CUDA (1 КБ) её не вмещает.
        Check(cudaDeviceSetLimit(cudaLimitStackSize, (size_t)stackBytes), "cudaLimitStackSize");
        cfgSet.assign((size_t)cfgCount, false);
        std::memset(&hostCfg, 0, sizeof(hostCfg));
        cfgReady = false;
        return 0;
    }
    catch (const std::exception& e) { return Fail(e); }
}

// Настройка по имени. Незнакомое имя — отказ: C# завёл поле, которого GPU не знает.
RM_API int rm_cfg_set(const char* name, double value)
{
    try
    {
        if (cfgSet.size() != (size_t)cfgCount) cfgSet.assign((size_t)cfgCount, false);
        for (int i = 0; i < cfgCount; i++)
        {
            if (std::strcmp(cfgFields[i].name, name) != 0) continue;
            char* base = (char*)&hostCfg + cfgFields[i].offset;
            switch (cfgFields[i].kind)
            {
                case 'B': *(bool*)base = value != 0.0; break;
                case 'I': *(int*)base = (int)value; break;
                case 'L': *(long long*)base = (long long)value; break;
                case 'D': *(double*)base = value; break;
                default: throw std::runtime_error("cfg.h: неизвестный вид поля");
            }

            cfgSet[(size_t)i] = true;
            cfgReady = false;
            return 0;
        }

        throw std::runtime_error(std::string("настройка «") + name
            + "» GPU-пути не известна: поле EfficiencySimulator заведено после cfg.h "
              "— пересоздать cfg.h (gen_cfg.ps1) и перенести то, что оно включает");
    }
    catch (const std::exception& e) { return Fail(e); }
}

// Все ли настройки поставлены — и в константную память.
RM_API int rm_cfg_commit()
{
    try
    {
        std::string missing;
        for (int i = 0; i < cfgCount; i++)
        {
            if (!cfgSet[(size_t)i])
            {
                if (!missing.empty()) missing += ", ";
                missing += cfgFields[i].name;
            }
        }

        if (!missing.empty())
        {
            throw std::runtime_error("настройки не переданы с хоста: " + missing
                + " — поле есть в cfg.h, но в EfficiencySimulator его больше нет");
        }

        Check(cudaMemcpyToSymbol(C, &hostCfg, sizeof(Cfg)), "C");
        cfgReady = true;
        return 0;
    }
    catch (const std::exception& e) { return Fail(e); }
}

RM_API int rm_load(const unsigned char* blob, int length)
{
    try
    {
        FreeDevice();
        HostData h;
        BlobReader r(blob, (size_t)length, h);
        // Разделы — в порядке GpuPack.Pack: таблицы (GpuPackTables.cs), потом сцена
        // (GpuPackScene.cs). Имя раздела в потоке сверяется: перепутанный порядок —
        // отказ с именами, а не чтение чужих байтов.
        struct Sec { const char* name; void (*read)(BlobReader&); };
        const Sec order[] = {
            { "elements", Read_elements }, { "fluor", Read_fluor }, { "photoShell", Read_photoShell },
            { "relax", Read_relax }, { "atoms", Read_atoms }, { "electronMats", Read_electronMats },
            { "brems", Read_brems }, { "lightYields", Read_lightYields },
            { "materials", Read_materials }, { "fluorescers", Read_fluorescers },
            { "scatterers", Read_scatterers }, { "scatterElements", Read_scatterElements },
            { "regions", Read_regions }, { "scene", Read_scene },
        };

        for (const Sec& s : order)
        {
            std::string tag = r.Tag();
            if (tag != s.name)
            {
                throw std::runtime_error("упаковка: ожидался раздел «" + std::string(s.name)
                    + "», пришёл «" + tag + "»");
            }

            s.read(r);
        }

        if (!r.AtEnd()) throw std::runtime_error("упаковка: после раздела scene остались байты");
        if (h.scene.size() != 1) throw std::runtime_error("упаковка: сцена должна быть ровно одна");

        DevData d;
        std::memset(&d, 0, sizeof(d));
        d.R = Upload(h.R, "R");
        d.I = Upload(h.I, "I");
        d.B = Upload(h.B, "B");
        d.elements = Upload(h.elements, "elements");          d.nElements = (int)h.elements.size();
        d.fluor = Upload(h.fluor, "fluor");                    d.nFluor = (int)h.fluor.size();
        d.photoShell = Upload(h.photoShell, "photoShell");     d.nPhotoShell = (int)h.photoShell.size();
        d.relax = Upload(h.relax, "relax");                    d.nRelax = (int)h.relax.size();
        d.transitions = Upload(h.transitions, "transitions");  d.nTransitions = (int)h.transitions.size();
        d.atoms = Upload(h.atoms, "atoms");                    d.nAtoms = (int)h.atoms.size();
        d.electronMats = Upload(h.electronMats, "electronMats"); d.nElectronMats = (int)h.electronMats.size();
        d.brems = Upload(h.brems, "brems");                    d.nBrems = (int)h.brems.size();
        d.lightYields = Upload(h.lightYields, "lightYields");  d.nLightYields = (int)h.lightYields.size();
        d.elementsByZ = Upload(h.elementsByZ, "elementsByZ");
        d.fluorByZ = Upload(h.fluorByZ, "fluorByZ");
        d.photoShellByZ = Upload(h.photoShellByZ, "photoShellByZ");
        d.relaxByZ = Upload(h.relaxByZ, "relaxByZ");
        d.atomsByZ = Upload(h.atomsByZ, "atomsByZ");
        d.materials = Upload(h.materials, "materials");        d.nMaterials = (int)h.materials.size();
        d.fluorescers = Upload(h.fluorescers, "fluorescers");  d.nFluorescers = (int)h.fluorescers.size();
        d.scatterers = Upload(h.scatterers, "scatterers");     d.nScatterers = (int)h.scatterers.size();
        d.scatterElements = Upload(h.scatterElements, "scatterElements"); d.nScatterElements = (int)h.scatterElements.size();
        d.regions = Upload(h.regions, "regions");              d.nRegions = (int)h.regions.size();
        d.scene = Upload(h.scene, "scene");
        Check(cudaMemcpyToSymbol(D, &d, sizeof(DevData)), "D");
        loaded = true;
        return 0;
    }
    catch (const std::exception& e) { FreeDevice(); return Fail(e); }
}

// Одна ветвь узла: branch 0 — взвешенная (`WeightedKernel`), 1 — аналоговая (`AnalogKernel`).
// Накопители — хостовые массивы вызывающего: hist/light — bins, hist2 — bins (у
// взвешенной не трогается), chan — 7·bins, scal — W_SLOTS/A_SLOTS. Ядро ДОБАВЛЯЕТ к
// ним (хост обнуляет сам), поэтому узел можно дробить на несколько запусков.
// states — n состояний xorshift64* (rngMode 0) или null; perHistory — n записей или null.
RM_API int rm_run(int branch, double energyKev, double binKev, int bins,
                  long long n, long long first, int rngMode,
                  const unsigned long long* states, unsigned int key0, unsigned int key1,
                  double* hist, double* hist2, double* chan, double* light, double* scal, int scalCount,
                  HistoryOut* perHistory, int blocks, int threads)
{
    std::vector<void*> tmp;
    try
    {
        if (!loaded) throw std::runtime_error("rm_run до rm_load");
        if (!cfgReady) throw std::runtime_error("rm_run до rm_cfg_commit");
        int need = branch == 0 ? (int)W_SLOTS : (int)A_SLOTS;
        if (scalCount != need) throw std::runtime_error("scal: длина " + std::to_string(scalCount)
                                                        + ", ядро ждёт " + std::to_string(need));
        if (rngMode == 0 && states == nullptr) throw std::runtime_error("rngMode 0 без состояний");

        auto alloc = [&](size_t bytes) { void* p = nullptr; Check(cudaMalloc(&p, bytes), "cudaMalloc"); tmp.push_back(p); Check(cudaMemset(p, 0, bytes), "cudaMemset"); return p; };

        NodeArgs a;
        std::memset(&a, 0, sizeof(a));
        a.energyKev = (real)energyKev;
        a.binKev = (real)binKev;
        a.bins = bins;
        a.n = n;
        a.first = first;
        a.rngMode = rngMode;
        a.key0 = key0;
        a.key1 = key1;
        {
            const char* reset = std::getenv("BQ_GPU_RESET");
            a.resetMask = reset != nullptr ? std::atoi(reset) : 0;
        }
        a.hist = (double*)alloc(sizeof(double) * (size_t)bins);
        a.hist2 = (double*)alloc(sizeof(double) * (size_t)bins);
        a.chan = (double*)alloc(sizeof(double) * (size_t)bins * RM_CHANNELS);
        a.light = (double*)alloc(sizeof(double) * (size_t)bins);
        a.scal = (double*)alloc(sizeof(double) * (size_t)scalCount);
        if (states != nullptr)
        {
            a.states = (const unsigned long long*)alloc(sizeof(unsigned long long) * (size_t)n);
            Check(cudaMemcpy((void*)a.states, states, sizeof(unsigned long long) * (size_t)n, cudaMemcpyHostToDevice), "states");
        }

        if (perHistory != nullptr)
        {
            a.perHistory = (HistoryOut*)alloc(sizeof(HistoryOut) * (size_t)n);
        }

        // blocks == 0 (умолчание) — ОДНА ИСТОРИЯ НА НИТЬ: ceil(n / threads) блоков.
        // ⛔ (`AMBER161`, П227, 03.10.2026) Постоянные нити, принятые в П221, оказались
        // МЕДЛЕННЕЕ: нить вела свою долю историй подряд, и варп держал SM, пока не
        // кончит самая длинная из его нитей; короткие блоки планировщик доливает сам.
        // Замер (G1S_petri60_oisn16_167_p24, 144 узла × 3 млн, ядра): постоянные нити
        // 214.1 / 220.5 с, сетка 196.5 / 196.9 с (−10 %); тела матриц побайтно те же.
        // blocks < 0 — прежние постоянные нити (−k — k волн по занятости), для сверки.
        if (blocks == 0)
        {
            long long need = (n + threads - 1) / threads;
            blocks = (int)(need < 1 ? 1 : (need > 0x7FFFFFFFLL ? 0x7FFFFFFFLL : need));
        }
        else if (blocks < 0)
        {
            int perSm = 0, device = 0, sms = 0;
            Check(cudaGetDevice(&device), "cudaGetDevice");
            Check(cudaDeviceGetAttribute(&sms, cudaDevAttrMultiProcessorCount, device), "SM");
            if (branch == 0) Check(cudaOccupancyMaxActiveBlocksPerMultiprocessor(&perSm, WeightedKernel, threads, 0), "occupancy");
            else Check(cudaOccupancyMaxActiveBlocksPerMultiprocessor(&perSm, AnalogKernel, threads, 0), "occupancy");
            blocks = (perSm > 0 ? perSm : 1) * sms * (blocks < 0 ? -blocks : 1);
        }

        // (`AMBER161`) Взвешенная ветвь рабочего счёта — стадиями; одно ядро — ступень 1
        // (xorshift, выход историй), замеры сброса и `BQ_GPU_STAGED=0` (сверка).
        const char* stagedEnv = std::getenv("BQ_GPU_STAGED");
        bool staged = branch == 0 && rngMode == 1 && perHistory == nullptr && (a.resetMask & ~24) == 0
                      && !(stagedEnv != nullptr && stagedEnv[0] == '0');
        // (`AMBER161`) Аналоговая ветвь рабочего счёта — шагами с подкачкой историй и ПАЧКОЙ
        // входов в кристалл (`AnalogKernelRegen`); по историям точна (double: 288/288 в 1e-12).
        // Сама подкачка выигрыша не дала (1.04), пачка — да: порог `BQ_GPU_BATCH` (восьмые
        // доли активных нитей варпа, умолчание 8 — все): k0 (без пачки) 5.50–6.72 с,
        // k4 4.73–5.95, k8 4.66–5.75. `BQ_GPU_REGEN=0` — прежнее ядро «нить — история»,
        // `=2` — положительный контроль сверки (чужие потоки).
        const char* regenEnv = std::getenv("BQ_GPU_REGEN");
        bool regen = branch == 1 && rngMode == 1 && perHistory == nullptr && a.resetMask == 0
                     && !(regenEnv != nullptr && regenEnv[0] == '0');
        if (staged) RunWeightedStaged(a, threads);
        else if (regen)
        {
            int perSm = 0, device = 0, sms = 0;
            Check(cudaGetDevice(&device), "cudaGetDevice");
            Check(cudaDeviceGetAttribute(&sms, cudaDevAttrMultiProcessorCount, device), "SM");
            Check(cudaOccupancyMaxActiveBlocksPerMultiprocessor(&perSm, AnalogKernelRegen, threads, 0), "occupancy");
            unsigned long long* next = (unsigned long long*)alloc(sizeof(unsigned long long));
            AnalogKernelRegen<<<(perSm > 0 ? perSm : 1) * sms, threads>>>(a, next, regenEnv != nullptr && regenEnv[0] == '2',
                                                                        std::getenv("BQ_GPU_BATCH") != nullptr ? std::atoi(std::getenv("BQ_GPU_BATCH")) : 8);
        }
        else if (branch == 0) WeightedKernel<<<blocks, threads>>>(a);
        else AnalogKernel<<<blocks, threads>>>(a);
        Check(cudaGetLastError(), "запуск ядра");
        Check(cudaDeviceSynchronize(), "ядро");
#ifdef RM_PHASES
        {
            // (`AMBER161`) Замер частей истории: печать и обнуление (common.cuh, RM_PHASE_*).
            unsigned long long cyc[RM_PHASE_N], act[RM_PHASE_N], cnt[RM_PHASE_N], zero[RM_PHASE_N] = {};
            Check(cudaMemcpyFromSymbol(cyc, rmPhaseCycles, sizeof cyc), "фазы");
            Check(cudaMemcpyFromSymbol(act, rmPhaseActive, sizeof act), "фазы");
            Check(cudaMemcpyFromSymbol(cnt, rmPhaseCount, sizeof cnt), "фазы");
            Check(cudaMemcpyToSymbol(rmPhaseCycles, zero, sizeof zero), "фазы");
            Check(cudaMemcpyToSymbol(rmPhaseActive, zero, sizeof zero), "фазы");
            Check(cudaMemcpyToSymbol(rmPhaseCount, zero, sizeof zero), "фазы");
            std::fprintf(stderr, "PHASES branch %d E %.3f", branch, energyKev);
            for (int p = 0; p < RM_PHASE_N; p++)
            {
                if (cnt[p] == 0) continue;
                std::fprintf(stderr, " | %d: n %llu cyc %.4g act %.2f", p, cnt[p], (double)cyc[p], (double)act[p] / (double)cnt[p]);
            }
            std::fprintf(stderr, "\n");
        }
#endif

        auto add = [&](double* host, const double* dev, size_t count, const char* what)
        {
            if (host == nullptr) return;
            std::vector<double> v(count);
            Check(cudaMemcpy(v.data(), dev, sizeof(double) * count, cudaMemcpyDeviceToHost), what);
            for (size_t i = 0; i < count; i++) host[i] += v[i];
        };

        add(hist, a.hist, (size_t)bins, "hist");
        add(hist2, a.hist2, (size_t)bins, "hist2");
        add(chan, a.chan, (size_t)bins * RM_CHANNELS, "chan");
        add(light, a.light, (size_t)bins, "light");
        add(scal, a.scal, (size_t)scalCount, "scal");
        if (perHistory != nullptr)
        {
            Check(cudaMemcpy(perHistory, a.perHistory, sizeof(HistoryOut) * (size_t)n, cudaMemcpyDeviceToHost), "perHistory");
        }

        for (void* p : tmp) cudaFree(p);
        return 0;
    }
    catch (const std::exception& e)
    {
        for (void* p : tmp) cudaFree(p);
        return Fail(e);
    }
}

RM_API int rm_sizeof_history_out() { return (int)sizeof(HistoryOut); }
RM_API int rm_real_bytes() { return (int)sizeof(real); }
RM_API int rm_slots(int branch) { return branch == 0 ? (int)W_SLOTS : (int)A_SLOTS; }

RM_API void rm_shutdown()
{
    FreeDevice();
    // (`AMBER219`) Буферы стадий живут в памяти устройства, которую сброс снимает целиком:
    // забыть указатель обязательно, иначе следующий `rm_init` → `StageBuffers` отдал бы
    // ядрам мёртвую память. В пробе за процесс один init и ни одного shutdown — там это не
    // проявлялось; приложение считает много раз за жизнь процесса.
    stageMem = nullptr;
    stageBytes = 0;
    stageBuf = WBuf{};
    cfgSet.clear();
    cfgReady = false;
    cudaDeviceReset();
}
