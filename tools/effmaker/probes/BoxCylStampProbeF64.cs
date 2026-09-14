using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace BoxCylStampProbeF64
{
    /// <summary>
    /// `T161`, полоса F64: СДВИНЕТ ЛИ ЧТО-НИБУДЬ правка производного цилиндра
    /// в файле бруска.
    ///
    /// Зачем проба. `BoxDeadFieldsProbe` отказывает на чистом `HEAD` строкой
    /// «в файле лежит ПРОИЗВОДНЫЙ цилиндр: D=18.5400 (ждём 18.5412),
    /// H=59.0000 (ждём 60.0000) мм»: `Nano16Pro_box.in` — копия файла ЛСРМ, и
    /// цилиндр в нём ихний (округлённый диаметр, высота 5.9 при бруске 6.0), а
    /// не наш производный. Прежде чем такое править, надо знать ЦЕНУ: правка
    /// геометрии, сдвигающая клеймо, обесценивает готовые матрицы —
    /// содержимое остаётся верным, а происхождение портится, и ни побитовый
    /// замер, ни клеймо этого не ловят (`A77`).
    ///
    /// Меряется поэтому не «правильно ли число», а РОВНО ТРИ величины, которыми
    /// пользуется потребитель:
    ///
    ///   * текст геометрии `GeometryWriter.Render` (его берёт `ComputeStamp`);
    ///   * клеймо `ResponseMatrix.ComputeStamp` — по нему матрица судится годной;
    ///   * дамп сцены `EfficiencySimulator.DumpScene` — по нему считается перенос.
    ///
    /// ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ОБЯЗАТЕЛЕН: «правка ничего не изменила»
    /// проходит и у пробы, которая не меряет ничего вовсе. Поэтому рядом стоит
    /// плечо с правкой РАБОЧЕГО размера (`DS_CrystalBoxZ`), и оно ОБЯЗАНО
    /// сдвинуть все три величины. Не сдвинуло — проба отказывает.
    ///
    ///     boxcylstampprobef64 &lt;брусок.in&gt; [каталог для временных копий]
    ///
    /// Ожидание: «СОШЛОСЬ», код 0. Код 1 — расхождение, код 2 — доводы.
    /// </summary>
    static class Program
    {
        static int bad;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            if (args.Length < 1 || args.Length > 2)
            {
                Console.Error.WriteLine("boxcylstampprobef64 <брусок.in> [каталог для временных копий]");
                return 2;
            }

            string src = args[0];
            // ⛔ Временные файлы — в СВОЙ каталог, а не в текущий (правило захода
            //    06.09.2026: три прогона намусорили в корне дерева).
            string tmp = args.Length == 2
                ? args[1]
                : Path.Combine(Path.GetTempPath(), "bqf64_t161");
            Directory.CreateDirectory(tmp);

            // Библиотека веществ читает matdb — без менеджера сцена не соберётся.
            GlobalConfigManager.GetInstance();

            string text = File.ReadAllText(src);
            if (text.IndexOf("DS_CrystalBoxX", StringComparison.Ordinal) < 0)
            {
                Console.Error.WriteLine("это не брусок: в файле нет DS_CrystalBoxX");
                return 2;
            }

            Console.WriteLine("исходный файл: " + src);
            Snap baseline = Take(src, "как есть (D=1.854 см, H=5.9 см — цилиндр ЛСРМ)");

            // Плечо 1: производный цилиндр приведён к тому, что кладёт НАШ
            // писатель — 2·sqrt(1.5·1.8/pi) = 1.8541162 см и Z бруска 6.0 см.
            string fixedPath = Path.Combine(tmp, "f64_cyl_fixed.in");
            File.WriteAllText(fixedPath,
                text.Replace("DS_CrystalDiameter = 1.854 cm", "DS_CrystalDiameter = 1.8541162 cm")
                    .Replace("DS_CrystalHeight = 5.9 cm", "DS_CrystalHeight = 6.0 cm"));
            Snap fixedUp = Take(fixedPath, "производный цилиндр исправлен (D=1.8541162, H=6.0)");

            // Плечо 2, ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: двинут РАБОЧИЙ размер бруска.
            string movedPath = Path.Combine(tmp, "f64_boxz_moved.in");
            File.WriteAllText(movedPath, text.Replace("DS_CrystalBoxZ = 6.0 cm", "DS_CrystalBoxZ = 6.1 cm"));
            Snap moved = Take(movedPath, "КОНТРОЛЬ: рабочий размер DS_CrystalBoxZ 6.0 -> 6.1");

            Console.WriteLine();
            Same("правка производного цилиндра НЕ двигает текст геометрии", baseline.Text, fixedUp.Text);
            Same("правка производного цилиндра НЕ двигает клеймо", baseline.Stamp, fixedUp.Stamp);
            Same("правка производного цилиндра НЕ двигает сцену", baseline.Scene, fixedUp.Scene);

            Different("КОНТРОЛЬ: рабочий размер ДВИГАЕТ текст геометрии", baseline.Text, moved.Text);
            Different("КОНТРОЛЬ: рабочий размер ДВИГАЕТ клеймо", baseline.Stamp, moved.Stamp);
            Different("КОНТРОЛЬ: рабочий размер ДВИГАЕТ сцену", baseline.Scene, moved.Scene);

            Console.WriteLine();
            if (bad == 0)
            {
                Console.WriteLine("СОШЛОСЬ: цена правки производного цилиндра — НОЛЬ сдвинутых матриц.");
                return 0;
            }

            Console.WriteLine("РАСХОЖДЕНИЙ: " + bad.ToString(CultureInfo.InvariantCulture));
            return 1;
        }

        struct Snap
        {
            public string Text;
            public string Stamp;
            public string Scene;
        }

        static Snap Take(string path, string what)
        {
            GeometryModel g = GeometryModel.Load(path);
            // Настройки фиксированные: меряется геометрия, а не они (так же, как
            // в `BoxDeadFieldsProbe.Stamp`).
            var options = new ResponseMatrixOptions { NodeCount = 10, Histories = 4000, BinKev = 4.0 };
            var s = new Snap
            {
                Text  = GeometryWriter.Render(g),
                Stamp = ResponseMatrix.ComputeStamp(g, options),
                Scene = new EfficiencySimulator(g).DumpScene(),
            };

            Console.WriteLine("  {0,-58} текст sha {1}  клеймо {2}  сцена sha {3}",
                              what, Sha(s.Text), Short(s.Stamp), Sha(s.Scene));
            return s;
        }

        static void Same(string what, string a, string b)
        {
            bool ok = string.Equals(a, b, StringComparison.Ordinal);
            if (!ok) { bad++; }
            Console.WriteLine("[{0}] {1}", ok ? "  ok  " : "ПРОВАЛ", what);
        }

        static void Different(string what, string a, string b)
        {
            bool ok = !string.Equals(a, b, StringComparison.Ordinal);
            if (!ok) { bad++; }
            Console.WriteLine("[{0}] {1}", ok ? "  ok  " : "ПРОВАЛ", what);
        }

        static string Sha(string s)
        {
            using (SHA256 h = SHA256.Create())
            {
                byte[] d = h.ComputeHash(Encoding.UTF8.GetBytes(s ?? ""));
                var sb = new StringBuilder();
                for (int i = 0; i < 6; i++) { sb.Append(d[i].ToString("x2", CultureInfo.InvariantCulture)); }
                return sb.ToString();
            }
        }

        static string Short(string s)
        {
            if (string.IsNullOrEmpty(s)) { return "(пусто)"; }
            return s.Length <= 12 ? s : Sha(s);
        }
    }
}
