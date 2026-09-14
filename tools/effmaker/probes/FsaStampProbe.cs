using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml.Serialization;

namespace FsaStampProbe
{
    /// <summary>
    /// Читатель отпечатка разложения (`A31`).
    ///
    /// ЗАЧЕМ ПРОБА. Разложение пересчитывается, только когда сменился ОТПЕЧАТОК
    /// входных данных (<c>FsaAnalysisSession.BuildStamp</c>). Набора нуклидов в нём не
    /// было, и правка набора на экране не меняла ничего: состав библиотеки идёт
    /// от подписей пиков, подписи ставит набор, а пересчитывать их после правки
    /// было некому. Признак починен — здесь у него появляется читатель, иначе
    /// он снова станет неотличим от «оно и так работало».
    ///
    ///     fsastampprobe --spectrum=X.xml
    ///
    /// Проверяется ТРИ вещи:
    ///
    ///   1. смена ВЫБОРА набора (все нуклиды → набор) меняет отпечаток;
    ///   2. правка СОСТАВА набора (нуклид добавлен в него) меняет отпечаток;
    ///   3. возврат состава назад возвращает и отпечаток — то есть отпечаток
    ///      следит за содержанием, а не просто «что-то трогали».
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ».
    /// </summary>
    static class Program
    {
        static int bad;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string spectrumPath = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal))
                {
                    spectrumPath = a.Substring(11);
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            if (spectrumPath == null)
            {
                Console.Error.WriteLine("нужен --spectrum=<файл>");
                return 2;
            }

            // (`A145`, критерий 7 / `A170`) ОТПЕЧАТОК НАСТРОЕК РАСЧЁТА — до окон
            // и до спектра: семь двоичных настроек дают 128 раскладок, и все
            // 128 отпечатков обязаны быть РАЗНЫМИ, а переключение любой одной
            // настройки — менять отпечаток. Сеанс разбора кладёт эту строку в
            // общий отпечаток (`FsaAnalysisSession.BuildStamp`); что она туда
            // доезжает целиком, меряет `FsaSessionProbe` на настоящем спектре.
            OptionsStampSection();

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();

            ResultData rd = Load(spectrumPath);
            if (rd == null)
            {
                return 2;
            }

            if (nuclides.NuclideSets == null || nuclides.NuclideSets.Count == 0)
            {
                Console.Error.WriteLine("в конфигурации нет ни одного набора — проверять нечего");
                return 2;
            }

            // (`A145`, этап 2) Отпечаток строит сеанс разбора, и строит
            // ОТКРЫТО: прежде это был закрытый метод `FsaOverlay`, и проба
            // звала его отражением.
            Func<string> stamp = () => FsaAnalysisSession.BuildStamp(rd, true);

            nuclides.ActiveSet = null;
            string all = stamp();
            Console.WriteLine("все нуклиды      : {0}", Tail(all));

            NuclideSet set = nuclides.NuclideSets[0];
            nuclides.ActiveSet = set;
            string chosen = stamp();
            Console.WriteLine("набор «{0}»: {1}", set.Name, Tail(chosen));
            Check("выбор набора меняет отпечаток", all != chosen);

            // Правка СОСТАВА: берётся нуклид, которого в наборе ещё нет.
            NuclideDefinition victim = null;
            foreach (NuclideDefinition definition in nuclides.NuclideDefinitions)
            {
                if (definition != null && definition.Sets != null && !definition.Sets.Contains(set.Id))
                {
                    victim = definition;
                    break;
                }
            }

            if (victim == null)
            {
                Console.Error.WriteLine("в наборе уже все нуклиды — правку состава проверить нечем");
                return 2;
            }

            victim.Sets.Add(set.Id);
            string edited = stamp();
            Console.WriteLine("+ «{0}»       : {1}", victim.Name, Tail(edited));
            Check("правка состава набора меняет отпечаток", chosen != edited);

            victim.Sets.Remove(set.Id);
            string restored = stamp();
            Check("возврат состава возвращает отпечаток", chosen == restored);

            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "РАСХОЖДЕНИЙ: " + bad.ToString(CultureInfo.InvariantCulture));
            return bad == 0 ? 0 : 1;
        }

        /// <summary>
        /// (`A170`) Все 128 раскладок семи настроек расчёта — 128 разных
        /// отпечатков; каждая одиночная перестановка меняет отпечаток.
        /// </summary>
        static void OptionsStampSection()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int collisions = 0, unchanged = 0, layouts = 0;
            for (int mask = 0; mask < 128; mask++)
            {
                FsaCalculationOptions options = OptionsOf(mask);
                string stamp = options.Stamp;
                layouts++;
                if (!seen.Add(stamp))
                {
                    collisions++;
                    Console.WriteLine("  ⛔ два разных положения дали один отпечаток: {0}", stamp);
                }

                for (int bit = 0; bit < 7; bit++)
                {
                    if (OptionsOf(mask ^ (1 << bit)).Stamp == stamp)
                    {
                        unchanged++;
                        Console.WriteLine("  ⛔ перестановка настройки {0} не меняет отпечаток {1}", bit, stamp);
                    }
                }
            }

            Console.WriteLine("настройки расчёта: раскладок {0}, разных отпечатков {1}, пример: {2}",
                              layouts, seen.Count, OptionsOf(0).Stamp);
            Check("128 раскладок настроек — 128 разных отпечатков", collisions == 0 && seen.Count == 128);
            Check("переключение любого расчётного флага меняет отпечаток", unchanged == 0);
        }

        static FsaCalculationOptions OptionsOf(int mask)
        {
            return new FsaCalculationOptions
            {
                DbLookups = (mask & 1) != 0,
                ChainEquilibrium = (mask & 2) != 0,
                AtomicXray = (mask & 4) != 0,
                CascadeSumming = (mask & 8) != 0,
                Backscatter = (mask & 16) != 0,
                EscapeAndAnnihilation = (mask & 32) != 0,
                PileUp = (mask & 64) != 0
            };
        }

        /// <summary>Хвост отпечатка — набор и пики; начало у всех одинаково.</summary>
        static string Tail(string stamp)
        {
            if (string.IsNullOrEmpty(stamp))
            {
                return "(пусто)";
            }

            string[] parts = stamp.Split('|');
            return parts.Length >= 2
                ? parts[parts.Length - 2]
                : stamp;
        }

        static void Check(string what, bool ok)
        {
            Console.WriteLine("{0}: {1}", ok ? "СОШЛОСЬ " : "РАЗОШЛОСЬ", what);
            if (!ok)
            {
                bad++;
            }
        }

        static ResultData Load(string path)
        {
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }

            if (file.ResultDataList == null || file.ResultDataList.Count == 0)
            {
                Console.Error.WriteLine("в файле нет ни одного результата");
                return null;
            }

            ResultData rd = file.ResultDataList[0];
            ProbeDeviceConfig.Attach(rd);
            return rd;
        }
    }
}
