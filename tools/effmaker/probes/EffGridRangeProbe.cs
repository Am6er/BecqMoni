using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.Text;
using System.Threading;

namespace EffGridRangeProbe
{
    /// <summary>
    /// Сетка энергий расчёта кривой из геометрии обязана покрывать диапазон,
    /// выставленный в полях формы (TODO E16).
    ///
    ///     effgridrangeprobe
    ///
    /// Прежде поля диапазона штатную сетку только ОБРЕЗАЛИ: выставленные 20 кэВ
    /// считались от 40 (первый узел `DefaultEnergies`), выставленные 5000 — до
    /// 3000. Ошибка тихая вдвойне: число в поле принято, кнопка нажата, кривая
    /// посчитана — и только строка журнала называет настоящий диапазон, а
    /// читают поле.
    ///
    /// Проверяется на каждом случае:
    ///
    /// 1. ПОКРЫТИЕ — первый узел не выше нижней границы, последний не ниже
    ///    верхней;
    /// 2. ПОРЯДОК — узлы строго возрастают (продолжение вниз идёт вставкой в
    ///    голову, и перепутанный порядок дал бы кривую, не читаемую никем);
    /// 3. ШТАТНЫЕ УЗЛЫ ЦЕЛЫ — те из `DefaultEnergies`, что попали в диапазон,
    ///    стоят на своих местах: их положение выбрано по изгибу кривой и рабочим
    ///    линиям, и раздвинуть их значило бы получить другую сетку под тем же
    ///    именем;
    /// 4. НЕТ УЗЛА ВПЛОТНУЮ к соседнему: граница ближе десятой доли шага — это
    ///    та же точка, а лишний узел стоит полного прогона историй.
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ».
    /// </summary>
    static class Program
    {
        static int bad;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo culture = (CultureInfo)Thread.CurrentThread.CurrentCulture.Clone();
            culture.NumberFormat.NumberDecimalSeparator = ".";
            Thread.CurrentThread.CurrentCulture = culture;

            // Умолчание — ровно штатная сетка, ни одного лишнего узла.
            Grid("умолчание", 40, 3000, EfficiencyGridMode.Standard, 34, true);
            Grid("низ 20 (случай Amber)", 20, 3000, EfficiencyGridMode.Standard, 34, true);
            Grid("низ 25 (граница не в шаг)", 25, 3000, EfficiencyGridMode.Standard, 34, true);
            Grid("низ 1 (край поля формы)", 1, 3000, EfficiencyGridMode.Standard, 34, true);
            Grid("верх 5000", 40, 5000, EfficiencyGridMode.Standard, 34, true);
            Grid("20..5000 — оба края", 20, 5000, EfficiencyGridMode.Standard, 34, true);
            Grid("39.999 — узел вплотную", 39.999, 3000, EfficiencyGridMode.Standard, 34, true);
            Grid("500..1000 внутри сетки", 500, 1000, EfficiencyGridMode.Standard, 34, true);
            // Штатных узлов внутри меньше двух — сетка становится
            // логарифмической, и узел 3000 в ней не обязан уцелеть.
            Grid("2900..3100 — штатных <2", 2900, 3100, EfficiencyGridMode.Standard, 34, false);
            Grid("логарифмическая 20..5000", 20, 5000, EfficiencyGridMode.Logarithmic, 10, false);

            // E24: K-край ВЕЩЕСТВА ПРОБЫ. Штатные узлы идут «…60, 70, 80…», а
            // край лютеция лежит на 63.31 — между ними, и кривая рисовалась
            // прямой там, где на деле падает в 4.2 раза. Проверяется, что пара
            // узлов встала по бокам края, что на самом крае узла НЕТ и что
            // безобидная проба (воздух) сетку не трогает.
            Edges("проба — оксид лютеция", "Lutetium oxide", 63.314, true);
            Edges("проба — вольфрам (WT-20)", "Tungsten", 69.525, true);
            Edges("проба — воздух", "Air, dry", 0.0, false);

            // E31: то же правило у сетки МАТРИЦЫ отклика. Ключ ВЫКЛЮЧЕН
            // умолчанием (включение обесценивает все посчитанные матрицы), и
            // проверяется здесь именно это: выключенный не трогает сетку,
            // включённый ставит ту же пару узлов, что у кривой.
            MatrixEdges();

            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : bad + " ПРОВЕРОК ПРОВАЛЕНО");
            return bad == 0 ? 0 : 1;
        }

        /// <summary>
        /// Сетка ПО ГЕОМЕТРИИ: сцена берётся у встроенной заготовки редактора,
        /// детектор — у первого пресета, меняется только вещество пробы. Свои
        /// числа здесь не набираются нарочно — иначе проба разойдётся с формой
        /// при первой же правке заготовки.
        /// </summary>
        static void Edges(string title, string sampleName, double edgeKev, bool expected)
        {
            GeometryMaterialLibrary.Entry entry = GeometryMaterialLibrary.ByName(sampleName);
            if (entry == null)
            {
                Console.WriteLine("    ПЛОХО  {0}: вещества «{1}» нет в библиотеке", title, sampleName);
                bad++;
                return;
            }

            GeometryModel g = BecquerelMonitor.GeometryEditorPanel.Blank();
            GeometryPresets.Items[0].Apply(g);
            g.SourceType = GeometrySourceType.Cylinder;
            g.Source = GeometryMaterialLibrary.Make(entry, entry.Density);

            EfficiencyCalculationOptions options = new EfficiencyCalculationOptions
            {
                MinEnergyKev = 40,
                MaxEnergyKev = 3000,
                GridMode = EfficiencyGridMode.Standard,
            };

            System.Collections.Generic.List<string> notes = new System.Collections.Generic.List<string>();
            double[] plain = options.BuildGrid();
            double[] grid = options.BuildGrid(g, notes);

            Console.WriteLine("{0,-28} {1,3} узлов (без геометрии {2}), краёв разрешено {3}",
                              title, grid.Length, plain.Length, notes.Count);
            foreach (string note in notes)
            {
                Console.WriteLine("    " + note);
            }

            bool ordered = true;
            for (int i = 1; i < grid.Length; i++)
            {
                ordered &= grid[i] > grid[i - 1];
            }

            Check(title + ": узлы возрастают", ordered);

            bool kept = true;
            foreach (double energy in plain)
            {
                bool found = false;
                foreach (double node in grid)
                {
                    found |= Math.Abs(node - energy) < 1e-9;
                }

                kept &= found;
            }

            Check(title + ": прежние узлы целы", kept);

            if (!expected)
            {
                Check(title + ": сетка не тронута", grid.Length == plain.Length);
                return;
            }

            bool below = false, above = false, onEdge = false;
            foreach (double node in grid)
            {
                double d = (node - edgeKev) / edgeKev;
                if (Math.Abs(d) < 1e-6) onEdge = true;
                else if (d < 0.0 && d > -2.0 * EfficiencyCalculationOptions.EdgeOffset) below = true;
                else if (d > 0.0 && d < 2.0 * EfficiencyCalculationOptions.EdgeOffset) above = true;
            }

            Check(title + ": узел под краем", below);
            Check(title + ": узел над краем", above);
            Check(title + ": на самом крае узла НЕТ", !onEdge);
            Check(title + ": край назван в журнале", notes.Count > 0);
        }

        static void Grid(string title, double lo, double hi,
                         EfficiencyGridMode mode, int nodes, bool standardNodes)
        {
            EfficiencyCalculationOptions options = new EfficiencyCalculationOptions
            {
                MinEnergyKev = lo,
                MaxEnergyKev = hi,
                GridMode = mode,
                NodeCount = nodes,
            };

            double[] grid = options.BuildGrid();
            Console.WriteLine("{0,-28} {1,3} узлов  {2:0.###} .. {3:0.###}",
                              title, grid.Length, grid[0], grid[grid.Length - 1]);
            Console.WriteLine("    " + string.Join(" ", Array.ConvertAll(grid,
                e => e.ToString("0.###", CultureInfo.InvariantCulture))));

            bool ordered = true;
            double minGap = double.MaxValue;
            for (int i = 1; i < grid.Length; i++)
            {
                ordered &= grid[i] > grid[i - 1];
                minGap = Math.Min(minGap, grid[i] - grid[i - 1]);
            }

            // Недобор до границы разрешён ровно в одном случае — когда узел уже
            // стоит вплотную к ней: десятая доля шага в кэВ на кривой не значит
            // ничего, а лишний узел стоит полного прогона историй.
            // Допуск — от МЕСТНОГО шага сетки, а не от среднего по ней: внизу
            // шаг 10 кэВ, вверху 200, и один допуск на обоих концах либо
            // придирался бы к верху, либо прощал бы низу целый узел.
            Check(title + ": низ покрыт", grid[0] <= lo + (grid[1] - grid[0]) * 0.1);
            Check(title + ": верх покрыт",
                  grid[grid.Length - 1] >= hi - (grid[grid.Length - 1] - grid[grid.Length - 2]) * 0.1);
            Check(title + ": узлы возрастают", ordered);

            // Самый частый участок штатной сетки — 10 кэВ (40…100). Узел ближе
            // одного кэВ к соседу означает приклеенную границу.
            Check(title + ": нет узла вплотную", minGap > 1.0);

            if (!standardNodes)
            {
                return;
            }

            bool kept = true;
            foreach (double energy in EfficiencyCalculation.DefaultEnergies)
            {
                if (energy < lo || energy > hi)
                {
                    continue;
                }

                bool found = false;
                foreach (double node in grid)
                {
                    found |= Math.Abs(node - energy) < 1e-9;
                }

                kept &= found;
            }

            Check(title + ": штатные узлы целы", kept);
        }

        static void MatrixEdges()
        {
            GeometryMaterialLibrary.Entry entry = GeometryMaterialLibrary.ByName("Lutetium oxide");
            if (entry == null)
            {
                Console.WriteLine("    ПЛОХО  сетка матрицы: вещества «Lutetium oxide» нет");
                bad++;
                return;
            }

            GeometryModel g = BecquerelMonitor.GeometryEditorPanel.Blank();
            GeometryPresets.Items[0].Apply(g);
            g.SourceType = GeometrySourceType.Cylinder;
            g.Source = GeometryMaterialLibrary.Make(entry, entry.Density);

            ResponseMatrixOptions off = new ResponseMatrixOptions();
            ResponseMatrixOptions on = new ResponseMatrixOptions { ResolveEdges = true };
            double[] plain = off.BuildGrid(g);
            double[] with = on.BuildGrid(g);

            Console.WriteLine("сетка матрицы                 {0,3} узлов без ключа, {1,3} с ключом",
                              plain.Length, with.Length);
            Check("матрица: выключенный ключ сетку не трогает",
                  plain.Length == off.BuildGrid().Length);
            // Не «ровно два»: у логарифмической сетки из ста узлов сосед может
            // сам стоять вплотную к краю (на 63.2 при крае 63.314), и тогда
            // добавляется один — правило «узел вплотную не заводится» общее с
            // сеткой кривой. Важно не число, а то, что край взят в вилку.
            Check("матрица: узлов прибавилось", with.Length > plain.Length);

            bool below = false, above = false, onEdge = false;
            foreach (double node in with)
            {
                double d = (node - 63.314) / 63.314;
                if (Math.Abs(d) < 1e-6) onEdge = true;
                else if (d < 0.0 && d > -2.0 * EfficiencyCalculationOptions.EdgeOffset) below = true;
                else if (d > 0.0 && d < 2.0 * EfficiencyCalculationOptions.EdgeOffset) above = true;
            }

            Check("матрица: узел под краем", below);
            Check("матрица: узел над краем", above);
            Check("матрица: на самом крае узла НЕТ", !onEdge);
        }

        static void Check(string title, bool ok)
        {
            if (!ok)
            {
                bad++;
                Console.WriteLine("    ПЛОХО  " + title);
            }
        }
    }
}
