using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MaterialAirGlassProbe
{
    /// <summary>
    /// `AMBER53` (П123, 22.09.2026): засевные «Air, dry» и «Glass» против строк
    /// NIST того же засева — составом, ослаблением и умолчанием писателя.
    ///
    ///     materialairglassprobe
    ///
    /// Что меряется (все числа печатаются инвариантом, дробные — «R», чтобы
    /// выдачи ДО и ПОСЛЕ правки сравнивались построчно):
    ///
    /// 1. Состав и плотность засевных (<see cref="GeometryMaterialLibrary.Seed"/>)
    ///    «Air, dry» и «Glass» рядом со строками NIST «Air, dry (near sea
    ///    level)» и «Glass, plate» из того же засева.
    /// 2. μ/ρ смеси (сумма по долям <see cref="AttenuationData.MassAttenuation"/>,
    ///    как в <see cref="GeometryMaterial.LinearAttenuation"/>) на 30, 60,
    ///    100, 662 и 1332 кэВ и пропускание слоя: воздух 21.7 мм (самый толстый
    ///    зазор поставки, AS Pro 80x80), стекло 2 мм (стенка сосуда).
    /// 3. Контроль неизменности: «Sodium iodide» и «Aluminum» — тем же
    ///    столбцом; их строки обязаны совпасть у ДО и ПОСЛЕ до знака.
    /// 4. Умолчание писателя: геометрия из пресета (разбора `Raw` нет) →
    ///    <see cref="GeometryWriter.Render"/> → блок `SC_..EmptySpace` — ровно
    ///    тот путь, которым умолчание попадает и в файл `.in`, и в клеймо
    ///    матрицы (<c>ResponseMatrix.ComputeStamp</c>).
    /// 5. <see cref="GeometryModel.DefaultGapMaterial"/> — вещество зазора по
    ///    умолчанию (`AMBER47`): состав, который получит новая геометрия.
    ///
    /// Код возврата: 0 — засевные «Air, dry» и «Glass» совпали с NIST по
    /// составу (до 1e-6 по доле) и плотности, а умолчание писателя несёт
    /// аргон; 1 — дефект воспроизведён (так и должно быть ДО правки).
    /// </summary>
    static class Program
    {
        static readonly double[] Energies = { 30.0, 60.0, 100.0, 662.0, 1332.0 };

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            int bad = 0;

            List<GeometryMaterialLibrary.Entry> seed = GeometryMaterialLibrary.Seed();
            Console.WriteLine("засев: {0} веществ", seed.Count);
            Console.WriteLine();

            Console.WriteLine("1. Состав засевных веществ (массовые доли по Z, «R»)");
            GeometryMaterial air = Show(seed, "Air, dry");
            GeometryMaterial airNist = Show(seed, "Air, dry (near sea level)");
            // Стекло СТЕНКИ СОСУДА — то, что редактор предложит в списке
            // `BeakerWall`: до правки это «Glass» (кварц), после — «Glass, plate»
            // тем же видом. Ищется по виду, а не по имени, чтобы проба мерила
            // ОБА состояния одним кодом.
            GeometryMaterial glass = ShowWallGlass(seed);
            GeometryMaterial glassNist = Show(seed, "Glass, plate");
            GeometryMaterial nai = Show(seed, "Sodium iodide");
            GeometryMaterial al = Show(seed, "Aluminum");
            if (air == null || airNist == null || glass == null || glassNist == null || nai == null || al == null)
            {
                Console.Error.WriteLine("в засеве нет одного из шести веществ");
                return 2;
            }

            Console.WriteLine();
            Console.WriteLine("2. μ/ρ смеси, см²/г, и пропускание слоя");
            Console.WriteLine("   {0,-28} {1,8} {2,10}  {3}", "вещество", "ρ", "слой, мм", string.Join("  ", Header()));
            Row("Air, dry (засев)", air, 21.7);
            Row("Air, dry (near sea level)", airNist, 21.7);
            Row("стекло BeakerWall (засев)", glass, 2.0);
            Row("Glass, plate", glassNist, 2.0);
            Console.WriteLine();
            Console.WriteLine("   отношение пропускания засев / NIST (слой тот же):");
            Ratio("воздух 21.7 мм", air, airNist, 21.7);
            Ratio("стекло 2 мм", glass, glassNist, 2.0);

            Console.WriteLine();
            Console.WriteLine("3. Контроль неизменности (строки ДО и ПОСЛЕ обязаны совпасть до знака)");
            Row("Sodium iodide", nai, 10.0);
            Row("Aluminum", al, 2.0);

            Console.WriteLine();
            Console.WriteLine("4. Умолчание писателя: пресет «Atom Spectra Nano 16», Raw пуст → Render → EmptySpace");
            GeometryModel g = new GeometryModel();
            GeometryPresets.Preset preset = null;
            foreach (GeometryPresets.Preset p in GeometryPresets.Items)
            {
                if (p.Name == "Atom Spectra Nano 16")
                {
                    preset = p;
                }
            }

            if (preset == null)
            {
                Console.Error.WriteLine("нет пресета «Atom Spectra Nano 16»");
                return 2;
            }

            preset.Apply(g);
            Console.WriteLine("   Raw.Count = {0}", g.Raw.Count);
            string text = GeometryWriter.Render(g);
            bool writerHasArgon = false;
            int writerElements = 0;
            foreach (string line in text.Split(new[] { "\r\n" }, StringSplitOptions.None))
            {
                if (Regex.IsMatch(line, @"^(SC|SM)_(n|Ro|Z|Fractions)EmptySpace|^M_(SC|SM)_EmptySpace\.MName"))
                {
                    Console.WriteLine("   " + line);
                    Match m = Regex.Match(line, @"^SC_ZEmptySpace\[\d+\] = (\d+)");
                    if (m.Success)
                    {
                        writerElements++;
                        if (m.Groups[1].Value == "18")
                        {
                            writerHasArgon = true;
                        }
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine("5. GeometryModel.DefaultGapMaterial() (вещество зазора по умолчанию, AMBER47)");
            GeometryMaterial gap = GeometryModel.DefaultGapMaterial();
            Console.WriteLine("   {0} ρ={1} {2}", gap.Name, R(gap.Density), Fractions(gap));

            Console.WriteLine();
            Console.WriteLine("Приёмка:");
            bad += Same("«Air, dry» = NIST «Air, dry (near sea level)» по составу", airNist, air);
            bad += Same("стекло BeakerWall = NIST «Glass, plate» по составу", glassNist, glass);
            bad += Check("стекло BeakerWall: плотность = 2.4", Math.Abs(glass.Density - 2.4) < 1e-9);
            bad += Check("умолчание писателя (EmptySpace) несёт аргон (Z=18)", writerHasArgon);
            bad += Check("умолчание писателя (EmptySpace): 4 элемента", writerElements == 4);
            bad += Check("DefaultGapMaterial несёт аргон", gap.Fractions.ContainsKey(18));

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "РАСХОЖДЕНИЙ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        static GeometryMaterial ShowWallGlass(List<GeometryMaterialLibrary.Entry> seed)
        {
            foreach (GeometryMaterialLibrary.Entry e in seed)
            {
                if (e.Kind == GeometryMaterialLibrary.MaterialKind.BeakerWall
                    && e.Name.StartsWith("Glass", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Write("   [стекло стенки сосуда по виду BeakerWall]");
                    Console.WriteLine();
                    return Show(seed, e.Name);
                }
            }

            Console.WriteLine("   {0,-28} НЕТ В ЗАСЕВЕ", "стекло вида BeakerWall");
            return null;
        }

        static GeometryMaterial Show(List<GeometryMaterialLibrary.Entry> seed, string name)
        {
            GeometryMaterialLibrary.Entry entry = null;
            foreach (GeometryMaterialLibrary.Entry e in seed)
            {
                if (string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    entry = e;
                    break;
                }
            }

            if (entry == null)
            {
                Console.WriteLine("   {0,-28} НЕТ В ЗАСЕВЕ", name);
                return null;
            }

            GeometryMaterial m = GeometryMaterialLibrary.Make(entry, entry.Density);
            Console.WriteLine("   {0,-28} abbr={1,-6} formula=«{2}» ρ={3} kind={4}  {5}",
                              name, entry.Abbr, entry.Formula, R(entry.Density), entry.Kind, Fractions(m));
            return m;
        }

        static string Fractions(GeometryMaterial m)
        {
            List<int> zs = new List<int>(m.Fractions.Keys);
            zs.Sort();
            StringBuilder sb = new StringBuilder();
            foreach (int z in zs)
            {
                if (sb.Length > 0)
                {
                    sb.Append(' ');
                }

                sb.Append(z).Append(':').Append(R(m.Fractions[z]));
            }

            return sb.ToString();
        }

        static IEnumerable<string> Header()
        {
            foreach (double e in Energies)
            {
                yield return string.Format(CultureInfo.InvariantCulture, "{0,6:0.#} кэВ", e);
            }
        }

        static double MassAttenuation(GeometryMaterial m, double energyKev)
        {
            double sum = 0.0;
            foreach (KeyValuePair<int, double> pair in m.Fractions)
            {
                sum += pair.Value * AttenuationData.MassAttenuation(pair.Key, energyKev);
            }

            return sum;
        }

        static double Transmission(GeometryMaterial m, double thicknessMm, double energyKev)
        {
            return Math.Exp(-m.LinearAttenuation(energyKev) * thicknessMm / 10.0);
        }

        static void Row(string label, GeometryMaterial m, double thicknessMm)
        {
            StringBuilder mu = new StringBuilder();
            StringBuilder tr = new StringBuilder();
            foreach (double e in Energies)
            {
                mu.Append(' ').Append(R(MassAttenuation(m, e)));
                tr.Append(' ').Append(R(Transmission(m, thicknessMm, e)));
            }

            Console.WriteLine("   {0,-28} {1,8} {2,10}  μ/ρ:{3}", label, R(m.Density), R(thicknessMm), mu);
            Console.WriteLine("   {0,-28} {1,8} {2,10}  T:  {3}", "", "", "", tr);
        }

        static void Ratio(string label, GeometryMaterial a, GeometryMaterial b, double thicknessMm)
        {
            StringBuilder sb = new StringBuilder();
            foreach (double e in Energies)
            {
                double ta = Transmission(a, thicknessMm, e);
                double tb = Transmission(b, thicknessMm, e);
                sb.Append(string.Format(CultureInfo.InvariantCulture, "  {0,6:0.#} кэВ: T {1:F6}/{2:F6} = {3:F6} ({4:+0.000;-0.000} %), μ/ρ {5:+0.00;-0.00} %",
                                        e, ta, tb, ta / tb, 100.0 * (ta / tb - 1.0),
                                        100.0 * (MassAttenuation(a, e) / MassAttenuation(b, e) - 1.0)));
                sb.Append("\r\n");
            }

            Console.WriteLine("   {0}:", label);
            Console.Write(sb.ToString());
        }

        static int Same(string what, GeometryMaterial expected, GeometryMaterial got)
        {
            bool ok = expected.Fractions.Count == got.Fractions.Count;
            if (ok)
            {
                foreach (KeyValuePair<int, double> pair in expected.Fractions)
                {
                    double mine;
                    if (!got.Fractions.TryGetValue(pair.Key, out mine) || Math.Abs(mine - pair.Value) > 1e-6)
                    {
                        ok = false;
                    }
                }
            }

            return Check(what, ok);
        }

        static int Check(string what, bool ok)
        {
            Console.WriteLine("   {0} — {1}", what, ok ? "сошлось" : "НЕ СОШЛОСЬ");
            return ok ? 0 : 1;
        }

        static string R(double v)
        {
            return v.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
