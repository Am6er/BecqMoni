// rm_replay.cpp — нативный повторитель вызовов rmgpu (`AMBER161`, П227).
//
// .NET-обвязка (`GpuMatrixRun.cs`, RmGpu) при `BQ_GPU_DUMP=<каталог>` пишет calls.txt:
// init, настройки (cfg имя биты-double), commit, load blobN.bin, run (параметры запуска)
// и sums (приращение сумм этого запуска). Повторитель зовёт ту же DLL тем же порядком
// и сверяет суммы: так ядро гоняется без .NET — под Nsight Compute (проба AnyCPU с
// заголовком PE32 под `ncu` падает 0xC000007B) и быстрым стендом событийной схемы.
//
//   rm_replay <dll> <каталог записи> [--only=<номер run>] [--repeat=<k>] [--tol=<отн.>]
//             [--launch=<блоков>,<нитей>]   (0 — одна история на нить, как у пробы; <0 — постоянные нити)
//
// Код 0 — все суммы сошлись в допуске (по умолчанию 1e-9 отн.: атомарные сложения
// в другом порядке), 1 — расхождение, 2 — отказ.
#include <windows.h>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>

typedef const char* (*LastErrorFn)();
typedef int (*InitFn)(int, long long);
typedef int (*CfgSetFn)(const char*, double);
typedef int (*VoidIntFn)();
typedef int (*LoadFn)(const unsigned char*, int);
typedef int (*RunFn)(int, double, double, int, long long, long long, int, const unsigned long long*,
                     unsigned int, unsigned int, double*, double*, double*, double*, double*, int, void*, int, int);

static double Bits(long long b) { double d; std::memcpy(&d, &b, sizeof d); return d; }

static double Sum(const std::vector<double>& v) { double s = 0; for (double x : v) s += x; return s; }

int main(int argc, char** argv)
{
    if (argc < 3)
    {
        std::fprintf(stderr, "rm_replay <dll> <каталог записи> [--only=N] [--repeat=K] [--tol=X]\n");
        return 2;
    }

    int only = -1, repeat = 1, blocks = 0, threads = 128;
    double tol = 1e-9;
    for (int i = 3; i < argc; i++)
    {
        if (std::strncmp(argv[i], "--only=", 7) == 0) only = std::atoi(argv[i] + 7);
        else if (std::strncmp(argv[i], "--repeat=", 9) == 0) repeat = std::atoi(argv[i] + 9);
        else if (std::strncmp(argv[i], "--tol=", 6) == 0) tol = std::atof(argv[i] + 6);
        else if (std::strncmp(argv[i], "--launch=", 9) == 0) std::sscanf(argv[i] + 9, "%d,%d", &blocks, &threads);
        else { std::fprintf(stderr, "неизвестный ключ: %s\n", argv[i]); return 2; }
    }

    HMODULE h = LoadLibraryA(argv[1]);
    if (!h) { std::fprintf(stderr, "LoadLibrary(%s): %lu\n", argv[1], GetLastError()); return 2; }
    auto lastError = (LastErrorFn)GetProcAddress(h, "rm_last_error");
    auto init = (InitFn)GetProcAddress(h, "rm_init");
    auto cfgSet = (CfgSetFn)GetProcAddress(h, "rm_cfg_set");
    auto cfgCommit = (VoidIntFn)GetProcAddress(h, "rm_cfg_commit");
    auto load = (LoadFn)GetProcAddress(h, "rm_load");
    auto run = (RunFn)GetProcAddress(h, "rm_run");
    if (!lastError || !init || !cfgSet || !cfgCommit || !load || !run)
    {
        std::fprintf(stderr, "в DLL нет нужных экспортов\n");
        return 2;
    }

    std::string dir = argv[2];
    std::ifstream in(dir + "\\calls.txt");
    if (!in) { std::fprintf(stderr, "нет %s\\calls.txt\n", dir.c_str()); return 2; }

    auto ok = [&](int code, const char* what) -> bool
    {
        if (code == 0) return true;
        std::fprintf(stderr, "%s: %s\n", what, lastError());
        return false;
    };

    std::string line, pendingRun;
    int runIndex = -1, bad = 0, done = 0;
    double kernelSeconds = 0, recordedSeconds = 0;
    while (std::getline(in, line))
    {
        std::istringstream s(line);
        std::string op;
        s >> op;
        if (op == "init")
        {
            long long stack; s >> stack;
            if (!ok(init(0, stack), "rm_init")) return 2;
        }
        else if (op == "cfg")
        {
            std::string name; long long bits; s >> name >> bits;
            if (!ok(cfgSet(name.c_str(), Bits(bits)), "rm_cfg_set")) return 2;
        }
        else if (op == "commit")
        {
            if (!ok(cfgCommit(), "rm_cfg_commit")) return 2;
        }
        else if (op == "load")
        {
            std::string name; s >> name;
            std::ifstream b(dir + "\\" + name, std::ios::binary);
            std::vector<unsigned char> blob((std::istreambuf_iterator<char>(b)), std::istreambuf_iterator<char>());
            if (blob.empty()) { std::fprintf(stderr, "пустой %s\n", name.c_str()); return 2; }
            if (!ok(load(blob.data(), (int)blob.size()), "rm_load")) return 2;
        }
        else if (op == "run")
        {
            pendingRun = line;
            runIndex++;
        }
        else if (op == "sums")
        {
            if (only >= 0 && runIndex != only) continue;
            std::istringstream r(pendingRun);
            std::string tag;
            int branch, bins, rngMode, lh, lh2, lc, ll, ls;
            long long eBits, bBits, n, first;
            unsigned int key0, key1;
            double secRec;
            r >> tag >> branch >> eBits >> bBits >> bins >> n >> first >> rngMode >> key0 >> key1
              >> lh >> lh2 >> lc >> ll >> ls >> secRec;
            std::vector<double> want;
            { double v; while (s >> v) want.push_back(v); }

            for (int k = 0; k < repeat; k++)
            {
                std::vector<double> hist(lh > 0 ? lh : 0), hist2(lh2 > 0 ? lh2 : 0), chan(lc > 0 ? lc : 0),
                                    light(ll > 0 ? ll : 0), scal(ls);
                auto t0 = std::chrono::steady_clock::now();
                if (!ok(run(branch, Bits(eBits), Bits(bBits), bins, n, first, rngMode, nullptr, key0, key1,
                            lh >= 0 ? hist.data() : nullptr, lh2 >= 0 ? hist2.data() : nullptr,
                            lc >= 0 ? chan.data() : nullptr, ll >= 0 ? light.data() : nullptr,
                            scal.data(), ls, nullptr, blocks, threads), "rm_run")) return 2;
                double sec = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
                kernelSeconds += sec;
                if (k == 0) recordedSeconds += secRec;

                std::vector<double> got = { Sum(hist), Sum(hist2), Sum(chan), Sum(light) };
                got.insert(got.end(), scal.begin(), scal.end());
                double worst = 0; int worstAt = -1;
                for (size_t i = 0; i < got.size() && i < want.size(); i++)
                {
                    double d = std::fabs(got[i] - want[i]);
                    double rel = d / (std::fabs(want[i]) > 1e-300 ? std::fabs(want[i]) : 1.0);
                    if (rel > worst) { worst = rel; worstAt = (int)i; }
                }

                bool pass = got.size() == want.size() && worst <= tol;
                if (!pass) bad++;
                std::printf("run %3d ветвь %d E %9.3f кэВ n %lld: %.3f с (запись %.3f с), худшая отн. разница %.2e (поле %d) %s\n",
                            runIndex, branch, Bits(eBits), n, sec, secRec, worst, worstAt, pass ? "ok" : "РАСХОДИТСЯ");
                done++;
            }
        }
    }

    std::printf("прогонов %d, расходится %d; ядро %.2f с (в записи %.2f с)\n", done, bad, kernelSeconds, recordedSeconds);
    return done == 0 ? 2 : (bad ? 1 : 0);
}
