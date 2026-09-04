using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace RefusalWordsProbe
{
    /// <summary>
    /// ЧИТАТЕЛЬ СЛОВ ОТКАЗА (`A89`, `A95`).
    ///
    /// ЗАЧЕМ ПРОБА. Оба дефекта одного разряда: отказ ЕСТЬ, а слов у него нет,
    /// и по тому, что видит человек, нельзя сделать ничего.
    ///
    ///   * `A89` — <c>MaterialDatabase.Load()</c> называл файл ТОЛЬКО в ветке
    ///     «файла нет». Битая <c>matdb.sqlite</c> (или отсутствующий
    ///     <c>&lt;проба&gt;.exe.config</c>, из-за которого не поднимается
    ///     поставщик SQLite) убивала расчёт словами «SQLite Error 26: 'file is
    ///     not a database'» — ни имени файла, ни пути, ни класса причины.
    ///     Потребителей у этого отказа десять, и ни один его не перехватывает.
    ///   * `A95` — <c>EnergySpectrum.Clone()</c> падал пустым
    ///     <c>NullReferenceException</c>, когда у спектра нет энергетической
    ///     калибровки, а <c>FsaOverlay</c> глушил это в «Полноспектральное
    ///     разложение не удалось».
    ///
    /// ⛔ ПРИЗНАК ПРОВЕРЯЕТСЯ МАШИННО, А НЕ ГЛАЗАМИ. У каждого плеча одна и та
    /// же мерка: в СКАЗАННОМ приложением обязаны стоять
    ///   (1) имя вещи, об которую споткнулись (файл <c>matdb.sqlite</c> с полным
    ///       путём — либо место и имя недостающего поля),
    ///   (2) ИМЯ КЛАССА исключения-причины.
    /// Оба есть — «ПРИЧИНА НАЗВАНА»; хоть одного нет — «ПРИЧИНА МОЛЧОК».
    ///
    /// ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ОБЯЗАТЕЛЕН: та же проба на СТАРОЙ сборке
    /// приложения обязана дать «ПРИЧИНА МОЛЧОК» во всех плечах. Опыт без него
    /// мерит пустоту — это уже стоило захода (см. `A95`: обе половины молчали
    /// одинаково, и контроль проходил впустую).
    ///
    ///     refusalwordsprobe [--arm=matdb|clone|fsa|all]
    ///
    /// Плечо <c>matdb</c> требует, чтобы рядом с пробой лежала ИСПОРЧЕННАЯ
    /// <c>matdb.sqlite</c> (база берётся из каталога сборки, см.
    /// <c>MaterialDatabase.DatabasePath</c>) — каталог готовит вызывающий.
    /// Плечи <c>clone</c> и <c>fsa</c> базы не трогают вовсе.
    ///
    /// Ожидание на новой сборке: «ВСЕ СОШЛИСЬ», код возврата 0.
    /// </summary>
    static class Program
    {
        static int bad;

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string arm = "all";
            foreach (string a in args)
            {
                if (a.StartsWith("--arm=", StringComparison.Ordinal))
                {
                    arm = a.Substring(6);
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            Console.WriteLine("=== слова отказа (A89, A95) ===");
            Console.WriteLine("каталог сборки: {0}", AppDomain.CurrentDomain.BaseDirectory);
            Console.WriteLine();

            if (arm == "all" || arm == "matdb")
            {
                CheckMaterialDatabase();
            }

            if (arm == "all" || arm == "clone")
            {
                CheckSpectrumClone();
            }

            if (arm == "all" || arm == "fsa")
            {
                CheckFsaDoor();
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------
        // `A89`: база веществ
        // ------------------------------------------------------------------

        static void CheckMaterialDatabase()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "matdb.sqlite");
            Console.WriteLine("ПЛЕЧО matdb — {0}", path);
            Console.WriteLine("  файл на месте : {0}, байт: {1}",
                              File.Exists(path) ? "да" : "НЕТ",
                              File.Exists(path) ? new FileInfo(path).Length.ToString() : "-");
            Console.WriteLine();

            // Три двери в базу, и все три раньше молчали одинаково: общие
            // таблицы (`Load`), оболочечная модель фотоэффекта и кривая
            // световыхода читаются РАЗНЫМИ методами, каждый со своим
            // соединением. Меряются все три — одна названная и две молчащие
            // выглядели бы как «починено».
            One("matdb/таблицы", () => MaterialDatabase.FluorescenceOf(55));
            One("matdb/оболочки", () => MaterialDatabase.PhotoShellOf(55));
            One("matdb/световыход", () => MaterialDatabase.LightYieldOf("CsI:Tl"));
        }

        static void One(string arm, Func<object> call)
        {
            Exception caught;
            try
            {
                object got = call();
                Console.WriteLine("  {0}: ⚠ ОТКАЗА НЕ БЫЛО ВОВСЕ ({1}) — плечо ничего не мерит",
                                  arm, got == null ? "пусто" : "есть ответ");
                Console.WriteLine();
                bad++;
                return;
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            // Класс причины обязан стоять В ТЕКСТЕ: у этого отказа читатель —
            // сам потребитель базы, он видит только сообщение.
            Report(arm, caught,
                   new[] { "matdb.sqlite", AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\') },
                   new[] { "имя файла", "полный путь" }, true);
        }

        // ------------------------------------------------------------------
        // `A95`, половина первая: снимок спектра
        // ------------------------------------------------------------------

        static void CheckSpectrumClone()
        {
            Console.WriteLine("ПЛЕЧО clone — EnergySpectrum.Clone() без энергетической калибровки");
            EnergySpectrum spectrum = MakeSpectrumWithoutCalibration();

            Exception caught = null;
            try
            {
                spectrum.Clone();
                Console.WriteLine("  ⚠ СНИМОК УДАЛСЯ — значит калибровка всё-таки была, плечо не мерит");
                bad++;
                return;
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            // Имени класса в СВОЁМ тексте у прямого броска не бывает и быть не
            // должно: класс приписывает `AppUi.Reason` у того, кто отказ
            // показывает, — это меряет плечо `fsa`. Здесь мерка другая и не
            // слабее: тип обязан быть НАЗВАННЫМ отказом, а не голым
            // `NullReferenceException`, из которого не следует ничего.
            Report("clone", caught,
                   new[] { "EnergySpectrum", "EnergyCalibration" },
                   new[] { "место отказа", "имя недостающего" }, false);
        }

        // ------------------------------------------------------------------
        // `A95`, половина вторая: дверь разложения
        // ------------------------------------------------------------------

        static void CheckFsaDoor()
        {
            Console.WriteLine("ПЛЕЧО fsa — FsaOverlay.EnsureUpToDate на том же спектре");

            // Менеджеры поднимаются ДО подмены потока ошибок: их собственные
            // заверения (`AppUi.Note`) не должны попасть в замеряемое.
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager.GetInstance();

            ResultData data = new ResultData();
            data.EnergySpectrum = MakeSpectrumWithoutCalibration();

            FsaOverlay overlay = new FsaOverlay();
            TextWriter saved = Console.Error;
            StringWriter captured = new StringWriter();
            string status;
            try
            {
                Console.SetError(captured);
                overlay.EnsureUpToDate(data, false);
            }
            finally
            {
                Console.SetError(saved);
            }

            status = overlay.Status;
            string stderr = OneLine(captured.ToString());
            Console.WriteLine("  строка на экране : {0}", status == null ? "(пусто)" : OneLine(status));
            Console.WriteLine("  поток ошибок     : {0}", stderr.Length == 0 ? "(пусто)" : stderr);

            if (string.IsNullOrEmpty(status))
            {
                Console.WriteLine("  ⚠ РАЗЛОЖЕНИЕ НЕ ОТКАЗАЛО — плечо ничего не мерит");
                bad++;
                return;
            }

            bool onScreen = status.IndexOf("EnergyCalibration", StringComparison.Ordinal) >= 0;
            bool headless = stderr.IndexOf("EnergyCalibration", StringComparison.Ordinal) >= 0;
            bool named = status.IndexOf("InvalidOperationException", StringComparison.Ordinal) >= 0;

            Console.WriteLine("  называет недостающее человеку за экраном : {0}", onScreen ? "ДА" : "НЕТ");
            Console.WriteLine("  называет недостающее в потоке ошибок     : {0}", headless ? "ДА" : "НЕТ");
            Console.WriteLine("  называет класс причины                   : {0}", named ? "ДА" : "НЕТ");
            Verdict("fsa", onScreen && headless && named);
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Спектр без энергетической калибровки — ровно то, обо что спотыкался
        /// снимок. Отсчёты заполняются: пустой <c>Spectrum</c> отсекается
        /// сторожем <c>EnsureUpToDate</c> раньше, и плечо не дошло бы до отказа.
        /// </summary>
        static EnergySpectrum MakeSpectrumWithoutCalibration()
        {
            EnergySpectrum spectrum = new EnergySpectrum(1.0, 128);
            for (int i = 0; i < spectrum.Spectrum.Length; i++)
            {
                spectrum.Spectrum[i] = 1;
            }

            spectrum.LiveTime = 1.0;
            spectrum.MeasurementTime = 1.0;
            return spectrum;
        }

        /// <summary>
        /// Одна мерка на все плечи с исключением: печатается ровно то, что
        /// приложение СКАЗАЛО, и в этом тексте ищутся обязательные слова плюс
        /// имя класса причины.
        ///
        /// ⚠ Класс причины ищется по САМОМУ ВНУТРЕННЕМУ исключению, а не по
        /// внешнему: внешнее у старой сборки и есть причина, и его собственное
        /// имя в его же тексте не стоит — именно это и должно давать «МОЛЧОК».
        /// </summary>
        static void Report(string arm, Exception ex, string[] must, string[] labels, bool classInText)
        {
            Exception inner = ex;
            while (inner.InnerException != null)
            {
                inner = inner.InnerException;
            }

            string said = OneLine(ex.Message);
            Console.WriteLine("  брошено  : {0}", ex.GetType().Name);
            Console.WriteLine("  причина  : {0}", inner.GetType().Name);
            Console.WriteLine("  сказано  : {0}", said);

            bool all = true;
            for (int i = 0; i < must.Length; i++)
            {
                bool has = said.IndexOf(must[i], StringComparison.OrdinalIgnoreCase) >= 0;
                Console.WriteLine("  называет {0,-24} : {1}", labels[i], has ? "ДА" : "НЕТ");
                all &= has;
            }

            bool named;
            if (classInText)
            {
                named = said.IndexOf(inner.GetType().Name, StringComparison.Ordinal) >= 0
                        || said.IndexOf(ex.GetType().Name, StringComparison.Ordinal) >= 0;
                Console.WriteLine("  называет {0,-24} : {1}", "класс причины", named ? "ДА" : "НЕТ");
            }
            else
            {
                named = !(ex is NullReferenceException);
                Console.WriteLine("  отказ {0,-27} : {1}", "назван типом", named ? "ДА" : "НЕТ (голый NRE)");
            }

            Verdict(arm, all && named);
        }

        static void Verdict(string arm, bool ok)
        {
            Console.WriteLine("  ИТОГ {0}: {1}", arm, ok ? "ПРИЧИНА НАЗВАНА" : "ПРИЧИНА МОЛЧОК");
            Console.WriteLine();
            if (!ok)
            {
                bad++;
            }
        }

        static string OneLine(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            return text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Trim();
        }
    }
}
