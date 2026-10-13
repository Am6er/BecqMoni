// host.h — разбор упаковки, присланной C# (`GpuMatrix.cs`, класс `GpuWriter`), в таблицы
// хоста; потом `api.cu` копирует их на устройство. Полоса П221 (`AMBER160`).
//
// ФОРМАТ — позиционный, порядок полей задают пары «писатель C# ↔ читатель здесь»
// одного раздела. Типы значений в потоке не пишутся; рассинхрон ловит КОНТРОЛЬНОЕ
// СЛОВО: после каждой записи-структуры писатель кладёт `End()` (0x5EC710ED), читатель
// сверяет `End()` и при несовпадении бросает с именем раздела и номером записи.
//
//   раздел:   Tag(string) Int(count) { запись ... End }×count
//   число:    8 байт double (на устройство — как `real`)
//   целое:    4 байта
//   массив:   Int(n) + n значений → смещение в арену
//   зубчатый: Int(rows) + rows×(Int(len) + len значений) → смещение плоского + смещение
//             таблицы начал строк (rows + 1 целых, относительно плоского)
#pragma once
#include <vector>
#include <string>
#include <stdexcept>
#include <cstring>
#include <cstdint>
#include "data.cuh"

struct HostData
{
    std::vector<real> R;
    std::vector<int> I;
    std::vector<unsigned char> B;

    std::vector<ElementG> elements;
    std::vector<FluorescenceG> fluor;
    std::vector<PhotoShellModelG> photoShell;
    std::vector<RelaxationG> relax;
    std::vector<TransitionsG> transitions;
    std::vector<AtomG> atoms;
    std::vector<ElectronMaterialG> electronMats;
    std::vector<ThickTargetBremG> brems;
    std::vector<LightYieldCurveG> lightYields;
    std::vector<int> elementsByZ, fluorByZ, photoShellByZ, relaxByZ, atomsByZ;

    std::vector<MaterialG> materials;
    std::vector<FluorescersG> fluorescers;
    std::vector<ScatterersG> scatterers;
    std::vector<ScatterElementG> scatterElements;
    std::vector<RegionG> regions;
    std::vector<SceneG> scene;

    HostData()
    {
        elementsByZ.assign(128, -1);
        fluorByZ.assign(128, -1);
        photoShellByZ.assign(128, -1);
        relaxByZ.assign(128, -1);
        atomsByZ.assign(128, -1);
        // Нулевые смещения арен — «пустой массив»: кладём по одному нулю, чтобы
        // смещение 0 всегда указывало на существующую ячейку.
        R.push_back((real)0);
        I.push_back(0);
        B.push_back(0);
    }
};

class BlobReader
{
public:
    BlobReader(const unsigned char* data, size_t length, HostData& host)
        : p(data), end(data + length), h(host), record(0) {}

    bool AtEnd() const { return p >= end; }

    std::string Tag()
    {
        int n = Int();
        Need((size_t)n);
        std::string s((const char*)p, (size_t)n);
        p += n;
        section = s;
        record = 0;
        return s;
    }

    double Double()
    {
        Need(8);
        double v;
        std::memcpy(&v, p, 8);
        p += 8;
        return v;
    }

    real Real() { return (real)Double(); }

    int Int()
    {
        Need(4);
        int v;
        std::memcpy(&v, p, 4);
        p += 4;
        return v;
    }

    long long Long()
    {
        Need(8);
        long long v;
        std::memcpy(&v, p, 8);
        p += 8;
        return v;
    }

    bool Bool() { return Int() != 0; }

    // Массив чисел → арена R; возвращает смещение, длину кладёт в len.
    int Reals(int& len)
    {
        len = Int();
        if (len < 0) Fail("отрицательная длина массива");
        int off = (int)h.R.size();
        for (int i = 0; i < len; i++) h.R.push_back((real)Double());
        return len == 0 ? 0 : off;
    }

    int Ints(int& len)
    {
        len = Int();
        if (len < 0) Fail("отрицательная длина массива");
        int off = (int)h.I.size();
        for (int i = 0; i < len; i++) h.I.push_back(Int());
        return len == 0 ? 0 : off;
    }

    int Bytes(int& len)
    {
        len = Int();
        if (len < 0) Fail("отрицательная длина массива");
        Need((size_t)len);
        int off = (int)h.B.size();
        for (int i = 0; i < len; i++) h.B.push_back(p[i]);
        p += len;
        return len == 0 ? 0 : off;
    }

    // Зубчатый массив чисел: плоский → R, начала строк (rows + 1, относительно
    // плоского) → I. Возвращает смещение плоского; смещение начал — в offOff.
    int Jagged(int& rows, int& offOff)
    {
        rows = Int();
        if (rows < 0) Fail("отрицательное число строк");
        int flat = (int)h.R.size();
        offOff = (int)h.I.size();
        h.I.push_back(0);
        int total = 0;
        for (int r = 0; r < rows; r++)
        {
            int len = Int();
            if (len < 0) Fail("отрицательная длина строки");
            for (int i = 0; i < len; i++) h.R.push_back((real)Double());
            total += len;
            h.I.push_back(total);
        }

        return flat;
    }

    // То же для целых: плоский → I, начала → I.
    int JaggedInts(int& rows, int& offOff)
    {
        rows = Int();
        if (rows < 0) Fail("отрицательное число строк");
        std::vector<std::vector<int>> tmp((size_t)rows);
        for (int r = 0; r < rows; r++)
        {
            int len = Int();
            if (len < 0) Fail("отрицательная длина строки");
            tmp[(size_t)r].resize((size_t)len);
            for (int i = 0; i < len; i++) tmp[(size_t)r][(size_t)i] = Int();
        }

        offOff = (int)h.I.size();
        int total = 0;
        h.I.push_back(0);
        for (int r = 0; r < rows; r++)
        {
            total += (int)tmp[(size_t)r].size();
            h.I.push_back(total);
        }

        int flat = (int)h.I.size();
        for (int r = 0; r < rows; r++)
            for (int v : tmp[(size_t)r]) h.I.push_back(v);
        return flat;
    }

    void End()
    {
        int magic = Int();
        if ((unsigned)magic != 0x5EC710EDu) Fail("контрольное слово не сошлось — порядок полей писателя и читателя разошёлся");
        record++;
    }

    [[noreturn]] void Fail(const char* what)
    {
        throw std::runtime_error("упаковка, раздел «" + section + "», запись " + std::to_string(record) + ": " + what);
    }

    HostData& h;

private:
    void Need(size_t n)
    {
        if ((size_t)(end - p) < n) Fail("поток кончился раньше записи");
    }

    const unsigned char* p;
    const unsigned char* end;
    std::string section;
    int record;
};

// Читатели разделов. Объявлены здесь, определены модулями:
//   host_tables.h — сечения, рассеяние, релаксация, электроны, тормозное, свет;
//   host_scene.h  — вещества, флуоресценты, рассеиватели, области, сцена.
// Имя раздела в потоке = имя функции без `Read_`.
void Read_elements(BlobReader& r);
void Read_fluor(BlobReader& r);
void Read_photoShell(BlobReader& r);
void Read_relax(BlobReader& r);
void Read_atoms(BlobReader& r);
void Read_electronMats(BlobReader& r);
void Read_brems(BlobReader& r);
void Read_lightYields(BlobReader& r);
void Read_materials(BlobReader& r);
void Read_fluorescers(BlobReader& r);
void Read_scatterers(BlobReader& r);
void Read_scatterElements(BlobReader& r);
void Read_regions(BlobReader& r);
void Read_scene(BlobReader& r);
