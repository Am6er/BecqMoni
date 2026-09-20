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
//                кристалла или обвязки в этом файле не набрано. Зазор между
//                отражателем и корпусом (`AMBER1`) — тоже оттуда: 21.7 мм у
//                «Atom Spectra Pro 80x80», 3.5 мм у «RadiaCode-103» (`E43`,
//                приказ Amber 14.09.2026; измерен П73 по контакту Cs-137 и
//                точке 50 мм) и у «RadiaCode-101» (`B30`, решение Amber
//                18.09.2026 «Ввести 50 мм; RC-101 тоже 3.5 мм»; сцен RC101 в
//                корпусе нет). Единственное исключение названо у самой сцены:
//                `RC103_marinelli05_kcl` ставит ноль поверх пресета (приказ
//                «маринелли не трогать»; зазор её и не двигает — П73).
//   РАССТОЯНИЕ  — точечных сцен: `pdistance` мерится от НАРУЖНОЙ грани корпуса
//                модели (`EfficiencySimulator`: `zFace = −(отражатель + зазор +
//                корпус)`, источник в `zFace − PointDistance`; П73 §3). У G1S
//                (`G1S_point5`/`G1S_point25`) к именному расстоянию ПРИБАВЛЕНО
//                7 мм (`S170`/`B30`, решение Amber 14.09.2026 «Точечные сцены
//                G1S +7 мм — в заход rev25 с B30»; П71: обвязка пресета G1S
//                заимствована от Nano 16 (`E15`), и матрица на 5 см завышена
//                ×1.20…1.27, на 25 см ×1.05…1.06 против паспортов и сумм-пика
//                Co-60; ОДНО Δ = 7 мм закрывает оба расстояния — подпись
//                расстояния, не кристалла). Это ИЗМЕРЕННАЯ поправка постановки,
//                а не добыча обвязки; сосудные сцены G1S её не получали.
//   ЗАЩИТА     — признак `InShield` (`DS_Shield = YES`, `AMBER12`) у сцен
//                «в домике» (`ASN16_point0_house`, `ASN16_point10_house`): в
//                перенос и клеймо матрицы он НЕ входит, его читает разбор
//                (`FsaMatrixBinding` → образ обратного рассеяния от обстановки).
//   СОСУД      — «восстановлен из объёма» (решение Amber): ОДИН размер принят
//                по виду посуды, остальные ВЫВЕДЕНЫ из паспортного объёма.
//                Принятое помечено в печати словом «принято», выведенное —
//                «выведено». Это ДОПУЩЕНИЕ: самопоглощение зависит от формы, а
//                не только от объёма, и при той же вместимости плоская чашка и
//                высокая банка дают разные кривые.
//   ПЛОТНОСТЬ  — ИЗМЕРЕНА: паспортная масса, делённая на объём пробы. Объём —
//                паспортный, когда паспорт его называет (сцена ПОСТРОЕНА под
//                него, см. `T258` ниже), иначе объём построенной сцены. Масса
//                при этом сохраняется точно.
//   МАРИНЕЛЛИ  — у RC-103 не восстанавливается вовсе: это тот самый сосуд, что
//                лежит в поставке ЛСРМ (`RadiaCode_Marinelli0.5.in`), и его
//                размеры слово в слово повторяет заготовка редактора. Берём
//                заготовку.
//   МАРИНЕЛЛИ 1 л G1S — ПО ЧЕРТЕЖУ ОМАСН (`T258`, решение Amber 14.09.2026
//                «Сосуды сейчас, отдельно», П66): корпус Ø154 × 112, колодец
//                Ø97 × 65, стенки 2 мм — все размеры НАЗВАНЫ чертежом, из
//                объёма ВЫВЕДЕНА только высота засыпки (`MarinelliOmasn`).
//                До 14.09.2026 сосуд «восстанавливался из объёма» (колодец по
//                прибору плюс 1.5 мм, слой 100 мм, внешний Ø из объёма —
//                Ø135.2 × 104, колодец Ø76 × 70) и давал кривую в 1.22…1.23
//                раза выше (П64 §7). ⛔ Прежний сосуд из кода УБРАН, а не
//                оставлен под ключом: генератор, умеющий строить сцену двумя
//                способами, однажды построит её не тем (`A77`).
//   ЗАКРЕПЛЁННЫЕ — сцены, которые НЕ строятся, а хранятся байт в байт
//                (`Geom.PinnedFrom`, источник — `corpus/geometries/pinned/`):
//                диск ториевого стекла `AS80_th_disk` выгружен П13/П22 из
//                спектра Amber (`FsaCascadeProbe --dump-geometry=`) и несёт
//                блоки, которых из модели не собрать (у маринелли-блока своё
//                вещество). Генератор их только переносит и вписывает в опись:
//                до 14.09.2026 сцена жила в корпусе мимо генератора, и полный
//                прогон молча выкидывал её строку из `index.csv`.
//
// Объём пробы считается ТЕМИ ЖЕ формулами, по которым сцену строит
// `EfficiencySimulator.Build` (цилиндр: π·r_вн²·h; маринелли: кольцо вокруг
// колодца плюс шапка над его потолком). Это не второй расчёт того же, а
// проверка: разойдись они — сойдётся и печать, и файл, а сцена будет другой.
//
//   corpusgeomprobe [--out=<АБСОЛЮТНЫЙ путь>] [--dry]
//
// ⛔ ПУТИ — ТОЛЬКО ОТ КОРНЯ ДЕРЕВА ИЛИ АБСОЛЮТНЫЕ (`T143`, 05.09.2026). До этого
// и выход (`--out` по умолчанию), и таблица сосудов (`CorpusRoot`) считались от
// ТЕКУЩЕГО каталога. Измерено 05.09.2026 запуском из постороннего каталога: таблица
// `data\lsrm_spectrum_geometry.csv` не нашлась, проба построила 9 геометрий вместо
// 44, напечатала «ВСЕ СОШЛИСЬ» и вышла кодом 0 — а без `--dry` положила бы девять
// файлов в `<чужой каталог>\tools\CORPUS\corpus\geometries`. Тот же разряд, что
// `T142` и `A110`. Теперь корень дерева ищется от КАТАЛОГА СБОРКИ пробы (она лежит в
// `tools\effmaker\probes\build_*`), относительный `--out` — отказ словами, а
// отсутствие таблицы сосудов — отказ, а не «построим меньше». Заодно список
// `Build()` сведён с корпусом: снятая решением Amber геометрия `ASN16_lu_front`
// (`B19`) из него убрана — иначе полный прогон возвращал её в корпус молча.
// Приёмка: `--out=<временный каталог>` даёт 49 файлов `.in` и опись `index.csv`
// (46 до П99 18.09.2026),
// равные корпусным (`handover/p72-t258-t259/geom_diff.py`, до 14.09.2026 —
// `handover/f13-t164/geom_check.py`). До 05.09.2026 (`T164`) опись в корпусе была
// правлена руками — несла BOM и алфавитный порядок, — и сверялась лишь по
// множеству строк; решением Amber 05.09.2026 проба первична, опись перестроена
// ею, и с тех пор равенство описи побайтное.
//
// ⚠ Равенство самих `.in` — С ТОЧНОСТЬЮ ДО КЛЕЙМА, а не до байта (`T258`,
// 14.09.2026): с `AMBER1` (08.09.2026) `GeometryWriter` пишет блок зазора
// («Gap between reflector and cladding», два размера и вещество), а 42 сцены
// корпуса записаны до него и блока не несут. Клеймо матрицы от блока НЕ
// зависит (`ResponseMatrix.StampView` снимает вещество нулевого зазора), и
// переписывать 42 файла ради пятнадцати строк, ничего не меняющих в физике,
// незачем. Приёмка — клеймом: `MatrixStampProbe --geometry=` на живом и на
// построенном файле обязан печатать одно и то же (`handover/p72-t258-t259/
// stamp_check.py`), а байтное равенство держится по модулю этого блока.
class CorpusGeomProbe
{
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

        /// <summary>
        /// Состав набивки массовыми долями по Z — ИЗ ЗАГОЛОВКА САМОГО СПЕКТРА.
        /// Пуст у сцен, вещество которых берётся из библиотеки по имени.
        ///
        /// ⚠ Заведено `B13` (16.08.2026), и вот зачем: библиотека держит ОДИН
        /// состав на имя вещества, а поставка ЛСРМ называет РАЗНЫЙ состав под
        /// одним именем в разные поверки. `ОИСН-06` в 2016 — без железа вовсе,
        /// в 2024 — Fe 0.151; `ОИСН-16` — Fe 0.655412 против 0.714. Один и тот
        /// же эталон (`K40_420-7-20_Маринелли`) лежит в обеих поверках с
        /// одинаковыми массой и объёмом и РАЗНЫМ составом, то есть набивку
        /// переобъявили, а не пересыпали. Пока состав брался по имени, все 24
        /// съёмки 2024 года считались с веществом 2016-го: самопоглощение на
        /// 46.5 кэВ расходится в 1.36 раза (μ/ρ — в 2.52).
        /// </summary>
        public Dictionary<int, double> SourceFractions;
        public Action<GeometryModel> Shape;
        public string Assumed;          // что ПРИНЯТО, словами
        public GeometryDetectorFacing Facing = GeometryDetectorFacing.Front;

        /// <summary>
        /// ЗАКРЕПЛЁННАЯ сцена: путь источника от корня `tools/CORPUS`, файл
        /// переносится в выход байт в байт, модель не строится, объём не
        /// проверяется (его проверять не по чему — сцена не из паспорта).
        /// Пусто — сцена строится <see cref="Shape"/>, как все.
        /// </summary>
        public string PinnedFrom;
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

        // `T143`: относительный путь ОТКАЗ, а не «от текущего каталога». Проба
        // запускается откуда угодно, и от какого каталога считать — она не
        // угадывает: 04.09.2026 сорок пять файлов легли мимо дерева и молча.
        if (outDir != null && !Path.IsPathRooted(outDir))
        {
            Console.Error.WriteLine("--out={0}: путь ОТНОСИТЕЛЬНЫЙ, а текущий каталог {1} — "
                                    + "от какого из них считать, проба не угадывает (T143). "
                                    + "Дайте абсолютный путь.",
                                    outDir, Directory.GetCurrentDirectory());
            return 2;
        }

        string corpus = CorpusRoot();
        if (corpus == null)
        {
            Console.Error.WriteLine("корень дерева не найден: ни над каталогом сборки {0}, ни над "
                                    + "текущим {1} нет `tools\\CORPUS\\corpus` (T143). "
                                    + "Запускайте пробу из каталога сборки внутри дерева.",
                                    AppDomain.CurrentDomain.BaseDirectory,
                                    Directory.GetCurrentDirectory());
            return 2;
        }

        if (outDir == null)
        {
            outDir = Path.Combine(corpus, "corpus", "geometries");
        }

        Console.WriteLine("корень корпуса : {0}", corpus);
        Console.WriteLine("выход          : {0}{1}", outDir, dry ? " (--dry, не пишется)" : "");
        Console.WriteLine();

        // Менеджеры нужны библиотеке веществ: пресеты зовут GeometryMaterialLibrary,
        // а она читает matdb.
        GlobalConfigManager.GetInstance();

        List<Geom> all;
        try
        {
            all = Build(corpus);
        }
        catch (FileNotFoundException e)
        {
            Console.Error.WriteLine(e.Message);
            return 2;
        }
        bool ok = true;
        int written = 0;

        Console.WriteLine("Геометрии «понятной» части корпуса (B1)");
        Console.WriteLine();

        foreach (Geom spec in all)
        {
            if (!string.IsNullOrEmpty(spec.PinnedFrom))
            {
                // Закреплённая сцена: перенос байт в байт, без модели. Нет
                // источника — ОТКАЗ всего прогона, а не «построим без неё»:
                // опись пишется одним проходом, и сцена, выпавшая из него,
                // выпадает и из описи, а с ней её спектры — в «непонятные».
                string src = Path.Combine(corpus, spec.PinnedFrom.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(src))
                {
                    Console.Error.WriteLine("{0}: закреплённого источника нет — {1}", spec.Key, src);
                    return 2;
                }

                Console.WriteLine("== {0} ==", spec.Key);
                Console.WriteLine("   детектор : пресет «{0}»", spec.Preset);
                Console.WriteLine("   сосуд    : {0}", spec.Vessel);
                Console.WriteLine("   спектры  : {0}", string.Join(", ", spec.Spectra));
                Console.WriteLine("   принято  : сцена ЗАКРЕПЛЕНА — {0} байт из {1}, объём не судится",
                                  new FileInfo(src).Length, spec.PinnedFrom);
                if (!dry)
                {
                    Directory.CreateDirectory(outDir);
                    string dst = Path.Combine(outDir, spec.Key + ".in");
                    File.Copy(src, dst, true);
                    written++;
                    Console.WriteLine("   файл     : {0}", dst);
                }

                Console.WriteLine();
                continue;
            }

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
                // Плотность ИЗМЕРЕНА: масса паспорта на объём пробы. Объём —
                // ПАСПОРТНЫЙ, когда паспорт его называет: сцена построена под
                // него, и объём сцены отличается от паспортного только тем,
                // как записаны размеры в файле. ⛔ (`T258`) Брать здесь объём
                // сцены нельзя: у маринелли по чертежу высота засыпки выведена
                // из объёма и записана с точностью файла (0.0001 см,
                // `MarinelliOmasn`), объём сцены от этого отходит от паспорта на
                // 4·10⁻⁷, и плотность вместо `0.57` уезжала бы в файл как
                // `0.5699998` — восемь цифр там, где паспорт даёт две, и другое
                // клеймо у семнадцати посчитанных матриц. У сосудов «из объёма»
                // объём сцены равен паспортному до последнего бита, и для них
                // это то же самое число (проверено побайтно, П72).
                double density = spec.PassportMassG
                               / (spec.PassportVolumeMl > 0.0 ? spec.PassportVolumeMl : volumeMl);
                if (spec.SourceFractions != null && spec.SourceFractions.Count > 0)
                {
                    // Состав назван САМИМ спектром — библиотеку не спрашиваем
                    // вовсе (`B13`): она хранит один состав на имя, а имя у
                    // двух поверок общее. Имя оставляем родное — оно уезжает в
                    // `.in`, и их программа узнаёт вещество по нему.
                    GeometryMaterial source = new GeometryMaterial
                    {
                        Name = spec.SourceMaterial,
                        Density = density,
                    };
                    foreach (KeyValuePair<int, double> pair in spec.SourceFractions)
                    {
                        source.Fractions[pair.Key] = pair.Value;
                    }

                    g.Source = source;
                }
                else
                {
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
                                  + " на объём {3} — ИЗМЕРЕНО)",
                                  g.Source.Name, g.Source.Density, spec.PassportMassG,
                                  spec.PassportVolumeMl > 0.0 ? "паспорта, под который сцена построена" : "сцены");
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

    // Маринелли 1 л по чертежу ОМАСН (Радиевый институт; `YandexDisk\Спектры\G1S\
    // ОМАСН.pdf`, разбор П64 §0.3), мм. Крышка Ø156.5 не моделируется.
    // Семантика полей модели (`EfficiencySimulator`, ветка Marinelli;
    // `SampleVolumeMm3`): `MarinelliBeakerDiameter` — НАРУЖНЫЙ Ø, `MarinelliHoleDiameter`
    // — ВНУТРЕННИЙ Ø колодца (стенки снаружи от него), `MarinelliHoleHeight` —
    // глубина колодца, `MarinelliSourceHeight` — полная высота пробы от дна.
    const double OmasnOuterDiameterMm = 154.0;
    const double OmasnHeightMm = 112.0;
    const double OmasnWellDiameterMm = 97.0;
    const double OmasnWellDepthMm = 65.0;
    const double OmasnWallMm = 2.0;

    /// <summary>
    /// Маринелли 1 л G1S — ПО ЧЕРТЕЖУ ОМАСН (`T258`, решение Amber 14.09.2026
    /// «Сосуды сейчас, отдельно», П66). НАЗВАНЫ чертежом корпус, колодец и
    /// стенки; ВЫВЕДЕНА из паспортного объёма только высота засыпки — обратная
    /// задача к <see cref="SampleVolumeMm3"/>: h = (V + π·r_кол²·h_кол)/(π·r_вн²),
    /// r_кол = 48.5 + 2 = 50.5, r_вн = 77 − 2 = 75 (1 л → 86.058 мм: 21 мм над
    /// потолком колодца, 3 мм воздуха под крышкой).
    ///
    /// ⚠ Высота засыпки ОКРУГЛЯЕТСЯ до 0.001 мм — до точности, с которой её
    /// несёт файл (`GeometryWriter` пишет `G8` в сантиметрах: без округления в
    /// файл уехало бы `8.605798 cm`, а сцены корпуса и клейма семнадцати их
    /// матриц несут `8.6058 cm`, П66). Цена округления — 4·10⁻⁷ объёма; в
    /// плотность оно не переносится (см. `Main`).
    ///
    /// До 14.09.2026 здесь стоял сосуд «из объёма» — колодец по наружному
    /// размеру прибора плюс 1.5 мм, глубина 70, слой 100, внешний диаметр из
    /// объёма (Ø135.2 × 104, колодец Ø76 × 70) — и давал кривую в 1.22…1.23 раза
    /// выше чертёжной (П64 §7). Он убран, а не оставлен под ключом (`A77`).
    /// </summary>
    static void MarinelliOmasn(GeometryModel g, double volumeMl)
    {
        g.SourceType = GeometrySourceType.Marinelli;
        g.MarinelliBeakerDiameter = OmasnOuterDiameterMm;
        g.MarinelliBeakerHeight = OmasnHeightMm;
        g.MarinelliHoleDiameter = OmasnWellDiameterMm;
        g.MarinelliHoleHeight = OmasnWellDepthMm;
        g.MarinelliSideThickness = OmasnWallMm;
        g.MarinelliEndWallThickness = OmasnWallMm;
        g.MarinelliHoleSideThickness = OmasnWallMm;
        g.MarinelliHoleEndWallThickness = OmasnWallMm;

        double rHole = 0.5 * OmasnWellDiameterMm + OmasnWallMm;
        double rSrcOut = 0.5 * OmasnOuterDiameterMm - OmasnWallMm;
        double h = (volumeMl * 1000.0 + Math.PI * rHole * rHole * OmasnWellDepthMm)
                 / (Math.PI * rSrcOut * rSrcOut);
        g.MarinelliSourceHeight = Math.Round(h, 3);
        if (g.MarinelliSourceHeight <= OmasnWellDepthMm
            || g.MarinelliSourceHeight > OmasnHeightMm - 2.0 * OmasnWallMm)
        {
            // Засыпка ниже потолка колодца или выше сосуда — не тот сосуд
            // для такого объёма; отказ словами, а не сцена с невозможной пробой.
            throw new InvalidOperationException(string.Format(
                CultureInfo.InvariantCulture,
                "маринелли ОМАСН: объём {0} мл даёт засыпку {1} мм при колодце {2} и высоте {3}",
                volumeMl, g.MarinelliSourceHeight, OmasnWellDepthMm, OmasnHeightMm));
        }
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
    static List<Geom> VesselScenes(string preset, string corpus)
    {
        string path = Path.Combine(corpus, "data", "lsrm_spectrum_geometry.csv");
        List<Geom> list = new List<Geom>();
        // ⛔ Нет таблицы — ОТКАЗ, а не «построим без сосудных сцен» (`T143`).
        // Прежде здесь печаталась строка и возвращался пустой список: проба
        // строила 9 геометрий вместо 44 и заканчивала словами «ВСЕ СОШЛИСЬ»
        // кодом 0 — измерено 05.09.2026. Опись из девяти строк переписала бы
        // `index.csv`, и `split_corpus.py` молча увёл бы 35 спектров в
        // «непонятную» часть.
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "нет " + path + " — без таблицы сосудные сцены не строятся, а строить "
                + "корпус без них значит переписать опись на 9 геометрий из 44 "
                + "(T143; таблицу пишет import_spe_geometry.py)", path);
        }

        string[] lines = File.ReadAllLines(path, Encoding.UTF8);
        if (lines.Length < 2)
        {
            throw new FileNotFoundException("таблица " + path + " пуста — сосудные сцены строить не из чего", path);
        }

        List<string> head = new List<string>(lines[0].TrimStart('﻿').Split(','));
        int iKey = head.IndexOf("спектр"), iVessel = head.IndexOf("сосуд");
        int iMat = head.IndexOf("вещество"), iMass = head.IndexOf("масса_г");
        int iVol = head.IndexOf("объём_мл"), iRo = head.IndexOf("плотность");
        int iComp = head.IndexOf("состав_Z_доля");
        if (iKey < 0 || iVessel < 0 || iMat < 0 || iRo < 0 || iComp < 0)
        {
            Console.WriteLine("в {0} нет нужных колонок", path);
            return list;
        }

        Dictionary<string, List<string>> scenes = new Dictionary<string, List<string>>();
        Dictionary<string, string[]> sample = new Dictionary<string, string[]>();
        Dictionary<string, string> composition = new Dictionary<string, string>();
        foreach (string line in lines)
        {
            string[] c = line.Split(',');
            if (c.Length <= iComp || c[iKey] == "спектр" || c[iMat].Length == 0)
            {
                continue;
            }

            string key = SceneKey(c[iKey], c[iVessel], c[iMat], c[iRo]);
            if (key == null)
            {
                continue;
            }

            if (!scenes.ContainsKey(key))
            {
                scenes[key] = new List<string>();
                sample[key] = c;
                composition[key] = c[iComp].Trim();
            }
            else if (!string.Equals(composition[key], c[iComp].Trim(), StringComparison.Ordinal))
            {
                // ⛔ Отказ, а не предупреждение (`B13`). Две разные набивки под
                // одним именем сцены — это ровно та ошибка, ради которой эпоха
                // вошла в ключ: одна сцена не может быть верна для обеих, а
                // выбранная молча окажется верной для одной. Сегодня такого
                // нет; появится — прогон обязан встать, а не усреднить.
                Console.Error.WriteLine(
                    "сцена {0}: два разных состава — «{1}» у {2} и «{3}» у {4}",
                    key, composition[key], scenes[key][0], c[iComp].Trim(), c[iKey]);
                throw new InvalidOperationException("сцена с двумя составами: " + key);
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
                SourceFractions = ParseFractions(composition[pair.Key]),
                Assumed = "ничего: сосуд, вещество, СОСТАВ, масса и объём взяты из заголовка `.spe`; "
                          + "внутренний диаметр выведен из объёма и толщины слоя поставки",
            };

            double vol = volume;
            if (vessel.StartsWith("Маринелли", StringComparison.Ordinal))
            {
                // Маринелли 1 л — по чертежу ОМАСН (`T258`); от объёма зависит
                // только высота засыпки, меняются вещество и плотность.
                g.Shape = m => MarinelliOmasn(m, vol);
                g.Assumed = "ничего: сосуд по чертежу ОМАСН (Ø154 × 112, колодец Ø97 × 65, стенки 2 мм), "
                          + "вещество, СОСТАВ, масса и объём взяты из заголовка `.spe`; "
                          + "высота засыпки выведена из объёма";
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

    /// <summary>
    /// Имя сцены: сосуд, набивка, плотность И ЭПОХА ПОВЕРКИ — всё, чем они
    /// различаются.
    ///
    /// ⚠ Эпоха вошла в ключ 16.08.2026 (`B13`). Без неё семь маринелли-сцен
    /// были ОБЩИМИ у поверок 2016 и 2024, а состав набивки у них разный —
    /// сцена молча оказывалась верна для одной эпохи и неверна для другой.
    /// Эпоха берётся из имени спектра корпуса (`G1S16_*` / `G1S24_*`), то есть
    /// из того же разделения на эпохи, которым живёт весь корпус, а не
    /// угадывается по дате.
    /// </summary>
    static string SceneKey(string spectrum, string vessel, string material, string density)
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

        int cut = spectrum.IndexOf('_');
        string epoch = cut > 0 ? spectrum.Substring(0, cut) : spectrum;
        epoch = epoch.StartsWith("G1S", StringComparison.Ordinal)
                ? epoch.Substring(3) : epoch;
        if (epoch.Length == 0)
        {
            return null;
        }

        string m = material.Replace("ОИСН-", "oisn").Replace("РИСН-", "risn").Replace(" ", "");
        return string.Format(CultureInfo.InvariantCulture, "G1S_{0}_{1}_{2:000}_p{3}",
                             v, m, Math.Round(ro * 100.0), epoch);
    }

    /// <summary>
    /// Состав из колонки `состав_Z_доля`: пары «Z:доля» через пробел. Пустая
    /// строка — вещество назовётся библиотекой по имени, как было до `B13`.
    /// </summary>
    static Dictionary<int, double> ParseFractions(string text)
    {
        Dictionary<int, double> fractions = new Dictionary<int, double>();
        if (string.IsNullOrEmpty(text))
        {
            return fractions;
        }

        foreach (string part in text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = part.Split(':');
            int z;
            double f;
            if (pair.Length == 2
                && int.TryParse(pair[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out z)
                && double.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out f))
            {
                fractions[z] = f;
            }
        }

        return fractions;
    }

    static double Num(string text)
    {
        double value;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            ? value : 0.0;
    }

    /// <summary>
    /// Корень `tools/CORPUS` — вверх от КАТАЛОГА СБОРКИ пробы (она лежит в
    /// `tools\effmaker\probes\build_*` дерева), и лишь затем от текущего.
    /// Не нашёлся — <c>null</c>, и это отказ у вызывающего; относительной
    /// заглушки, как прежде, не возвращается (`T143`). Признак — каталог
    /// `corpus` внутри: одного имени `tools\CORPUS` мало, его создаёт и
    /// сама проба под чужим корнем.
    /// </summary>
    static string CorpusRoot()
    {
        foreach (string start in new[] { AppDomain.CurrentDomain.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            string dir = start;
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                string cand = Path.Combine(dir, "tools", "CORPUS");
                if (Directory.Exists(Path.Combine(cand, "corpus")))
                {
                    return Path.GetFullPath(cand);
                }

                dir = Path.GetDirectoryName(dir);
            }
        }

        return null;
    }

    static List<Geom> Build(string corpus)
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
        // ⛔ +7 мм К ИМЕННОМУ РАССТОЯНИЮ ОБЕИХ ТОЧЕЧНЫХ СЦЕН G1S (`S170` → `B30`,
        // решение Amber 14.09.2026, вопросником, дословно: «Точечные сцены G1S
        // +7 мм — в заход rev25 с B30»; исполнено П99 18.09.2026, база rev29).
        // Измерено П71 (`SumPeakProbe`, `handover/handover-2026-09-14-p71-s170-
        // sum-peak.md` §3–§4): при `pdistance = 5 cm` матрица завышена против
        // паспортов Co-60 ×1.218/1.214 (G1S16) и ×1.274/1.265 (G1S24) на
        // 1173/1332 кэВ, при 25 см — ×1.048/1.063; сумм-пик Co-60 модель/данные
        // 1.12 (изотропно) и 1.22 (с корреляциями). Одно Δ = 7 мм (таблица
        // двухузловых матриц: 5 → ÷1.20/1.19, 7 → ÷1.20/1.19 и ÷1.06/1.05)
        // закрывает ОБА расстояния — подпись расстояния (телесный угол ~1/d²),
        // а не кристалла или плотности; при 5.7 см матрица/паспорт 1.030/1.030
        // и 1.077/1.073, сумм-пик 0.98 ± 0.03 изотропно / 1.07 ± 0.04 с
        // корреляциями. Причина — обвязка пресета G1S заимствована от Nano 16
        // (`E15`, решение Amber 14.08.2026 «не будет»): расстояние до крышки в
        // ней чужое. Это ИЗМЕРЕННАЯ поправка постановки, а не добыча обвязки;
        // число выбрано по паспортам (25 см и G1S16), не подгонкой под сумм-пик.
        // ⚠ Сосудные сцены G1S поправки НЕ получали (число Δ для них не мерено,
        // П71 §6) — они стоят как были.
        const double G1SPointCorrectionMm = 7.0;

        list.Add(new Geom
        {
            Key = "G1S_point5",
            Preset = G1S,
            Vessel = "точечный источник, 5 см от торца (+7 мм, S170)",
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
            Shape = g =>
            {
                g.SourceType = GeometrySourceType.Point;
                g.PointDistance = 50.0 + G1SPointCorrectionMm;    // 57 мм (было 50 до 18.09.2026)
            },
        });

        list.Add(new Geom
        {
            Key = "G1S_point25",
            Preset = G1S,
            Vessel = "точечный источник, 25 см от торца (+7 мм, S170)",
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
            Shape = g =>
            {
                g.SourceType = GeometrySourceType.Point;
                g.PointDistance = 250.0 + G1SPointCorrectionMm;   // 257 мм (было 250 до 18.09.2026)
            },
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
            // `AS80_Am241` добавлен 08.09.2026: америциевых источников ТРИ, но
            // лежали они кучно (между центрами менее 3-4 мм, слово Amber), а
            // смещение от оси до 3 мм меняет телесный угол менее чем на 0.3 %.
            // Точка описывает эту постановку, отдельной сцены не нужно.
            Spectra = new[] { "AS80_Cs137_0cm", "AS80_Am241" },
            Shape = g => { g.SourceType = GeometrySourceType.Point; g.PointDistance = 0.0; },
        });

        // ⛔ ЗАЗОР 3.5 мм У ТОРЦА RC103 (`E43`, приказ Amber 14.09.2026 «Зазор для
        // RC103 из V10 вноси прямо сейчас в геометрию без вопросов, раз совпало.
        // Разрешаю. Который 3.5 мм») приходит СЮДА ИЗ ПРЕСЕТА «RadiaCode-103»
        // (`GeometryPresets.cs`, `FrontGapThickness = 3.5`) — тем же движением,
        // каким приходит вся обвязка: в этом файле ни одного размера детектора
        // не набрано, и зазор — не исключение (так же 21.7 мм у `AS80_*` с
        // `AMBER1`). Число измерено П73 (~~`V10`~~): контакт Cs-137 0.443 и точка
        // 50 мм 1.05 сшиваются осевым зазором 3.3–3.6 мм; тот же зазор чинит
        // `RC103_lu_front` (0.806 → ~1.05). Сцены `RC103_point0` и
        // `RC103_lu_front` его несут; `RC103_marinelli05_kcl` — НЕТ, явно (ниже).
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
        // Постановка ОДНА — БОКОМ (§13и — отношение сумм-пика к одиночному
        // втрое больше, чем у контрольной, и Geant4 даёт для пары «бок / торец»
        // ровно те же 3.03). ⛔ Прежде здесь стояла вторая, «с торца»
        // (`ASN16_lu_front` для `ASN16_Lu176_P0`), и решением Amber 17.08.2026
        // она СНЯТА (`B19`): у Lu-176 на ASN16 съёмка была ТОЛЬКО сбоку, а
        // 02.09.2026 `ASN16_Lu176_P0` привязан к `ASN16_lu_side` — та же банка,
        // другая дата и набор. ⚠ Корпус тогда правили РУКАМИ (`index.csv`), а
        // этот список — нет: до 05.09.2026 полный прогон пробы возвращал снятую
        // геометрию в корпус файлом и строкой описи, и `ASN16_Lu176_P0` уезжал
        // обратно под «с торца» — измерено при `T143`, откачено. Список и
        // корпус сведены; расходиться им больше нельзя: опись пишет ЭТА проба.
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
            Spectra = new[] { "ASN16_Lu176", "ASN16_Lu176_P0" },
            PassportVolumeMl = 18.85,
            PassportMassG = 20.0,
            SourceMaterial = "Lutetium oxide",
            Facing = GeometryDetectorFacing.Side,
            Assumed = "",
            Shape = g => Beaker(g, 40.0, 18.85, 0.0),
        });

        // Та же банка лютеция на двух остальных приборах (`B16`, указание Amber
        // 16.08.2026: «тот же источник Lu, что и у ASN16, цилиндр в притык к
        // кристаллу»). Проба одна и та же физически — цилиндр 50 мл Ø40 × h15,
        // Lu₂O₃, масса 20 г, отсюда ρ = 1.061 г/см³ и активность 919.1 Бк, —
        // поэтому и объём, и масса, и вещество взяты те же, что у `ASN16_lu_*`,
        // а меняется ТОЛЬКО кристалл. Этим замыкается ось «один источник, одна
        // геометрия, три кристалла»: до сих пор у `AS80_Lu176` и `RC103_Lu176`
        // геометрии не было вовсе, они лежали в непонятной части, и `Ann-511`
        // держалась у них как контроль (`E21`).
        //
        // Постановка ФРОНТАЛЬНАЯ, в отличие от `ASN16_lu_side`: Amber назвала
        // «в притык к кристаллу», то есть торцом и без зазора (так стояла и
        // снятая `ASN16_lu_front`, `B19`). Разворот к широкой грани — свойство той одной
        // съёмки ASN16, восстановленное измерением, и переносить его сюда
        // догадкой нельзя.
        list.Add(new Geom
        {
            Key = "AS80_lu_front",
            Preset = "Atom Spectra Pro 80x80",
            Vessel = "банка 50 мл Ø40×h15, ВПРИТЫК к торцу",
            // `AS80_Lu176_v2` — та же банка и та же постановка, съёмка
            // 08.09.2026 втрое длиннее (12 600 с против 3 824). Прежний ключ
            // остаётся: он закреплён (`corpus/pinned`).
            Spectra = new[] { "AS80_Lu176", "AS80_Lu176_v2" },
            PassportVolumeMl = 18.85,
            PassportMassG = 20.0,
            SourceMaterial = "Lutetium oxide",
            Assumed = "",
            Shape = g => Beaker(g, 40.0, 18.85, 0.0),
        });

        list.Add(new Geom
        {
            Key = "RC103_lu_front",
            Preset = RC103,
            Vessel = "банка 50 мл Ø40×h15, ВПРИТЫК к торцу",
            Spectra = new[] { "RC103_Lu176" },
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
            // ⛔ БЕЗ зазора — ЯВНО, поверх пресета (`E43`, 14.09.2026): приказ
            // Amber велел зазор 3.5 мм КОНТАКТНЫМ сценам RC103 и «маринелли не
            // трогать». Измерено П73 (`sweep2`): у этого сосуда ε(1461) при
            // зазоре 0 / 3 / 4 / 5 мм — 6.28 / 6.49 / 5.89 / 6.37 e-5 при
            // разбросе МК ±5–6 %, то есть зазор её не двигает, а недобор
            // маринелли RC103 (0.658) — своя причина (сосуд из шаблона ЛСРМ без
            // чертежа). Ноль стоит здесь, а не «пресет без зазора для
            // маринелли»: пресет — прибор, один на все сцены; исключение
            // названо у сцены, к которой относится, и видно в файле `.in`
            // (`DS_CrystalFrontGapThickness = 0 cm`), клеймо матрицы прежнее.
            Shape = g => { g.SourceType = GeometrySourceType.Marinelli; g.FrontGapThickness = 0.0; },
        });

        // ⚠ Шесть прежних сосудных сцен G1S (`G1S_denta_th232`, `_ra226`,
        // `_k40`, `G1S_petri_th232`, `_ra226`, `G1S_marinelli1l_th232`) СНЯТЫ
        // 16.08.2026 (`B12`). Их вытеснили сцены из таблицы: у прежних вещество
        // стояло `Silicon dioxide`, а в банках две трети железа по массе, и
        // внутренний диаметр был принят на глаз. Ни одна из шести не осталась в
        // описи — все их спектры перешли на сцены, построенные по данным.
        // Мёртвый код с неверным веществом опаснее отсутствующего: его копируют.

        // Сосудные сцены поверки — из таблицы, а не отсюда (`B12`).
        list.AddRange(VesselScenes(G1S, corpus));

        // ⚠ Две сцены ниже стоят ПОСЛЕ табличных нарочно: порядок списка — это
        // порядок описи `index.csv`, а обе сцены дописаны в корпус своими
        // полосами в конец описи (П22 12.09.2026, П66 14.09.2026), и опись
        // обязана воспроизводиться побайтно (`T164`).

        // Диск ториевого стекла на AS80x80 (`AS80_th_disk`, П22 12.09.2026,
        // понятным стал решением Amber по `B17`). ЗАКРЕПЛЁН, а не построен:
        // сцена выгружена П13 из спектра Amber (`FsaCascadeProbe
        // --dump-geometry=` по `!AS80x80\калибровка 08.09.2026\Th-232.xml`) с
        // заменой вещества пробы на «Ториевое стекло» (4.345 г/см³ — 27.3 г на
        // 6.283 см³; состав — модель П13 по семейству патента Morey, допущение
        // П13), и несёт следы конфигурации Amber, которых из модели не
        // собрать: в маринелли-блоке стоит `Glass, plate`, а `GeometryWriter`
        // пишет в оба блока ОДНО вещество пробы. Пересобранная из пресета сцена
        // дала бы другой текст — и другое клеймо у посчитанной матрицы.
        // Источник — `corpus/geometries/pinned/AS80_th_disk.in` (sha256
        // `850176dd…`, побайтно = `handover/p22-th-disk/geometries/`).
        list.Add(new Geom
        {
            Key = "AS80_th_disk",
            Preset = "Atom Spectra Pro 80x80",
            Vessel = "стеклянный диск Ø40×5 мм (ториевое стекло 4.345 г/см³), ВПРИТЫК к торцу",
            Spectra = new[] { "AS80_Th232Medal" },
            PinnedFrom = "corpus/geometries/pinned/AS80_th_disk.in",
        });

        // Активированный уголь с радоном в маринелли 1 л ОМАСН (`AMBER29`,
        // решение Amber 14.09.2026 «3 ранних + 1 равновесный», П64/П66): четыре
        // съёмки одной засыпки — 461 г на паспортные 1000 мл (заголовок `.spe`:
        // `SAMPLEMASS 461`, `SAMPLEVOLUME 1000`, `GEOMETRY Маринелли 1л`),
        // прибор G1S24. Сцена НЕ из таблицы `lsrm_spectrum_geometry.csv`: там
        // строки берутся из заголовка `.spe` вместе с составом, а у угля
        // состава в заголовке нет и в `matdb` угля нет — состав задан здесь
        // явно (углерод, П64), имя «Activated charcoal». Сосуд тот же, что у
        // семнадцати эталонных маринелли (`MarinelliOmasn`), высота засыпки —
        // из тех же 1000 мл. Ключ — по образцу табличных: сосуд, вещество,
        // ρ·100, эпоха.
        const double CoalMassG = 461.0, CoalVolumeMl = 1000.0;
        list.Add(new Geom
        {
            Key = "G1S_mar1l_coal_046_p24",
            Preset = G1S,
            Vessel = string.Format(CultureInfo.InvariantCulture,
                                   "Маринелли, набивка активированный уголь {0:0.###} г/см³, вплотную",
                                   CoalMassG / CoalVolumeMl),
            Spectra = new[]
            {
                "G1S24_Rn222Coal_Mar_20m", "G1S24_Rn222Coal_Mar_2h",
                "G1S24_Rn222Coal_Mar_3h", "G1S24_Rn222Coal_Mar_eq01"
            },
            PassportVolumeMl = CoalVolumeMl,
            PassportMassG = CoalMassG,
            SourceMaterial = "Activated charcoal",
            SourceFractions = new Dictionary<int, double> { { 6, 1.0 } },
            Assumed = "состав пробы — чистый углерод (в заголовке `.spe` состава нет, в matdb угля нет; П64); "
                      + "сосуд по чертежу ОМАСН, масса и объём из заголовка `.spe`",
            Shape = g => MarinelliOmasn(g, CoalVolumeMl),
        });

        // ⚠ Три сцены ниже — конец описи (П99, 18.09.2026, `B30`, база rev29):
        // дописаны в хвост тем же правилом, что диск и уголь выше, — опись
        // обязана воспроизводиться побайтно, а прежние строки не двигаться.

        // Cs-137 на RC103 в 50 мм от корпуса (`RC103_point50`; ~~`V10`~~/П73 →
        // `B30`, решение Amber 18.09.2026 вопросником, дословно: «Ввести 50 мм;
        // RC-101 тоже 3.5 мм»). Съёмка Amber `!КОТ-103\Cs-137 точка дистанция
        // 50 мм.xml` (14.09.2026, 3561 с, 37.5 тыс. отсчётов, фон встроен) —
        // 136-й спектр корпуса `RC103_Cs137_50mm`, паспорт 5235.6 Бк на день
        // съёмки. «50 мм» — слово Amber «дистанция 50 мм», принято как
        // `pdistance` от НАРУЖНОЙ грани корпуса модели (та же условность, что
        // у `Nano16Pro_point10` и у `ASN16_point10_house` ниже; П73 §3).
        // Зазор 3.5 мм приходит ИЗ ПРЕСЕТА, как у `RC103_point0`: он свойство
        // прибора, а не постановки, и точка 50 мм его несёт. ⚠ Цена, названная
        // П73 и пресетом: на 50 мм с зазором ожидается изм/ожид 1.15–1.22 (без
        // зазора было 1.05) — контакт несёт ещё и положение источника у
        // корпуса, которого две осевые точки не разводят. Сцена П73
        // (`D:\BqMoni_Claude\p73\store\RC103_point50.in`, без зазора, физика 18)
        // — только ориентир, не источник: строится здесь заново.
        list.Add(new Geom
        {
            Key = "RC103_point50",
            Preset = RC103,
            Vessel = "точечный источник, 50 мм от торца корпуса",
            Spectra = new[] { "RC103_Cs137_50mm" },
            Shape = g => { g.SourceType = GeometrySourceType.Point; g.PointDistance = 50.0; },
        });

        // Точка в свинцовом домике на ASN16 (`B30`: «Группа ASN16 в корпусе без
        // геометрии … корпус слеп к повседневному случаю Amber — точечный
        // источник в свинцовом домике с матрицей»; решение Amber 14.09.2026
        // вопросником «Да, в понятную часть и в малую базу»). Постановки
        // подтверждены Amber 18.09.2026 вопросником, дословно:
        //   * `ASN16_Cs137` («Cs 137 в домике 24.11.2022», 251 М отсчётов):
        //     «Как в файле: впритык, торец, домик» — блок `<Geometry>` самого
        //     спектра (сохранённая в приложении геометрия «Точка в защите»):
        //     SourceType Point, PointDistance 0, Facing Front, InShield true,
        //     кристалл 15×18×60, отражатель 1.3/1, зазор 0/0, корпус 1.8/2,
        //     оправа 2 — то есть в точности пресет «Atom Spectra Nano 16» +
        //     точка на торце + защита; та же сцена стоит витриной FSA
        //     (`tools/fsa_showcase/scenes/ASN16_point_house.in`, П74);
        //   * `ASN16_Cs137_10cm` («Cs137 - 10cm», паспорт 5712 Бк на
        //     03.12.2022): «Ввести: 10 см, в домике» — та же сцена с
        //     `pdistance = 10 cm` от наружной грани корпуса (условность
        //     `Nano16Pro_point10` августа: «10 см от наружной грани корпуса,
        //     10.31 до кристалла»);
        //   * прочие ASN16 (Am241, Th232, Th232_Am241): «больше нет таких» —
        //     сцен им НЕ заводится, они остаются в непонятной части.
        // Домик — признак `InShield` (`DS_Shield = YES`): в перенос и клеймо не
        // входит (`ResponseMatrix.StampView`), читается разбором — образ
        // обратного рассеяния от обстановки поверх матрицы (`AMBER12`).
        // Зазор у ASN16 нулевой (пресет), вещество зазора в клеймо не входит.
        list.Add(new Geom
        {
            Key = "ASN16_point0_house",
            Preset = ASN16,
            Vessel = "точечный источник, вплотную к торцу, в свинцовом домике",
            Spectra = new[] { "ASN16_Cs137" },
            Shape = g =>
            {
                g.SourceType = GeometrySourceType.Point;
                g.PointDistance = 0.0;
                g.InShield = true;
            },
        });

        list.Add(new Geom
        {
            Key = "ASN16_point10_house",
            Preset = ASN16,
            Vessel = "точечный источник, 10 см от торца корпуса, в свинцовом домике",
            Spectra = new[] { "ASN16_Cs137_10cm" },
            Shape = g =>
            {
                g.SourceType = GeometrySourceType.Point;
                g.PointDistance = 100.0;
                g.InShield = true;
            },
        });

        return list;
    }
}
