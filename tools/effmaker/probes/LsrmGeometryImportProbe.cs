using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;

namespace LsrmGeometryImportProbe
{
    /// <summary>
    /// Приёмка остатка `AMBER18` (полоса П7, 12.09.2026) — решение Amber
    /// 12.09.2026, вопросником, дословно: «Привязать геометрию при ввозе ЛСРМ».
    /// У кривой ЛСРМ с геометрией `.in` появляется масштаб для дозы.
    ///
    /// ⛔ С 15.10.2026 (`AMBER227`, П331) импорта ЛСРМ на вкладке Efficiency
    /// НЕТ — кнопка снята вместе со своим путём (`ImportLsrmEfficiencyWithGeometry`,
    /// автоподбор `.in`, второй шаг диалога). Кривую ЛСРМ заводит конструктор:
    /// «Импорт из LSRM…» редактора геометрии (`AMBER226`). Проба переведена на
    /// этот путь: настоящий `EfficiencyMakerForm` вне экрана, привязанный к
    /// конфигурации с геометрией из `.in` (так её даёт та же кнопка `.in`),
    /// `ImportCurveFile` экспорта и «Сохранить» (`SaveIntoConfig`) — отражением.
    /// Покрытие дозы (§2) сохранено целиком; §3 — отказ «сцена поля», §4
    /// (автоподбор) снят вместе с путём.
    ///
    ///  §1 ИМПОРТ ПАРЫ ЛСРМ (с `--lsrm=` — настоящая пара `RadiaCode - marinelli
    ///     0.5.txt` + `RadiaCode_Marinelli0.5.in` из названного каталога; без
    ///     ключа — пара ИЗ ДЕРЕВА: узел `Efficiency` спектра корпуса
    ///     `G1S24_K40_Mar` — кривая пишется экспортом ЛСРМ, геометрия —
    ///     писателем `.in` в рабочий каталог пробы. ⚠ До П331 пара из дерева
    ///     бралась из поставочного `config\ROI\RadiaCode Marinelli 0.5.xml`, а
    ///     `config\ROI` снят 06–07.10.2026 (`AMBER208`, коммит `4916c626`) — с
    ///     тех пор проба без `--lsrm=` отказывала кодом 2): в конфигурации
    ///     прибора кривая с `Origin = Lsrm`, `Geometry != null`, клеймо
    ///     `ComputeStamp` ПУСТОЕ (кривая не выдаёт себя за посчитанную из
    ///     геометрии); размеры сцены сверяются с числами, прочитанными из
    ///     текста `.in` НЕЗАВИСИМО (регулярным выражением, см → мм); текст
    ///     `.in`, собранный писателем из геометрии кривой, равен тексту от
    ///     прямого чтения тем же читателем; после XML туда-обратно
    ///     (`DeviceConfigInfo`) геометрия НАЙДЕНА и её текст тот же.
    ///
    ///  §2 ДОЗА: (а) спектр корпуса с кривой §1 — число со знаком «≈» вместо
    ///     отказа. С `--lsrm=` — `RC103_K40Village` (RadiaCode-103, K-40 в
    ///     маринелли 0.5 л); ⚠ эта пара УСЛОВНАЯ: модель ЛСРМ — RadiaCode с
    ///     кристаллом Ø11.28×10 мм, паспортной связи «эта кривая считана для
    ///     этого прибора» нет, число — проверка механики, не измерение. Без
    ///     ключа — сам `G1S24_K40_Mar`, чья кривая и импортирована. (б) СТРОГИЙ КОНТРОЛЬ: точки кривой из узла
    ///     `Efficiency` спектра `AS80_Cs137_0cm` записываются экспортом ЛСРМ и
    ///     импортируются с `.in` той же сцены `AS80_point0` — доза «≈» по импортированной
    ///     кривой обязана совпасть с дозой «≈» по родной кривой снимка
    ///     (`DoseRateInput.Of(родная, null)`) до 1e-12 относительных: те же
    ///     точки, та же геометрия, тот же путь. Число печатается рядом с
    ///     ≈ 0.195 мкЗв/ч из журнала П1.
    ///
    ///  §3 СЦЕНА ПОЛЯ: кривая ЛСРМ с геометрией `DS_Scene = ISO` (единичный
    ///     флюенс) — «Сохранить» конструктора ОТКАЗЫВАЕТ, конфигурация не
    ///     тронута; правило `LsrmCurveGeometryProblem` — только для кривой ЛСРМ
    ///     и только для ISO; мусорный `.in` — нет кристалла (`HasScintillatorCrystal`).
    ///
    ///  §5 ПОДПИСИ: текст отказа §3 — в обеих культурах и различается; четыре
    ///     ключа снятого второго шага (`DeviceConfigForm.resx`) и три ключа
    ///     снятой кнопки (`Resources.resx`) не вернулись.
    ///
    ///   lsrmgeometryimportprobe [--dir=&lt;корпус&gt;] [--lsrm=&lt;каталог с Exported Curves\ и Models\&gt;]
    ///   lsrmgeometryimportprobe --sabotage=badin|stamp     (ждёт ОТКАЗ пробы)
    ///
    /// ⛔ Приёмка, которая проходит всегда, не мерит ничего. `--sabotage`
    /// портит РОВНО ОДНУ вещь и требует, чтобы проба ОТКАЗАЛА; коды у него
    /// перевёрнуты: 0 — отказ получен (проба смотрит), 1 — не получен (слепа).
    ///
    ///   badin — вместо `.in` §1 подсунут БИТЫЙ файл (текст экспорта под
    ///           именем `.in`): его, как кнопка `.in` конструктора, обязан
    ///           отвергнуть признак «нет кристалла», и кривая ляжет с
    ///           геометрией-заготовкой полей; проба обязана это поймать (§1
    ///           «источник — маринелли» и размеры из `.in` падают);
    ///   stamp — импортированная кривая выдаёт себя за посчитанную из геометрии
    ///           (`Origin = Simulation`, клеймо `phys=…`): проба обязана
    ///           отказать на §1.
    ///
    /// Коды возврата обычного прогона: 0 — все проверки прошли, 1 — есть
    /// непрошедшие, 2 — отказ оснастки.
    /// </summary>
    static class Program
    {
        static int failed;
        static int checks;
        static string corpusDir = @"tools\CORPUS\corpus";

        /// <summary>
        /// Каталог с настоящей парой ЛСРМ (`Exported Curves\` + `Models\`) — только
        /// ключом `--lsrm=`. ⛔ Умолчания нет: `LSRM Geometries/` снят из дерева
        /// 15.09.2026 (решение Amber); без ключа пара собирается из дерева
        /// (<see cref="PairFromTree"/>).
        /// </summary>
        static string lsrmDir;
        static string sabotage;

        /// <summary>Пара ЛСРМ для §1 и §2(а): экспорт и модель одной геометрии.</summary>
        const string LsrmExport = @"Exported Curves\RadiaCode - marinelli 0.5.txt";
        const string LsrmModel = @"Models\RadiaCode_Marinelli0.5.in";

        /// <summary>
        /// Пара из дерева (П331): спектр корпуса в маринелли со своей кривой и
        /// геометрией в узле `Efficiency`. Прежний источник — поставочный
        /// `config\ROI\RadiaCode Marinelli 0.5.xml` — снят 06–07.10.2026 (`AMBER208`).
        /// </summary>
        const string TreeSpectrum = "G1S24_K40_Mar";

        /// <summary>Спектр §2(а): с настоящей парой `--lsrm=` — условная пара RC-103.</summary>
        static string DoseSpectrum = TreeSpectrum;

        /// <summary>Журнал П1 §9: `AS80_Cs137_0cm` по пиковой — ≈ 0.195 мкЗв/ч.</summary>
        const double P1Approximate = 0.195;

        [STAThread]
        static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch (IOException) { }
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            foreach (string a in args)
            {
                if (a.StartsWith("--dir=", StringComparison.Ordinal)) corpusDir = a.Substring(6);
                else if (a.StartsWith("--lsrm=", StringComparison.Ordinal)) lsrmDir = a.Substring(7);
                else if (a.StartsWith("--sabotage=", StringComparison.Ordinal)) sabotage = a.Substring(11);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (sabotage != null && sabotage != "badin" && sabotage != "stamp")
            {
                Console.Error.WriteLine("неизвестная порча: " + sabotage + " (badin|stamp)");
                return 2;
            }

            string scratch = Path.Combine(Path.GetTempPath(), "lsrmgeomimport-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            Console.WriteLine("LsrmGeometryImportProbe — `AMBER18`, полоса П7 12.09.2026: геометрия при импорте ЛСРМ");
            Console.WriteLine("корпус: {0}; ЛСРМ: {1}; порча: {2}", corpusDir,
                              lsrmDir ?? "пара из дерева (каталог снят 15.09.2026)", sabotage ?? "нет");

            string exportPath, modelPath;
            if (lsrmDir != null)
            {
                exportPath = Path.Combine(lsrmDir, LsrmExport);
                modelPath = Path.Combine(lsrmDir, LsrmModel);
                DoseSpectrum = "RC103_K40Village";
            }
            else if (!PairFromTree(scratch, out exportPath, out modelPath))
            {
                return 2;
            }

            if (!File.Exists(exportPath) || !File.Exists(modelPath))
            {
                Console.Error.WriteLine("нет пары ЛСРМ: " + exportPath + " / " + modelPath);
                return 2;
            }

            try
            {
                // Конструктору нужны общие менеджеры (готовые детекторы, вещества).
                GlobalConfigManager.GetInstance();
                EfficiencyConfigData imported = ImportPair(exportPath, modelPath, scratch);
                DoseOnCorpus(imported);
                StrictControlAs80(scratch);
                Refusals(exportPath, modelPath, scratch);
                FormStrings();
            }
            catch (Exception ex)
            {
                Console.WriteLine("  ⛔ оснастка: " + ex);
                return 2;
            }
            finally
            {
                try { Directory.Delete(scratch, true); } catch (Exception) { }
            }

            Console.WriteLine();
            Console.WriteLine("проверок {0}, провалов {1}", checks, failed);
            if (sabotage != null)
            {
                bool caught = failed > 0;
                Console.WriteLine(caught
                    ? "ПОРЧА «" + sabotage + "» ПОЙМАНА — проба смотрит"
                    : "ПОРЧА «" + sabotage + "» НЕ ПОЙМАНА — проба слепа");
                return caught ? 0 : 1;
            }

            Console.WriteLine(failed == 0 ? "ВСЁ СОШЛОСЬ" : "ПРОВАЛОВ " + failed);
            return failed > 0 ? 1 : 0;
        }

        // ==================================================================
        // §1. Импорт пары ЛСРМ
        // ==================================================================

        static EfficiencyConfigData ImportPair(string exportPath, string modelPath, string scratch)
        {
            Head("§1. ИМПОРТ ПАРЫ ЛСРМ: " + Path.GetFileName(exportPath) + " + " + Path.GetFileName(modelPath));

            string geometryPath = modelPath;
            if (sabotage == "badin")
            {
                // Битый `.in`: текст ЭКСПОРТА под именем геометрии. Читатель
                // `ключ = значение` на нём не падает — модель выйдет с нулевым
                // кристаллом, и ловить это обязан импорт, а не доза.
                geometryPath = Path.Combine(scratch, "broken.in");
                File.Copy(exportPath, geometryPath);
                Console.WriteLine("  ⚠ ПОРЧА: вместо .in подсунут текст экспорта: " + geometryPath);
            }

            var device = new DeviceConfigInfo();
            device.Name = "P7 probe";
            string problem, geometryProblem;
            EfficiencyConfigData config = ImportViaDesigner(
                device, exportPath, geometryPath, out problem, out geometryProblem);
            Ok(config != null, config != null
                ? "экспорт импортирован: точек " + config.Curve.Count
                : "экспорт НЕ импортирован: " + (problem ?? "причина не названа"));
            if (config == null)
            {
                throw new InvalidOperationException("без кривой дальше мерить нечего");
            }

            if (sabotage == "stamp")
            {
                config.Origin = EfficiencyOrigin.Simulation;
                config.ComputeStamp = "phys=16; hist=200000; grid=40-3000 keV/34 std";
                Console.WriteLine("  ⚠ ПОРЧА: кривая выдаёт себя за посчитанную: Origin = " + config.Origin
                                  + ", клеймо «" + config.ComputeStamp + "»");
            }

            Console.WriteLine("  сообщение о геометрии: " + (geometryProblem == null ? "нет" : "«" + Short(geometryProblem) + "»"));
            Ok(device.EfficiencyConfigs.Contains(config), "кривая положена в EfficiencyConfigs прибора");
            Ok(config.Origin == EfficiencyOrigin.Lsrm, "Origin = Lsrm: " + config.Origin);
            Ok(string.IsNullOrEmpty(config.ComputeStamp),
               "клеймо ComputeStamp пустое (не выдаёт себя за посчитанную): «" + (config.ComputeStamp ?? "") + "»");
            Ok(config.Geometry != null, "Geometry != null" + (geometryProblem != null ? " (причина отказа: " + Short(geometryProblem) + ")" : ""));
            if (sabotage == "badin")
            {
                Ok(!string.IsNullOrEmpty(geometryProblem), "битый .in: причина НАЗВАНА (импорт с сообщением)");
            }

            string note = config.Note == null ? "" : config.Note.ToString();
            Console.WriteLine("  Note: «" + Short(note) + "»");
            Ok(note.IndexOf(Path.GetFileName(exportPath), StringComparison.Ordinal) >= 0,
               "Note называет файл экспорта");
            if (config.Geometry == null)
            {
                return config;
            }

            // Геометрия кривой из конструктора — его поля (`.in` в них загружен
            // той же кнопкой): заметка называет поля, а не файл.
            Ok(note.IndexOf("editor fields", StringComparison.Ordinal) >= 0,
               "Note называет источник геометрии — поля конструктора");
            Ok(!config.CurveGeometryMismatch, "отпечаток геометрии кривой сходится с геометрией конфигурации");

            // Числа сцены — против текста `.in`, разобранного НЕЗАВИСИМО.
            GeometryModel g = config.Geometry;
            Dictionary<string, double> raw = ReadInCentimeters(geometryPath);
            Console.WriteLine("  геометрия: " + g.Describe());
            Ok(g.SourceType == GeometrySourceType.Marinelli, "источник — маринелли: " + g.SourceType);
            if (g.CrystalBoxX > 0.0)
            {
                // Копия из `tools\effmaker\models` несёт наши ключи `DS_CrystalBox*`
                // (брусок); цилиндрические `DS_CrystalDiameter`/`Height` в ней —
                // приведение ЛСРМ для чужих программ, у модели-бруска они нули.
                Number("DS_CrystalBoxX", raw, g.CrystalBoxX, "брусок X, мм");
                Number("DS_CrystalBoxY", raw, g.CrystalBoxY, "брусок Y, мм");
                Number("DS_CrystalBoxZ", raw, g.CrystalBoxZ, "брусок Z, мм");
            }
            else
            {
                Number("DS_CrystalDiameter", raw, g.CrystalDiameter, "диаметр кристалла, мм");
                Number("DS_CrystalHeight", raw, g.CrystalHeight, "высота кристалла, мм");
            }

            Number("SM_BeakerDiameter", raw, g.MarinelliBeakerDiameter, "диаметр маринелли, мм");
            Number("SM_BeakerHoleDiameter", raw, g.MarinelliHoleDiameter, "диаметр колодца, мм");
            Number("SM_SourceHeight", raw, g.MarinelliSourceHeight, "высота пробы, мм");
            Number("SM_BeakerToDetectorFrontDistance", raw, g.MarinelliToDetectorDistance, "расстояние до торца, мм");

            // Тот же читатель, что у конструктора: текст `.in` от геометрии
            // кривой равен тексту от прямого GeometryModel.Load.
            string direct = GeometryWriter.Render(GeometryModel.Load(geometryPath));
            string viaImport = GeometryWriter.Render(g);
            Ok(direct == viaImport, "текст .in из геометрии кривой = тексту от прямого GeometryModel.Load ("
                                    + viaImport.Length + " знаков)");

            // XML туда-обратно: конфигурация прибора целиком.
            var serializer = new XmlSerializer(typeof(DeviceConfigInfo));
            string xml = Path.Combine(scratch, "device.xml");
            using (var fs = new FileStream(xml, FileMode.Create, FileAccess.Write))
            {
                serializer.Serialize(fs, device);
            }

            DeviceConfigInfo back;
            using (var fs = new FileStream(xml, FileMode.Open, FileAccess.Read))
            {
                back = (DeviceConfigInfo)serializer.Deserialize(fs);
            }

            EfficiencyConfigData found = null;
            foreach (EfficiencyConfigData c in back.EfficiencyConfigs)
            {
                if (c.Guid == config.Guid) found = c;
            }

            Ok(found != null, "после XML туда-обратно кривая найдена по Guid");
            if (found != null)
            {
                Ok(found.Origin == EfficiencyOrigin.Lsrm, "после XML Origin = Lsrm: " + found.Origin);
                Ok(found.Geometry != null, "после XML геометрия НАЙДЕНА");
                Ok(found.Curve.Count == config.Curve.Count,
                   "после XML точек " + found.Curve.Count + " (было " + config.Curve.Count + ")");
                if (found.Geometry != null)
                {
                    string again = GeometryWriter.Render(found.Geometry);
                    Ok(again == viaImport, "после XML текст .in геометрии тот же");
                    // Кристалл сверяется тем размером, который у модели НЕ ноль:
                    // у бруска (копии с `DS_CrystalBox*`) диаметр нулевой, и его
                    // равенство ничего бы не мерило.
                    double side = g.CrystalBoxX > 0.0 ? g.CrystalBoxX : g.CrystalDiameter;
                    double sideAgain = g.CrystalBoxX > 0.0 ? found.Geometry.CrystalBoxX : found.Geometry.CrystalDiameter;
                    Ok(Math.Abs(found.Geometry.MarinelliSourceHeight - g.MarinelliSourceHeight) < 1e-9
                       && side > 0.0 && Math.Abs(sideAgain - side) < 1e-9,
                       string.Format(CultureInfo.InvariantCulture,
                           "после XML высота пробы и {0} кристалла те же ({1:R} мм)",
                           g.CrystalBoxX > 0.0 ? "сторона бруска" : "диаметр", side));
                }
            }

            return config;
        }

        static void Number(string key, Dictionary<string, double> raw, double modelMm, string what)
        {
            double cm;
            if (!raw.TryGetValue(key, out cm))
            {
                Ok(false, what + ": ключа " + key + " в тексте .in нет");
                return;
            }

            double fileMm = cm * GeometryModel.MmPerCm;
            Ok(Math.Abs(fileMm - modelMm) < 1e-6,
               string.Format(CultureInfo.InvariantCulture, "{0}: модель {1:f4} = файл {2:f4} ({3} = {4} cm)",
                             what, modelMm, fileMm, key, cm));
        }

        /// <summary>Независимый разбор `.in`: `ключ = число cm` → см.</summary>
        static Dictionary<string, double> ReadInCentimeters(string path)
        {
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var line = new Regex(@"^\s*([A-Za-z_][A-Za-z0-9_\[\]]*)\s*=\s*([-+0-9.eE]+)\s*cm\s*$");
            foreach (string raw in File.ReadAllLines(path))
            {
                int comment = raw.IndexOf("//", StringComparison.Ordinal);
                string text = comment >= 0 ? raw.Substring(0, comment) : raw;
                Match m = line.Match(text);
                if (m.Success)
                {
                    result[m.Groups[1].Value] = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                }
            }

            return result;
        }

        // ==================================================================
        // §2(а). Доза на корпусном спектре — число со знаком «≈»
        // ==================================================================

        static void DoseOnCorpus(EfficiencyConfigData curve)
        {
            Head("§2(а). ДОЗА: " + DoseSpectrum + " × кривая ЛСРМ §1");
            if (DoseSpectrum != TreeSpectrum)
            {
                Console.WriteLine("  ⚠ пара УСЛОВНАЯ: модель ЛСРМ RadiaCode Ø11.28×10 мм / маринелли 0.5 л, спектр RC-103 в маринелли 0.5 л;");
                Console.WriteLine("    паспортной связи кривой с прибором нет — число проверяет механику, а не измеряет дозу");
            }

            ResultData data = LoadSpectrum(Path.Combine(corpusDir, "spectra", DoseSpectrum + ".xml"));
            if (data == null)
            {
                Ok(false, "нет спектра корпуса " + DoseSpectrum);
                return;
            }

            ResultData copy = new ResultData();
            copy.EnergySpectrum = data.EnergySpectrum;
            copy.Efficiency = curve;
            var manager = new DoseRateManager(Config());
            DoseRate dose = manager.Calculate(copy);
            Ok(dose != null, "строка состояния НЕ пуста (кривая выбрана и с точками)");
            if (dose == null)
            {
                return;
            }

            // Пометка о матрице печатается только при числе: при отказе вход
            // не собран, и `LastMatrixNote` относится к прежнему входу.
            Console.WriteLine("  строка состояния: «" + dose + "»"
                              + (string.IsNullOrEmpty(dose.Refusal)
                                  ? "  (матрица: " + (manager.LastMatrixNote == "" ? "есть" : manager.LastMatrixNote) + ")"
                                  : ""));
            if (curve.Geometry == null)
            {
                Ok(string.IsNullOrEmpty(dose.Refusal) == false, "без геометрии — отказ словами: «" + Short(dose.Refusal) + "»");
                return;
            }

            Ok(string.IsNullOrEmpty(dose.Refusal), "число, а не отказ" + (dose.Refusal == "" ? "" : ": «" + Short(dose.Refusal) + "»"));
            Ok(dose.Approximate, "число со знаком «≈» (по пиковой, матрицы нет по построению)");
            Ok(dose.ToString().StartsWith(DoseRate.ApproximateMark, StringComparison.Ordinal),
               "строка состояния начинается с «" + DoseRate.ApproximateMark + "»");
            Ok(dose.Rate > 0.0 && !double.IsNaN(dose.Rate) && !double.IsInfinity(dose.Rate),
               string.Format(CultureInfo.InvariantCulture, "≈ {0:f4} мкЗв/ч — конечное положительное число", dose.Rate));
        }

        // ==================================================================
        // §2(б). Строгий контроль: AS80_Cs137_0cm, те же точки, тот же .in
        // ==================================================================

        static void StrictControlAs80(string scratch)
        {
            Head("§2(б). СТРОГИЙ КОНТРОЛЬ: AS80_Cs137_0cm — точки снимка экспортом ЛСРМ + AS80_point0.in");
            ResultData data = LoadSpectrum(Path.Combine(corpusDir, "spectra", "AS80_Cs137_0cm.xml"));
            string inPath = Path.Combine(corpusDir, "geometries", "AS80_point0.in");
            if (data == null || data.Efficiency == null || !data.Efficiency.HasGeometry || !File.Exists(inPath))
            {
                Ok(false, "нет спектра AS80_Cs137_0cm с узлом Efficiency и геометрией либо нет " + inPath);
                return;
            }

            EfficiencyConfigData native = data.Efficiency;
            string exportPath = Path.Combine(scratch, "AS80_point0_lsrm.txt");
            WriteLsrmExport(exportPath, native.Curve);
            Console.WriteLine("  родная кривая: {0}, точек {1}, клеймо «{2}»", native.Origin, native.Curve.Count, Short(native.ComputeStamp));

            var device = new DeviceConfigInfo();
            device.Name = "P7 probe AS80";
            string problem, geometryProblem;
            EfficiencyConfigData imported = ImportViaDesigner(
                device, exportPath, inPath, out problem, out geometryProblem);
            Ok(imported != null && imported.Geometry != null,
               "импорт с AS80_point0.in: кривая " + (imported == null ? "НЕ импортирована: " + problem
                   : "импортирована, геометрия " + (imported.Geometry == null ? "НЕТ: " + geometryProblem : "есть")));
            if (imported == null || imported.Geometry == null)
            {
                return;
            }

            Ok(imported.Curve.Count == native.Curve.Count,
               "точек импортировано " + imported.Curve.Count + " из " + native.Curve.Count + " (ни одна не отсечена)");

            var manager = new DoseRateManager(Config());
            DoseRate reference;
            try
            {
                reference = manager.Calculate(data, DoseRateInput.Of(native, null));
            }
            catch (DoseRateRefusalException ex)
            {
                Ok(false, "родная кривая по пиковой отказала: " + Short(ex.Message));
                return;
            }

            ResultData copy = new ResultData();
            copy.EnergySpectrum = data.EnergySpectrum;
            copy.Efficiency = imported;
            DoseRate dose = manager.Calculate(copy);
            Console.WriteLine("  родная по пиковой:   «" + reference + "»");
            Console.WriteLine("  импортированная ЛСРМ + .in: «" + dose + "»");
            Ok(dose != null && string.IsNullOrEmpty(dose.Refusal), "импортированная кривая даёт число" + (dose == null ? " (пусто)" : dose.Refusal == "" ? "" : ": «" + Short(dose.Refusal) + "»"));
            if (dose == null || !string.IsNullOrEmpty(dose.Refusal))
            {
                return;
            }

            Ok(dose.Approximate && reference.Approximate, "оба числа со знаком «≈»");
            double rel = Math.Abs(dose.Rate - reference.Rate) / Math.Max(1e-300, Math.Abs(reference.Rate));
            Ok(rel < 1e-12, string.Format(CultureInfo.InvariantCulture,
                "≈ {0:f6} (импортированная) = ≈ {1:f6} (родная) мкЗв/ч, отн. {2:e2} < 1e-12", dose.Rate, reference.Rate, rel));
            Ok(Math.Abs(dose.Rate - P1Approximate) / P1Approximate < 0.02,
               string.Format(CultureInfo.InvariantCulture, "против журнала П1 ≈ {0:f3}: {1:+0.0;-0.0} %",
                             P1Approximate, 100.0 * (dose.Rate / P1Approximate - 1.0)));
        }

        static void WriteLsrmExport(string path, IList<ROIEfficiencyData> points)
        {
            var sb = new StringBuilder();
            sb.Append("Energy, keV\tEfficiency\tUncertainty, %\r\n");
            foreach (ROIEfficiencyData p in points)
            {
                // Погрешность ниже 100 % — иначе читатель ЛСРМ (правило `T174`)
                // отсечёт точку, и «те же точки» перестанут быть теми же.
                // Числа — форматом `R` (полный двоичный обход): шесть значащих
                // настоящего экспорта (`4.55616E-02`) дали 7.4e-9 относительных
                // на дозе — измерено, и это была точность ЗАПИСИ пробы, а не
                // путь приложения.
                double error = p.ErrorPercent < 100.0 ? p.ErrorPercent : 1.0;
                sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:R}\t\t\t{1:R}\t\t{2:R}\r\n",
                                        p.Energy, p.Efficiency, error));
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        /// <summary>
        /// Пара §1 из дерева, когда каталога `--lsrm=` нет (П331): узел
        /// `Efficiency` спектра <see cref="TreeSpectrum"/> — кривая пишется
        /// экспортом ЛСРМ (<see cref="WriteLsrmExport"/>), геометрия — писателем
        /// `.in` (<see cref="GeometryWriter.Save"/>, кодировка файлов ЛСРМ) в
        /// <paramref name="scratch"/>.
        /// </summary>
        static bool PairFromTree(string scratch, out string exportPath, out string modelPath)
        {
            exportPath = Path.Combine(scratch, TreeSpectrum + "_lsrm.txt");
            modelPath = Path.Combine(scratch, TreeSpectrum + ".in");
            ResultData data = LoadSpectrum(Path.Combine(corpusDir, "spectra", TreeSpectrum + ".xml"));
            if (data == null || data.Efficiency == null || !data.Efficiency.HasCurve || data.Efficiency.Geometry == null)
            {
                Console.Error.WriteLine("нет спектра " + TreeSpectrum + " с кривой и геометрией в узле Efficiency — пары из дерева не собрать");
                return false;
            }

            WriteLsrmExport(exportPath, data.Efficiency.Curve);
            GeometryWriter.Save(data.Efficiency.Geometry, modelPath);
            Console.WriteLine("пара из дерева: {0} — экспорт {1} ({2} точек), модель {3}",
                              TreeSpectrum, exportPath, data.Efficiency.Curve.Count, modelPath);
            return true;
        }

        // ==================================================================
        // §3. Сцена поля: «Сохранить» конструктора отказывает кривой ЛСРМ
        // ==================================================================

        static void Refusals(string exportPath, string modelPath, string scratch)
        {
            Head("§3. СЦЕНА ПОЛЯ (ISO) У КРИВОЙ ЛСРМ — ОТКАЗ «СОХРАНИТЬ»; МУСОРНЫЙ .in");

            // Правило — функцией, без окна.
            GeometryModel marinelli = GeometryModel.Load(modelPath);
            string iso = Path.Combine(scratch, "iso.in");
            // Кодировка файлов ЛСРМ (её пишет `GeometryWriter.Save`): имя вещества
            // бывает кириллицей, и чтение как UTF-8 его испортило бы.
            Encoding lsrm = Encoding.GetEncoding(1251);
            File.WriteAllText(iso, File.ReadAllText(modelPath, lsrm) + "\r\nDS_Scene = ISO\r\nDS_FieldRadius = 50 cm\r\n", lsrm);
            GeometryModel field = GeometryModel.Load(iso);
            // Сцена поля у редактора — пара «точечный источник + ISO» (его
            // таблица видов источника); маринелли с приписанной сценой панель
            // законно сводит к сосуду, и окно мерило бы не ту сцену.
            field.SourceType = GeometrySourceType.Point;
            Ok(field.Scene == GeometrySceneKind.Iso, "подставная сцена прочиталась как ISO: " + field.Scene);
            Ok(Rule(EfficiencyOrigin.Lsrm, field) != null,
               "ЛСРМ + ISO: отказ — «" + Short(Rule(EfficiencyOrigin.Lsrm, field)) + "»");
            Ok(Rule(EfficiencyOrigin.Simulation, field) == null, "расчёт + ISO: можно (кривая из геометрии поля законна)");
            Ok(Rule(EfficiencyOrigin.Lsrm, marinelli) == null, "ЛСРМ + маринелли: можно");

            // То же окном: конфигурация с геометрией ISO, импорт экспорта, «Сохранить».
            var device = new DeviceConfigInfo();
            device.Name = "P331 probe iso";
            string problem, geometryProblem = null;
            EfficiencyConfigData config = new EfficiencyConfigData("iso") { Geometry = field };
            device.EfficiencyConfigs.Add(config);
            using (EfficiencyMakerForm form = new EfficiencyMakerForm())
            {
                form.BindTo(device, config);
                bool imported = Import(form, exportPath, out problem);
                Ok(imported, "экспорт импортирован в окно с геометрией ISO" + (imported ? "" : ": " + problem));
                object panel = typeof(EfficiencyMakerForm).GetField("geometryPanel", Any).GetValue(form);
                GeometryModel shown = (GeometryModel)panel.GetType().GetProperty("Model", Any).GetValue(panel, null);
                Ok(shown != null && shown.Scene == GeometrySceneKind.Iso,
                   "поля конструктора держат сцену ISO: " + (shown == null ? "нет модели" : shown.Scene.ToString()));
                bool saved = imported && Save(form, out geometryProblem);
                Ok(imported && !saved && geometryProblem == "SaveIntoConfig = false",
                   "«Сохранить» ОТКАЗАЛО (кривая ЛСРМ + ISO)" + (geometryProblem == null ? "" : ": " + geometryProblem));
                Ok(!config.HasCurve && config.Origin != EfficiencyOrigin.Lsrm,
                   "конфигурация не тронута: кривой нет, Origin = " + config.Origin);
            }

            // Мусорный `.in` — признак, которым отказывает кнопка `.in` конструктора.
            string broken = Path.Combine(scratch, "garbage.in");
            File.WriteAllText(broken, "this is not a geometry\r\nEnergy, keV\tEfficiency\r\n20.0\t0.01\r\n");
            Ok(!GeometryModel.Load(broken).HasScintillatorCrystal(), "мусорный .in: кристалла нет — кнопка .in его отвергнет");
        }

        static string Rule(EfficiencyOrigin origin, GeometryModel geometry)
        {
            return (string)typeof(EfficiencyMakerForm).GetMethod("LsrmCurveGeometryProblem", Any)
                .Invoke(null, new object[] { origin, geometry });
        }

        // ==================================================================
        // §5. Подписи: текст отказа §3 в обеих культурах; снятые ключи не вернулись
        // ==================================================================

        static void FormStrings()
        {
            Head("§5. ПОДПИСИ: отказ «сцена поля» en/ru; ключи снятой кнопки (`AMBER227`) не вернулись");
            var en = CultureInfo.GetCultureInfo("en-US");
            var ru = CultureInfo.GetCultureInfo("ru-RU");
            var resources = BecquerelMonitor.Properties.Resources.ResourceManager;
            string english = resources.GetString("EfficiencyMakerLsrmFieldScene", en);
            string russian = resources.GetString("EfficiencyMakerLsrmFieldScene", ru);
            Ok(!string.IsNullOrEmpty(english) && !string.IsNullOrEmpty(russian) && english != russian,
               "EfficiencyMakerLsrmFieldScene: en «" + Short(english) + "» / ru «" + Short(russian) + "»");

            foreach (string key in new[] { "EfficiencyTabImportLsrm", "EfficiencyTabImportLsrmTitle", "EffCalcMCFileFilter" })
            {
                Ok(resources.GetString(key, en) == null && resources.GetString(key, ru) == null,
                   "Resources: ключа " + key + " нет");
            }

            var form = new System.ComponentModel.ComponentResourceManager(typeof(DeviceConfigForm));
            foreach (string key in new[] { "lsrmGeometryDialogTitle", "lsrmGeometryProblem",
                                           "lsrmGeometryNoCrystal", "lsrmGeometryFieldScene" })
            {
                Ok(form.GetString(key, en) == null && form.GetString(key, ru) == null,
                   "DeviceConfigForm.resx: ключа " + key + " нет");
            }

            Ok(typeof(DeviceConfigForm).GetMethod("ImportLsrmEfficiencyWithGeometry", Any | BindingFlags.Static) == null
               && typeof(DeviceConfigForm).GetMethod("SuggestLsrmGeometryPath", Any | BindingFlags.Static) == null
               && typeof(DeviceConfigForm).GetField("efficiencyImportButton", Any) == null,
               "путь снятой кнопки (кнопка, импорт с геометрией, автоподбор .in) в приложении не живёт");
        }

        // ==================================================================
        // Импорт через конструктор кривой — единственный путь с `AMBER227`
        // ==================================================================

        static readonly BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        /// <summary>
        /// Кривая ЛСРМ с геометрией так, как её заводит человек: конфигурация
        /// прибора, геометрия `.in` в полях конструктора (кнопка `.in` отвергает
        /// файл без кристалла — здесь тем же признаком, и тогда остаётся
        /// заготовка полей, а причина — в <paramref name="geometryProblem"/>),
        /// «Импорт из LSRM…» экспорта и «Сохранить». null — кривая не легла.
        /// </summary>
        static EfficiencyConfigData ImportViaDesigner(DeviceConfigInfo device, string exportPath, string geometryPath,
                                                      out string problem, out string geometryProblem)
        {
            problem = null;
            geometryProblem = null;
            GeometryModel geometry = null;
            if (geometryPath != null)
            {
                try
                {
                    geometry = GeometryModel.Load(geometryPath);
                    if (!geometry.HasScintillatorCrystal())
                    {
                        geometryProblem = geometryPath + ": нет размеров сцинтиллятора — кнопка .in конструктора его отвергает";
                        geometry = null;
                    }
                }
                catch (Exception ex)
                {
                    geometryProblem = geometryPath + ": " + ex.Message;
                    geometry = null;
                }
            }

            EfficiencyConfigData config = new EfficiencyConfigData(Path.GetFileNameWithoutExtension(exportPath))
            {
                Geometry = geometry,
            };
            device.EfficiencyConfigs.Add(config);
            using (EfficiencyMakerForm form = new EfficiencyMakerForm())
            {
                form.BindTo(device, config);
                if (!Import(form, exportPath, out problem))
                {
                    device.EfficiencyConfigs.Remove(config);
                    return null;
                }

                string saveProblem;
                if (!Save(form, out saveProblem))
                {
                    problem = "«Сохранить» отказало: " + saveProblem;
                    device.EfficiencyConfigs.Remove(config);
                    return null;
                }
            }

            return config;
        }

        static bool Import(EfficiencyMakerForm form, string path, out string problem)
        {
            object[] a = { path, null, null };
            bool ok = (bool)typeof(EfficiencyMakerForm).GetMethod("ImportCurveFile", Any).Invoke(form, a);
            problem = (string)a[2];
            return ok;
        }

        /// <summary>«Сохранить» — только при годных полях: иначе `TryCommit` поднял бы модальное окно.</summary>
        static bool Save(EfficiencyMakerForm form, out string problem)
        {
            problem = null;
            object panel = typeof(EfficiencyMakerForm).GetField("geometryPanel", Any).GetValue(form);
            Type pt = panel.GetType();
            bool marks = (bool)pt.GetMethod("MarkBadValues", Any).Invoke(panel, null);
            object g = pt.GetMethod("BuildModel", Any).Invoke(panel, null);
            string error = (string)pt.GetMethod("Validate", Any, null, new[] { typeof(GeometryModel) }, null)
                .Invoke(panel, new[] { g });
            if (!marks || error != null)
            {
                problem = "поля геометрии негодны: " + (error ?? "неверное число");
                return false;
            }

            bool saved = (bool)typeof(EfficiencyMakerForm).GetMethod("SaveIntoConfig", Any).Invoke(form, null);
            if (!saved)
            {
                problem = "SaveIntoConfig = false";
            }

            return saved;
        }

        // ==================================================================
        // Оснастка
        // ==================================================================

        static ResultData LoadSpectrum(string path)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine("  нет " + path);
                return null;
            }

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var file = (ResultDataFile)new XmlSerializer(typeof(ResultDataFile)).Deserialize(fs);
                return file.ResultDataList.Count > 0 ? file.ResultDataList[0] : null;
            }
        }

        static GlobalConfigManager Config()
        {
            var manager = new GlobalConfigManager();
            var info = new GlobalConfigInfo();
            if (info.ColorConfig != null
                && (info.ColorConfig.SpectrumColorList == null || info.ColorConfig.SpectrumColorList.Count == 0))
            {
                info.ColorConfig.InitializeSpectrumColor();
            }

            manager.GlobalConfig = info;
            return manager;
        }

        static void Ok(bool condition, string what)
        {
            checks++;
            if (!condition) failed++;
            Console.WriteLine((condition ? "  [ок]   " : "  [НЕТ]  ") + what);
        }

        static void Head(string text)
        {
            Console.WriteLine();
            Console.WriteLine("──────────────────────────────────────────────────────────────");
            Console.WriteLine(text);
            Console.WriteLine("──────────────────────────────────────────────────────────────");
        }

        static string Short(string text)
        {
            text = (text ?? "").Replace(Environment.NewLine, " ");
            return text.Length > 140 ? text.Substring(0, 137) + "..." : text;
        }
    }
}
