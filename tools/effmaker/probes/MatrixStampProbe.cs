using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.IO;
using System.Text;

/// <summary>
/// Почему гвард не признал готовую матрицу — по клейму, а не по догадке
/// (`S130`).
///
/// Печатает три клейма для одной сцены: то, что лежит В ФАЙЛЕ; то, что даёт
/// нынешний код при УМОЛЧАНИЯХ настроек; и то, что даёт нынешний код при
/// настройках, ЗАПИСАННЫХ В САМОМ ФАЙЛЕ. Последнее и разделяет два случая:
///
///   * файл ≠ умолчания, но файл = свои настройки — расходятся НАСТРОЙКИ
///     (густота, сетка, ключи прогона), код клейма ни при чём;
///   * файл ≠ своим настройкам — изменился САМ СОСТАВ клейма или версия
///     физики, и тогда все посчитанные матрицы разом стали чужими.
///
/// Ниже — те же клейма с каждым из трёх ключей физики 02.09.2026 по очереди:
/// они обязаны ОТЛИЧАТЬСЯ от умолчаний, иначе матрица с новой физикой легла бы
/// поверх старой под тем же именем.
///
/// ⛔ Ключ в контроле ставится в значение, ПЕРЕВЁРНУТОЕ относительно умолчания
/// самих <see cref="ResponseMatrixOptions"/>, а не в зашитое `true`. Что было:
/// с 02.09.2026 (`S130`) контроль писал `o.PositronTransport = true` и
/// `o.RayleighToCrystal = true` — тогда оба ключа были ВЫКЛ умолчанием, и
/// `true` их переворачивал. С П37 (13.09.2026, физика 17) оба ВКЛ умолчанием,
/// `true` стал равен умолчанию, клеймо не менялось, и проба на КАЖДОЙ сцене
/// печатала «КЛЕЙМО ТО ЖЕ — расхождение» и возвращала код 1 — четыре дня, пока
/// её читали только по строкам клейм (П72 `stamp_check.py`, П91
/// `check_corpus_scenes.py`). Найдено и исправлено П91 17.09.2026: перевёрнутое
/// значение не зависит от того, какое умолчание физика выберет завтра, а метка
/// строки печатает то значение, которое реально подано (`--positron=0`).
///
///     matrixstampprobe --geometry=X.in [--matrix=X.rmx]
/// </summary>
static class MatrixStampProbe
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        string geometryPath = null, matrixPath = null;
        foreach (string a in args)
        {
            if (a.StartsWith("--geometry=", StringComparison.Ordinal))
            {
                geometryPath = a.Substring(11);
            }
            else if (a.StartsWith("--matrix=", StringComparison.Ordinal))
            {
                matrixPath = a.Substring(9);
            }
            else
            {
                Console.Error.WriteLine("неизвестный ключ: " + a);
                return 2;
            }
        }

        if (geometryPath == null)
        {
            Console.Error.WriteLine("нужен --geometry=<файл .in>");
            return 2;
        }

        if (matrixPath == null)
        {
            matrixPath = Path.ChangeExtension(geometryPath, ".rmx");
        }

        GeometryModel geometry = GeometryModel.Load(geometryPath);
        if (geometry == null)
        {
            Console.Error.WriteLine("не читается геометрия: " + geometryPath);
            return 2;
        }

        var plain = new ResponseMatrixOptions();
        string byDefaults = ResponseMatrix.ComputeStamp(geometry, plain);
        Console.WriteLine("клеймо при умолчаниях : {0}", byDefaults);

        ResponseMatrix have = File.Exists(matrixPath) ? ResponseMatrix.Load(matrixPath) : null;
        if (have == null)
        {
            Console.WriteLine("матрицы рядом нет: {0}", matrixPath);
        }
        else
        {
            Console.WriteLine("клеймо в файле        : {0}", have.Stamp);
            Console.WriteLine("историй в файле       : {0} (в умолчаниях {1})",
                              have.Histories, plain.Histories);
            if (have.Options != null)
            {
                string byOwn = ResponseMatrix.ComputeStamp(geometry, have.Options);
                Console.WriteLine("клеймо по НАСТРОЙКАМ ФАЙЛА: {0}", byOwn);
                Console.WriteLine("  {0}", byOwn == have.Stamp
                    ? "сошлось — состав клейма прежний, расходятся только настройки"
                    : "НЕ СОШЛОСЬ — изменился сам состав клейма или версия физики");
                Console.WriteLine("  узлов {0} (умолчание {1}), сетка {2}-{3} (умолчание {4}-{5})",
                                  have.Options.NodeCount, plain.NodeCount,
                                  have.Options.MinEnergyKev, have.Options.MaxEnergyKev,
                                  plain.MinEnergyKev, plain.MaxEnergyKev);
            }
            else
            {
                Console.WriteLine("настроек в файле нет (старый формат)");
            }
        }

        Console.WriteLine();
        Console.WriteLine("а различает ли клеймо ключи физики 02.09.2026 (ключ — перевёрнут против умолчания):");
        int bad = 0;
        // Каждый ключ — в значение, ОБРАТНОЕ умолчанию `plain` (см. шапку: `true`
        // здесь однажды совпало с умолчанием и ослепило контроль на четыре дня).
        bad += Differs(Flag("--pairth", !plain.XcomPairThreshold), byDefaults, geometry,
                       o => o.XcomPairThreshold = !plain.XcomPairThreshold);
        bad += Differs(Flag("--positron", !plain.PositronTransport), byDefaults, geometry,
                       o => o.PositronTransport = !plain.PositronTransport);
        // Смещение позитрона имеет смысл только при ВКЛ переносе: перенос
        // держится включённым, переворачивается само смещение.
        bad += Differs(Flag("--positron", true) + " " + Flag("--posoffset", !plain.PositronOffset),
                       byDefaults, geometry,
                       o => { o.PositronTransport = true; o.PositronOffset = !plain.PositronOffset; });
        bad += Differs(Flag("--rayl2", !plain.RayleighToCrystal), byDefaults, geometry,
                       o => o.RayleighToCrystal = !plain.RayleighToCrystal);
        // `A57` — ОЦЕНЩИК, а не физика, но клеймо обязано различать и его:
        // числа матрицы с конусом другие, и подменять ими готовую нельзя.
        bad += Differs(Flag("--cone", !plain.AnalogConeSampling), byDefaults, geometry,
                       o => o.AnalogConeSampling = !plain.AnalogConeSampling);

        // Половины `S126` обязаны различаться и МЕЖДУ СОБОЙ, а не только от
        // умолчаний: иначе гвард отдаст матрицу одной половины другой. Обе — с
        // ВКЛ переносом, различаются только смещением (одна — умолчание, другая
        // — перевёрнутое).
        var a1 = new ResponseMatrixOptions(); a1.PositronTransport = true;
        var a2 = new ResponseMatrixOptions(); a2.PositronTransport = true; a2.PositronOffset = !plain.PositronOffset;
        string s1 = ResponseMatrix.ComputeStamp(geometry, a1);
        string s2 = ResponseMatrix.ComputeStamp(geometry, a2);
        Console.WriteLine("  {0,-28} {1}", "половины S126 между собой",
                          s1 != s2 ? "различаются" : "СОВПАЛИ — расхождение");
        if (s1 == s2)
        {
            bad++;
        }

        Console.WriteLine();
        Console.WriteLine(bad == 0
            ? "СОШЛОСЬ: каждый ключ физики меняет клеймо"
            : "НЕ СОШЛОСЬ: клеймо не различает " + bad + " случаев");
        return bad == 0 ? 0 : 1;
    }

    /// <summary>Метка строки контроля — то значение, что РЕАЛЬНО подано ключу.</summary>
    static string Flag(string key, bool value)
    {
        return key + "=" + (value ? "1" : "0");
    }

    static int Differs(string what, string reference, GeometryModel geometry,
                       Action<ResponseMatrixOptions> tweak)
    {
        var options = new ResponseMatrixOptions();
        tweak(options);
        string stamp = ResponseMatrix.ComputeStamp(geometry, options);
        bool ok = stamp != reference;
        Console.WriteLine("  {0,-28} {1}", what, ok ? "клеймо другое" : "КЛЕЙМО ТО ЖЕ — расхождение");
        return ok ? 0 : 1;
    }
}
