using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace MaterialMigrationProbe
{
    /// <summary>
    /// `AMBER67` (П133, 22.09.2026): ПЕРЕНОС библиотеки веществ существующего
    /// пользователя на засев поколения 8 — что лежит у человека в файле, что с
    /// этим делает <see cref="GeometryMaterialStore"/> и что после этого
    /// получают слоты его приборов.
    ///
    ///     materialmigrationprobe --lib=&lt;GeometryMaterials.xml&gt;
    ///                            [--devices=&lt;каталог config\device&gt;]
    ///                            [--expect=old|new|handedit|seed]
    ///
    /// ⛔ Файл `--lib` КЛАДЁТСЯ РЯДОМ С ПРОБОЙ (`config\GeometryMaterials.xml`)
    /// и оттуда читается: путь библиотеки берёт <see cref="Package"/> от
    /// каталога сборки, задать его снаружи нечем. Сам `--lib` не трогается —
    /// работа идёт с копией, и каталог пробы одноразовый.
    ///
    /// Что печатается (всё — инвариантной культурой, точкой):
    ///
    /// 1. Файл: `SeedVersion`, число записей, список `Removed`.
    /// 2. Три записи, вокруг которых спор: «Air, dry», «Glass» и «Glass, plate» —
    ///    сокращение, формула, плотность, ВИД и массовые доли, как их отдаёт
    ///    <see cref="GeometryMaterialStore.Entries"/> (то есть ПОСЛЕ сведения с
    ///    засевом, а не как в файле).
    /// 3. Ослабление: стенка сосуда 2 мм и зазор 21.7 мм — μ/ρ и пропускание на
    ///    30 / 60 / 100 / 662 / 1332 кэВ тем же счётом, что
    ///    <see cref="GeometryMaterial.LinearAttenuation"/>.
    /// 4. Слоты приборов (`--devices`): каждая геометрия каждой кривой — слоты
    ///    с веществом «Air, dry», их состав, толщина зазора и КЛЕЙМО
    ///    (<see cref="ResponseMatrix.ComputeStamp"/>) — чтобы видеть числом,
    ///    поедет ли оно от переноса.
    ///
    /// Код возврата: 0 — наблюдаемое состояние совпало с `--expect`, 1 — нет,
    /// 2 — обращение неверно (нет файла, нет записи). `--expect` проверяет:
    ///   `old`      — переноса НЕ было (дефект `AMBER67` воспроизведён);
    ///   `new`      — перенос состоялся: воздух с аргоном, «Glass» снято,
    ///                «Glass, plate» стала стенкой сосуда, слоты с аргоном;
    ///   `handedit` — воздух ПРАВЛЕН РУКОЙ и потому НЕ тронут (положительный
    ///                контроль: на этом входе проверка обязана отказать, если
    ///                перенос тронет правленную запись);
    ///   `seed`     — файл свежего засева: записи совпадают с засевом побитово.
    /// </summary>
    static class Program
    {
        static readonly double[] Energies = { 30.0, 60.0, 100.0, 662.0, 1332.0 };

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string lib = null;
            string devices = null;
            string expect = null;
            string seedout = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--lib=", StringComparison.Ordinal)) lib = a.Substring(6);
                else if (a.StartsWith("--devices=", StringComparison.Ordinal)) devices = a.Substring(10);
                else if (a.StartsWith("--expect=", StringComparison.Ordinal)) expect = a.Substring(9);
                else if (a.StartsWith("--seedout=", StringComparison.Ordinal)) seedout = a.Substring(10);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (string.IsNullOrEmpty(lib) || !File.Exists(lib))
            {
                Console.Error.WriteLine("нет файла библиотеки: --lib=<GeometryMaterials.xml>");
                return 2;
            }

            string target = GeometryMaterialStore.FilePath;
            string dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.Copy(lib, target, true);
            GeometryMaterialStore.Reload();

            Console.WriteLine("библиотека: {0}", lib);
            Console.WriteLine("читается как: {0}", target);
            Console.WriteLine("поколение засева в коде (CurrentSeedVersion): {0}",
                              GeometryMaterialStore.CurrentSeedVersion.ToString(CultureInfo.InvariantCulture));

            GeometryMaterialConfig raw = ReadRaw(target);
            if (raw == null)
            {
                Console.Error.WriteLine("файл библиотеки не прочёлся");
                return 2;
            }

            Console.WriteLine();
            Console.WriteLine("1. Файл как он есть");
            Console.WriteLine("   SeedVersion = {0}", raw.SeedVersion.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("   записей     = {0}", (raw.Materials == null ? 0 : raw.Materials.Length).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("   Removed     = {0}", raw.Removed == null || raw.Removed.Length == 0 ? "(пусто)" : string.Join(", ", raw.Removed));

            List<GeometryMaterialLibrary.Entry> entries = GeometryMaterialStore.Entries;
            if (!string.IsNullOrEmpty(GeometryMaterialStore.LoadError))
            {
                Console.Error.WriteLine("отказ чтения: " + GeometryMaterialStore.LoadError);
                return 2;
            }

            Console.WriteLine("   библиотека после сведения с засевом: {0} веществ",
                              entries.Count.ToString(CultureInfo.InvariantCulture));

            Console.WriteLine();
            Console.WriteLine("2. Спорные записи (как их отдаёт GeometryMaterialStore.Entries)");
            GeometryMaterialLibrary.Entry air = Show(entries, "Air, dry");
            GeometryMaterialLibrary.Entry glass = Show(entries, "Glass");
            GeometryMaterialLibrary.Entry plate = Show(entries, "Glass, plate");
            GeometryMaterialLibrary.Entry airNist = Show(entries, "Air, dry (near sea level)");
            Console.WriteLine("   [контроль неизменности] ");
            GeometryMaterialLibrary.Entry nai = Show(entries, "Sodium iodide");
            GeometryMaterialLibrary.Entry al = Show(entries, "Aluminum");
            GeometryMaterialLibrary.Entry water = Show(entries, "Water, liquid");

            GeometryMaterialLibrary.Entry wall = WallGlass(entries);
            Console.WriteLine("   стекло вида BeakerWall: {0}", wall == null ? "НЕТ" : wall.Name);

            Console.WriteLine();
            Console.WriteLine("3. Ослабление: стенка 2 мм и зазор 21.7 мм");
            Console.WriteLine("   {0,-30} {1,10} {2,9}  {3}", "вещество", "ρ", "слой, мм", string.Join("  ", Header()));
            GeometryMaterial wallM = Make(wall);
            GeometryMaterial airM = Make(air);
            if (wallM != null) Row("стенка сосуда (BeakerWall)", wallM, 2.0);
            if (airM != null) Row("Air, dry", airM, 21.7);

            int badSlots = 0;
            int oldSlots = 0;
            int newSlots = 0;
            if (!string.IsNullOrEmpty(devices))
            {
                Console.WriteLine();
                Console.WriteLine("4. Слоты приборов ({0})", devices);
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                Slots(devices, ref oldSlots, ref newSlots, ref badSlots);
                watch.Stop();
                Console.WriteLine("   чтение всех конфигураций приборов: {0} мс (в них — подъём matdb, если перенос слотов до него дошёл)",
                                  watch.Elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture));
                Console.WriteLine("   ИТОГО слотов с веществом «Air, dry»: прежний состав (N,O) {0}, состав библиотеки {1}",
                                  oldSlots.ToString(CultureInfo.InvariantCulture),
                                  newSlots.ToString(CultureInfo.InvariantCulture));
            }

            // Свод — одной строкой, чтобы выдачи ДО и ПОСЛЕ сравнивались глазом.
            bool airArgon = air != null && air.ElementFractions.ContainsKey(18);
            bool glassGone = glass == null;
            bool plateWall = plate != null && plate.Kind == GeometryMaterialLibrary.MaterialKind.BeakerWall;
            Console.WriteLine();
            Console.WriteLine("СВОД: air.elements={0} air.argon={1} air.rho={2} glass={3} plate.kind={4} slots.old={5} slots.new={6}",
                              (air == null ? 0 : air.ElementFractions.Count).ToString(CultureInfo.InvariantCulture),
                              airArgon ? "1" : "0",
                              air == null ? "-" : R(air.Density),
                              glassGone ? "нет" : "есть",
                              plate == null ? "-" : plate.Kind.ToString(),
                              oldSlots.ToString(CultureInfo.InvariantCulture),
                              newSlots.ToString(CultureInfo.InvariantCulture));

            // Файл СВЕЖЕГО засева — для контроля неизменности: библиотека,
            // записанная нынешним поколением, обязана прочитаться побитово тем
            // же списком и переносом не тронуться.
            if (!string.IsNullOrEmpty(seedout))
            {
                GeometryMaterialStore.Save(GeometryMaterialLibrary.Seed());
                File.Copy(GeometryMaterialStore.FilePath, seedout, true);
                Console.WriteLine();
                Console.WriteLine("файл свежего засева записан: {0}", seedout);
            }

            if (string.IsNullOrEmpty(expect))
            {
                return 0;
            }

            Console.WriteLine();
            Console.WriteLine("Приёмка (--expect={0}):", expect);
            int bad = 0;
            switch (expect)
            {
                case "old":
                    bad += Check("воздух БЕЗ аргона (перенос не состоялся)", !airArgon);
                    bad += Check("«Glass» (кварц) в списке остался", !glassGone);
                    bad += Check("«Glass, plate» вида Other", plate != null && plate.Kind == GeometryMaterialLibrary.MaterialKind.Other);
                    bad += Check("слоты приборов несут прежний воздух", string.IsNullOrEmpty(devices) || oldSlots > 0);
                    break;
                case "new":
                    bad += Check("воздух С аргоном", airArgon);
                    bad += Check("воздух: 4 элемента", air != null && air.ElementFractions.Count == 4);
                    bad += Check("воздух: плотность 0.001205", air != null && air.Density == 0.001205);
                    bad += Check("«Glass» (кварц) снято", glassGone);
                    bad += Check("«Glass, plate» стала BeakerWall", plateWall);
                    bad += Check("«Glass, plate» плотность 2.4", plate != null && plate.Density == 2.4);
                    bad += Check("стекло вида BeakerWall — «Glass, plate»", wall != null && wall.Name == "Glass, plate");
                    bad += Check("слотов с прежним воздухом не осталось", string.IsNullOrEmpty(devices) || oldSlots == 0);
                    bad += Check("слоты приборов несут состав библиотеки", string.IsNullOrEmpty(devices) || newSlots > 0);
                    break;
                case "handedit":
                    bad += Check("воздух ПРАВЛЕН РУКОЙ и не тронут: без аргона", !airArgon);
                    bad += Check("воздух ПРАВЛЕН РУКОЙ и не тронут: плотность своя", air != null && air.Density != 0.001205);
                    bad += Check("слоты с прежним воздухом остались (состав не с чего брать)", string.IsNullOrEmpty(devices) || oldSlots > 0);
                    break;
                case "seed":
                    bad += Check("воздух С аргоном", airArgon);
                    bad += Check("«Glass» в засеве нет", glassGone);
                    bad += Check("«Glass, plate» — BeakerWall", plateWall);
                    bad += Check("библиотека совпала с засевом побитово", SameAsSeed(entries));
                    break;
                default:
                    Console.Error.WriteLine("неизвестное --expect: " + expect);
                    return 2;
            }

            bad += Check("контроль неизменности: Sodium iodide 3.667 Crystal",
                         nai != null && nai.Density == 3.667 && nai.Kind == GeometryMaterialLibrary.MaterialKind.Crystal);
            bad += Check("контроль неизменности: Aluminum 2.7 Cladding",
                         al != null && al.Density == 2.7 && al.Kind == GeometryMaterialLibrary.MaterialKind.Cladding);
            bad += Check("контроль неизменности: Water, liquid 1 Source",
                         water != null && water.Density == 1.0 && water.Kind == GeometryMaterialLibrary.MaterialKind.Source);
            bad += Check("контроль неизменности: строка ЛСРМ «Air, dry (near sea level)» на месте",
                         airNist != null && airNist.Kind == GeometryMaterialLibrary.MaterialKind.Other);
            bad += Check("слоты: вещество с именем «Air, dry» и третьим составом не встретилось", badSlots == 0);

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "РАСХОЖДЕНИЙ: " + bad.ToString(CultureInfo.InvariantCulture));
            return bad == 0 ? 0 : 1;
        }

        // ---- слоты приборов --------------------------------------------------

        static void Slots(string devices, ref int oldSlots, ref int newSlots, ref int bad)
        {
            if (!Directory.Exists(devices))
            {
                Console.Error.WriteLine("нет каталога приборов: " + devices);
                bad++;
                return;
            }

            string[] files = Directory.GetFiles(devices, "*.xml");
            Array.Sort(files, StringComparer.Ordinal);
            XmlSerializer serializer = new XmlSerializer(typeof(DeviceConfigInfo));
            foreach (string file in files)
            {
                DeviceConfigInfo info;
                try
                {
                    using (FileStream stream = new FileStream(file, FileMode.Open, FileAccess.Read))
                    {
                        info = (DeviceConfigInfo)serializer.Deserialize(stream);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("   {0}: не прочёлся — {1}", Path.GetFileName(file), e.Message);
                    continue;
                }

                if (info == null || info.EfficiencyConfigs == null)
                {
                    continue;
                }

                foreach (EfficiencyConfigData eff in info.EfficiencyConfigs)
                {
                    GeometryModel g = eff == null ? null : eff.Geometry;
                    if (g == null)
                    {
                        continue;
                    }

                    string stamp = ResponseMatrix.ComputeStamp(g, new ResponseMatrixOptions());
                    List<string> shown = new List<string>();
                    Slot(g.Crystal, "Crystal", shown, ref oldSlots, ref newSlots, ref bad);
                    Slot(g.Reflector, "Reflector", shown, ref oldSlots, ref newSlots, ref bad);
                    Slot(g.Gap, "Gap", shown, ref oldSlots, ref newSlots, ref bad);
                    Slot(g.Cladding, "Cladding", shown, ref oldSlots, ref newSlots, ref bad);
                    Slot(g.BeakerWall, "BeakerWall", shown, ref oldSlots, ref newSlots, ref bad);
                    Slot(g.Source, "Source", shown, ref oldSlots, ref newSlots, ref bad);
                    if (shown.Count == 0)
                    {
                        continue;
                    }

                    Console.WriteLine("   {0} / «{1}»: зазор {2}/{3} мм, клеймо {4}",
                                      Path.GetFileName(file), eff.Name,
                                      R(g.FrontGapThickness), R(g.SideGapThickness),
                                      stamp.Length > 20 ? stamp.Substring(0, 20) + "…" : stamp);
                    foreach (string s in shown)
                    {
                        Console.WriteLine("      " + s);
                    }
                }
            }
        }

        static void Slot(GeometryMaterial m, string what, List<string> shown,
                         ref int oldSlots, ref int newSlots, ref int bad)
        {
            if (m == null || m.Name != "Air, dry")
            {
                return;
            }

            bool argon = m.Fractions.ContainsKey(18);
            if (argon)
            {
                newSlots++;
            }
            else if (m.Fractions.Count == 2 && m.Fractions.ContainsKey(7) && m.Fractions.ContainsKey(8))
            {
                oldSlots++;
            }
            else
            {
                bad++;
            }

            shown.Add(string.Format(CultureInfo.InvariantCulture, "{0,-11} ρ={1} {2}",
                                    what, R(m.Density), Fractions(m.Fractions)));
        }

        // ---- печать ----------------------------------------------------------

        static GeometryMaterialConfig ReadRaw(string path)
        {
            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(GeometryMaterialConfig));
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    return (GeometryMaterialConfig)serializer.Deserialize(stream);
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
                return null;
            }
        }

        static GeometryMaterialLibrary.Entry Find(List<GeometryMaterialLibrary.Entry> list, string name)
        {
            foreach (GeometryMaterialLibrary.Entry e in list)
            {
                if (string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return e;
                }
            }

            return null;
        }

        static GeometryMaterialLibrary.Entry WallGlass(List<GeometryMaterialLibrary.Entry> list)
        {
            foreach (GeometryMaterialLibrary.Entry e in list)
            {
                if (e.Kind == GeometryMaterialLibrary.MaterialKind.BeakerWall
                    && e.Name != null
                    && e.Name.StartsWith("Glass", StringComparison.OrdinalIgnoreCase))
                {
                    return e;
                }
            }

            return null;
        }

        static GeometryMaterialLibrary.Entry Show(List<GeometryMaterialLibrary.Entry> list, string name)
        {
            GeometryMaterialLibrary.Entry e = Find(list, name);
            if (e == null)
            {
                Console.WriteLine("   {0,-30} НЕТ В СПИСКЕ", name);
                return null;
            }

            Console.WriteLine("   {0,-30} abbr={1,-6} formula=«{2}» ρ={3} kind={4} доли: {5}",
                              e.Name, e.Abbr, e.Formula, R(e.Density), e.Kind, Fractions(e.ElementFractions));
            return e;
        }

        static GeometryMaterial Make(GeometryMaterialLibrary.Entry e)
        {
            if (e == null)
            {
                return null;
            }

            try
            {
                return GeometryMaterialLibrary.Make(e, e.Density);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("состав «" + e.Name + "» не собрался: " + ex.Message);
                return null;
            }
        }

        static bool SameAsSeed(List<GeometryMaterialLibrary.Entry> list)
        {
            List<GeometryMaterialLibrary.Entry> seed = GeometryMaterialLibrary.Seed();
            if (seed.Count != list.Count)
            {
                Console.WriteLine("   засев {0} записей, библиотека {1}",
                                  seed.Count.ToString(CultureInfo.InvariantCulture),
                                  list.Count.ToString(CultureInfo.InvariantCulture));
                return false;
            }

            for (int i = 0; i < seed.Count; i++)
            {
                if (!Same(seed[i], list[i]))
                {
                    Console.WriteLine("   разошлась запись {0}: «{1}» против «{2}»",
                                      i.ToString(CultureInfo.InvariantCulture), seed[i].Name, list[i].Name);
                    return false;
                }
            }

            return true;
        }

        static bool Same(GeometryMaterialLibrary.Entry a, GeometryMaterialLibrary.Entry b)
        {
            if (a.Name != b.Name || a.Abbr != b.Abbr || a.Formula != b.Formula
                || a.Density != b.Density || a.Kind != b.Kind
                || a.ElementFractions.Count != b.ElementFractions.Count
                || a.Components.Count != b.Components.Count)
            {
                return false;
            }

            foreach (KeyValuePair<int, double> pair in a.ElementFractions)
            {
                double other;
                if (!b.ElementFractions.TryGetValue(pair.Key, out other) || other != pair.Value)
                {
                    return false;
                }
            }

            for (int i = 0; i < a.Components.Count; i++)
            {
                if (a.Components[i].Material != b.Components[i].Material
                    || a.Components[i].Weight != b.Components[i].Weight)
                {
                    return false;
                }
            }

            return true;
        }

        static string Fractions(Dictionary<int, double> f)
        {
            if (f == null || f.Count == 0)
            {
                return "(нет)";
            }

            List<int> zs = new List<int>(f.Keys);
            zs.Sort();
            StringBuilder sb = new StringBuilder();
            foreach (int z in zs)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(z.ToString(CultureInfo.InvariantCulture)).Append(':').Append(R(f[z]));
            }

            return sb.ToString();
        }

        static IEnumerable<string> Header()
        {
            foreach (double e in Energies)
            {
                yield return string.Format(CultureInfo.InvariantCulture, "{0,8:0.#} кэВ", e);
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

        static void Row(string label, GeometryMaterial m, double thicknessMm)
        {
            StringBuilder mu = new StringBuilder();
            StringBuilder tr = new StringBuilder();
            foreach (double e in Energies)
            {
                mu.Append(' ').Append(string.Format(CultureInfo.InvariantCulture, "{0,12:F8}", MassAttenuation(m, e)));
                tr.Append(' ').Append(string.Format(CultureInfo.InvariantCulture, "{0,12:F8}",
                                                    Math.Exp(-m.LinearAttenuation(e) * thicknessMm / 10.0)));
            }

            Console.WriteLine("   {0,-30} {1,10} {2,9}  μ/ρ:{3}", label, R(m.Density), R(thicknessMm), mu);
            Console.WriteLine("   {0,-30} {1,10} {2,9}  T:  {3}", "", "", "", tr);
        }

        static int Check(string what, bool ok)
        {
            Console.WriteLine("   [{0}] {1}", ok ? "да" : "НЕТ", what);
            return ok ? 0 : 1;
        }

        static string R(double v)
        {
            return v.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
