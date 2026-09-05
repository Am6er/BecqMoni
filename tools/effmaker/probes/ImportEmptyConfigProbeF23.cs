using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace ImportEmptyConfigProbeF23
{
    /// <summary>
    /// ⛔ ВВОЗ СПЕКТРА, У КОТОРОГО НЕТ МОДЕЛИ РАЗРЕШЕНИЯ (`A212`), — ВСТРЕЧНЫЙ
    ///    ЗАМЕР ПОЛОСЫ F23, 05.09.2026.
    ///
    /// Строка `A212` к приходу этой полосы уже была закрыта полосой О7
    /// (коммит `6eb823fb`), и настоящая проба — НЕ повторение её пробы, а
    /// ВСТРЕЧНАЯ ПРИЁМКА: свой вход, свой слепок, свой приговор. Смысл в том,
    /// что «сторож стоит в исходнике» проверяется чтением, а «сторож работает»
    /// — только замером, и замер обязан быть чужим.
    ///
    /// Мерится ПЯТЬ плеч на 12 корпусных `.n42`, разрез по трём величинам:
    ///
    ///   состояние      — СПИСОК ПОЛОН / СПИСОК ПУСТ / УМОЛЧАНИЕ НЕ СТРОИТСЯ;
    ///   кем документ   — `new DocEnergySpectrum()` (мимо `CheckDocument`) или
    ///                    `DocumentManager.CreateDocument()` (ровно то, что
    ///                    зовёт пункт меню «Import spectrum file»,
    ///                    `MainForm.cs:2748`);
    ///   какая дверь    — `ImportDocumentSpecUtils` или `ImportDocumentN42`.
    ///
    /// ⛔ ПОЛНЫЕ ПЛЕЧИ — ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ, А НЕ УКРАШЕНИЕ. Без них
    ///    «ввезло 12 из 12 при пустой кривой» неотличимо от «проба ввозит что
    ///    угодно и ничего не мерит».
    ///
    /// ⛔ «НЕ УПАЛО» ≠ «ВВЕЗЛО». Сторож `null` мог бы увести ввоз мимо спектров
    ///    и молча отдать пустой документ. Поэтому каждая строка плеча несёт
    ///    СЛЕПОК (число спектров, каналы, сумма отсчётов, времена), и слепок
    ///    плеча со сломанным умолчанием сверяется ПОСТРОЧНО со слепком полного
    ///    плеча той же двери. Расхождение — отказ пробы.
    ///
    /// ⛔ ШЕСТОЕ ПЛЕЧО — `ResultData.Clone` (`ResultData.cs:470`), второе место
    ///    строки `A212`. Там сторож стоял и до правки: он ЭТАЛОН, по которому
    ///    писан сторож в `DocumentManager`. Плечо доказывает, что эталон
    ///    действительно держит `null`, а не считается держащим по чтению.
    ///
    /// Проба безоконная (входная сборка — не `BecquerelMonitor.exe`), то есть
    /// `AppUi.HasWindows == false`, и голос двери уходит в поток ошибок; он
    /// перехватывается и печатается — именно его увидит человек за экраном
    /// окном `MessageBox`.
    ///
    /// Ключи:
    ///   --n42=&lt;каталог&gt;   — где лежат `.n42` (умолчание: `..\..\..\CORPUS\n42`)
    ///   --modal-control   — положительный контроль СТОРОЖА ОКОН: проба сама
    ///                       поднимает `MessageBox` и обязана кончиться кодом 1.
    /// </summary>
    static class Program
    {
        static string n42Dir = null;
        static bool modalControl = false;
        static int failures = 0;

        [STAThread]
        static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }

            foreach (string a in args)
            {
                if (a.StartsWith("--n42=")) n42Dir = a.Substring(6);
                else if (a == "--modal-control") modalControl = true;
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            // ⛔ Сторож модальных окон — ПЕРВЫМ ДЕЛОМ, до менеджеров-одиночек:
            //    молчащая безоконная проба = модальное окно, и без сторожа
            //    прогон висит до убийства процесса (образец — `CultureProbeO14`,
            //    полоса F20).
            ModalWatchStart();

            // ⛔ Обе карты примитивов ROI — ДО любого менеджера-одиночки (`T60`).
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();

            if (n42Dir == null)
            {
                n42Dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                      @"..\..\..\CORPUS\n42");
            }
            n42Dir = Path.GetFullPath(n42Dir);

            string asm = typeof(DocumentManager).Assembly.Location;
            Console.WriteLine("=== СБОРКА ===");
            Console.WriteLine("  " + asm);
            Console.WriteLine("  собрана " + File.GetLastWriteTime(asm).ToString("yyyy-MM-dd HH:mm:ss"));
            Console.WriteLine("  sha256  " + Sha256(asm));
            Console.WriteLine("  окна у приложения: " + AppUi.HasWindows);
            Console.WriteLine("  каталог .n42: " + n42Dir);
            Console.WriteLine();

            if (modalControl)
            {
                ModalControl();
                ModalWatchStop();
                Console.WriteLine();
                Console.WriteLine("РАСХОЖДЕНИЙ: " + failures
                                  + (failures == 0
                                     ? "  ⛔ КОНТРОЛЬ НЕ СРАБОТАЛ: сторож не засчитал ни одного окна"
                                     : "  (так и надо: это контроль самого сторожа)"));
                return failures == 0 ? 1 : 1;
            }

            if (!Directory.Exists(n42Dir))
            {
                Console.Error.WriteLine("каталога нет: " + n42Dir);
                return 2;
            }
            string[] files = Directory.GetFiles(n42Dir, "*.n42");
            Array.Sort(files, StringComparer.Ordinal);
            if (files.Length == 0)
            {
                Console.Error.WriteLine("в каталоге нет ни одного .n42: " + n42Dir);
                return 2;
            }

            DeviceConfigManager dcm = DeviceConfigManager.GetInstance();
            List<DeviceConfigInfo> saved = new List<DeviceConfigInfo>(dcm.DeviceConfigList);
            Console.WriteLine("  файлов: " + files.Length
                              + ", конфигураций приборов загружено: " + saved.Count);
            Console.WriteLine();
            if (saved.Count == 0)
            {
                Console.Error.WriteLine("⛔ конфигураций приборов НЕТ ВОВСЕ: полные плечи "
                                        + "мерить нечем, положительный контроль невозможен");
                return 2;
            }

            // Слепки поставочных конфигураций ДО прогона: проба правит их ТОЛЬКО
            // в памяти, и это утверждение проверяется, а не заявляется.
            Dictionary<string, string> cfgBefore = ConfigHashes();

            Snap full = null, empty = null, broken = null, brokenN42 = null, fullN42 = null;
            try
            {
                full = Arm("СПИСОК ПОЛОН", "документ приложения", "SpecUtils", files, true);
                fullN42 = Arm("СПИСОК ПОЛОН", "документ приложения", "N42", files, true);

                dcm.DeviceConfigList.Clear();
                Console.WriteLine("--- список конфигураций приборов ОПУСТОШЁН: "
                                  + dcm.DeviceConfigList.Count + " ---");
                Console.WriteLine();
                empty = Arm("СПИСОК ПУСТ", "документ пробы", "SpecUtils", files, true);
                dcm.DeviceConfigList.AddRange(saved);

                // ⛔ СОСТОЯНИЕ, КОТОРОЕ ДОСТАЁТ ЧЕЛОВЕКА ШТАТНЫМ ПУНКТОМ МЕНЮ.
                //    `DefaultCalibration` кладёт прямую через (0, FWHM_AT_0) и
                //    (Ch_Fwhm, Width_Fwhm) и отдаёт null, если она не растёт
                //    (`ResultData.cs:470`). Тогда `CheckDocument` достроить ПШПВ
                //    НЕ МОЖЕТ, и в дверь приходит ровно null.
                //    ⚠ Ломаются ВСЕ конфигурации списка, а не первая: какую из
                //    них возьмёт `new DocEnergySpectrum(...)`, решает глобальная
                //    настройка, и полагаться на порядок нельзя.
                List<Undo> undo = BreakAllDefaults(dcm);
                try
                {
                    broken = Arm("УМОЛЧАНИЕ НЕ СТРОИТСЯ", "документ приложения", "SpecUtils", files, true);
                    brokenN42 = Arm("УМОЛЧАНИЕ НЕ СТРОИТСЯ", "документ приложения", "N42", files, true);
                }
                finally
                {
                    foreach (Undo u in undo) u.Restore();
                }
            }
            finally
            {
                if (dcm.DeviceConfigList.Count == 0) dcm.DeviceConfigList.AddRange(saved);
            }

            // ── ШЕСТОЕ ПЛЕЧО: второе место строки, `ResultData.cs:470` ─────────
            CloneArm();

            // ── СВЕРКА СЛЕПКОВ ────────────────────────────────────────────────
            Console.WriteLine("=== СВЕРКА СЛЕПКОВ: «не упало» обязано значить «ввезло» ===");
            CompareSnaps(broken, full, "УМОЛЧАНИЕ НЕ СТРОИТСЯ · SpecUtils", "СПИСОК ПОЛОН · SpecUtils");
            CompareSnaps(empty, full, "СПИСОК ПУСТ · документ пробы · SpecUtils", "СПИСОК ПОЛОН · SpecUtils");
            CompareSnaps(brokenN42, fullN42, "УМОЛЧАНИЕ НЕ СТРОИТСЯ · N42", "СПИСОК ПОЛОН · N42");
            Console.WriteLine();

            // ── ПОСТАВОЧНЫЕ КОНФИГУРАЦИИ НЕ ТРОНУТЫ ───────────────────────────
            Console.WriteLine("=== ФАЙЛЫ КОНФИГУРАЦИЙ ПОСЛЕ ПРОГОНА ===");
            Dictionary<string, string> cfgAfter = ConfigHashes();
            int moved = 0;
            foreach (KeyValuePair<string, string> kv in cfgBefore)
            {
                string now;
                if (!cfgAfter.TryGetValue(kv.Key, out now) || now != kv.Value)
                {
                    moved++;
                    failures++;
                    Console.WriteLine("  ⛔ ИЗМЕНЁН: " + kv.Key);
                }
            }
            Console.WriteLine("  файлов сверено: " + cfgBefore.Count + ", изменено: " + moved
                              + (moved == 0 ? "  (правка жила только в памяти — так и надо)" : ""));
            Console.WriteLine();

            ModalWatchStop();

            Console.WriteLine("=== ИТОГ ===");
            foreach (string s in armTotals) Console.WriteLine("  " + s);
            Console.WriteLine();
            Console.WriteLine("  модальных окон за прогон: " + modalSeen
                              + (modalSeen == 0 ? "  (безоконный путь чист)" : "  ⛔ ОКНО В БЕЗОКОННОМ ПУТИ"));
            Console.WriteLine("РАСХОЖДЕНИЙ: " + failures);
            return failures == 0 ? 0 : 1;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  ПЛЕЧО
        // ══════════════════════════════════════════════════════════════════════

        static readonly List<string> armTotals = new List<string>();

        sealed class Snap
        {
            public string Head;
            public readonly List<string> Lines = new List<string>();
        }

        /// <summary>
        /// Одно плечо: каждый файл каталога ввозится названной дверью в документ,
        /// созданный названным способом.
        ///
        /// Печатается: состояние кривой ДО двери, приговор, состояние ПОСЛЕ,
        /// слепок чисел и — отдельно — ГОЛОС двери (то, что человек увидит
        /// окном). У отказа берётся кадр САМОГО ГЛУБОКОГО исключения: внешний
        /// `catch` двери заворачивает беду в свой `InvalidOperationException`,
        /// и по нему место настоящего броска не найти вовсе.
        /// </summary>
        static Snap Arm(string state, string docWay, string door, string[] files, bool mustPass)
        {
            string head = state + " | " + docWay + " | дверь " + door;
            Console.WriteLine("=== " + head + " ===");
            Snap snap = new Snap();
            snap.Head = head;

            int ok = 0, bad = 0, nullBefore = 0, nullAfter = 0, spoke = 0;
            Dictionary<string, int> kinds = new Dictionary<string, int>();
            List<string> voices = new List<string>();

            foreach (string f in files)
            {
                string name = Path.GetFileName(f);
                DocEnergySpectrum doc = null;
                string said = null, frame = "";
                string before = "?";

                TextWriter realErr = Console.Error;
                StringWriter caught = new StringWriter();
                Console.SetError(caught);
                try
                {
                    doc = docWay == "документ приложения"
                          ? DocumentManager.GetInstance().CreateDocument(name + ".f23.xml")
                          : new DocEnergySpectrum();
                    if (doc == null) throw new InvalidOperationException("CreateDocument вернул null");

                    before = doc.ActiveResultData == null
                             ? "нет активного спектра"
                             : (doc.ActiveResultData.FwhmCalibration == null ? "ПШПВ null" : "ПШПВ есть");
                    if (doc.ActiveResultData != null && doc.ActiveResultData.FwhmCalibration == null) nullBefore++;

                    if (door == "SpecUtils")
                        DocumentManager.GetInstance().ImportDocumentSpecUtils(doc, f, 3600);
                    else
                        DocumentManager.GetInstance().ImportDocumentN42(doc, f);
                }
                catch (Exception ex)
                {
                    said = ex.GetType().Name + ": " + Flat(ex.Message);
                    frame = DeepestFrame(ex);
                    string kind = ex.GetType().Name + (frame.Length == 0 ? "" : " <- " + frame);
                    int had; kinds.TryGetValue(kind, out had); kinds[kind] = had + 1;
                }
                finally { Console.SetError(realErr); }

                string print = Print(doc, out int gone);
                if (gone > 0) nullAfter++;

                string voice = caught.ToString().Trim();
                if (voice.Length > 0)
                {
                    spoke++;
                    foreach (string line in voice.Split('\n'))
                    {
                        string one = line.Trim();
                        if (one.Length > 0 && !voices.Contains(one)) voices.Add(one);
                    }
                }

                if (said == null)
                {
                    ok++;
                    string row = name + " | ВВЕЗЁН | до: " + before + " | " + print;
                    Console.WriteLine("  " + row);
                    snap.Lines.Add(row);
                }
                else
                {
                    bad++;
                    string row = name + " | ОТКАЗ | до: " + before + " | " + said
                                 + (frame.Length == 0 ? "" : "   [" + frame + "]");
                    Console.WriteLine("  " + row);
                    snap.Lines.Add(row);
                }
            }

            StringBuilder k = new StringBuilder();
            foreach (KeyValuePair<string, int> kv in kinds)
            {
                if (k.Length > 0) k.Append("; ");
                k.Append(kv.Value).Append("× ").Append(kv.Key);
            }
            string total = head + " -> ВВЕЗЕНО " + ok + " / ОТКАЗ " + bad + " (из " + files.Length + ")"
                           + ", ПШПВ null ДО: " + nullBefore + ", спектров без ПШПВ ПОСЛЕ: " + nullAfter
                           + ", дверь сказала слово: " + spoke
                           + (k.Length == 0 ? "" : "   |   " + k);
            Console.WriteLine("  ИТОГ: " + total);
            if (voices.Count > 0)
            {
                Console.WriteLine("  ГОЛОС ДВЕРИ (человек увидит это окном):");
                foreach (string v in voices) Console.WriteLine("    " + v);
            }
            if (mustPass && bad > 0)
            {
                failures += bad;
                Console.WriteLine("  ⛔ ПЛЕЧО ОБЯЗАНО БЫЛО ПРОЙТИ ЦЕЛИКОМ, а отказов " + bad);
            }
            Console.WriteLine();
            armTotals.Add(total);
            return snap;
        }

        /// <summary>Слепок документа: числа, по которым видно, что ввоз состоялся.</summary>
        static string Print(DocEnergySpectrum doc, out int withoutFwhm)
        {
            withoutFwhm = 0;
            if (doc == null || doc.ResultDataFile == null || doc.ResultDataFile.ResultDataList == null)
                return "документа нет";
            int n = 0;
            long sum = 0;
            int channels = 0;
            double live = 0.0, real = 0.0;
            foreach (ResultData rd in doc.ResultDataFile.ResultDataList)
            {
                if (rd == null) continue;
                n++;
                if (rd.FwhmCalibration == null) withoutFwhm++;
                EnergySpectrum es = rd.EnergySpectrum;
                if (es == null) continue;
                if (channels == 0) channels = es.NumberOfChannels;
                live += es.LiveTime;
                real += es.MeasurementTime;
                if (es.Spectrum != null)
                    for (int i = 0; i < es.Spectrum.Length; i++) sum += es.Spectrum[i];
            }
            // ⛔ Числа печатаются ИНВАРИАНТНОЙ культурой: разделитель дробной
            //    части — всегда точка, группировки разрядов нет (правило Amber
            //    05.09.2026). Слепки двух плеч иначе сравнивались бы по-разному
            //    на разных машинах.
            return "спектров " + n.ToString(CultureInfo.InvariantCulture)
                   + ", без ПШПВ " + withoutFwhm.ToString(CultureInfo.InvariantCulture)
                   + ", каналов " + channels.ToString(CultureInfo.InvariantCulture)
                   + ", сумма " + sum.ToString(CultureInfo.InvariantCulture)
                   + ", живое " + live.ToString("F3", CultureInfo.InvariantCulture)
                   + ", полное " + real.ToString("F3", CultureInfo.InvariantCulture);
        }

        static void CompareSnaps(Snap a, Snap b, string nameA, string nameB)
        {
            if (a == null || b == null) { Console.WriteLine("  плечо не снято — сверять нечего"); return; }
            int diff = 0;
            int n = Math.Min(a.Lines.Count, b.Lines.Count);
            for (int i = 0; i < n; i++)
            {
                if (a.Lines[i] == b.Lines[i]) continue;
                // Строка «до:» законно разная (в одном плече ПШПВ есть, в другом
                // нет) — сравниваются ЧИСЛА, то есть хвост от слепка.
                if (Tail(a.Lines[i]) == Tail(b.Lines[i])) continue;
                diff++;
                if (diff <= 3)
                {
                    Console.WriteLine("    ⛔ " + a.Lines[i]);
                    Console.WriteLine("    ⛔ " + b.Lines[i]);
                }
            }
            if (a.Lines.Count != b.Lines.Count) { diff++; Console.WriteLine("    ⛔ разное число строк"); }
            Console.WriteLine("  «" + nameA + "» против «" + nameB + "»: строк " + n
                              + ", расхождений " + diff
                              + (diff == 0 ? "  (числа совпали — ввоз настоящий)" : "  ⛔"));
            failures += diff;
        }

        /// <summary>
        /// Хвост слепка, по которому плечи вообще сравнимы.
        ///
        /// ⛔ Сравнение начинается с «каналов», а не со «спектров», и это не
        ///    послабление. Числа «до: ПШПВ null/есть» и «без ПШПВ N» — это САМО
        ///    ИЗМЕРЯЕМОЕ СОСТОЯНИЕ, они у плеч ОБЯЗАНЫ различаться, иначе плечо
        ///    не мерит. Сверяется же то, что состояние менять НЕ ДОЛЖНО:
        ///    каналы, сумма отсчётов, живое и полное время. Первый заход этой
        ///    пробы сравнивал вместе с состоянием и дал 12 расхождений из 12 —
        ///    отказ был мой, а не приложения.
        /// </summary>
        static string Tail(string row)
        {
            int i = row.IndexOf("каналов ", StringComparison.Ordinal);
            return i < 0 ? row : row.Substring(i);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  ВТОРОЕ МЕСТО СТРОКИ: `ResultData.cs:470`
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ⛔ `ResultData.Clone` при `FwhmCalibration == null` — ЭТАЛОН, а не
        ///    предмет правки: именно с него списан сторож в `DocumentManager`.
        ///    Плечо доказывает замером, что эталон действительно держит `null`
        ///    (клон получается, поле остаётся `null`), а не считается держащим
        ///    по чтению исходника. Заодно проверяется обратное: когда кривая
        ///    ЕСТЬ, клон получает СВОЮ копию, а не ту же ссылку, — иначе сторож
        ///    «держал» бы `null` ценой общей на два спектра кривой.
        /// </summary>
        static void CloneArm()
        {
            Console.WriteLine("=== ВТОРОЕ МЕСТО СТРОКИ: ResultData.Clone при пустой ПШПВ (`ResultData.cs:470`) ===");
            try
            {
                ResultData rd = new ResultData();
                rd.EnergySpectrum = new EnergySpectrum(1, 16);
                // ⚠ Шкала энергии нужна САМОМУ клону спектра: `EnergySpectrum.Clone`
                //    без неё отказывает словами («подставить калибровку значило бы
                //    объявить номер канала энергией»). Первый заход этой пробы её не
                //    ставил и получил отказ НЕ ПРО ПШПВ — плечо мерило не то.
                rd.EnergySpectrum.EnergyCalibration = new PolynomialEnergyCalibration();
                rd.FwhmCalibration = null;
                ResultData copy = rd.Clone();
                bool ok = copy != null && copy.FwhmCalibration == null;
                Console.WriteLine("  ПШПВ null -> клон: " + (copy == null ? "null" : "получен")
                                  + ", ПШПВ клона: " + (copy != null && copy.FwhmCalibration == null ? "null" : "не null")
                                  + "  " + (ok ? "СХОДИТСЯ" : "⛔ РАСХОЖДЕНИЕ"));
                if (!ok) failures++;

                rd.FwhmCalibration = new SimpleSqrtFwhmCalibration();
                ResultData copy2 = rd.Clone();
                bool own = copy2 != null && copy2.FwhmCalibration != null
                           && !object.ReferenceEquals(copy2.FwhmCalibration, rd.FwhmCalibration);
                Console.WriteLine("  ПШПВ есть -> у клона СВОЯ копия: " + (own ? "да, СХОДИТСЯ" : "⛔ РАСХОЖДЕНИЕ"));
                if (!own) failures++;
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine("  ⛔ ОТКАЗ: " + ex.GetType().Name + ": " + Flat(ex.Message)
                                  + "   [" + DeepestFrame(ex) + "]");
            }
            Console.WriteLine();
        }

        // ══════════════════════════════════════════════════════════════════════
        //  СОСТОЯНИЕ «УМОЛЧАНИЕ НЕ СТРОИТСЯ»
        // ══════════════════════════════════════════════════════════════════════

        sealed class Undo
        {
            public FWHMPeakDetectionMethodConfig Cfg;
            public double At0, Width;
            public FwhmCalibration Curve;
            public void Restore()
            {
                Cfg.FWHM_AT_0 = At0;
                Cfg.Width_Fwhm = Width;
                Cfg.FwhmCalibration = Curve;
            }
        }

        static List<Undo> BreakAllDefaults(DeviceConfigManager dcm)
        {
            List<Undo> undo = new List<Undo>();
            int broke = 0, refused = 0;
            string sample = null;
            foreach (DeviceConfigInfo dc in dcm.DeviceConfigList)
            {
                FWHMPeakDetectionMethodConfig cfg = dc.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
                if (cfg == null) continue;
                Undo u = new Undo();
                u.Cfg = cfg; u.At0 = cfg.FWHM_AT_0; u.Width = cfg.Width_Fwhm; u.Curve = cfg.FwhmCalibration;
                undo.Add(u);
                // ⚠ Числа НАРОЧНО дробные (40.5 и 1.25), а не круглые: на круглых
                //    разделитель дробной части в тексте отказа не появляется вовсе,
                //    и правило Amber «разделитель ВСЕГДА ТОЧКА» на этом пути было бы
                //    неизмеримо. Прямая через (0, 40.5) и (Ch_Fwhm, 1.25) по-прежнему
                //    не растёт — плечо мерит то же, что мерило.
                cfg.FWHM_AT_0 = 40.5;
                cfg.Width_Fwhm = 1.25;
                cfg.FwhmCalibration = null;
                broke++;
                string refusal;
                if (FwhmCalibration.DefaultCalibration(cfg, new PolynomialEnergyCalibration(), out refusal) != null) refused++;
                if (sample == null) sample = refusal;
            }
            Console.WriteLine("--- умолчание ПШПВ сломано у " + broke + " конфигураций "
                              + "(ПШПВ у нуля 40.5 > ПШПВ на канале Ch_Fwhm 1.25 — прямая не растёт) ---");
            Console.WriteLine("    проверка: DefaultCalibration отдал кривую вопреки этому у "
                              + refused + " из " + broke
                              + (refused == 0 ? "  (у всех null — плечо мерит)" : "  ⛔ ПЛЕЧО НЕ МЕРИТ"));
            if (refused > 0) failures += refused;

            // ⛔ РАЗДЕЛИТЕЛЬ ДРОБНОЙ ЧАСТИ В САМОМ ТЕКСТЕ ОТКАЗА (правило Amber
            //    05.09.2026, разряд `A242`/`A244`). Причина складывается
            //    `string.Format(CultureInfo.CurrentCulture, …)` в
            //    `FwhmCalibration.cs:113`, то есть числа в ней печатаются
            //    КУЛЬТУРОЙ ПОТОКА. Проба безоконная: подпорки `MainForm.cs:158-160`,
            //    подменяющей разделитель на точку, тут нет — и видно, что печатает
            //    сам код. Замер, а не рассуждение: строка приводится целиком.
            Console.WriteLine("    культура потока: " + Thread.CurrentThread.CurrentCulture.Name
                              + " (разделитель «"
                              + Thread.CurrentThread.CurrentCulture.NumberFormat.NumberDecimalSeparator
                              + "»), подпорки MainForm нет");
            if (sample != null)
            {
                bool comma = sample.Contains("40,5") || sample.Contains("1,25");
                bool dot = sample.Contains("40.5") || sample.Contains("1.25");
                Console.WriteLine("    текст отказа: " + sample);
                Console.WriteLine("    разделитель в тексте: "
                                  + (comma ? "⛔ ЗАПЯТАЯ — правило нарушено" : (dot ? "точка — правило соблюдено" : "чисел не видно")));
            }
            Console.WriteLine();
            return undo;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  СЛУЖЕБНОЕ
        // ══════════════════════════════════════════════════════════════════════

        static Dictionary<string, string> ConfigHashes()
        {
            Dictionary<string, string> map = new Dictionary<string, string>();
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config");
            if (!Directory.Exists(root)) return map;
            foreach (string f in Directory.GetFiles(root, "*.xml", SearchOption.AllDirectories))
                map[f.Substring(root.Length).TrimStart('\\')] = Sha256(f);
            return map;
        }

        static string Sha256(string path)
        {
            try
            {
                using (SHA256 h = SHA256.Create())
                using (FileStream fs = File.OpenRead(path))
                    return BitConverter.ToString(h.ComputeHash(fs)).Replace("-", "").Substring(0, 16);
            }
            catch (Exception) { return "?"; }
        }

        static string Flat(string s)
        {
            if (s == null) return "";
            return s.Replace("\r", " ").Replace("\n", " ").Trim();
        }

        /// <summary>
        /// Место падения — «файл:строка» САМОГО ГЛУБОКОГО исключения, а не
        /// внешнего: `ImportDocumentSpecUtils` ловит любую беду одним `catch` и
        /// без окон бросает свой `InvalidOperationException`, след у которого
        /// начинается со строки ЭТОГО `catch`.
        /// </summary>
        static string DeepestFrame(Exception ex)
        {
            Exception e = ex, deepest = ex;
            while (e != null) { deepest = e; e = e.InnerException; }
            string st = deepest.StackTrace;
            if (string.IsNullOrEmpty(st)) return deepest.GetType().Name;
            foreach (string line in st.Split('\n'))
            {
                if (line.IndexOf(".cs:", StringComparison.OrdinalIgnoreCase) < 0) continue;
                int i = line.LastIndexOf('\\');
                string tail = i < 0 ? line.Trim() : line.Substring(i + 1).Trim();
                return deepest.GetType().Name + " " + tail;
            }
            return deepest.GetType().Name;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  СТОРОЖ МОДАЛЬНЫХ ОКОН (образец — `CultureProbeO14`, полоса F20)
        //
        //  Каждые 200 мс перечисляет окна СВОЕГО процесса класса `#32770` (класс
        //  `MessageBox` и любого диалога), называет их текст, засчитывает
        //  расхождение и посылает `WM_CLOSE`: нажать «ОК» здесь некому, и без
        //  сторожа прогон висел бы до убийства процесса.
        // ══════════════════════════════════════════════════════════════════════

        const string DialogClass = "#32770";
        const uint WM_CLOSE = 0x0010;

        delegate bool EnumWindowProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowProc lpfn, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool EnumChildWindows(IntPtr hWnd, EnumWindowProc lpfn, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        static volatile bool modalWatchStop;
        static Thread modalWatchThread;
        static int modalSeen;

        static void ModalWatchStart()
        {
            modalWatchThread = new Thread(delegate()
            {
                uint self = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                Dictionary<long, bool> known = new Dictionary<long, bool>();
                while (!modalWatchStop)
                {
                    List<IntPtr> found = new List<IntPtr>();
                    try
                    {
                        EnumWindows(delegate(IntPtr h, IntPtr l)
                        {
                            uint pid;
                            GetWindowThreadProcessId(h, out pid);
                            if (pid != self) return true;
                            StringBuilder cls = new StringBuilder(64);
                            GetClassNameW(h, cls, cls.Capacity);
                            if (cls.ToString() == DialogClass) found.Add(h);
                            return true;
                        }, IntPtr.Zero);
                    }
                    catch (Exception) { }

                    foreach (IntPtr h in found)
                    {
                        long key = h.ToInt64();
                        if (known.ContainsKey(key)) continue;
                        known[key] = true;
                        modalSeen++;
                        failures++;
                        Console.WriteLine("⛔ БЕЗОКОННЫЙ ПУТЬ УПЁРСЯ В МОДАЛЬНОЕ ОКНО: «"
                                          + ModalText(h) + "» — сторож закрывает его сам");
                        try { PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); }
                        catch (Exception) { }
                    }
                    Thread.Sleep(200);
                }
            });
            modalWatchThread.IsBackground = true;
            modalWatchThread.Start();
        }

        /// <summary>Текст диалога лежит в его детях класса `Static`.</summary>
        static string ModalText(IntPtr dialog)
        {
            StringBuilder acc = new StringBuilder();
            try
            {
                EnumChildWindows(dialog, delegate(IntPtr ch, IntPtr l)
                {
                    StringBuilder cls = new StringBuilder(64);
                    GetClassNameW(ch, cls, cls.Capacity);
                    if (cls.ToString() == "Static")
                    {
                        StringBuilder txt = new StringBuilder(512);
                        GetWindowTextW(ch, txt, txt.Capacity);
                        string s = txt.ToString().Trim();
                        if (s.Length > 0)
                        {
                            if (acc.Length > 0) acc.Append(" / ");
                            acc.Append(s);
                        }
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception) { }
            return acc.Length == 0 ? "(текст не прочитан)" : acc.ToString();
        }

        static void ModalWatchStop()
        {
            modalWatchStop = true;
            if (modalWatchThread != null) modalWatchThread.Join(2000);
        }

        /// <summary>
        /// Положительный контроль САМОГО сторожа: окно поднимается нарочно на
        /// фоновом потоке. Сторож обязан назвать его и закрыть, иначе «окон не
        /// было» неотличимо от «сторож молчит».
        /// </summary>
        static void ModalControl()
        {
            Console.WriteLine("=== ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ СТОРОЖА ОКОН ===");
            int before = modalSeen;
            DateTime t0 = DateTime.UtcNow;
            Thread th = new Thread(delegate()
            {
                System.Windows.Forms.MessageBox.Show("контрольное окно полосы F23",
                                                     "контроль",
                                                     System.Windows.Forms.MessageBoxButtons.OK);
            });
            th.IsBackground = true;
            th.SetApartmentState(ApartmentState.STA);
            th.Start();
            bool closed = th.Join(20000);
            double sec = (DateTime.UtcNow - t0).TotalSeconds;
            Console.WriteLine("  окно поднято нарочно, закрыто сторожем: " + (closed ? "да" : "НЕТ")
                              + ", секунд " + sec.ToString("F1", CultureInfo.InvariantCulture)
                              + ", окон назвал сторож: " + (modalSeen - before));
            if (!closed) { Console.WriteLine("  ⛔ СТОРОЖ НЕ РАБОТАЕТ"); failures++; }
        }
    }
}
