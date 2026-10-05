using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CsvChannelsProbeP174
{
    /// <summary>
    /// (`S200`, полоса П174 28.09.2026) ДВЕРИ «CSV» ПРИ ФАЙЛЕ С ДРУГИМ ЧИСЛОМ
    /// КАНАЛОВ, ЧЕМ У ДОКУМЕНТА. Сочиняет файлы «CSV с энергиями»
    /// (`ImportCsvEnergyToDocument`, шапка «Energy,Count #0d0h10m0s», центры по
    /// полиному) и «CSV со счётом» (`ImportCsvToDocument`, «Channel,Counts
    /// (TotalTime=600s)») на 16384, 1024 и 512 строк, импортирует каждый в СВЕЖИЙ
    /// документ при снятой и поднятой настройке «Import spectrum with empty
    /// config» и печатает: каналов у документа, Σ отсчётов файла и документа,
    /// TotalPulseCount / ValidPulseCount, E(последний канал) против полинома.
    ///
    /// Код 1 — хоть одна строка потеряла отсчёты молча (Σ документа ≠ Σ файла),
    /// то есть ровно дефект `S200`.
    ///
    ///   CsvChannelsProbeP174 --dir=&lt;каталог для файлов&gt;
    /// </summary>
    static class Program
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static int lost = 0;

        [STAThread]
        static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string dir = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--dir=", StringComparison.Ordinal)) dir = a.Substring(6);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (string.IsNullOrEmpty(dir))
            {
                Console.Error.WriteLine("нужен --dir=<каталог>");
                return 2;
            }

            Directory.CreateDirectory(dir);
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();
            string exe = typeof(DocumentManager).Assembly.Location;
            Console.WriteLine("=== СБОРКА === " + exe + "  ("
                              + File.GetLastWriteTime(exe).ToString("yyyy-MM-dd HH:mm:ss", Inv) + ")");
            Console.WriteLine("документ по умолчанию: каналов " + new DocEnergySpectrum().ActiveResultData.EnergySpectrum.NumberOfChannels.ToString(Inv));
            Console.WriteLine();

            GlobalConfigInfo config = GlobalConfigManager.GetInstance().GlobalConfig;
            bool saved = config.ImportSpectrumWithEmptyConfig;
            try
            {
                foreach (bool empty in new[] { false, true })
                {
                    config.ImportSpectrumWithEmptyConfig = empty;
                    Console.WriteLine("--- Import spectrum with empty config = " + (empty ? "ВКЛ" : "ВЫКЛ"));
                    foreach (int n in new[] { 16384, 1024, 512 })
                    {
                        One("csve", Write(dir, "csve", n), n);
                        One("csv", Write(dir, "csv", n), n);
                    }

                    Console.WriteLine();
                }
            }
            finally
            {
                config.ImportSpectrumWithEmptyConfig = saved;
            }

            Console.WriteLine(lost == 0 ? "ИТОГ: отсчёты не теряются ни одной дверью" : "ИТОГ: ПОТЕРЯ ОТСЧЁТОВ МОЛЧА — строк " + lost.ToString(Inv));
            return lost == 0 ? 0 : 1;
        }

        static double Poly(int k, int n)
        {
            // шкала 0…3000 кэВ на любом числе каналов
            return 1.0 + 3000.0 * k / n + 1.0e-6 * k * 3000.0 / n;
        }

        static int Count(int k)
        {
            return 50 + (k * 7919) % 23;
        }

        static string Write(string dir, string door, int n)
        {
            string path = Path.Combine(dir, "p174_" + door + "_" + n.ToString(Inv) + ".csv");
            var sb = new StringBuilder(door == "csve" ? "Energy,Count #0d0h10m0s\r\n" : "Channel,Counts (TotalTime=600s)\r\n");
            for (int k = 0; k < n; k++)
            {
                if (door == "csve") sb.Append(Poly(k, n).ToString("R", Inv));
                else sb.Append(k.ToString(Inv));
                sb.Append(',').Append(Count(k).ToString(Inv)).Append("\r\n");
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            return path;
        }

        static void One(string door, string path, int n)
        {
            long fileSum = 0;
            for (int k = 0; k < n; k++) fileSum += Count(k);
            DocEnergySpectrum doc = new DocEnergySpectrum();
            int before = doc.ActiveResultData.EnergySpectrum.NumberOfChannels;
            TextWriter realErr = Console.Error;
            var err = new StringWriter();
            Console.SetError(err);
            string refusal = null;
            try
            {
                if (door == "csve") DocumentManager.GetInstance().ImportCsvEnergyToDocument(doc, 600, path);
                else DocumentManager.GetInstance().ImportCsvToDocument(doc, 600, path);
            }
            catch (Exception ex)
            {
                refusal = ex.GetType().Name + ": " + ex.Message.Replace("\r", " ").Replace("\n", " ");
            }
            finally
            {
                Console.SetError(realErr);
            }

            EnergySpectrum es = doc.ActiveResultData.EnergySpectrum;
            long docSum = 0;
            for (int i = 0; i < es.NumberOfChannels; i++) docSum += es.Spectrum[i];
            string said = err.ToString().Replace("\r", " ").Replace("\n", " ").Trim();
            if (said.Length > 160) said = said.Substring(0, 160) + "…";
            string eLast = es.EnergyCalibration != null
                ? es.EnergyCalibration.ChannelToEnergy(es.NumberOfChannels - 1).ToString("F2", Inv) : "—";
            bool silentLoss = refusal == null && docSum != fileSum && said.Length == 0;
            bool loss = refusal == null && docSum != fileSum;
            if (loss) lost++;
            Console.WriteLine("{0,-5} строк {1,6}: каналов {2,5} → {3,5}; Σ файла {4,9}, Σ документа {5,9}, Total {6,9}, Valid {7,9}; E(посл.) {8} (полином {9}){10}{11}",
                              door, n, before, es.NumberOfChannels, fileSum, docSum, es.TotalPulseCount, es.ValidPulseCount,
                              eLast, door == "csve" ? Poly(n - 1, n).ToString("F2", Inv) : "—",
                              loss ? (silentLoss ? "  ⛔ ПОТЕРЯ МОЛЧА" : "  ⛔ ПОТЕРЯ") : "",
                              refusal != null ? "  ОТКАЗ: " + refusal : (said.Length > 0 ? "  сказано: " + said : ""));
        }
    }
}
