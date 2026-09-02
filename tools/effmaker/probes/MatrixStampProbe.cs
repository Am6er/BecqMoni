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
        Console.WriteLine("а различает ли клеймо ключи физики 02.09.2026:");
        int bad = 0;
        bad += Differs("--pairth=1", byDefaults, geometry, o => o.XcomPairThreshold = true);
        bad += Differs("--positron=1", byDefaults, geometry, o => o.PositronTransport = true);
        bad += Differs("--positron=1 --posoffset=0", byDefaults, geometry,
                       o => { o.PositronTransport = true; o.PositronOffset = false; });
        bad += Differs("--rayl2=1", byDefaults, geometry, o => o.RayleighToCrystal = true);

        // Половины `S126` обязаны различаться и МЕЖДУ СОБОЙ, а не только от
        // умолчаний: иначе гвард отдаст матрицу одной половины другой.
        var a1 = new ResponseMatrixOptions(); a1.PositronTransport = true;
        var a2 = new ResponseMatrixOptions(); a2.PositronTransport = true; a2.PositronOffset = false;
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
