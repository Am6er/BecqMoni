using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace FsaP42ArmProbe
{
    /// <summary>
    /// (`AMBER22`, П42 13.09.2026) Плечи абляции на СЦЕНЕ AMBER для ключей, которых
    /// у чужой пробы `FsaStackShot` нет: суммирование каскада и маска каналов
    /// матрицы. Разбор гонит САМ `FsaStackShot.exe` (лежит рядом) отражением,
    /// как `FsaNnlsDumpProbe` (`A308`): сцена, библиотека, матрица, отчёт — те же,
    /// что видит человек. Ключи ставятся хуком <see cref="FsaAnalyzer.ProbeSetup"/>
    /// на каждый свежесозданный анализатор; после — хук снимается.
    ///
    ///     fsap42armprobe [--cascade=off] [--channels=peak|<битовая маска>] -- <ключи FsaStackShot>
    ///
    /// `--cascade=off` — `CascadeSumming = CascadeSumPeaks = false` (как `--no-cascade`
    /// у `CorpusFsaProbe`); `--channels=peak` — в образы кладётся только канал
    /// полного поглощения матрицы (континуум, вылеты сняты; пиковая эффективность
    /// матрицы и суммирование остаются). Без ключей — умолчания анализатора, и
    /// дамп обязан выйти побитово равным прямому запуску `FsaStackShot` (контроль).
    /// Печатает, сколько анализаторов создано и что им поставлено.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

            bool cascade = true;
            int mask = -1;
            var rest = new List<string>();
            bool passthrough = false;
            foreach (string a in args)
            {
                if (passthrough)
                {
                    rest.Add(a);
                }
                else if (a == "--")
                {
                    passthrough = true;
                }
                else if (a == "--cascade=off")
                {
                    cascade = false;
                }
                else if (a == "--cascade=on")
                {
                    cascade = true;
                }
                else if (a.StartsWith("--channels=", StringComparison.Ordinal))
                {
                    string v = a.Substring(11);
                    if (v == "peak")
                    {
                        mask = 1 << (int)BecquerelMonitor.EfficiencyMaker.EfficiencySimulator.ResponseChannel.Peak;
                    }
                    else if (v == "all")
                    {
                        mask = -1;
                    }
                    else if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out mask))
                    {
                        Console.Error.WriteLine("неизвестное значение --channels=: " + v + " (peak | all | <маска>)");
                        return 2;
                    }
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a + " (ключи FsaStackShot — после `--`)");
                    return 2;
                }
            }

            if (rest.Count == 0)
            {
                Console.Error.WriteLine("нужны ключи FsaStackShot после `--`");
                return 2;
            }

            string probeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string shotPath = Path.Combine(probeDir, "FsaStackShot.exe");
            if (!File.Exists(shotPath))
            {
                Console.Error.WriteLine("рядом нет FsaStackShot.exe: " + shotPath);
                return 3;
            }

            int created = 0;
            // Хук ставится ДО загрузки чужой сборки: BecquerelMonitor.exe тем самым уже
            // поднят в основном контексте, и FsaStackShot.exe (LoadFrom) привяжется к ТОЙ
            // ЖЕ сборке — статика одна на двоих (довод FsaNnlsDumpProbe).
            FsaAnalyzer.ProbeSetup = analyzer =>
            {
                created++;
                if (!cascade)
                {
                    analyzer.CascadeSumming = false;
                    analyzer.CascadeSumPeaks = false;
                }

                analyzer.MatrixChannelMask = mask;
            };

            int code;
            try
            {
                Assembly shot = Assembly.LoadFrom(shotPath);
                object result = shot.EntryPoint.Invoke(null, new object[] { rest.ToArray() });
                code = result is int ? (int)result : 0;
            }
            catch (TargetInvocationException ex)
            {
                Console.Error.WriteLine("FsaStackShot упал: " + ex.InnerException);
                code = 4;
            }
            finally
            {
                FsaAnalyzer.ProbeSetup = null;
            }

            Console.WriteLine("p42-arm: анализаторов создано {0}, суммирование {1}, маска каналов {2}, код снимка {3}",
                              created, cascade ? "вкл" : "ВЫКЛ", mask == -1 ? "все" : mask.ToString(CultureInfo.InvariantCulture), code);
            return code;
        }
    }
}
