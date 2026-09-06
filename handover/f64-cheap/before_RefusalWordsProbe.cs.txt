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
    ///     калибровки, а <c>FsaOverlay</c> (ныне <c>FsaAnalysisSession</c>) глушил это в «Полноспектральное
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
    ///     refusalwordsprobe [--arm=matdb|clone|fsa|all] [--control=empty]
    ///
    /// Плечо <c>matdb</c> требует, чтобы рядом с пробой лежала ИСПОРЧЕННАЯ
    /// <c>matdb.sqlite</c> (база берётся из каталога сборки, см.
    /// <c>MaterialDatabase.DatabasePath</c>) — каталог готовит вызывающий.
    /// Плечи <c>clone</c> и <c>fsa</c> базы не трогают вовсе.
    ///
    /// Ожидание на новой сборке: «ВСЕ СОШЛИСЬ», код возврата 0.
    ///
    /// ⛔ «ПЛЕЧО ОТКАЗАЛО» И «ПЛЕЧО НЕ ЗАПУСТИЛОСЬ» — РАЗНЫЕ ИСХОДЫ (`T150`,
    /// 05.09.2026). Плечо <c>fsa</c> поднимало <c>DeviceConfigManager</c> ДО
    /// всякого перехвата, а тот без <c>config\device</c> и без окон законно
    /// бросает (`A90`): проба умирала кодом −532462766 со стеком на экране, и
    /// отказ СРЕДЫ выглядел как отказ ПРОБЫ. Измерено 04.09.2026 (`A129`) и
    /// 05.09.2026 на голом каталоге проб — одинаково.
    ///
    /// ⚠ ПОСЫЛКА СТРОКИ `T150` («плечу нужен <c>config\device</c>») УЖЕ
    /// РЕАЛЬНОСТИ, и это установлено поиском, а не мнением: во всём
    /// <c>FullSpectrumAnalysis\*.cs</c>, в <c>EnergySpectrum.cs</c> и в
    /// <c>ResultData.cs</c> единственный вызов <c>GetInstance()</c> — это
    /// <c>NuclideDefinitionManager</c> (<c>FsaOverlay.cs:251</c>, <c>:583</c>).
    /// Ни <c>DeviceConfigManager</c>, ни <c>GlobalConfigManager</c> дверь
    /// разложения не трогает; требование <c>config\device</c> проба выдумала
    /// себе сама. Поэтому под перехватом поднимается РОВНО ТО, что дверь может
    /// тронуть, — библиотека нуклидов, — и среда, в которой плечо не
    /// запускается, это каталог без <c>config\NuclideDefinition.xml</c>, а не
    /// без <c>config\device</c>. Отказ подготовки печатается словами «ПЛЕЧО НЕ
    /// ЗАПУСТИЛОСЬ» с ОТДЕЛЬНЫМ кодом:
    ///
    ///   0 — все плечи запустились и у всех «ПРИЧИНА НАЗВАНА»;
    ///   1 — все запустились, хоть у одного «ПРИЧИНА МОЛЧОК» (дефект ПРИЛОЖЕНИЯ);
    ///   2 — неизвестный ключ;
    ///   3 — хоть одно плечо НЕ ЗАПУСТИЛОСЬ (дефект СРЕДЫ: нет <c>config\</c>,
    ///       менеджер не поднялся) — вердикта по приложению у такого прогона НЕТ.
    ///
    /// Положительный контроль плеча <c>fsa</c> — ключ <c>--control=empty</c>:
    /// у спектра СНИМАЕТСЯ массив отсчётов (<c>Spectrum = null</c>), дверь
    /// разложения на нём НЕ отказывает (сторож в начале <c>EnsureUpToDate</c>
    /// отсекает его раньше, чем дело дойдёт до снимка), и плечо обязано сказать
    /// «РАЗЛОЖЕНИЕ НЕ ОТКАЗАЛО — плечо ничего не мерит» кодом 1. Плечо, которое
    /// на этом входе даёт «ПРИЧИНА НАЗВАНА», не мерит ничего. Ключ относится
    /// только к плечу <c>fsa</c>: с другим <c>--arm=</c> — отказ кодом 2.
    /// </summary>
    static class Program
    {
        static int bad;
        static int notStarted;
        static string control = "";

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
                else if (a == "--control=empty")
                {
                    control = "empty";
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            if (control.Length > 0 && arm != "all" && arm != "fsa")
            {
                Console.Error.WriteLine("--control=" + control + " относится к плечу fsa, а выбрано --arm=" + arm
                                        + ": контроль, который ни к чему не приложен, мерит пустоту");
                return 2;
            }

            Console.WriteLine("=== слова отказа (A89, A95) ===");
            Console.WriteLine("каталог сборки: {0}", AppDomain.CurrentDomain.BaseDirectory);
            if (control.Length > 0)
            {
                Console.WriteLine("контроль      : {0} (плечо fsa обязано сказать «не мерит» и вернуть 1)", control);
            }
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
            if (notStarted > 0)
            {
                // Вердикта по приложению НЕТ: плечо, которое не запустилось,
                // ничего не измерило, и «НЕ СОШЛОСЬ» здесь было бы ложью о
                // приложении. Код отдельный — читатель отличит среду от дефекта.
                Console.WriteLine("НЕ ЗАПУСТИЛИСЬ: {0} (отказ СРЕДЫ, а не приложения; см. слова выше){1}",
                                  notStarted, bad > 0 ? "; у запустившихся НЕ СОШЛОСЬ: " + bad : "");
                return 3;
            }

            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        /// <summary>
        /// Подготовка плеча под перехватом (`T150`): всё, что плечо поднимает ДО
        /// замера, — менеджеры, конфиги — может отказать по вине СРЕДЫ, и такой
        /// отказ обязан быть назван как «не запустилось», а не уронить пробу
        /// стеком и кодом −532462766. Причина печатается словами
        /// <see cref="AppUi.Reason"/> — той же дверью, что у приложения.
        /// </summary>
        static bool Prepare(string arm, string what, Action warmUp)
        {
            try
            {
                warmUp();
                Console.WriteLine("  подготовка       : {0} — поднялось", what);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  ПЛЕЧО НЕ ЗАПУСТИЛОСЬ ({0}): не поднялось {1}", arm, what);
                Console.WriteLine("  причина          : {0}", OneLine(AppUi.Reason(ex)));
                Console.WriteLine("  ИТОГ {0}: НЕ ЗАПУСТИЛОСЬ — это отказ среды (каталог проб), вердикта по приложению нет", arm);
                Console.WriteLine();
                notStarted++;
                return false;
            }
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
            Console.WriteLine("ПЛЕЧО fsa — FsaAnalysisSession.EnsureUpToDate на том же спектре");

            // Поднимается ДО подмены потока ошибок и ПОД ПЕРЕХВАТОМ (`T150`)
            // ровно то, что дверь разложения может тронуть, — библиотека
            // нуклидов (`FsaOverlay.Launch`, `NuclideDefinitionManager`): её
            // заверение (`AppUi.Note`) не должно попасть в замеряемое, а её
            // отказ без `config\NuclideDefinition.xml` — это отказ СРЕДЫ, и
            // выглядеть отказом пробы он не должен. `DeviceConfigManager` и
            // `GlobalConfigManager` здесь БОЛЬШЕ НЕ поднимаются: дверь их не
            // зовёт (см. шапку), а требовать `config\device` от плеча, которому
            // он не нужен, значило ронять пробу на каждом голом каталоге.
            if (!Prepare("fsa", "библиотека нуклидов (NuclideDefinitionManager, config\\NuclideDefinition.xml)",
                         () => NuclideDefinitionManager.GetInstance()))
            {
                return;
            }

            ResultData data = new ResultData();
            data.EnergySpectrum = MakeSpectrumWithoutCalibration();
            if (control == "empty")
            {
                // Положительный контроль: без отсчётов дверь молчит ЗАКОННО, а
                // плечо обязано это заметить и сказать «не мерит» — иначе
                // «ПРИЧИНА НАЗВАНА» ниже ничего не значило бы.
                data.EnergySpectrum.Spectrum = null;
                Console.WriteLine("  контроль         : у спектра снят массив отсчётов (Spectrum = null)");
            }

            // (`A145`, этап 2) Дверь разложения переехала из `FsaOverlay` в
            // сеанс документа; подпись `EnsureUpToDate` та же.
            FsaAnalysisSession overlay = new FsaAnalysisSession();
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
        /// снимок. Отсчёты заполняются: <c>Spectrum == null</c> отсекается
        /// сторожем <c>EnsureUpToDate</c> раньше, и плечо не дошло бы до отказа
        /// — на этом и стоит контроль <c>--control=empty</c>.
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
