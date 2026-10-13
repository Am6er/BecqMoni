using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

/// <summary>
/// Упаковка сцены для GPU — побайтная приёмка правок упаковщика (`AMBER219`, П245,
/// 07.10.2026; решение Amber того же дня вопросником: «Перевести на прямой доступ
/// сейчас»). Упаковщик (`GpuPack`, приложение `EfficiencyMaker\Gpu*.cs`) снимает с
/// симулятора таблицы и сцену и шлёт их на устройство одним потоком; любая правка
/// упаковщика, которая не меняет физики, обязана дать ТОТ ЖЕ поток байт в байт у
/// каждой сцены склада. Эта проба и есть читатель этого признака.
///
///   GpuPackDumpProbe --dir=&lt;склад с .in&gt; --out=&lt;каталог&gt;
///       пишет &lt;ключ&gt;.blob (упаковка) и &lt;ключ&gt;.cfg (настройки симулятора: имя и биты
///       значения, по одной на строку) для каждой геометрии;
///   GpuPackDumpProbe --dir=&lt;склад с .in&gt; --compare=&lt;каталог&gt;
///       считает упаковку заново и сверяет с записанной: печатает сцену и первое
///       расхождение (смещение байта, либо имя настройки); код 0 — все сошлись,
///       1 — есть расхождение, 2 — нечего сверять.
///   --only=a,b — только названные ключи; --poison — ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: перед
///       сверкой у каждой сцены портится один байт упаковки и одна настройка — проба
///       обязана назвать расхождение у ВСЕХ сцен.
///
/// Симулятор узла — тот же, что у счёта (`ResponseMatrixBuilder.MakeSimulator`,
/// верхний узел сетки), с плоскими умолчаниями склада (`--target=0`). Упаковка от
/// числа историй не зависит; от узла — тоже (это сверяет `RmGpu.AssertSameBlob`).
///
/// ⚠ Закрытый `MakeSimulator` зовётся отражением — здесь это законно: проба лежит
/// вне сборки приложения и внутренних членов не видит; само приложение отражением
/// больше не пользуется (решение Amber выше).
/// </summary>
static class GpuPackDumpProbe
{
    static int Main(string[] args)
    {
        string dir = null, outDir = null, compareDir = null, only = null;
        bool poison = false;
        foreach (string a in args)
        {
            if (a.StartsWith("--dir=", StringComparison.Ordinal)) dir = a.Substring(6);
            else if (a.StartsWith("--out=", StringComparison.Ordinal)) outDir = a.Substring(6);
            else if (a.StartsWith("--compare=", StringComparison.Ordinal)) compareDir = a.Substring(10);
            else if (a.StartsWith("--only=", StringComparison.Ordinal)) only = a.Substring(7);
            else if (a == "--poison") poison = true;
            else
            {
                Console.Error.WriteLine("неизвестный ключ: " + a);
                return 2;
            }
        }

        if (dir == null || (outDir == null) == (compareDir == null))
        {
            Console.Error.WriteLine("нужно --dir=<склад> и ровно одно из --out=<каталог> / --compare=<каталог>");
            return 2;
        }

        var files = new List<string>(Directory.GetFiles(dir, "*.in"));
        files.Sort(StringComparer.Ordinal);
        if (only != null)
        {
            var wanted = new List<string>(only.Split(','));
            files.RemoveAll(f => !wanted.Contains(Path.GetFileNameWithoutExtension(f)));
        }

        if (files.Count == 0)
        {
            Console.Error.WriteLine("нет геометрий *.in в " + dir);
            return 2;
        }

        if (outDir != null) Directory.CreateDirectory(outDir);
        MethodInfo make = null;
        foreach (MethodInfo m in typeof(ResponseMatrixBuilder).GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
        {
            if (m.Name == "MakeSimulator" && m.GetParameters().Length == 4) make = m;
        }

        if (make == null)
        {
            Console.Error.WriteLine("ResponseMatrixBuilder.MakeSimulator(geometry, options, index, energyKev) не найден");
            return 2;
        }

        int bad = 0;
        foreach (string path in files)
        {
            string key = Path.GetFileNameWithoutExtension(path);
            GeometryModel geometry = GeometryModel.Load(path);
            var options = new ResponseMatrixOptions { ContinuumErrorTarget = 0.0 };
            double[] grid = options.BuildGrid(geometry);
            int index = grid.Length - 1;
            EfficiencySimulator sim;
            try
            {
                sim = (EfficiencySimulator)make.Invoke(null, new object[] { geometry, options, index, grid[index] });
            }
            catch (TargetInvocationException e)
            {
                throw e.InnerException ?? e;
            }

            byte[] blob;
            string cfg;
            try
            {
                blob = GpuPack.Pack(sim);
                cfg = Settings(sim);
            }
            catch (NotSupportedException e)
            {
                // Сцены, которых GPU-путь не несёт (ISO, важностный розыгрыш), — вслух и мимо.
                Console.WriteLine("{0}: не упаковывается — {1}", key, e.Message);
                continue;
            }

            if (poison)
            {
                blob[blob.Length / 2] ^= 1;
                cfg = cfg.Replace(" ", "  ");
            }

            if (outDir != null)
            {
                File.WriteAllBytes(Path.Combine(outDir, key + ".blob"), blob);
                File.WriteAllText(Path.Combine(outDir, key + ".cfg"), cfg, new UTF8Encoding(false));
                Console.WriteLine("{0}: {1} байт, настроек {2}", key, blob.Length, cfg.Split('\n').Length - 1);
                continue;
            }

            string blobPath = Path.Combine(compareDir, key + ".blob");
            string cfgPath = Path.Combine(compareDir, key + ".cfg");
            if (!File.Exists(blobPath) || !File.Exists(cfgPath))
            {
                Console.WriteLine("{0}: записи нет в {1}", key, compareDir);
                bad++;
                continue;
            }

            byte[] was = File.ReadAllBytes(blobPath);
            int diffAt = FirstDiff(was, blob);
            string cfgWas = File.ReadAllText(cfgPath);
            string cfgDiff = FirstCfgDiff(cfgWas, cfg);
            if (diffAt < 0 && cfgDiff == null)
            {
                Console.WriteLine("{0}: сошлось ({1} байт)", key, blob.Length);
            }
            else
            {
                bad++;
                Console.WriteLine("{0}: РАСХОЖДЕНИЕ — {1}{2}", key,
                    diffAt < 0 ? "упаковка та же" : string.Format(CultureInfo.InvariantCulture,
                        "упаковка со смещения {0} (было {1} байт, стало {2})", diffAt, was.Length, blob.Length),
                    cfgDiff == null ? "; настройки те же" : "; настройки: " + cfgDiff);
            }
        }

        if (compareDir != null)
        {
            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ ({0} сцен)" : "расходятся: {1} из {0}", files.Count, bad);
            return bad == 0 ? 0 : 1;
        }

        return 0;
    }

    static string Settings(EfficiencySimulator sim)
    {
        var sb = new StringBuilder();
        foreach (KeyValuePair<string, double> kv in GpuPack.Settings(sim))
        {
            sb.Append(kv.Key).Append(' ')
              .Append(BitConverter.DoubleToInt64Bits(kv.Value).ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        return sb.ToString();
    }

    static int FirstDiff(byte[] a, byte[] b)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++) if (a[i] != b[i]) return i;
        return a.Length == b.Length ? -1 : n;
    }

    static string FirstCfgDiff(string was, string now)
    {
        string[] a = was.Split('\n'), b = now.Split('\n');
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++)
        {
            if (a[i] != b[i]) return "строка " + (i + 1).ToString(CultureInfo.InvariantCulture) + ": «" + a[i] + "» → «" + b[i] + "»";
        }

        return a.Length == b.Length ? null : "число настроек " + a.Length + " → " + b.Length;
    }
}
