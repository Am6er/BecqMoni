using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Serialization;

namespace LsrmGeometryImportProbe
{
    /// <summary>
    /// Приёмка остатка `AMBER18` (полоса П7, 12.09.2026) — решение Amber
    /// 12.09.2026, вопросником, дословно: «Привязать геометрию при ввозе ЛСРМ».
    /// Ввоз экспорта ЛСРМ на вкладке Efficiency вторым шагом берёт `.in` той
    /// же геометрии (`DeviceConfigForm.ImportLsrmEfficiencyWithGeometry`), и
    /// у кривой появляется масштаб для дозы. Окно `BecqMoni` не поднимается:
    /// проба зовёт тот же статический метод, что и кнопка.
    ///
    ///  §1 ВВОЗ ПАРЫ ЛСРМ (экспорт `RadiaCode - marinelli 0.5.txt` + модель
    ///     `RadiaCode_Marinelli0.5.in` из `LSRM Geometries/`): в конфигурации
    ///     прибора кривая с `Origin = Lsrm`, `Geometry != null`, клеймо
    ///     `ComputeStamp` ПУСТОЕ (кривая не выдаёт себя за посчитанную из
    ///     геометрии); размеры сцены сверяются с числами, прочитанными из
    ///     текста `.in` НЕЗАВИСИМО (регулярным выражением, см → мм); текст
    ///     `.in`, собранный писателем из геометрии кривой, равен тексту от
    ///     прямого чтения тем же читателем; после XML туда-обратно
    ///     (`DeviceConfigInfo`) геометрия НАЙДЕНА и её текст тот же.
    ///
    ///  §2 ДОЗА: (а) корпусный `RC103_K40Village` (RadiaCode-103, K-40 в
    ///     маринелли 0.5 л) с кривой §1 — число со знаком «≈» вместо отказа.
    ///     ⚠ Пара УСЛОВНАЯ: модель ЛСРМ — RadiaCode с кристаллом Ø11.28×10 мм в
    ///     маринелли 0.5 л, спектр — RC-103 в маринелли 0.5 л, но паспортной
    ///     связи «эта кривая считана для этого прибора» нет, число — проверка
    ///     механики, не измерение. (б) СТРОГИЙ КОНТРОЛЬ: точки кривой из узла
    ///     `Efficiency` спектра `AS80_Cs137_0cm` записываются экспортом ЛСРМ и
    ///     ввозятся с `.in` той же сцены `AS80_point0` — доза «≈» по ввезённой
    ///     кривой обязана совпасть с дозой «≈» по родной кривой снимка
    ///     (`DoseRateInput.Of(родная, null)`) до 1e-12 относительных: те же
    ///     точки, та же геометрия, тот же путь. Число печатается рядом с
    ///     ≈ 0.195 мкЗв/ч из журнала П1.
    ///
    ///  §3 ОТКАЗ ОТ `.in` (Cancel → null) — `Geometry == null`, доза — прежний
    ///     отказ словами («геометрии нет»); `.in`, которого нет на диске, —
    ///     кривая ввезена, геометрии нет, причина названа; `.in` сцены поля
    ///     (`DS_Scene = ISO`) — то же: кривая ЛСРМ в долях к сцене на флюенс
    ///     не привязывается.
    ///
    ///  §4 АВТОПОДБОР: одноимённый `.in` рядом с экспортом находится, без
    ///     него — null.
    ///
    ///  §5 ПОДПИСИ: четыре ключа ввоза в `DeviceConfigForm.resx` / `.ru.resx`
    ///     читаются по обеим культурам и различаются (сторож `check_resx_designer`
    ///     обращений через `ComponentResourceManager` не видит).
    ///
    ///   lsrmgeometryimportprobe [--dir=&lt;корпус&gt;] [--lsrm=&lt;каталог LSRM Geometries&gt;]
    ///   lsrmgeometryimportprobe --sabotage=badin|stamp     (ждёт ОТКАЗ пробы)
    ///
    /// ⛔ Приёмка, которая проходит всегда, не мерит ничего. `--sabotage`
    /// портит РОВНО ОДНУ вещь и требует, чтобы проба ОТКАЗАЛА; коды у него
    /// перевёрнуты: 0 — отказ получен (проба смотрит), 1 — не получен (слепа).
    ///
    ///   badin — вместо `.in` §1 подсунут БИТЫЙ файл (текст экспорта под
    ///           именем `.in`): ввоз обязан положить кривую БЕЗ геометрии и
    ///           НАЗВАТЬ причину; проба обязана это поймать (§1 «геометрия
    ///           есть» падает, сообщение непустое);
    ///   stamp — ввезённая кривая выдаёт себя за посчитанную из геометрии
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
        static string lsrmDir = @"LSRM Geometries";
        static string sabotage;

        /// <summary>Пара ЛСРМ для §1 и §2(а): экспорт и модель одной геометрии.</summary>
        const string LsrmExport = @"Exported Curves\RadiaCode - marinelli 0.5.txt";
        const string LsrmModel = @"Models\RadiaCode_Marinelli0.5.in";

        /// <summary>Журнал П1 §9: `AS80_Cs137_0cm` по пиковой — ≈ 0.195 мкЗв/ч.</summary>
        const double P1Approximate = 0.195;

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

            string exportPath = Path.Combine(lsrmDir, LsrmExport);
            string modelPath = Path.Combine(lsrmDir, LsrmModel);
            if (!File.Exists(exportPath) || !File.Exists(modelPath))
            {
                Console.Error.WriteLine("нет пары ЛСРМ: " + exportPath + " / " + modelPath);
                return 2;
            }

            string scratch = Path.Combine(Path.GetTempPath(), "lsrmgeomimport-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            Console.WriteLine("LsrmGeometryImportProbe — `AMBER18`, полоса П7 12.09.2026: геометрия при ввозе ЛСРМ");
            Console.WriteLine("корпус: {0}; ЛСРМ: {1}; порча: {2}", corpusDir, lsrmDir, sabotage ?? "нет");

            try
            {
                EfficiencyConfigData imported = ImportPair(exportPath, modelPath, scratch);
                DoseOnCorpus(imported);
                StrictControlAs80(scratch);
                Refusals(exportPath, modelPath, scratch);
                AutoSuggest(exportPath, modelPath, scratch);
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
        // §1. Ввоз пары ЛСРМ
        // ==================================================================

        static EfficiencyConfigData ImportPair(string exportPath, string modelPath, string scratch)
        {
            Head("§1. ВВОЗ ПАРЫ ЛСРМ: " + Path.GetFileName(exportPath) + " + " + Path.GetFileName(modelPath));

            string geometryPath = modelPath;
            if (sabotage == "badin")
            {
                // Битый `.in`: текст ЭКСПОРТА под именем геометрии. Читатель
                // `ключ = значение` на нём не падает — модель выйдет с нулевым
                // кристаллом, и ловить это обязан ввоз, а не доза.
                geometryPath = Path.Combine(scratch, "broken.in");
                File.Copy(exportPath, geometryPath);
                Console.WriteLine("  ⚠ ПОРЧА: вместо .in подсунут текст экспорта: " + geometryPath);
            }

            var device = new DeviceConfigInfo();
            device.Name = "P7 probe";
            string problem, geometryProblem;
            EfficiencyConfigData config = DeviceConfigForm.ImportLsrmEfficiencyWithGeometry(
                device, exportPath, geometryPath, out problem, out geometryProblem);
            Ok(config != null, config != null
                ? "экспорт ввезён: точек " + config.Curve.Count
                : "экспорт НЕ ввезён: " + (problem ?? "причина не названа"));
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
                Ok(!string.IsNullOrEmpty(geometryProblem), "битый .in: причина НАЗВАНА (ввоз с сообщением)");
            }

            string note = config.Note == null ? "" : config.Note.ToString();
            Console.WriteLine("  Note: «" + Short(note) + "»");
            Ok(note.IndexOf(Path.GetFileName(exportPath), StringComparison.Ordinal) >= 0,
               "Note называет файл экспорта");
            if (config.Geometry == null)
            {
                return config;
            }

            Ok(note.IndexOf(Path.GetFileName(geometryPath), StringComparison.Ordinal) >= 0,
               "Note называет файл геометрии");

            // Числа сцены — против текста `.in`, разобранного НЕЗАВИСИМО.
            GeometryModel g = config.Geometry;
            Dictionary<string, double> raw = ReadInCentimeters(geometryPath);
            Console.WriteLine("  геометрия: " + g.Describe());
            Ok(g.SourceType == GeometrySourceType.Marinelli, "источник — маринелли: " + g.SourceType);
            Number("DS_CrystalDiameter", raw, g.CrystalDiameter, "диаметр кристалла, мм");
            Number("DS_CrystalHeight", raw, g.CrystalHeight, "высота кристалла, мм");
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
                    Ok(Math.Abs(found.Geometry.MarinelliSourceHeight - g.MarinelliSourceHeight) < 1e-9
                       && Math.Abs(found.Geometry.CrystalDiameter - g.CrystalDiameter) < 1e-9,
                       "после XML высота пробы и диаметр кристалла те же");
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
            Head("§2(а). ДОЗА: RC103_K40Village (RadiaCode-103, K-40 в маринелли 0.5 л) × кривая ЛСРМ §1");
            Console.WriteLine("  ⚠ пара УСЛОВНАЯ: модель ЛСРМ RadiaCode Ø11.28×10 мм / маринелли 0.5 л, спектр RC-103 в маринелли 0.5 л;");
            Console.WriteLine("    паспортной связи кривой с прибором нет — число проверяет механику, а не измеряет дозу");
            ResultData data = LoadSpectrum(Path.Combine(corpusDir, "spectra", "RC103_K40Village.xml"));
            if (data == null)
            {
                Ok(false, "нет спектра корпуса RC103_K40Village");
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
            EfficiencyConfigData imported = DeviceConfigForm.ImportLsrmEfficiencyWithGeometry(
                device, exportPath, inPath, out problem, out geometryProblem);
            Ok(imported != null && imported.Geometry != null,
               "ввоз с AS80_point0.in: кривая " + (imported == null ? "НЕ ввезена: " + problem
                   : "ввезена, геометрия " + (imported.Geometry == null ? "НЕТ: " + geometryProblem : "есть")));
            if (imported == null || imported.Geometry == null)
            {
                return;
            }

            Ok(imported.Curve.Count == native.Curve.Count,
               "точек ввезено " + imported.Curve.Count + " из " + native.Curve.Count + " (ни одна не отсечена)");

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
            Console.WriteLine("  ввезённая ЛСРМ + .in: «" + dose + "»");
            Ok(dose != null && string.IsNullOrEmpty(dose.Refusal), "ввезённая кривая даёт число" + (dose == null ? " (пусто)" : dose.Refusal == "" ? "" : ": «" + Short(dose.Refusal) + "»"));
            if (dose == null || !string.IsNullOrEmpty(dose.Refusal))
            {
                return;
            }

            Ok(dose.Approximate && reference.Approximate, "оба числа со знаком «≈»");
            double rel = Math.Abs(dose.Rate - reference.Rate) / Math.Max(1e-300, Math.Abs(reference.Rate));
            Ok(rel < 1e-12, string.Format(CultureInfo.InvariantCulture,
                "≈ {0:f6} (ввезённая) = ≈ {1:f6} (родная) мкЗв/ч, отн. {2:e2} < 1e-12", dose.Rate, reference.Rate, rel));
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

        // ==================================================================
        // §3. Отказы: Cancel, нет файла, сцена поля
        // ==================================================================

        static void Refusals(string exportPath, string modelPath, string scratch)
        {
            Head("§3. ОТКАЗ ОТ .in (Cancel), .in которого нет, .in сцены поля");
            var manager = new DoseRateManager(Config());
            ResultData data = LoadSpectrum(Path.Combine(corpusDir, "spectra", "RC103_K40Village.xml"));

            // Cancel → null
            var device = new DeviceConfigInfo();
            device.Name = "P7 probe cancel";
            string problem, geometryProblem;
            EfficiencyConfigData config = DeviceConfigForm.ImportLsrmEfficiencyWithGeometry(
                device, exportPath, null, out problem, out geometryProblem);
            Ok(config != null && config.Geometry == null && geometryProblem == null,
               "Cancel (.in = null): кривая ввезена, Geometry == null, сообщения о геометрии нет");
            Ok(config != null && config.Origin == EfficiencyOrigin.Lsrm, "Cancel: Origin = Lsrm");
            if (config != null && data != null)
            {
                ResultData copy = new ResultData();
                copy.EnergySpectrum = data.EnergySpectrum;
                copy.Efficiency = config;
                DoseRate dose = manager.Calculate(copy);
                string refusal = dose == null ? "" : dose.Refusal;
                Ok(dose != null && !string.IsNullOrEmpty(refusal)
                   && (refusal.IndexOf("geometry", StringComparison.OrdinalIgnoreCase) >= 0
                       || refusal.IndexOf("геометри", StringComparison.OrdinalIgnoreCase) >= 0),
                   "Cancel: доза — прежний отказ словами: «" + Short(refusal) + "»");
            }

            // Файла нет
            string missing = Path.Combine(scratch, "no-such-file.in");
            config = DeviceConfigForm.ImportLsrmEfficiencyWithGeometry(
                device, exportPath, missing, out problem, out geometryProblem);
            Ok(config != null && config.Geometry == null && !string.IsNullOrEmpty(geometryProblem),
               ".in отсутствует: кривая ввезена без геометрии, причина: «" + Short(geometryProblem) + "»");

            // Сцена поля
            string iso = Path.Combine(scratch, "iso.in");
            File.WriteAllText(iso, File.ReadAllText(modelPath) + "\r\nDS_Scene = ISO\r\nDS_FieldRadius = 50 cm\r\n");
            config = DeviceConfigForm.ImportLsrmEfficiencyWithGeometry(
                device, exportPath, iso, out problem, out geometryProblem);
            Ok(config != null && config.Geometry == null && !string.IsNullOrEmpty(geometryProblem),
               ".in сцены поля (ISO): кривая ввезена без геометрии, причина: «" + Short(geometryProblem) + "»");

            // Прямая проверка читателя: битый файл → причина «нет кристалла»
            string broken = Path.Combine(scratch, "garbage.in");
            File.WriteAllText(broken, "this is not a geometry\r\nEnergy, keV\tEfficiency\r\n20.0\t0.01\r\n");
            GeometryModel model = DeviceConfigForm.ReadLsrmGeometry(broken, out geometryProblem);
            Ok(model == null && !string.IsNullOrEmpty(geometryProblem),
               "ReadLsrmGeometry на мусоре: null и причина: «" + Short(geometryProblem) + "»");
        }

        // ==================================================================
        // §4. Автоподбор одноимённого .in
        // ==================================================================

        static void AutoSuggest(string exportPath, string modelPath, string scratch)
        {
            Head("§4. АВТОПОДБОР ОДНОИМЁННОГО .in РЯДОМ С ЭКСПОРТОМ");
            string txt = Path.Combine(scratch, "Pair.txt");
            File.Copy(exportPath, txt);
            Ok(DeviceConfigForm.SuggestLsrmGeometryPath(txt) == null, "без Pair.in рядом — null");
            string inFile = Path.Combine(scratch, "Pair.in");
            File.Copy(modelPath, inFile);
            string suggested = DeviceConfigForm.SuggestLsrmGeometryPath(txt);
            Ok(string.Equals(suggested, inFile, StringComparison.OrdinalIgnoreCase),
               "с Pair.in рядом — найден: " + suggested);
            Ok(DeviceConfigForm.SuggestLsrmGeometryPath(exportPath) == null,
               "у настоящего экспорта ЛСРМ одноимённого .in нет (имена в поставке разные) — null");
        }

        // ==================================================================
        // §5. Подписи ввоза — в ресурсах САМОЙ ФОРМЫ, обе культуры
        // ==================================================================

        /// <summary>
        /// Четыре ключа `DeviceConfigForm.resx` / `.ru.resx`, которые читает
        /// ввоз через `ComponentResourceManager(typeof(DeviceConfigForm))`.
        /// ⚠ Сторож `check_resx_designer.py` разбирает только форму записи
        /// `Resources.ResourceManager.GetString("X")` и этих обращений НЕ
        /// видит: опечатка в ключе отдала бы запасной английский текст молча.
        /// Поэтому ключи сверяются здесь — по обеим культурам, и русский
        /// обязан отличаться от английского.
        /// </summary>
        static void FormStrings()
        {
            Head("§5. ПОДПИСИ ВВОЗА В РЕСУРСАХ ФОРМЫ: обе культуры, ключ в ключ");
            var manager = new System.ComponentModel.ComponentResourceManager(typeof(DeviceConfigForm));
            var en = CultureInfo.GetCultureInfo("en-US");
            var ru = CultureInfo.GetCultureInfo("ru-RU");
            foreach (string key in new[] { "lsrmGeometryDialogTitle", "lsrmGeometryProblem",
                                           "lsrmGeometryNoCrystal", "lsrmGeometryFieldScene" })
            {
                string english = manager.GetString(key, en);
                string russian = manager.GetString(key, ru);
                Ok(!string.IsNullOrEmpty(english) && !string.IsNullOrEmpty(russian) && english != russian,
                   key + ": en «" + Short(english) + "» / ru «" + Short(russian) + "»");
            }
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
