using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

// B1: геометрии «ПОНЯТНОЙ» части корпуса — построить их из встроенных шаблонов
// приложения и паспортных данных самих спектров, записать файлами `.in` и
// проверить машинно, что объём пробы у построенной сцены сходится с паспортным.
//
// Зачем. Корпусным спектрам геометрия не задана, поэтому образ компонента у них
// строится старым путём (из одних пиков), и числа «с матрицей» и «без матрицы»
// несравнимы — это и есть блокер B1. Геометрию можно восстановить не везде:
// решение Amber 09.08.2026 — «понятными» объявляются только те спектры, у
// которых сосуд и расстояние НАЗВАНЫ, остальные идут в «непонятные» без
// выдумывания. Названы они у группы G1S (Гамма-1С УДС-ГЦ 63x63, паспорт лежит
// в самом файле спектра: объём, масса, активность, расстояние) и у одного
// спектра RC-103 (маринелли 0.5 л с 680 г KCl).
//
// Что откуда берётся, и это здесь главное:
//
//   ДЕТЕКТОР   — целиком из `GeometryPresets`, по ИМЕНИ. Ни одного размера
//                кристалла или обвязки в этом файле не набрано.
//   СОСУД      — «восстановлен из объёма» (решение Amber): ОДИН размер принят
//                по виду посуды, остальные ВЫВЕДЕНЫ из паспортного объёма.
//                Принятое помечено в печати словом «принято», выведенное —
//                «выведено». Это ДОПУЩЕНИЕ: самопоглощение зависит от формы, а
//                не только от объёма, и при той же вместимости плоская чашка и
//                высокая банка дают разные кривые.
//   ПЛОТНОСТЬ  — ИЗМЕРЕНА: паспортная масса, делённая на объём пробы той сцены,
//                которая построена. Масса при этом сохраняется точно.
//   МАРИНЕЛЛИ  — у RC-103 не восстанавливается вовсе: это тот самый сосуд, что
//                лежит в поставке ЛСРМ (`RadiaCode_Marinelli0.5.in`), и его
//                размеры слово в слово повторяет заготовка редактора. Берём
//                заготовку.
//
// Объём пробы считается ТЕМИ ЖЕ формулами, по которым сцену строит
// `EfficiencySimulator.Build` (цилиндр: π·r_вн²·h; маринелли: кольцо вокруг
// колодца плюс шапка над его потолком). Это не второй расчёт того же, а
// проверка: разойдись они — сойдётся и печать, и файл, а сцена будет другой.
//
//   corpusgeomprobe [--out=tools\CORPUS\corpus\geometries] [--dry]
class CorpusGeomProbe
{
    const double Eps = 1e-9;

    sealed class Geom
    {
        public string Key;              // имя файла без расширения
        public string Preset;           // имя пресета детектора
        public string Vessel;           // как названо в паспорте
        public string[] Spectra;        // спектры корпуса, которым она принадлежит
        public double PassportVolumeMl; // 0 — объёма в паспорте нет (или источник точечный)
        public double PassportMassG;    // 0 — точечный источник
        public string NominalVolume;    // назван в паспорте, но под него НЕ подгонялось
        public string SourceMaterial;
        public Action<GeometryModel> Shape;
        public string Assumed;          // что ПРИНЯТО, словами
        public GeometryDetectorFacing Facing = GeometryDetectorFacing.Front;
    }

    static int Main(string[] args)
    {
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        string outDir = null;
        bool dry = false;
        foreach (string a in args)
        {
            if (a.StartsWith("--out=", StringComparison.Ordinal)) outDir = a.Substring(6);
            else if (a == "--dry") dry = true;
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }

        if (outDir == null)
        {
            outDir = Path.Combine("tools", "CORPUS", "corpus", "geometries");
        }

        // Менеджеры нужны библиотеке веществ: пресеты зовут GeometryMaterialLibrary,
        // а она читает matdb.
        GlobalConfigManager.GetInstance();

        List<Geom> all = Build();
        bool ok = true;
        int written = 0;

        Console.WriteLine("Геометрии «понятной» части корпуса (B1)");
        Console.WriteLine();

        foreach (Geom spec in all)
        {
            GeometryModel g = GeometryEditorPanel.Blank();
            GeometryPresets.Preset preset =
                GeometryPresets.Items.FirstOrDefault(p => p.Name == spec.Preset);
            if (preset == null)
            {
                Console.Error.WriteLine("во встроенных пресетах нет «" + spec.Preset + "»: "
                    + string.Join(", ", GeometryPresets.Items.Select(p => p.Name)));
                return 1;
            }

            preset.Apply(g);
            g.Name = spec.Key;
            g.Facing = spec.Facing;                 // E21: сторона, обращённая к пробе
            spec.Shape(g);
            if (!string.IsNullOrEmpty(g.FacingError))
            {
                Console.Error.WriteLine(spec.Key + ": " + g.FacingError);
                return 1;
            }

            double volumeMm3 = SampleVolumeMm3(g);
            double volumeMl = volumeMm3 / 1000.0;

            if (spec.PassportMassG > 0.0)
            {
                // Плотность ИЗМЕРЕНА: масса паспорта на объём построенной сцены.
                double density = spec.PassportMassG / volumeMl;
                GeometryMaterialLibrary.Entry entry =
                    GeometryMaterialLibrary.ByName(spec.SourceMaterial);
                if (entry == null)
                {
                    Console.Error.WriteLine("в библиотеке нет вещества «"
                                            + spec.SourceMaterial + "»");
                    return 1;
                }

                g.Source = GeometryMaterialLibrary.Make(entry, density);
            }

            Console.WriteLine("== {0} ==", spec.Key);
            Console.WriteLine("   детектор : пресет «{0}», ПШПВ {1:F2} % на 662",
                              spec.Preset, g.FwhmAt662Percent);
            Console.WriteLine("   сосуд    : {0}", spec.Vessel);
            Console.WriteLine("   спектры  : {0}", string.Join(", ", spec.Spectra));
            if (spec.PassportMassG > 0.0)
            {
                // Пустое `Assumed` — не «забыли написать», а «принимать нечего»:
                // всё названо. Печатать «принято : » с пустотой значило бы
                // изображать допущение там, где его нет.
                Console.WriteLine("   принято  : {0}",
                                  string.IsNullOrEmpty(spec.Assumed) ? "ничего, всё названо" : spec.Assumed);
                if (spec.PassportVolumeMl > 0.0)
                {
                    double diff = 100.0 * (volumeMl - spec.PassportVolumeMl) / spec.PassportVolumeMl;
                    bool fits = Math.Abs(diff) < 0.05;
                    ok &= fits;
                    Console.WriteLine("   объём    : сцена {0:F2} мл, паспорт {1:F2} мл,"
                                      + " расхождение {2:F3} %  {3}",
                                      volumeMl, spec.PassportVolumeMl, diff,
                                      fits ? "СОШЛОСЬ" : "РАЗОШЛОСЬ");
                }
                else
                {
                    Console.WriteLine("   объём    : сцена {0:F2} мл; в паспорте назван только"
                                      + " номинал {1} — под него НЕ подгонялось",
                                      volumeMl, spec.NominalVolume);
                }

                Console.WriteLine("   проба    : {0}, {1:F4} г/см3 (масса паспорта {2:F1} г"
                                  + " на объём сцены — ИЗМЕРЕНО)",
                                  g.Source.Name, g.Source.Density, spec.PassportMassG);
            }
            else
            {
                Console.WriteLine("   принято  : ничего — точечный источник, задано только"
                                  + " расстояние {0:F0} мм", g.PointDistance);
            }

            if (!dry)
            {
                Directory.CreateDirectory(outDir);
                string path = Path.Combine(outDir, spec.Key + ".in");
                GeometryWriter.Save(g, path);
                written++;
                Console.WriteLine("   файл     : {0}", path);
            }

            Console.WriteLine();
        }

        Console.WriteLine("геометрий: {0}, спектров под ними: {1}",
                          all.Count, all.Sum(x => x.Spectra.Length));
        if (!dry)
        {
            // Опись «геометрия -> спектры» пишется ЗДЕСЬ ЖЕ и тем же проходом,
            // что и сами файлы: второй список тех же пар, набранный в скрипте
            // раздела, разошёлся бы с файлами при первой правке. Читатель —
            // `tools/CORPUS/scripts/split_corpus.py`.
            string indexPath = Path.Combine(outDir, "index.csv");
            using (StreamWriter w = new StreamWriter(indexPath, false,
                                                     new System.Text.UTF8Encoding(false)))
            {
                w.WriteLine("geometry,spectrum,preset,vessel");
                foreach (Geom spec in all)
                {
                    foreach (string s in spec.Spectra)
                    {
                        w.WriteLine("{0},{1},{2},\"{3}\"",
                                    spec.Key, s, spec.Preset, spec.Vessel.Replace("\"", "\"\""));
                    }
                }
            }

            Console.WriteLine("записано файлов: {0} в {1}", written, Path.GetFullPath(outDir));
            Console.WriteLine("опись           : {0}", indexPath);
        }

        Console.WriteLine(ok ? "ВСЕ СОШЛИСЬ" : "ЕСТЬ РАЗОШЕДШИЕСЯ");
        return ok ? 0 : 1;
    }

    // ----------------------------------------------------------------------
    // Объём пробы — теми же формулами, что у EfficiencySimulator.Build
    // ----------------------------------------------------------------------
    static double SampleVolumeMm3(GeometryModel g)
    {
        switch (g.SourceType)
        {
            case GeometrySourceType.Point:
                return 0.0;

            case GeometrySourceType.Cylinder:
            {
                double rOut = 0.5 * g.BeakerDiameter;
                double rIn = Math.Max(0.0, rOut - g.BeakerSideWallThickness);
                return Math.PI * rIn * rIn * g.SourceHeight;
            }

            case GeometrySourceType.Box:
            {
                double ax = Math.Max(0.0, 0.5 * g.BoxSourceX - g.BoxSideWallThickness);
                double ay = Math.Max(0.0, 0.5 * g.BoxSourceY - g.BoxSideWallThickness);
                return 4.0 * ax * ay * g.BoxSourceHeight;
            }

            default:
            {
                double rHole = 0.5 * g.MarinelliHoleDiameter + g.MarinelliHoleSideThickness;
                double rOut = Math.Max(0.5 * g.MarinelliBeakerDiameter, rHole + 0.1);
                double rSrcOut = Math.Max(rHole, rOut - g.MarinelliSideThickness);
                double cap = Math.Max(0.0, g.MarinelliSourceHeight - g.MarinelliHoleHeight);
                return Math.PI * (rSrcOut * rSrcOut - rHole * rHole) * g.MarinelliSourceHeight
                     + Math.PI * rHole * rHole * cap;
            }
        }
    }

    /// <summary>
    /// Внешний радиус собранного детектора: кристалл плюс всё, что на нём
    /// надето сбоку. Нужен, чтобы колодец маринелли не оказался уже прибора —
    /// число берётся у пресета, а не набирается здесь.
    /// </summary>
    static double DetectorOuterRadius(GeometryModel g)
    {
        double rCrystal = g.Shape == CrystalShape.Box
            ? 0.5 * Math.Sqrt(g.CrystalBoxX * g.CrystalBoxX + g.CrystalBoxY * g.CrystalBoxY)
            : 0.5 * g.CrystalDiameter;
        return rCrystal + g.SideReflectorThickness + g.SideCladdingThickness + g.MountingThickness;
    }

    // ----------------------------------------------------------------------
    // Сосуды
    // ----------------------------------------------------------------------

    /// <summary>
    /// Цилиндрический сосуд: ПРИНЯТ внутренний диаметр (по виду посуды),
    /// ВЫВЕДЕНА высота слоя пробы — из паспортного объёма. Стенка и дно берутся
    /// у заготовки редактора и не трогаются.
    /// </summary>
    static void Beaker(GeometryModel g, double innerDiameterMm, double volumeMl,
                       double distanceMm)
    {
        g.SourceType = GeometrySourceType.Cylinder;
        double rIn = 0.5 * innerDiameterMm;
        g.BeakerDiameter = innerDiameterMm + 2.0 * g.BeakerSideWallThickness;
        g.SourceHeight = volumeMl * 1000.0 / (Math.PI * rIn * rIn);
        g.BeakerHeight = g.SourceHeight + g.BeakerEndWallThickness;
        g.BeakerToDetectorDistance = distanceMm;
    }

    /// <summary>
    /// Маринелли: ПРИНЯТЫ колодец (по внешнему размеру собранного детектора
    /// плюс зазор) и высота слоя пробы, ВЫВЕДЕН внешний диаметр — из
    /// паспортного объёма. Обратная задача к <see cref="SampleVolumeMm3"/>.
    /// </summary>
    static void Marinelli(GeometryModel g, double clearanceMm, double wellDepthMm,
                          double sourceHeightMm, double volumeMl)
    {
        g.SourceType = GeometrySourceType.Marinelli;
        g.MarinelliHoleDiameter = 2.0 * (DetectorOuterRadius(g) + clearanceMm);
        g.MarinelliHoleHeight = wellDepthMm;
        g.MarinelliSourceHeight = sourceHeightMm;

        double rHole = 0.5 * g.MarinelliHoleDiameter + g.MarinelliHoleSideThickness;
        double cap = Math.Max(0.0, sourceHeightMm - wellDepthMm);
        double rSrcOut2 = (volumeMl * 1000.0 / Math.PI - rHole * rHole * cap) / sourceHeightMm
                        + rHole * rHole;
        double rSrcOut = Math.Sqrt(Math.Max(rSrcOut2, rHole * rHole + Eps));
        g.MarinelliBeakerDiameter = 2.0 * (rSrcOut + g.MarinelliSideThickness);
        g.MarinelliBeakerHeight = sourceHeightMm + g.MarinelliEndWallThickness
                                + g.MarinelliHoleEndWallThickness;
    }

    // ----------------------------------------------------------------------
    // Состав «понятной» части
    // ----------------------------------------------------------------------
    /// <summary>
    /// Сосудные сцены поверки ЛСРМ — из таблицы `data/lsrm_spectrum_geometry.csv`
    /// (`B12`), а не из вписанного руками списка. Строка таблицы взята прямо из
    /// заголовка исходного `.spe`: сосуд, вещество с составом, масса, объём,
    /// расстояние. Одна сцена — на каждую тройку (сосуд, вещество, плотность):
    /// плотность у эталонов разная, от 0.55 до 1.67, а от неё и зависит
    /// самопоглощение.
    ///
    /// ⚠ Внутренние диаметры сосудов больше НЕ ПРИНЯТЫ на глаз (было: банка
    /// 60 мм, чашка 100 мм), а ВЫВЕДЕНЫ из объёма и толщины слоя, которые
    /// названы в поставке: D = 2·√(V/πh). Проверка сходится сама собой — у
    /// «Денты» 120 мл при h = 33 мм и 100 мл при h = 27.2 мм получается один и
    /// тот же диаметр, 68.0 и 68.4 мм, а это одна и та же банка.
    /// </summary>
    static List<Geom> VesselScenes(string preset)
    {
        string path = Path.Combine(CorpusRoot(), "data", "lsrm_spectrum_geometry.csv");
        List<Geom> list = new List<Geom>();
        if (!File.Exists(path))
        {
            Console.WriteLine("нет {0} — сосудные сцены не строятся (import_spe_geometry.py)", path);
            return list;
        }

        string[] lines = File.ReadAllLines(path, Encoding.UTF8);
        if (lines.Length < 2)
        {
            return list;
        }

        List<string> head = new List<string>(lines[0].TrimStart('﻿').Split(','));
        int iKey = head.IndexOf("спектр"), iVessel = head.IndexOf("сосуд");
        int iMat = head.IndexOf("вещество"), iMass = head.IndexOf("масса_г");
        int iVol = head.IndexOf("объём_мл"), iRo = head.IndexOf("плотность");
        if (iKey < 0 || iVessel < 0 || iMat < 0 || iRo < 0)
        {
            Console.WriteLine("в {0} нет нужных колонок", path);
            return list;
        }

        Dictionary<string, List<string>> scenes = new Dictionary<string, List<string>>();
        Dictionary<string, string[]> sample = new Dictionary<string, string[]>();
        foreach (string line in lines)
        {
            string[] c = line.Split(',');
            if (c.Length <= iRo || c[iKey] == "спектр" || c[iMat].Length == 0)
            {
                continue;
            }

            string key = SceneKey(c[iVessel], c[iMat], c[iRo]);
            if (key == null)
            {
                continue;
            }

            if (!scenes.ContainsKey(key))
            {
                scenes[key] = new List<string>();
                sample[key] = c;
            }

            scenes[key].Add(c[iKey]);
        }

        foreach (KeyValuePair<string, List<string>> pair in scenes)
        {
            string[] c = sample[pair.Key];
            string vessel = c[iVessel];
            double volume = Num(c[iVol]), mass = Num(c[iMass]), density = Num(c[iRo]);
            string material = c[iMat];
            Geom g = new Geom
            {
                Key = pair.Key,
                Preset = preset,
                Vessel = string.Format(CultureInfo.InvariantCulture,
                                       "{0}, набивка {1} {2:0.###} г/см³, вплотную",
                                       vessel, material, density),
                Spectra = pair.Value.ToArray(),
                PassportVolumeMl = volume,
                PassportMassG = mass,
                SourceMaterial = material,
                Assumed = "ничего: сосуд, вещество, масса и объём взяты из заголовка `.spe`; "
                          + "внутренний диаметр выведен из объёма и толщины слоя поставки",
            };

            double vol = volume;
            if (vessel.StartsWith("Маринелли", StringComparison.Ordinal))
            {
                // Маринелли кольцевой: сцена та же, что у прежней
                // `G1S_marinelli1l_th232` (колодец по внешнему размеру прибора
                // плюс 1.5 мм, глубина 70 мм, слой 100 мм, внешний диаметр из
                // объёма) — меняются только вещество и плотность.
                g.Shape = m => Marinelli(m, 1.5, 70.0, 100.0, vol);
            }
            else
            {
                double thick = vessel.Contains("100") ? 27.2 : (vessel.Contains("Петри") ? 10.0 : 33.0);
                double diameter = 2.0 * Math.Sqrt(vol * 1000.0 / (Math.PI * thick));
                g.Shape = m => Beaker(m, diameter, vol, 0.0);
            }

            list.Add(g);
        }

        list.Sort((x, y) => string.CompareOrdinal(x.Key, y.Key));
        return list;
    }

    /// <summary>Имя сцены: сосуд, набивка и плотность — всё, чем они различаются.</summary>
    static string SceneKey(string vessel, string material, string density)
    {
        double ro = Num(density);
        if (!(ro > 0.0))
        {
            return null;
        }

        string v = vessel.StartsWith("Маринелли", StringComparison.Ordinal) ? "mar1l"
                 : vessel.Contains("Петри") ? "petri60"
                 : vessel.Contains("100") ? "denta100"
                 : vessel.Contains("120") ? "denta120" : null;
        if (v == null)
        {
            return null;
        }

        string m = material.Replace("ОИСН-", "oisn").Replace("РИСН-", "risn").Replace(" ", "");
        return string.Format(CultureInfo.InvariantCulture, "G1S_{0}_{1}_{2:000}",
                             v, m, Math.Round(ro * 100.0));
    }

    static double Num(string text)
    {
        double value;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            ? value : 0.0;
    }

    /// <summary>Корень `tools/CORPUS` — от каталога запуска вверх.</summary>
    static string CorpusRoot()
    {
        string dir = Directory.GetCurrentDirectory();
        for (int i = 0; i < 6 && dir != null; i++)
        {
            string cand = Path.Combine(dir, "tools", "CORPUS");
            if (Directory.Exists(cand))
            {
                return cand;
            }

            dir = Path.GetDirectoryName(dir);
        }

        return Path.Combine("tools", "CORPUS");
    }

    static List<Geom> Build()
    {
        const string G1S = "Gamma-1S UDS-GC 63x63";
        const string RC103 = "RadiaCode-103";

        // Приняты по виду посуды: банка «Дента» — 60 мм, чашка Петри — 100 мм.
        // Всё остальное у этих двух сосудов выведено из объёма.
        const double DentaInnerDiameter = 60.0;
        const double PetriInnerDiameter = 100.0;

        List<Geom> list = new List<Geom>();

        // B6 (решение Amber 15.08.2026). Двенадцать прежних ключей `G1S_*`
        // оказались ПОБАЙТНЫМИ дубликатами двенадцати поверочных эталонов —
        // одно и то же измерение стояло в корпусе дважды, в понятной части под
        // старым именем и в непонятной под эталонным. Копии `G1S_*` сняты, а
        // геометрии перевешены сюда, на эталоны-оригиналы: у эталона в
        // `SampleInfo.Note` лежит паспорт источника, у прежней копии его не было.
        // Соответствие ключей — `tools/CORPUS/README.md`, раздел «Двенадцать
        // прежних G1S — побайтные дубликаты». Суффикс имени — ГОД ПОВЕРКИ, и
        // одна геометрия законно собирает спектры обоих годов: сосуд и
        // расстояние от года не зависят, зависит только разрешение (модель
        // разрешения делится отдельно, по группам `G1S16`/`G1S24`).
        list.Add(new Geom
        {
            Key = "G1S_point5",
            Preset = G1S,
            Vessel = "точечный источник, 5 см от торца",
            // Все поверочные точечные съёмки 5 см — ОДНА геометрия: паспорт
            // эталонов (`Паспорт эталонов\АСПЕКТ_ОСГИ_2024.src`) у всех ОСГИ
            // пишет `Geometry=Точечная`, `Material=not essential`, `Mass,g=0`
            // и `Thick,mm=0`, то есть вещества и объёма у источника нет вовсе
            // и сцена от нуклида не зависит. Расстояние стоит в ИМЕНИ файла
            // каждой съёмки (`…_Точечная-5см_5cm.xml`). До 16.08.2026 сюда
            // были вписаны два спектра из двадцати трёх, а остальные
            // двадцать один числились «геометрии нет».
            Spectra = new[]
            {
                "G1S16_Am241_P5", "G1S16_Ba133_P5", "G1S16_Cd109_P5",
                "G1S16_Ce139_P5", "G1S16_Co57_P5", "G1S16_Co60_P5",
                "G1S16_Cs137_P5", "G1S16_Eu152_P5", "G1S16_Mn54_P5",
                "G1S16_Na22_P5", "G1S16_Th228_P5", "G1S16_Y88_P5", "G1S24_Am241_P5",
                "G1S24_Ba133_P5", "G1S24_Bi207_P5", "G1S24_Cd109_P5",
                "G1S24_Co60_P5", "G1S24_Cs137_P5", "G1S24_Eu152_P5",
                "G1S24_Na22_P5", "G1S24_Th228_P5", "G1S24_Y88_P5", "G1S24_Zn65_P5"
            },
            Shape = g => { g.SourceType = GeometrySourceType.Point; g.PointDistance = 50.0; },
        });

        list.Add(new Geom
        {
            Key = "G1S_point25",
            Preset = G1S,
            Vessel = "точечный источник, 25 см от торца",
            // То же и здесь: расстояние из имени файла (`…_25cm.xml`),
            // вещества у точечного источника нет. Было четыре из пятнадцати.
            Spectra = new[]
            {
                "G1S16_Am241_P25", "G1S16_Ba133_P25", "G1S16_Cd109_P25",
                "G1S16_Ce139_P25", "G1S16_Co60_P25", "G1S16_Cs137_P25",
                "G1S16_Eu152_P25", "G1S16_Mn54_P25", "G1S16_Na22_P25",
                "G1S16_Th228_P25", "G1S16_Y88_P25", "G1S24_Cs137_P25",
                "G1S24_Na22_P25", "G1S24_Th228_P25", "G1S24_Y88_P25"
            },
            Shape = g => { g.SourceType = GeometrySourceType.Point; g.PointDistance = 250.0; },
        });

                                                        // Единственный сосуд, который НЕ восстанавливается: маринелли 0.5 л
        // RadiaCode лежит в поставке ЛСРМ (`RadiaCode_Marinelli0.5.in`), и
        // заготовка редактора повторяет его размеры слово в слово. Значит взят
        // настоящий сосуд, а не выведенный: ни одного принятого размера.
        // Вместимость его сцены — 631.9 мл при названных «0.5 л»: налито 85 мм
        // из 89, и паспортные 680 г KCl дают на этом объёме 1.076 г/см3 —
        // насыпная плотность рыхлого хлорида калия, что сходится.
        // Два паспортных Cs-137 «впритык» (Amber, 14.08.2026 вечер, B5):
        // точечный источник на самом торце (0 мм). Спектры в корпус ещё не
        // добавлены — ключи назначены вперёд, добавление идёт следом за
        // расчётом кривых и матриц. У AS80x80 кристалл — ИОДИД НАТРИЯ: 14.08
        // он был заменён на цезиевый по слову Amber, а 15.08 замена отменена
        // («я ошибся, кристалл NaI»), и пресет возвращён. Файл `AS80_point0.in`
        // всё это время оставался с натрием, поэтому матрица и кривая этой
        // геометрии верны и пересчёта не требуют (строка `B7`).
        list.Add(new Geom
        {
            Key = "AS80_point0",
            Preset = "Atom Spectra Pro 80x80",
            Vessel = "точечный источник, вплотную к торцу",
            Spectra = new[] { "AS80_Cs137_0cm" },
            Shape = g => { g.SourceType = GeometrySourceType.Point; g.PointDistance = 0.0; },
        });

        list.Add(new Geom
        {
            Key = "RC103_point0",
            Preset = RC103,
            Vessel = "точечный источник, вплотную к торцу",
            Spectra = new[] { "RC103_Cs137_0cm" },
            Shape = g => { g.SourceType = GeometrySourceType.Point; g.PointDistance = 0.0; },
        });

        // Оксид лютеция, ОДНА банка на двух постановках одного прибора
        // (Amber, 15.08.2026). Банка названа точно: 50 мл, Ø40 × h15;
        // МАССА 20 г — отсюда плотность 1.061 г/см3 (рыхлый порошок, 11 % от
        // монолитных 9.42) и активность 919.1 Бк (`scripts/lu176_activity.py`:
        // 45.954 Бк на грамм Lu₂O₃ — точно, из периода и распространённости).
        //
        // Постановки РАЗНЫЕ и обе известны: `ASN16_Lu176` снят БОКОМ (§13и —
        // отношение сумм-пика к одиночному втрое больше, чем у контрольной, и
        // Geant4 даёт для пары «бок / торец» ровно те же 3.03),
        // `ASN16_Lu176_P0` — с торца, так сказала Amber. Это первая в корпусе
        // пара «то же самое, но повёрнуто», и держится она на E21.
        //
        // Зазора НЕТ: банка лежала НА детекторе — сказано Amber 16.08.2026, и
        // ровно это здесь и стояло с самого начала (`Beaker(..., 0.0)` →
        // `SC_BeakerToDetectorFrontDistance = 0 cm` в обоих файлах).
        //
        // ⚠ До 16.08.2026 в этом месте было написано «ПРИНЯТО: зазор 5 мм
        // (заготовка редактора)» — и это была НЕПРАВДА о собственной модели:
        // пять миллиметров стоят в `GeometryEditorPanel.Blank()`, но `Beaker`
        // перезаписывает их нулём строкой ниже. Запись жила в поле `Assumed`,
        // то есть попадала в сводку корпуса как честно названное допущение, а
        // на деле называла то, чего в модели нет. Заодно она увела вопрос к
        // Amber: у неё спрашивали зазор, который уже был выставлен верно.
        const string ASN16 = "Atom Spectra Nano 16";
        list.Add(new Geom
        {
            Key = "ASN16_lu_side",
            Preset = ASN16,
            Vessel = "банка 50 мл Ø40×h15, СБОКУ у широкой грани",
            Spectra = new[] { "ASN16_Lu176" },
            PassportVolumeMl = 18.85,
            PassportMassG = 20.0,
            SourceMaterial = "Lutetium oxide",
            Facing = GeometryDetectorFacing.Side,
            Assumed = "",
            Shape = g => Beaker(g, 40.0, 18.85, 0.0),
        });

        list.Add(new Geom
        {
            Key = "ASN16_lu_front",
            Preset = ASN16,
            Vessel = "банка 50 мл Ø40×h15, С ТОРЦА",
            Spectra = new[] { "ASN16_Lu176_P0" },
            PassportVolumeMl = 18.85,
            PassportMassG = 20.0,
            SourceMaterial = "Lutetium oxide",
            Assumed = "",
            Shape = g => Beaker(g, 40.0, 18.85, 0.0),
        });

        list.Add(new Geom
        {
            Key = "RC103_marinelli05_kcl",
            Preset = RC103,
            Vessel = "маринелли 0.5 л (поставка ЛСРМ, размеры не восстановлены —"
                     + " взяты из шаблона)",
            Spectra = new[] { "RC103_K40" },
            NominalVolume = "0.5 л",
            PassportMassG = 680.0,
            SourceMaterial = "Potassium chloride",
            Assumed = "ничего — сосуд взят целиком из заготовки редактора",
            Shape = g => { g.SourceType = GeometrySourceType.Marinelli; },
        });

        // ⚠ Шесть прежних сосудных сцен G1S (`G1S_denta_th232`, `_ra226`,
        // `_k40`, `G1S_petri_th232`, `_ra226`, `G1S_marinelli1l_th232`) СНЯТЫ
        // 16.08.2026 (`B12`). Их вытеснили сцены из таблицы: у прежних вещество
        // стояло `Silicon dioxide`, а в банках две трети железа по массе, и
        // внутренний диаметр был принят на глаз. Ни одна из шести не осталась в
        // описи — все их спектры перешли на сцены, построенные по данным.
        // Мёртвый код с неверным веществом опаснее отсутствующего: его копируют.

        // Сосудные сцены поверки — из таблицы, а не отсюда (`B12`).
        list.AddRange(VesselScenes(G1S));

        return list;
    }
}
