using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace DosePointsProbeF9
{
    /// <summary>
    /// `A221` (полоса F9, 05.09.2026): точки мощности дозы ПОСТАВЛЯЮТСЯ —
    /// `RC-103.xml` положен в git. Проба меряет ровно три вещи.
    ///
    ///  1. **Поставочный файл читается кодом приложения.** Тем же
    ///     `XmlSerializer(typeof(DeviceConfigInfo))`, каким читает
    ///     `DeviceConfigManager.LoadAllConfigFiles`: число точек, их диапазон
    ///     и стык (верх одной = низ следующей), и потребитель —
    ///     `DoseRateManager.Calculate` на спектре корпуса того же прибора
    ///     обязан вернуть ЧИСЛО, а не отказ.
    ///
    ///  2. **Каталог, из которого приложение читает на самом деле.**
    ///     `DeviceConfigManager.GetInstance().LoadAllConfigFiles()` — каталог
    ///     задаёт `Package.DeviceDir` (каталог сборки, `S102`), снаружи он не
    ///     задаётся. Проба печатает, что там лежит под GUID `RC-103`, и СКОЛЬКО
    ///     у него точек: 36 — поставочный файл, 0 — корпусная заглушка
    ///     `tools\CORPUS\corpus\devices\RC-103.xml` с ТЕМ ЖЕ GUID перекрыла его
    ///     (пункт 4 задания, задвоение против `B6`).
    ///
    ///  3. **Положительный контроль читателя.** Три порченые копии во
    ///     временном каталоге: блок точек снят (ждём 0), одна точка вынута
    ///     (ждём 35), в число вписан текст (ждём ОТКАЗ разбора). Читатель, не
    ///     видящий порчи, сам ничего не значит.
    ///
    /// Ключи: `--file=` (умолчание — поставочный
    /// `BecquerelMonitor\config\device\RC-103.xml` от корня дерева),
    /// `--spectrum=` (умолчание `tools\CORPUS\corpus\spectra\RC103_Charoite.xml`),
    /// `--expect=` число точек (36), `--emin=`/`--emax=` (0 / 4996.78),
    /// `--catalog-expect=` — сколько точек ждём у RC-103 в каталоге приложения
    /// (без ключа — печать без вердикта).
    /// </summary>
    static class Program
    {
        static int checks;
        static int failed;

        const string Rc103Guid = "7fe39199-d0fe-455a-aef7-ac98e1cd58ec";

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            string repo = FindRepo();
            string file = repo == null ? null : Path.Combine(repo, @"BecquerelMonitor\config\device\RC-103.xml");
            string spectrum = repo == null ? null : Path.Combine(repo, @"tools\CORPUS\corpus\spectra\RC103_Charoite.xml");
            int expect = 36;
            double emin = 0.0, emax = 4996.78;
            int catalogExpect = -1;

            foreach (string a in args)
            {
                if (a.StartsWith("--file=", StringComparison.Ordinal)) file = a.Substring(7);
                else if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrum = a.Substring(11);
                else if (a.StartsWith("--expect=", StringComparison.Ordinal)) expect = int.Parse(a.Substring(9), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--emin=", StringComparison.Ordinal)) emin = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--emax=", StringComparison.Ordinal)) emax = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--catalog-expect=", StringComparison.Ordinal)) catalogExpect = int.Parse(a.Substring(17), CultureInfo.InvariantCulture);
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            if (file == null || !File.Exists(file))
            {
                Console.WriteLine("!! нет поставочного файла: " + (file ?? "<корень дерева не найден>"));
                return 3;
            }

            try
            {
                Console.WriteLine("== 1. поставочный файл кодом приложения ==");
                Console.WriteLine("  файл: " + file);
                DeviceConfigInfo device = Read(file);
                Describe(device, expect, emin, emax);
                Consume(device, spectrum);

                Console.WriteLine();
                Console.WriteLine("== 2. каталог приложения (Package.DeviceDir) ==");
                Catalog(catalogExpect);

                Console.WriteLine();
                Console.WriteLine("== 3. положительный контроль читателя ==");
                Controls(file, expect);
            }
            catch (Exception ex)
            {
                Console.WriteLine("!! проба сорвалась: " + ex);
                return 3;
            }

            Console.WriteLine();
            Console.WriteLine(failed == 0
                ? string.Format("ВСЁ СОШЛОСЬ: {0} проверок", checks)
                : string.Format("ПРОВАЛОВ {0} из {1}", failed, checks));
            return failed == 0 ? 0 : 1;
        }

        static void Ok(bool condition, string what)
        {
            checks++;
            if (!condition) failed++;
            Console.WriteLine("  {0} {1}", condition ? "ok  " : "ПРОВАЛ", what);
        }

        // Тот же читатель, что у `DeviceConfigManager.LoadAllConfigFiles`.
        static DeviceConfigInfo Read(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                return (DeviceConfigInfo)new XmlSerializer(typeof(DeviceConfigInfo)).Deserialize(fs);
            }
        }

        static List<DoseRateCalibrationPoint> Points(DeviceConfigInfo device)
        {
            if (device == null || device.DoseRateConfig == null || device.DoseRateConfig.DoseRateCalibrationPoints == null)
            {
                return new List<DoseRateCalibrationPoint>();
            }
            return device.DoseRateConfig.DoseRateCalibrationPoints;
        }

        static void Describe(DeviceConfigInfo device, int expect, double emin, double emax)
        {
            List<DoseRateCalibrationPoint> points = Points(device);
            RadiaCodeDeviceConfig rcConfig = device.InputDeviceConfig as RadiaCodeDeviceConfig;
            Console.WriteLine("  прибор: «{0}», GUID {1}, формат {2}, серийный {3}",
                device.Name, device.Guid, device.FormatVersion,
                rcConfig == null ? "<нет>" : rcConfig.DeviceSerial);
            Ok(device.Guid == Rc103Guid, "GUID тот, на который ссылаются спектры корпуса RC103_*");
            Ok(device.FormatVersion == "120920", "формат 120920 — читается новым разбором, без ветки _097b");
            Ok(points.Count == expect, string.Format("точек мощности дозы: {0} (ждали {1})", points.Count, expect));
            if (points.Count == 0) return;

            double lo = points.Min(p => p.LowerBound);
            double hi = points.Max(p => p.UpperBound);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  диапазон {0:f2} … {1:f2} кэВ", lo, hi));
            Ok(Math.Abs(lo - emin) < 1e-9, string.Format(CultureInfo.InvariantCulture, "низ {0:f2} (ждали {1:f2})", lo, emin));
            Ok(Math.Abs(hi - emax) < 1e-6, string.Format(CultureInfo.InvariantCulture, "верх {0:f2} (ждали {1:f2})", hi, emax));

            var sorted = points.OrderBy(p => p.LowerBound).ToList();
            int gaps = 0;
            for (int i = 1; i < sorted.Count; i++)
            {
                if (Math.Abs(sorted[i].LowerBound - sorted[i - 1].UpperBound) > 1e-9) gaps++;
            }
            Ok(gaps == 0, string.Format("диапазоны идут встык: разрывов/перекрытий {0}", gaps));
            int badSens = points.Count(p => !(p.CPS > 0.0) || !(p.EtalonDoseRateValue > 0.0)
                                          || double.IsNaN(p.Sensitivity) || double.IsInfinity(p.Sensitivity));
            Ok(badSens == 0, string.Format("чувствительность конечна и > 0 у всех: негодных {0}", badSens));
        }

        static void Consume(DeviceConfigInfo device, string spectrumPath)
        {
            if (spectrumPath == null || !File.Exists(spectrumPath))
            {
                Ok(false, "нет спектра корпуса: " + (spectrumPath ?? "<нет>"));
                return;
            }
            ResultData data;
            using (var fs = new FileStream(spectrumPath, FileMode.Open, FileAccess.Read))
            {
                var f = (ResultDataFile)new XmlSerializer(typeof(ResultDataFile)).Deserialize(fs);
                data = f.ResultDataList.Count > 0 ? f.ResultDataList[0] : null;
            }
            if (data == null)
            {
                Ok(false, "спектр пуст: " + spectrumPath);
                return;
            }
            string refGuid = data.DeviceConfigReference == null ? "<нет>" : data.DeviceConfigReference.Guid;
            Console.WriteLine("  спектр: {0}, ссылка на прибор {1}, {2} каналов, {3:f0} с",
                Path.GetFileName(spectrumPath), refGuid,
                data.EnergySpectrum.NumberOfChannels, data.EnergySpectrum.MeasurementTime);
            Ok(refGuid == device.Guid, "спектр ссылается на этот прибор");

            DoseRate dose = new DoseRateManager(Config()).Calculate(data, device.DoseRateConfig);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  DoseRateManager: {0:f4} ± {1:f4} мкЗв/ч, покрытие {2:f2} %, отказ «{3}»",
                dose.Rate, dose.Error, 100.0 * dose.Coverage, dose.Refusal));
            Ok(string.IsNullOrEmpty(dose.Refusal), "потребитель не отказал");
            Ok(dose.Rate > 0.0 && !double.IsNaN(dose.Rate) && !double.IsInfinity(dose.Rate), "число дозы конечно и > 0");
            Ok(dose.Coverage >= DoseRate.CoverageNoticeThreshold,
               string.Format(CultureInfo.InvariantCulture, "покрытие {0:f2} % ≥ {1:f0} % — приписки о неполном покрытии нет (A199)",
                             100.0 * dose.Coverage, 100.0 * DoseRate.CoverageNoticeThreshold));
        }

        static void Catalog(int catalogExpect)
        {
            string dir = Package.GetInstance().DeviceDir;
            Console.WriteLine("  каталог: " + dir);
            if (!Directory.Exists(dir))
            {
                Console.WriteLine("  каталога НЕТ — менеджер без окон бросил бы; пункт 2 не мерится отсюда");
                Ok(catalogExpect < 0, "каталог приложения существует");
                return;
            }
            string[] files = Directory.GetFiles(dir, "*.xml");
            Console.WriteLine("  файлов *.xml: {0}", files.Length);

            // Дубли GUID видны ДО менеджера: он второй файл с тем же GUID
            // отбрасывает с сообщением в поток ошибок, а нам нужно число.
            var byGuid = new Dictionary<string, List<string>>();
            foreach (string f in files)
            {
                try
                {
                    DeviceConfigInfo d = Read(f);
                    List<string> l;
                    if (!byGuid.TryGetValue(d.Guid, out l)) byGuid[d.Guid] = l = new List<string>();
                    l.Add(Path.GetFileName(f) + " (" + Points(d).Count + " точек)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  не читается: {0}: {1}", Path.GetFileName(f), ex.Message);
                }
            }
            int dups = byGuid.Count(kv => kv.Value.Count > 1);
            foreach (var kv in byGuid.Where(kv => kv.Value.Count > 1))
            {
                Console.WriteLine("  дубль GUID {0}: {1}", kv.Key, string.Join(", ", kv.Value));
            }
            Console.WriteLine("  дублей GUID среди файлов каталога: {0}", dups);

            DeviceConfigManager manager = DeviceConfigManager.GetInstance();
            manager.LoadAllConfigFiles();
            Console.WriteLine("  DeviceConfigManager загрузил: {0}", manager.DeviceConfigList.Count);
            DeviceConfigInfo rc;
            if (!manager.DeviceConfigMap.TryGetValue(Rc103Guid, out rc) || rc == null)
            {
                Console.WriteLine("  RC-103 ({0}) в каталоге НЕТ", Rc103Guid);
                if (catalogExpect >= 0) Ok(false, "RC-103 в каталоге приложения");
                return;
            }
            int n = Points(rc).Count;
            Console.WriteLine("  RC-103 в каталоге: файл «{0}», точек {1}, LastUpdated {2:yyyy-MM-dd} — {3}",
                rc.Filename, n, rc.LastUpdated,
                n > 0 ? "ПОСТАВОЧНЫЙ файл" : "корпусная заглушка без точек (перекрыла поставочный)");
            if (catalogExpect >= 0)
            {
                Ok(n == catalogExpect, string.Format("у RC-103 в каталоге приложения {0} точек (ждали {1})", n, catalogExpect));
            }
        }

        static void Controls(string file, int expect)
        {
            string scratch = Path.Combine(Path.GetTempPath(), "bq_f9_a221_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(scratch);
            try
            {
                string text = File.ReadAllText(file, Encoding.UTF8);
                int open = text.IndexOf("<DoseRateCalibrationPoints>", StringComparison.Ordinal);
                int close = text.IndexOf("</DoseRateCalibrationPoints>", StringComparison.Ordinal);
                Ok(open >= 0 && close > open, "в честном файле блок точек найден текстом");
                if (open < 0 || close < open) return;

                // (а) блок снят целиком — как у остальных девяти поставочных.
                string none = text.Substring(0, open) + "<DoseRateCalibrationPoints />"
                              + text.Substring(close + "</DoseRateCalibrationPoints>".Length);
                string pNone = Path.Combine(scratch, "none.xml");
                File.WriteAllText(pNone, none, Encoding.UTF8);
                int nNone = Points(Read(pNone)).Count;
                Ok(nNone == 0, string.Format("блок снят: читатель видит {0} точек (ждали 0)", nNone));

                // (б) одна точка вынута.
                int p1 = text.IndexOf("<DoseRateCalibrationPoint>", open, StringComparison.Ordinal);
                int p1e = text.IndexOf("</DoseRateCalibrationPoint>", p1, StringComparison.Ordinal) + "</DoseRateCalibrationPoint>".Length;
                string minusOne = text.Substring(0, p1) + text.Substring(p1e);
                string pMinus = Path.Combine(scratch, "minus_one.xml");
                File.WriteAllText(pMinus, minusOne, Encoding.UTF8);
                int nMinus = Points(Read(pMinus)).Count;
                Ok(nMinus == expect - 1, string.Format("одна точка вынута: читатель видит {0} (ждали {1})", nMinus, expect - 1));

                // (в) в число вписан текст — разбор обязан ОТКАЗАТЬ, а не дать 0.
                string broken = text.Substring(0, open) + text.Substring(open).Replace("<LowerBound>0</LowerBound>", "<LowerBound>ноль</LowerBound>");
                Ok(broken != text, "порча (в) реально внесена в текст");
                string pBroken = Path.Combine(scratch, "broken.xml");
                File.WriteAllText(pBroken, broken, Encoding.UTF8);
                checks++;
                try
                {
                    int nBroken = Points(Read(pBroken)).Count;
                    failed++;
                    Console.WriteLine("  ПРОВАЛ порченое число: прочиталось МОЛЧА, {0} точек", nBroken);
                }
                catch (InvalidOperationException ex)
                {
                    Console.WriteLine("  ok   порченое число: разбор отказал — {0}", Short(ex));
                }

                // Честный файл на том же читателе — чтобы контроль не был односторонним.
                int nGood = Points(Read(file)).Count;
                Ok(nGood == expect, string.Format("честный файл тем же читателем: {0} точек", nGood));
            }
            finally
            {
                try { Directory.Delete(scratch, true); } catch { Console.WriteLine("  !! не убран " + scratch); }
            }
        }

        static string Short(Exception ex)
        {
            Exception e = ex;
            while (e.InnerException != null) e = e.InnerException;
            string m = e.Message.Replace("\r", " ").Replace("\n", " ");
            return m.Length > 120 ? m.Substring(0, 120) + "…" : m;
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

        // Корень дерева — вверх от каталога exe до `BecquerelMonitor.sln`.
        static string FindRepo()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 8 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "BecquerelMonitor.sln"))) return dir;
                dir = Path.GetDirectoryName(dir.TrimEnd('\\'));
            }
            return null;
        }
    }
}
