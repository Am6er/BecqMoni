using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using XPTable.Models;

namespace FsaInputCheckProbeP195
{
    /// <summary>
    /// П195 (01.10.2026): приёмка ЗАВЕРЕНИЯ окна отчёта FSA по трём строкам —
    /// тем же путём, каким его видит человек: сеанс разбора
    /// (<see cref="FsaAnalysisSession"/>) → окно <see cref="FSAReportView"/> →
    /// строки блока «Качество разбора».
    ///
    ///   * `AMBER159` — сверка плотности пробы (вес/объём карточки) с плотностью
    ///     сцены: положительный контроль — W/V = 1.3 г/см³ против сцены
    ///     маринелли; отрицательные — плотность сцены и нулевые W, V.
    ///   * `AMBER157` — кривая обрезана выше 1250 кэВ, разбор без матрицы:
    ///     строка «кривая … вне: K-40 1460.8»; контроль — полная кривая.
    ///   * `AMBER156` (в) — разбор без матрицы: предупреждение (с `AMBER284` — красная строка
    ///     «Матрица отклика» с ценой в подсказке, отдельной строки нет); контроль —
    ///     матричный разбор того же спектра.
    ///   * П281 (решение Amber 14.10.2026 «Молчать у краёв комптона (Рекомендую)») —
    ///     в сцене (2) без матрицы уступ комптона K-40 не назван «пиком вне состава»
    ///     (до правки — «1167.4 кэВ … Cs-134 1168.0, Co-60 1173.0»), серия в ревизии
    ///     с причиной `ComptonEdge` у края ≈1243 кэВ.
    ///   * (`AMBER269`, (А1); правило 05.11.2026 — тормоз только «у нуля») у этого
    ///     спектра ОДНА опора (K-40 1460.8 кэВ), и она ВЫШЕ порога «у нуля»: тормоз
    ///     усиления её не трогает, шкала едет по ней (a ≈ 0.9942), строка «Опорных
    ///     пиков для шкалы» не пуста, строки «пик вне состава» нет ни одной. Прежнее
    ///     правило «меньше двух опор» держало здесь шкалу на АЦП и роняло активность
    ///     вдвое — снято 05.11.2026.
    ///   * `AMBER294` — сцена (2а): тот же спектр, порог «у нуля» поднят выше опоры K-40,
    ///     тормоз срабатывает на живом разборе: опора не взята, знака «+» нет, причина строкой
    ///     и в подсказке (en и ru). Контроль — сцена (2): порог умолчания, «+» есть, строки нет.
    ///   * `AMBER294`, `AMBER290` — сцена (2б): соседний спектр `AS80_Cs137_0cm.xml`, матрица
    ///     склада, порог 3000 кэВ: живой случай ТОРМОЗ + ОТКАТ на корпусе. Причина тормоза
    ///     переносится с отменённого разбора и в окно, и в служебную строку.
    ///   * `AMBER294` — сцена (2в): тот же K-40 с матрицей, порог 2000, без отката: усиление стоит, карта света
    ///     движется, строка своя. Сцена (2г): `AS80_Cs137_0cm`, матрица, порог 700: тормоз на проходе, а шкала
    ///     едет. Сцена (2д): счёт опор равен числу «+» на умолчании (`G1S16_Ba133_P5`, `AS80_Lu176`,
    ///     `G1S24_Y88_P25`, опора не из шага — `G1S24_Th232_Mar`) и при позднем тормозе
    ///     (`G1S24_Rn222Coal_Mar_3h`). ⚠ (`AMBER306`) Спектры (2г) и опоры не из шага сменены: прежние стояли на
    ///     линиях библиотеки в образе и на образах из базы условия сцены не дают (перебор 136 спектров).
    ///     Сцена (2е): `G1S16_Ce139_P25`, умолчание: откат при нуле опор, без тормоза.
    ///
    ///     FsaInputCheckProbeP195 --spectrum=G1S16_K40_Mar.xml
    ///                            [--store=tools\CORPUS\corpus\geometries\response]
    ///                            [--timeout=&lt;с&gt;] [--delay-compute=&lt;мс&gt;[:&lt;окно&gt;]]
    ///
    /// (полоса p195flake, 10.11.2026) Окно читается, когда сеанс ДОШЁЛ ДО ПОКОЯ ПО ПРИЗНАКУ (`Settle`:
    /// счёт не идёт, начатый запуск закончен событием `Completed`), а не по сроку: прежний срок 1200 × Sleep(50) ≈ 61–76 с
    /// истекал молча под сильной нагрузкой, и сцена (2д) на `AS80_Lu176` читала окно посреди счёта
    /// (три ложных отказа 09.11.2026). `--timeout=` — срок ожидания (умолчание 1800 с), истёк — отказ
    /// обвязки «⛔ обвязка: …». `--delay-compute=` — задвижка фонового счёта на столько миллисекунд (во
    /// всех окнах или в названном): воспроизводимая «нагрузка» для самопроверки сторожа.
    ///
    /// ⛔ (`T269`, П251 14.10.2026) СЦЕНА (1) ТРЕБУЕТ МАТРИЦЫ. Матрицу разбор
    /// берёт со склада приложения (`config\device\response\&lt;guid&gt;.rmx` рядом
    /// с exe), а в каталоге проб её нет: 01.10.2026 проба гонялась в оснастке
    /// корпуса (`D:\BqMoni_Claude\p195\cl_ui`, склад скопирован), из каталога
    /// проб — «(1) матричный разбор: ждали True, есть False». Ключ `--store=`
    /// — каталог склада по guid кривой (корпусный `geometries\response`, вне
    /// git): матрица кривой спектра оттуда, годная по геометрии, кладётся В
    /// ПАМЯТЬ блоком «матрица из файла спектра» (`AMBER202`,
    /// `EfficiencyConfigData.EmbeddedMatrix`) — тем же путём
    /// `ResponseMatrixStore.Resolve`, каким её
    /// читает разбор приложения; на диск не пишется ничего. Склад
    /// приложения, если в нём годная матрица есть, первичен. Годной нет нигде —
    /// код 2 («мерить нечем»), а не отказ сцены (1): без матрицы сцену (1)
    /// мерить нечем, и красный приговор лгал бы о приложении.
    ///
    /// Сторож: `tools/check_fsa_report_view.py` (с `T269`).
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ», код 0; иначе 1; мерить нечем — 2.
    /// </summary>
    static class Program
    {
        static int bad;

        // (`AMBER311`) Подбор сцены (2д): опора «не из шага» держится на конкретных образах и уходит при смене склада.
        static bool scanNotInStep;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            string path = null;
            string store = null;
            foreach (string a in args)
            {
                if (a == "--scan-notinstep") scanNotInStep = true;
                else if (a.StartsWith("--timeout=", StringComparison.Ordinal)) timeoutS = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--delay-compute=", StringComparison.Ordinal))
                {
                    string v = a.Substring(16);
                    int colon = v.IndexOf(':');
                    delayComputeMs = int.Parse(colon < 0 ? v : v.Substring(0, colon), CultureInfo.InvariantCulture);
                    delayScene = colon < 0 ? null : v.Substring(colon + 1);
                }
                else if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) path = a.Substring(11);
                else if (a.StartsWith("--store=", StringComparison.Ordinal)) store = a.Substring(8);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            storeDir = store;

            if (path == null || !File.Exists(path)) { Console.Error.WriteLine("нужен --spectrum=<файл.xml>"); return 2; }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            // (`AMBER309`, 05.11.2026) Сцены привязки стоят на составе ПРЕЖНЕЙ поставки (152 линии): откат на
            // AS80_Cs137_0cm и тормоз на AS80_Th232Medal требуют её подписей. Поставка теперь шесть сетов конфига
            // Amber, поэтому библиотека сцен — своя копия рядом с пробами, тем же путём чтения, что у приложения.
            string sceneLibrary = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "data", "P195_NuclideDefinition.xml"));
            if (!File.Exists(sceneLibrary)) { Console.Error.WriteLine("МЕРИТЬ НЕЧЕМ: нет библиотеки сцен " + sceneLibrary); return 2; }
            typeof(NuclideDefinitionManager).GetField("nuclideDefinitionFilename", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(nuclides, sceneLibrary);
            if (!nuclides.LoadDefinitionFile(true)) { Console.Error.WriteLine("МЕРИТЬ НЕЧЕМ: библиотека сцен не прочиталась " + sceneLibrary); return 2; }
            Application.EnableVisualStyles();

            MainForm mainForm = new MainForm();
            try
            {
                return Run(mainForm, path, nuclides);
            }
            finally
            {
                mainForm.Dispose();
            }
        }

        static int Run(MainForm mainForm, string path, NuclideDefinitionManager nuclides)
        {
            // (`AMBER294`) сценам (2б), (2г), (2д) нужны соседние спектры: нет их — мерить нечем, до счёта
            foreach (string name in new[] { "AS80_Cs137_0cm", "AS80_Th232Medal", "G1S16_Ba133_P5", "AS80_Lu176", "G1S24_Y88_P25", "G1S24_Rn222Coal_Mar_3h", "G1S16_Ce139_P25" })
            {
                if (!File.Exists(Sibling(path, name)))
                {
                    Console.Error.WriteLine("МЕРИТЬ НЕЧЕМ: сцене нужен соседний спектр " + Sibling(path, name));
                    return 2;
                }
            }

            ResultData rd = Load(path, nuclides);
            if (rd.Efficiency == null || !rd.Efficiency.HasGeometry)
            {
                Console.Error.WriteLine("МЕРИТЬ НЕЧЕМ: у спектра нет кривой с геометрией");
                return 2;
            }

            string matrixFrom = AttachMatrix(rd.Efficiency);
            if (matrixFrom == null)
            {
                return 2;
            }

            Console.WriteLine("матрица кривой {0}: {1}", rd.Efficiency.Guid, matrixFrom);

            double scene = rd.Efficiency.Geometry.Source.Density;
            Console.WriteLine("спектр {0}: кривая «{1}», сцена {2}, ρ источника сцены {3} г/см³, пиков {4}",
                              Path.GetFileName(path), rd.Efficiency.Name, rd.Efficiency.Geometry.SourceType,
                              scene.ToString("0.000", CultureInfo.InvariantCulture), rd.DetectedPeaks.Count);

            // ---------------- AMBER159: статическая сверка ----------------
            Console.WriteLine();
            Console.WriteLine("=== AMBER159: плотность пробы против сцены (FsaAnalysisSession.CheckSampleDensity) ===");
            var cases = new[]
            {
                new { Name = "W = 0, V = 0 (карточка пуста)", W = 0.0, V = 0.0, Row = false, Warn = false },
                new { Name = "W = 1.0 кг, V = 0 (объёма нет)", W = 1.0, V = 0.0, Row = false, Warn = false },
                new { Name = "W = 1 кг, V = 1 л (умолчание карточки)", W = 1.0, V = 1.0, Row = false, Warn = false },
                new { Name = "W = 1.01 кг, V = 1 л (вписано)", W = 1.01, V = 1.0, Row = true, Warn = true },
                new { Name = "W/V = плотности сцены", W = scene * 1.0, V = 1.0, Row = true, Warn = false },
                new { Name = "W/V = 1.05 × сцены (+5 %)", W = scene * 1.05, V = 1.0, Row = true, Warn = false },
                new { Name = "W = 1.3 кг, V = 1.0 л (грунт)", W = 1.3, V = 1.0, Row = true, Warn = true },
                new { Name = "W = 0.45 кг, V = 1.0 л (рыхлая)", W = 0.45, V = 1.0, Row = true, Warn = true }
            };
            foreach (var c in cases)
            {
                rd.SampleInfo.Weight = c.W;
                rd.SampleInfo.Volume = c.V;
                FsaSampleDensityCheck check = FSAReportView.DensityOf(rd);
                Console.WriteLine("  {0,-34} → {1}", c.Name,
                                  check == null ? "строки нет"
                                                : FSAReportView.DensityCaption(check) + " | " + (check.Warning ? "ПРЕДУПРЕЖДЕНИЕ" : "в пределах"));
                Same(c.Name + ": строка есть", c.Row, check != null);
                if (check != null) Same(c.Name + ": предупреждение", c.Warn, check.Warning);
            }

            // ---------------- окно отчёта: три сцены ----------------
            var session = new FsaAnalysisSession();
            using (var report = new FSAReportView(mainForm))
            {
                // (1) с матрицей, грунт 1.3: строка плотности красная; нет строки «без матрицы»
                rd.SampleInfo.Weight = 1.3; rd.SampleInfo.Volume = 1.0;
                List<string[]> rows = Window(report, session, rd, "с матрицей, W/V 1.3");
                Same("(1) матричный разбор", true, session.Result != null && session.Result.ResponseMatrixUsed);
                Same("(1) строка плотности в окне, красная", true, Has(rows, "1.30", true));
                // (`AMBER284`) цена разбора без матрицы — подсказкой строки «Матрица отклика», а не
                // своей строкой: у матричного разбора подсказки нет и строка не красная
                Same("(1) у строки «Матрица отклика» нет подсказки «без матрицы»", false,
                     MatrixRowWarned(rows));
                Same("(1) строки «вне кривой» нет", false, Has(rows, Own("FSAReport_CurveOutsideValue"), true));

                // (2) без матрицы, полная кривая, карточка пуста: предупреждение «без матрицы»,
                //     строки плотности и «вне кривой» нет
                rd.SampleInfo.Weight = 0.0; rd.SampleInfo.Volume = 0.0;
                bool useMatrix = rd.Efficiency.UseResponseMatrix;
                rd.Efficiency.UseResponseMatrix = false;
                session.Reset();
                rows = Window(report, session, rd, "без матрицы, кривая полная, W = V = 0");
                Same("(2) разбор без матрицы", true, session.Result != null && !session.Result.ResponseMatrixUsed);
                // (`AMBER284`, решение Amber 02.11.2026 «Объединить со строкой выше, где уже написано -
                // нет Матрицы отклика. Остальное убрать в подсказку.») предупреждение — красная строка
                // «Матрица отклика | не учтена» с ценой в подсказке; отдельной строки нет
                Same("(2) предупреждение «без матрицы» — подсказкой красной строки «Матрица отклика»", true,
                     MatrixRowWarned(rows));
                Same("(2) отдельной строки с ценой «без матрицы» нет (подпись строки — не её текст)", false,
                     Has(rows, Own("FSAReport_MatrixNotUsedTip"), false));
                Same("(2) строки «вне кривой» нет", false, Has(rows, Own("FSAReport_CurveOutsideValue"), true));
                Same("(2) строки плотности нет", false, Has(rows, "g/cm", false) || Has(rows, "г/см", false));
                // ⛔ (`AMBER269`, (А1); правило 05.11.2026 — тормоз только «у нуля»)
                // НА ЭТОМ СПЕКТРЕ ОПОРА ОДНА — K-40 1460.8 кэВ, и она выше порога «у нуля»:
                // тормоз усиления НЕ срабатывает, опора взята, шкала едет по ней (a ≈ 0.9942).
                // Прежнее правило «меньше двух опор» держало здесь шкалу на АЦП и роняло
                // активность вдвое (0.611 против 1.089) — снято 05.11.2026.
                Same("(2) привязка без матрицы: одна опора K-40 1460.8 взята", true,
                     session.Result != null && session.Result.ScaleAnchorsUsed > 0);
                Same("(2) тормоз усиления не назван: опора не у нуля", false,
                     session.Result != null && (session.Result.AnchorNote ?? "").Contains("тормоз усиления"));
                // (`AMBER294`) без тормоза: опора со знаком «+», строки причины нет
                Same("(2) тормоз опор не снимал", 0, session.Result != null ? session.Result.AnchorBrakedPeaks : -1);
                Same("(2) подпись тормоза пуста", (string)null, FSAReportView.AnchorBrakeCaption(session.Result));
                Same("(2) в подсказке опора со знаком «+»", true, AnchorTip(rows).Contains("\n+ K-40"));
                // (П281, решение Amber 14.10.2026 «Молчать у краёв комптона (Рекомендую)») уступ
                // комптона K-40 без матрицы — невязка, а не «пик вне состава» (до правки — строка
                // «1167.4 кэВ: пик вне состава … Cs-134 1168.0, Co-60 1173.0», П251)
                Same("(2) строки «пик вне состава» нет ни одной", false, Has(rows, Own("FSAReport_UncoveredValue"), true));
                // причина — строкой: проба собирается и сборкой до П281 (положительный контроль)
                Same("(2) ревизия: серия под краем комптона K-40 (1100…1243 кэВ) отвергнута краем", true,
                     session.Result != null && session.Result.UncoveredRejections.Exists(
                         j => j.Reason.ToString() == "ComptonEdge" && j.EnergyKev > 1100.0 && j.EnergyKev < 1243.0));

                BrakeScene(report, session, rd);
                if (!BrakeRollbackScene(report, path, nuclides))
                {
                    return 2;
                }

                BrakeMapScene(report, session, rd);
                if (!WarmBrakeScene(report, path, nuclides) || !StepAnchorsScene(report, path, nuclides)
                    || !ZeroRollbackScene(report, path, nuclides))
                {
                    return 2;
                }

                // (3) без матрицы, кривая до 1250 кэВ: K-40 1460.8 исключён и назван
                int before = rd.Efficiency.Curve.Count;
                var kept = new List<ROIEfficiencyData>(rd.Efficiency.Curve);
                rd.Efficiency.Curve.RemoveAll(p => p.Energy > 1250.0);
                Console.WriteLine("  кривая обрезана выше 1250 кэВ: точек {0} → {1}", before, rd.Efficiency.Curve.Count);
                session.Reset();
                rows = Window(report, session, rd, "без матрицы, кривая до 1250 кэВ");
                bool named = false;
                foreach (string[] r in rows) if (r[0].Contains("K-40 1460") && r[1] == Own("FSAReport_CurveOutsideValue")) named = true;
                Same("(3) строка «вне кривой» называет K-40 1460", true, named);
                Same("(3) результат: исключённая линия K-40 у 1460 кэВ", true,
                     session.Result != null && session.Result.EfficiencyOutOfRangeLines.Exists(l => Math.Abs(l.EnergyKev - 1460.8) < 1.5));
                if (session.Result != null)
                    foreach (FsaComponentResult c in session.Result.Components)
                    {
                        int top = 0;
                        for (int i = 1; c.Curve != null && i < c.Curve.Length; i++) if (c.Curve[i] > c.Curve[top]) top = i;
                        Console.WriteLine("  компонент {0,-14} {1,-9} A={2:E4} z={4:F2} пик.отсч={3:F0}, вершина ленты {5:F1} кэВ", c.Name, c.Kind, c.CountRate * session.Result.LiveTime, c.PeakCounts, c.Z,
                                          rd.EnergySpectrum.EnergyCalibration.ChannelToEnergy(top));
                    }
                Same("(3) компонента K-40 в разборе нет (единственная линия вне кривой)", true,
                     session.Result != null && !session.Result.Components.Exists(c => c.Name.StartsWith("K-40", StringComparison.Ordinal)));

                rd.Efficiency.Curve.Clear();
                rd.Efficiency.Curve.AddRange(kept);
                rd.Efficiency.UseResponseMatrix = useMatrix;
                report.SetProbeSource(null, null);
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        /// <summary>Счёт сеанса до покоя, окно — заполнить и вернуть строки блока (подпись, значение, красное?).</summary>
        static List<string[]> Window(FSAReportView report, FsaAnalysisSession session, ResultData rd, string title)
        {
            bool slow = delayComputeMs > 0 && (delayScene == null || delayScene == title);
            if (slow)
            {
                Gate(new DelayGate(delayComputeMs));
            }

            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                Track(session);
                int before = session.RunCount;
                session.EnsureUpToDate(rd, true);
                Settle(session, before, title);
            }
            finally
            {
                if (slow)
                {
                    Gate(null);
                }
            }

            report.SetProbeSource(session, rd);
            report.RefreshReport();
            Console.WriteLine();
            Console.WriteLine("--- окно: {0} (состояние: {1}; счёт {2:F1} с{3}) ---", title, session.Status ?? "—",
                              clock.Elapsed.TotalSeconds, slow ? ", замедлен на " + delayComputeMs + " мс" : "");
            var rows = new List<string[]>();
            TableModel model = report.ReportTable.TableModel;
            for (int i = 0; i < model.Rows.Count; i++)
            {
                var tag = model.Rows[i].Tag as FsaReportRow;
                if (tag == null || tag.Kind != FsaReportRowKind.Quality) continue;
                string name = model.Rows[i].Cells[0].Text, value = model.Rows[i].Cells[1].Text;
                if (name.Length == 0 && value.Length == 0) continue;
                bool red = tag.Warning;
                rows.Add(new[] { name, value, red ? "red" : "", model.Rows[i].Cells[0].ToolTipText ?? string.Empty });
                Console.WriteLine("  «{0}» | {1}{2}", name, value, red ? "  [внимание]" : "");
            }

            return rows;
        }

        /// <summary>
        /// (полоса p195flake, 10.11.2026) Срок ожидания сеанса, с. Прежде ожидание было 1200 × Sleep(50) ≈ 61–76 с (по разрешению таймера)
        /// и по истечении МОЛЧА шло читать окно: под сильной нагрузкой разбор `AS80_Lu176` шёл дольше, сеанс
        /// ещё считал (`Result` = null), и сцена (2д) давала три ложных отказа «опоры есть: ждали True, есть
        /// False». Теперь срок — с запасом на нагрузку (`--timeout=`), а истечение — отказ обвязки словами.
        /// </summary>
        static int timeoutS = 1800;

        /// <summary>
        /// (p195flake) `--delay-compute=&lt;мс&gt;[:&lt;окно&gt;]` — воспроизводимая «нагрузка»: фоновый счёт
        /// сеанса стоит на задвижке проб (`FsaAnalysisSession.probeGate`) столько миллисекунд перед разбором
        /// — во всех окнах или только в названном (заголовок окна, например `AS80_Lu176`). Прежняя
        /// обвязка с 90000:AS80_Lu176 давала ровно отказ 09.11.2026; ожидание по признаку — нет.
        /// </summary>
        static int delayComputeMs;
        static string delayScene;

        sealed class DelayGate : WaitHandle
        {
            readonly int ms;

            public DelayGate(int ms)
            {
                this.ms = ms;
            }

            public override bool WaitOne()
            {
                Thread.Sleep(this.ms);
                return true;
            }
        }

        static void Gate(WaitHandle gate)
        {
            System.Reflection.FieldInfo f = typeof(FsaAnalysisSession).GetField("probeGate",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            if (f == null)
            {
                throw new InvalidOperationException("нет FsaAnalysisSession.probeGate — проба смотрит не туда");
            }

            f.SetValue(null, gate);
        }

        static readonly object doneGate = new object();
        static readonly Dictionary<FsaAnalysisSession, int> completedRun = new Dictionary<FsaAnalysisSession, int>();

        /// <summary>Подписать пробу на конец счёта сеанса (повторная подписка не удваивает).</summary>
        static void Track(FsaAnalysisSession session)
        {
            session.Completed -= OnCompleted;
            session.Completed += OnCompleted;
        }

        static void OnCompleted(object sender, EventArgs e)
        {
            var session = (FsaAnalysisSession)sender;
            int run = session.RunCount;
            lock (doneGate)
            {
                completedRun[session] = run;
            }
        }

        /// <summary>
        /// (p195flake) Ждать ПО ПРИЗНАКУ, а не по сроку: счёт не идёт, и если заказ запустил счёт
        /// (`RunCount` вырос с <paramref name="before"/>), последний начатый запуск закончен событием
        /// `Completed` — то есть `Finish` целиком отработал и результат опубликован. Заказ, не
        /// запустивший счёта (тот же отпечаток), готов сразу. Срок <see cref="timeoutS"/> вышел — отказ
        /// обвязки словами (не «готово» и не чтение окна посреди счёта).
        /// </summary>
        static void Settle(FsaAnalysisSession session, int before, string what)
        {
            DateTime until = DateTime.UtcNow.AddSeconds(timeoutS);
            while (DateTime.UtcNow < until)
            {
                Application.DoEvents();
                int run = session.RunCount;
                bool done;
                lock (doneGate)
                {
                    int completed;
                    done = !session.IsRunning
                           && (run == before || (completedRun.TryGetValue(session, out completed) && completed == run));
                }

                if (done && session.RunCount == run)
                {
                    Application.DoEvents();
                    return;
                }

                Thread.Sleep(20);
            }

            Console.WriteLine("  ⛔ обвязка: окно «{0}» — сеанс не дошёл до покоя за {1} с (состояние: {2})", what, timeoutS,
                              session.Status ?? "—");
            bad++;
        }

        /// <summary>
        /// (`AMBER284`) Строка «Матрица отклика» красная (признак происшествия) и её подсказка —
        /// цена разбора без матрицы словами окна (`FSAReport_MatrixNotUsedTip`).
        /// </summary>
        static bool MatrixRowWarned(List<string[]> rows)
        {
            foreach (string[] r in rows)
            {
                if (r[0] == Own("FSAReport_MatrixRow"))
                {
                    return r[2] == "red" && r[3] == Own("FSAReport_MatrixNotUsedTip");
                }
            }

            return false;
        }

        static bool Has(List<string[]> rows, string text, bool redOnly)
        {
            foreach (string[] r in rows)
            {
                if ((r[0].Contains(text) || r[1].Contains(text)) && (!redOnly || r[2] == "red")) return true;
            }

            return false;
        }

        /// <summary>
        /// (`AMBER294`, решение Amber 05.11.2026 «Показать причину, снять «+» (Рекомендую)») Сцена
        /// (2а): живой разбор с сработавшим тормозом на спектре корпуса, а не подсаженный результат.
        /// Порог «у нуля» поднят выше единственной опоры K-40 1460.8 кэВ. Строка «пик вне состава»
        /// при замороженной шкале — положительный контроль строгой проверки сцены (2): та же проверка
        /// на том же спектре краснеет, когда шкала не подстроена.
        /// </summary>
        static void BrakeScene(FSAReportView report, FsaAnalysisSession session, ResultData rd)
        {
            CultureInfo ui = Thread.CurrentThread.CurrentUICulture;
            FsaAnalysisSession.ProbeAnalyzerHook = an => an.AnchorGainBrakeZeroMaxKev = 2000.0;
            try
            {
                session.Reset();
                foreach (string lang in new[] { "en", "ru" })
                {
                    Thread.CurrentThread.CurrentUICulture = new CultureInfo(lang);
                    List<string[]> rows = Window(report, session, rd, "без матрицы, тормоз усиления, порог 2000 кэВ, " + lang);
                    FsaResult result = session.Result;
                    string title = "(2а) " + lang + ": ";
                    Same(title + "снята одна опора", 1, result != null ? result.AnchorBrakedPeaks : -1);
                    Same(title + "опор в итоге 0", 0, result != null ? result.ScaleAnchorsUsed : -1);
                    List<FsaScaleAnchor> anchors = result == null ? null
                        : result.AnchorRolledBack ? result.AnchorRollbackScaleAnchors : result.ScaleAnchors;
                    Same(title + "ни одна опора не взята", false, anchors == null || anchors.Exists(a => a.Used));
                    Same(title + "у опоры причина тормоза", true, anchors != null
                        && anchors.Exists(a => a.Refusal == FsaScaleAnchor.RefusalGainBrake));
                    Same(title + "строка опор: нет", Own("FSAReport_AnchorNone"), AnchorValue(rows));
                    string caption = FSAReportView.AnchorBrakeCaption(result);
                    Same(title + "подпись тормоза названа числом", true, caption != null && result != null
                        && caption.Contains(result.AnchorBrakedTopKev.ToString("F1", CultureInfo.InvariantCulture)));
                    Same(title + "строка причины красная, значение «не подстроена»", true, caption != null && rows.Exists(r =>
                        r[0] == caption && r[1] == Own("FSAReport_AnchorBrakeValue") && r[2] == "red"));
                    string tip = AnchorTip(rows);
                    Same(title + "подпись и подсказка называют одно число", true, result != null && tip.Contains(
                        "K-40 " + result.AnchorBrakedTopKev.ToString("F1", CultureInfo.InvariantCulture) + " "));
                    Same(title + "подсказка называет и линию", true, result != null && tip.Contains(
                        result.AnchorBrakedTopLineKev.ToString("F1", CultureInfo.InvariantCulture)));
                    Same(title + "шапки «по которым подстроена» нет", false, tip.Contains(Own("FSAReport_AnchorTip")));
                    Same(title + "шапка тормоза есть", true, tip.Contains(Own("FSAReport_AnchorBrakeTipHead")));
                    Same(title + "в подсказке нет опоры со знаком «+»", false, tip.Contains("\n+ "));
                    Same(title + "в подсказке опора K-40 со знаком «-»", true, tip.Contains("\n- K-40"));
                    Same(title + "причина в подсказке словами", true, tip.Contains(Own("FSAReport_AnchorBrakeRefusal")));
                    Same(title + "служебное слово причины не на экране", false,
                         tip.Contains(" " + FsaScaleAnchor.RefusalGainBrake));
                    Same(title + "подсказка объясняет порог", true, tip.Contains(FSAReportView.AnchorBrakeHint(result)));
                    Same(title + "контроль сцены (2): при замороженной шкале строка «пик вне состава» есть", true,
                         Has(rows, Own("FSAReport_UncoveredValue"), true));
                    Same(title + "растяжения шкалы на экране нет", false, Has(rows, Own("FSAReport_AnchorGainRow"), false));
                }
            }
            finally
            {
                Thread.CurrentThread.CurrentUICulture = ui;
                FsaAnalysisSession.ProbeAnalyzerHook = null;
                session.Reset();
            }
        }

        static string Sibling(string path, string name)
        {
            return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)), name + ".xml");
        }

        /// <summary>
        /// (`AMBER294`, решение Amber 05.11.2026 «Своя строка для карты (Рекомендую)») Сцена (2в): матрица без
        /// отката, порог выше опоры K-40. Усиление 1, а карта света есть: строка без утверждения про неподвижную шкалу.
        /// </summary>
        static void BrakeMapScene(FSAReportView report, FsaAnalysisSession session, ResultData rd)
        {
            CultureInfo ui = Thread.CurrentThread.CurrentUICulture;
            bool useMatrix = rd.Efficiency.UseResponseMatrix;
            rd.Efficiency.UseResponseMatrix = true;
            FsaAnalysisSession.ProbeAnalyzerHook = an => an.AnchorGainBrakeZeroMaxKev = 2000.0;
            try
            {
                session.Reset();
                foreach (string lang in new[] { "en", "ru" })
                {
                    Thread.CurrentThread.CurrentUICulture = new CultureInfo(lang);
                    List<string[]> rows = Window(report, session, rd, "K-40, матрица, тормоз, порог 2000 кэВ, " + lang);
                    FsaResult result = session.Result;
                    string title = "(2в) " + lang + ": ";
                    Same(title + "разбор с матрицей, без отката", true, result != null && result.ResponseMatrixUsed && !result.AnchorRolledBack);
                    Same(title + "снята одна опора, усиление 1", "1 1.000000", result == null ? "—"
                        : result.AnchorBrakedPeaks.ToString(CultureInfo.InvariantCulture) + " " + result.Gain.ToString("F6", CultureInfo.InvariantCulture));
                    Same(title + "карта света есть", true, result != null && result.AdcScale > 0.0);
                    Same(title + "ветка карты", true, FSAReportView.AnchorBrakeOnMap(result));
                    string caption = result == null ? "—" : string.Format(CultureInfo.InvariantCulture, Own("FSAReport_AnchorBrakeMapRowOne"),
                        result.AnchorBrakedTopKev.ToString("F1", CultureInfo.InvariantCulture));
                    Same(title + "своя строка, своё значение, красная", true, rows.Exists(r =>
                        r[0] == caption && r[1] == Own("FSAReport_AnchorBrakeMapValue") && r[2] == "red"));
                    Same(title + "значения «не подстроена» нет", false, rows.Exists(r => r[1] == Own("FSAReport_AnchorBrakeValue")));
                    string tip = AnchorTip(rows);
                    Same(title + "подсказка карты", true, tip.Contains(FSAReportView.AnchorBrakeHint(result)) && FSAReportView.AnchorBrakeHint(result).Contains("1460"));
                    Same(title + "шапка карты", true, tip.Contains(Own("FSAReport_AnchorBrakeMapTipHead")));
                    Same(title + "подсказки про неподвижную шкалу нет", false, tip.Contains(string.Format(CultureInfo.InvariantCulture,
                        Own("FSAReport_AnchorBrakeTip"), "2000",
                        result == null ? "" : result.AnchorBrakedTopLineKev.ToString("F1", CultureInfo.InvariantCulture),
                        result == null ? "" : result.AnchorBrakedTopKev.ToString("F1", CultureInfo.InvariantCulture))));
                }
            }
            finally
            {
                Thread.CurrentThread.CurrentUICulture = ui;
                FsaAnalysisSession.ProbeAnalyzerHook = null;
                rd.Efficiency.UseResponseMatrix = useMatrix;
                session.Reset();
            }
        }

        /// <summary>
        /// (`AMBER294`) Сцена (2г): тормоз назван на проходе, а итог разбора со шкалой: это не «шкала не
        /// подстроена», опоры и растяжение на экране. Откат выключен: сцена судит строку тормоза, а не откат.
        /// </summary>
        static bool WarmBrakeScene(FSAReportView report, string path, NuclideDefinitionManager nuclides)
        {
            ResultData rd = Load(Sibling(path, "AS80_Cs137_0cm"), nuclides);
            if (rd.Efficiency == null || !rd.Efficiency.HasGeometry || AttachMatrix(rd.Efficiency) == null)
            {
                return false;
            }

            var session = new FsaAnalysisSession();
            FsaAnalysisSession.ProbeAnalyzerHook = an =>
            {
                an.AnchorRollback = false;
                an.AnchorGainBrakeZeroMaxKev = 700.0;
            };
            try
            {
                List<string[]> rows = Window(report, session, rd, "AS80_Cs137_0cm, матрица, порог 700 кэВ");
                FsaResult result = session.Result;
                Same("(2г) тормоз назван в служебной строке", true, result != null && (result.AnchorNote ?? "").Contains("тормоз усиления"));
                Same("(2г) усиление не 1", true, result != null && result.Gain != 1.0);
                Same("(2г) опоры тормоз не снимал", 0, result != null ? result.AnchorBrakedPeaks : -1);
                Same("(2г) строки «не подстроена» нет", false, rows.Exists(r => r[1] == Own("FSAReport_AnchorBrakeValue")
                     || r[1] == Own("FSAReport_AnchorBrakeMapValue")));
                Same("(2г) счёт опор равен числу «+», и он не 0", true, result != null && result.ScaleAnchorsUsed > 0
                     && result.ScaleAnchorsUsed == result.ScaleAnchors.FindAll(a => a.Used).Count);
                Same("(2г) растяжение шкалы на экране", true, Has(rows, Own("FSAReport_AnchorGainRow"), false));
            }
            finally
            {
                FsaAnalysisSession.ProbeAnalyzerHook = null;
                report.SetProbeSource(null, null);
            }

            return true;
        }

        /// <summary>
        /// (`AMBER294`) Сцена (2д): строка «опор N» и число «+» в таблице сходятся. Умолчание, где расходились
        /// (`G1S16_Ba133_P5` 1 и 3, `AS80_Lu176` 3 и 2), и поздний тормоз (`G1S24_Rn222Coal_Mar_3h`, порог 1765.49, 4 и 3).
        /// </summary>
        static bool StepAnchorsScene(FSAReportView report, string path, NuclideDefinitionManager nuclides)
        {
            if (scanNotInStep)
            {
                foreach (string file in Directory.GetFiles(Path.GetDirectoryName(Path.GetFullPath(path)), "*.xml"))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    ResultData one = Load(file, nuclides);
                    if (one.Efficiency == null || !one.Efficiency.HasGeometry || AttachMatrix(one.Efficiency) == null)
                    {
                        continue;
                    }

                    var scan = new FsaAnalysisSession();
                    FsaAnalysisSession.ProbeAnalyzerHook = an => an.AnchorRollback = false;
                    try
                    {
                        List<string[]> scanRows = Window(report, scan, one, name);
                        FsaResult got = scan.Result;
                        bool has = got != null && got.ScaleAnchors.Exists(a => a.Refusal == FsaScaleAnchor.RefusalNotInStep);
                        bool said = AnchorTip(scanRows).Contains(Own("FSAReport_AnchorNotInStepRefusal"));
                        if (has) Console.WriteLine("SCAN\t" + name + "\t" + said);
                    }
                    finally
                    {
                        FsaAnalysisSession.ProbeAnalyzerHook = null;
                        report.SetProbeSource(null, null);
                    }
                }

                Console.WriteLine("SCAN конец");
                return false;       // подбор, не приёмка: дальше сцен нет, код 2
            }

            var cases = new[]
            {
                new { Name = "G1S16_Ba133_P5", Matrix = true, Brake = double.NaN, Late = false, NotInStep = false },
                // (`AMBER311`) С разделом 800 тыс. опора «не из шага» окна живёт только на `AS80_Cs137_0cm` (перебор всего корпуса,
                // ключ `--scan-notinstep`); у `G1S24_Th232_Mar` её больше нет.
                new { Name = "G1S24_Th232_Mar", Matrix = true, Brake = double.NaN, Late = false, NotInStep = false },
                new { Name = "AS80_Cs137_0cm", Matrix = true, Brake = double.NaN, Late = false, NotInStep = true },
                new { Name = "AS80_Lu176", Matrix = true, Brake = double.NaN, Late = false, NotInStep = false },
                new { Name = "G1S24_Y88_P25", Matrix = true, Brake = double.NaN, Late = false, NotInStep = false },
                new { Name = "G1S24_Rn222Coal_Mar_3h", Matrix = false, Brake = 1765.49, Late = true, NotInStep = false }
            };
            foreach (var c in cases)
            {
                ResultData rd = Load(Sibling(path, c.Name), nuclides);
                if (rd.Efficiency == null)
                {
                    return false;
                }

                if (c.Matrix)
                {
                    if (!rd.Efficiency.HasGeometry || AttachMatrix(rd.Efficiency) == null)
                    {
                        return false;
                    }
                }
                else
                {
                    rd.Efficiency.EmbeddedMatrix = null;
                    rd.Efficiency.UseResponseMatrix = false;
                }

                var session = new FsaAnalysisSession();
                double brake = c.Brake;
                FsaAnalysisSession.ProbeAnalyzerHook = an =>
                {
                    an.AnchorRollback = false;
                    if (!double.IsNaN(brake))
                    {
                        an.AnchorGainBrakeZeroMaxKev = brake;
                    }
                };
                try
                {
                    List<string[]> rows = Window(report, session, rd, c.Name);
                    FsaResult result = session.Result;
                    Same("(2д) " + c.Name + ": опоры есть", true, result != null && result.ScaleAnchorsUsed > 0);
                    Same("(2д) " + c.Name + ": счёт опор равен числу «+»", result != null ? result.ScaleAnchorsUsed : -1,
                         result != null ? result.ScaleAnchors.FindAll(a => a.Used).Count : -2);
                    Same("(2д) " + c.Name + ": строка опор та же", result != null ? result.ScaleAnchorsUsed.ToString(CultureInfo.InvariantCulture) : "—",
                         AnchorValue(rows));
                    Same("(2д) " + c.Name + ": служебное слово причины не на экране", false,
                         AnchorTip(rows).Contains(FsaScaleAnchor.RefusalNotInStep));
                    if (c.NotInStep)
                    {
                        Same("(2д) " + c.Name + ": опора замера не из шага названа причиной словами", true,
                             result != null && result.ScaleAnchors.Exists(a => a.Refusal == FsaScaleAnchor.RefusalNotInStep)
                             && AnchorTip(rows).Contains(Own("FSAReport_AnchorNotInStepRefusal")));
                    }

                    if (c.Late)
                    {
                        Same("(2д) " + c.Name + ": тормоз на позднем проходе", true,
                             result != null && !(result.AnchorNote ?? "").Contains("на проходе 0") && (result.AnchorNote ?? "").Contains("тормоз усиления"));
                    }
                }
                finally
                {
                    FsaAnalysisSession.ProbeAnalyzerHook = null;
                    report.SetProbeSource(null, null);
                }
            }

            return true;
        }

        /// <summary>
        /// (`AMBER294`, `AMBER290`) Сцена (2б): ТОРМОЗ + ОТКАТ на спектре корпуса. Порог 3000 кэВ выше всех
        /// опор, разбор с тормозом хуже разбора без поиска опор, отдан второй. Единственный читатель в
        /// `check_all` переноса причины в `AnalyzeRolledBack` (`bare.AnchorBraked*`, `WithBrakeNote`): без него
        /// в окне снова «подстройка по 0 пикам (+0.00 %)». false — мерить нечем (нет матрицы).
        /// </summary>
        static bool BrakeRollbackScene(FSAReportView report, string path, NuclideDefinitionManager nuclides)
        {
            ResultData rd = Load(Sibling(path, "AS80_Cs137_0cm"), nuclides);
            if (rd.Efficiency == null || !rd.Efficiency.HasGeometry || AttachMatrix(rd.Efficiency) == null)
            {
                return false;
            }

            CultureInfo ui = Thread.CurrentThread.CurrentUICulture;
            var session = new FsaAnalysisSession();
            FsaAnalysisSession.ProbeAnalyzerHook = an => an.AnchorGainBrakeZeroMaxKev = 3000.0;
            try
            {
                foreach (string lang in new[] { "en", "ru" })
                {
                    Thread.CurrentThread.CurrentUICulture = new CultureInfo(lang);
                    List<string[]> rows = Window(report, session, rd, "AS80_Cs137_0cm, матрица, тормоз, порог 3000 кэВ, " + lang);
                    FsaResult result = session.Result;
                    string title = "(2б) " + lang + ": ";
                    Same(title + "разбор с матрицей", true, result != null && result.ResponseMatrixUsed);
                    Same(title + "откат", true, result != null && result.AnchorRolledBack);
                    Same(title + "причина тормоза доехала с отменённого разбора", true, result != null && result.AnchorBrakedPeaks > 0);
                    Same(title + "опор в итоге 0", 0, result != null ? result.ScaleAnchorsUsed : -1);
                    List<FsaScaleAnchor> anchors = result != null ? result.AnchorRollbackScaleAnchors : null;
                    Same(title + "у отменённого разбора ни одна опора не взята", false, anchors == null || anchors.Exists(a => a.Used));
                    string note = result != null ? result.AnchorNote ?? string.Empty : string.Empty;
                    int atBrake = note.IndexOf("тормоз усиления", StringComparison.Ordinal);
                    Same(title + "служебная строка: тормоз, затем откат", true,
                         atBrake >= 0 && note.IndexOf("откат привязки", StringComparison.Ordinal) > atBrake);
                    string caption = FSAReportView.AnchorBrakeCaption(result);
                    Same(title + "строка причины красная, значение «не подстроена»", true, caption != null && rows.Exists(r =>
                        r[0] == caption && r[1] == Own("FSAReport_AnchorBrakeValue") && r[2] == "red"));
                    string rollback = result == null ? "—" : string.Format(CultureInfo.InvariantCulture,
                        Own("FSAReport_AnchorBrakeRollbackRow"),
                        result.AnchorRollbackChi2NdfPoisson.ToString("F2", CultureInfo.InvariantCulture),
                        result.Chi2NdfPoisson.ToString("F2", CultureInfo.InvariantCulture));
                    Same(title + "строка отката без «по 0 пикам», значение «не подстроена»", true, rows.Exists(r =>
                        r[0] == rollback && r[1] == Own("FSAReport_AnchorBrakeValue") && r[2] == "red"));
                    Same(title + "строка опор: нет", Own("FSAReport_AnchorNone"), AnchorValue(rows));
                    string tip = AnchorTip(rows);
                    Same(title + "в подсказке нет опоры со знаком «+»", false, tip.Contains("\n+ "));
                    Same(title + "шапки «отменённой подстройки» нет", false, tip.Contains(Own("FSAReport_AnchorRollbackTip")));
                    Same(title + "линия доехала с отменённого разбора: в подсказке причины нет NaN", false,
                         result == null || FSAReportView.AnchorBrakeHint(result).Contains("NaN"));
                    Same(title + "верхнее число подписи — в строках опор", true, result != null && tip.Contains(
                        " " + result.AnchorBrakedTopKev.ToString("F1", CultureInfo.InvariantCulture) + " "));
                }
            }
            finally
            {
                Thread.CurrentThread.CurrentUICulture = ui;
                FsaAnalysisSession.ProbeAnalyzerHook = null;
                report.SetProbeSource(null, null);
            }

            return true;
        }

        /// <summary>
        /// (`AMBER294`) Сцена (2е): откат при нуле опор без тормоза, живая ветка умолчания (10 из 368 развёртки
        /// критика). Строка «подстройка по 0 пикам (+0.00 %)» читать нечего, значение — «шкала файла».
        /// </summary>
        static bool ZeroRollbackScene(FSAReportView report, string path, NuclideDefinitionManager nuclides)
        {
            ResultData rd = Load(Sibling(path, "G1S16_Ce139_P25"), nuclides);
            if (rd.Efficiency == null || !rd.Efficiency.HasGeometry || AttachMatrix(rd.Efficiency) == null)
            {
                return false;
            }

            var session = new FsaAnalysisSession();
            try
            {
                List<string[]> rows = Window(report, session, rd, "G1S16_Ce139_P25, матрица, умолчание");
                FsaResult result = session.Result;
                Same("(2е) откат", true, result != null && result.AnchorRolledBack);
                Same("(2е) у отменённого разбора опор 0, тормоза нет", "0 0", result == null ? "—"
                    : result.AnchorRollbackAnchors.ToString(CultureInfo.InvariantCulture) + " " + result.AnchorBrakedPeaks.ToString(CultureInfo.InvariantCulture));
                string rollback = result == null ? "—" : string.Format(CultureInfo.InvariantCulture,
                    Own("FSAReport_AnchorBrakeRollbackRow"),
                    result.AnchorRollbackChi2NdfPoisson.ToString("F2", CultureInfo.InvariantCulture),
                    result.Chi2NdfPoisson.ToString("F2", CultureInfo.InvariantCulture));
                Same("(2е) строка отката без «по 0 пикам», значение «шкала файла»", true, rows.Exists(r =>
                    r[0] == rollback && r[1] == Own("FSAReport_AnchorRollbackValue") && r[2] == "red"));
            }
            finally
            {
                report.SetProbeSource(null, null);
            }

            return true;
        }

        /// <summary>(`AMBER294`) Значение строки «Опорных пиков для шкалы».</summary>
        static string AnchorValue(List<string[]> rows)
        {
            string[] row = rows.Find(r => r[0] == Own("FSAReport_AnchorRow"));
            return row != null ? row[1] : null;
        }

        /// <summary>(`AMBER294`) Подсказка строки «Опорных пиков для шкалы», с единым переводом строки.</summary>
        static string AnchorTip(List<string[]> rows)
        {
            string[] row = rows.Find(r => r[0] == Own("FSAReport_AnchorRow"));
            return row != null ? row[3].Replace("\r\n", "\n") : string.Empty;
        }

        static readonly System.ComponentModel.ComponentResourceManager ViewResources =
            new System.ComponentModel.ComponentResourceManager(typeof(FSAReportView));

        static string Own(string key)
        {
            return ViewResources.GetString(key) ?? key;
        }

        static void Same<T>(string what, T expected, T actual)
        {
            bool ok = EqualityComparer<T>.Default.Equals(expected, actual);
            if (!ok) bad++;
            Console.WriteLine("  {0} {1}: ждали {2}, есть {3}", ok ? "OK " : "⛔ ", what, expected, actual);
        }

        /// <summary>Каталог склада по guid кривой (`--store=`), или null.</summary>
        static string storeDir;

        /// <summary>
        /// (`T269`) Дать кривой матрицу: склад приложения, если там годная;
        /// иначе `&lt;--store&gt;\&lt;guid&gt;.rmx`, годная по геометрии, — в память
        /// блоком «матрица из файла спектра» (`AMBER202`). Возвращает слова
        /// источника; null — годной нет, причина напечатана, сцену (1) мерить нечем.
        /// </summary>
        static string AttachMatrix(EfficiencyConfigData efficiency)
        {
            MatrixRefusal refusal;
            int format;
            ResponseMatrixSource source;
            string spectrumRefusal;
            ResponseMatrix own = ResponseMatrixStore.Resolve(efficiency, out refusal, out format, out source,
                                                             out spectrumRefusal);
            if (own != null && own.IsValidFor(efficiency.Geometry))
            {
                return "склад приложения " + ResponseMatrixStore.PathOf(efficiency.Guid) + " (" + source + ")";
            }

            string why = own == null ? "нет файла " + ResponseMatrixStore.PathOf(efficiency.Guid) + " (" + refusal + ")"
                                     : "матрица склада приложения негодна для геометрии кривой";
            if (string.IsNullOrEmpty(storeDir))
            {
                Console.Error.WriteLine("МЕРИТЬ НЕЧЕМ: сцене (1) нужна матрица кривой {0}: {1}; дайте --store=<склад по guid> "
                                        + "(корпусный tools\\CORPUS\\corpus\\geometries\\response)", efficiency.Guid, why);
                return null;
            }

            string file = Path.Combine(storeDir, efficiency.Guid + ".rmx");
            if (!File.Exists(file))
            {
                Console.Error.WriteLine("МЕРИТЬ НЕЧЕМ: сцене (1) нужна матрица кривой {0}: {1}; в --store её тоже нет: {2}",
                                        efficiency.Guid, why, file);
                return null;
            }

            ResponseMatrix matrix = ResponseMatrix.Load(file);
            if (matrix == null || !matrix.IsValidFor(efficiency.Geometry))
            {
                Console.Error.WriteLine("МЕРИТЬ НЕЧЕМ: матрица {0} {1}", file,
                                        matrix == null ? "не читается этой сборкой" : "негодна для геометрии кривой (клеймо)");
                return null;
            }

            efficiency.EmbeddedMatrix = EmbeddedResponseMatrix.FromFile(efficiency.Guid, file, matrix);
            ResponseMatrix carried = ResponseMatrixStore.Resolve(efficiency, out refusal, out format, out source,
                                                                 out spectrumRefusal);
            if (carried == null || source != ResponseMatrixSource.Spectrum)
            {
                Console.Error.WriteLine("МЕРИТЬ НЕЧЕМ: матрица из --store положена в кривую, но разбор её не берёт ({0}; {1})",
                                        source, spectrumRefusal);
                return null;
            }

            return "--store " + file + " → в память блоком матрицы из файла спектра (AMBER202), клеймо " + matrix.Stamp;
        }

        static ResultData Load(string path, NuclideDefinitionManager nuclides)
        {
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }

            ResultData rd = file.ResultDataList[0];
            EnergySpectrum s = rd.EnergySpectrum;
            if (s != null && s.Spectrum != null && s.TotalPulseCount == 0)
            {
                long total = 0;
                for (int i = 0; i < s.Spectrum.Length; i++) total += s.Spectrum[i];
                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
            }

            Console.WriteLine("прибор: {0}", ProbeDeviceConfig.Attach(rd));
            if (rd.FwhmCalibration == null && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                if (cfg.FwhmCalibration == null && rd.EnergySpectrum != null)
                {
                    cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, rd.EnergySpectrum.EnergyCalibration);
                }

                if (cfg.FwhmCalibration != null) rd.FwhmCalibration = cfg.FwhmCalibration.Clone();
            }

            if (rd.SampleInfo == null) rd.SampleInfo = new SampleInfoData();
            rd.DetectedPeaks = new PeakDetector().DetectPeak(
                rd, BackgroundMode.Invisible, SmoothingMethod.None, nuclides.ActiveSet, nuclides.NuclideDefinitions);
            return rd;
        }
    }
}
