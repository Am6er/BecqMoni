using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace ZeroTraceProbeP190
{
    /// <summary>
    /// (`S205`, П190) Трасса НУЛЯ СЪЁМКИ: гонит `CorpusFsaProbe.exe` (лежит
    /// рядом), загруженный отражением, с теми же ключами, и на время разбора
    /// ставит приёмник `FsaAnalyzer.ZeroTraceSink`. Каждая строка трассы идёт
    /// в stdout с префиксом `ZT\t` — между строкой пробы «полоса ПЕРВОГО
    /// разбора (СПЕКТР)» и строкой итога этого спектра; проба спектры гонит
    /// по одному, так что трасса ложится под свой спектр без перемешивания.
    ///
    ///     zerotraceprobep190 [--exe=&lt;проба рядом&gt;] &lt;ключи пробы&gt;
    ///
    /// `--exe=` (первым ключом) — другая безоконная проба того же каталога,
    /// например `FsaStackShot.exe` витрины (`tools/fsa_showcase/wd`): её
    /// разбор идёт тем же приёмником; умолчание — `CorpusFsaProbe.exe`.
    ///
    /// Запускать ИЗ каталога оснастки (как и саму пробу — `run_appwd.ps1`),
    /// иначе проба не найдёт `config\`. Код возврата — код пробы; ничего не
    /// пишет, кроме того, что пишет сама проба в свой `--out=`.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

            string here = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string exe = "CorpusFsaProbe.exe";
            if (args.Length > 0 && args[0].StartsWith("--exe=", StringComparison.Ordinal))
            {
                exe = args[0].Substring(6);
                string[] rest = new string[args.Length - 1];
                Array.Copy(args, 1, rest, 0, rest.Length);
                args = rest;
            }

            string probePath = Path.Combine(here, exe);
            if (!File.Exists(probePath))
            {
                Console.Error.WriteLine("нет {0}", probePath);
                return 2;
            }

            object gate = new object();
            FsaAnalyzer.ZeroTraceSink = line =>
            {
                lock (gate)
                {
                    Console.Out.WriteLine("ZT\t" + line);
                }
            };

            try
            {
                Assembly probe = Assembly.LoadFrom(probePath);
                object result = probe.EntryPoint.Invoke(null, new object[] { args });
                return result is int ? (int)result : 0;
            }
            finally
            {
                FsaAnalyzer.ZeroTraceSink = null;
            }
        }
    }
}
