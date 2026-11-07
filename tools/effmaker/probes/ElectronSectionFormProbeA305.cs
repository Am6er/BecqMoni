using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// (`AMBER305`, 05.11.2026) Окно «Матрица отклика…» строит раздел электрона пробы рецептом склада
// (решение Amber вопросником, дословно: «Строить в окне (Рекомендую)»).
//
//   ElectronSectionFormProbeA305 --mode=window --scene=<.in> --store=<.rmx склада> [--cpu] [--threads=N] [--out=<файл>]
//       окно над фотонной частью склада (раздел снят) считает заново; матрица против склада
//       побитно: клеймо, тело, раздел строка к строке; ход полосы и подпись фазы.
//   --mode=small --scene=<.in> [--cpu] [--threads=N] [--keep=<файл .rmx>]
//       маленькая фотонная часть и полный раздел: время раздела; --keep — матрицу на диск.
//   --mode=carry --scene=<.in> [--cpu]          перенос: только раздел того же клейма.
//   --mode=cancel --scene=<.in> [--cpu]         отмена посреди раздела.
//   --mode=old --scene=<.in> --store=<.rmx>     чтение форматов 11 и 12, строка подробностей.
//   --mode=iso --scene=<.in>                    сцена изотропного поля: раздела нет и отказа нет.
//   --mode=failure --scene=<.in>                отказ раздела не снимает фотонную матрицу.
//   --mode=recipe --scene=<.in> --store=<.rmx>  клеймо рецепта против клейма склада, поле к полю.
//   --mode=diff --a=<.rmx> --b=<.rmx>           разделы двух файлов строка к строке (код 1 — не равны).
//   --mode=gpufail --scene=<.in> [--ui=en|ru]   отказ GPU на фазе раздела: язык окна и совет снять галку (`Н7`).
//   --mode=texts [--ui=en|ru]                   пять строк раздела на языке окна: ключ, подстановка, влезает.
//   --mode=config --device=<имя> --curve=<имя>  окно над кривой портативной конфигурации: счёт, «Сохранить», склад.
//
// Окно НЕ показывается: форма строится, кнопки зовутся отражением, продолжение `await`
// прокачивается `Application.DoEvents` (приём `MatrixWindowProbeP291`). Склад — в каталоге пробы
// (портативная раскладка); настоящий `%AppData%\BecqMoni` проба отказывается трогать.
//
// Положительный контроль — мутант «снова только перенос» (`AddElectronSection` без счёта,
// журнал `handover/handover-2026-11-05-fix305.md`): `--mode=window` на нём обязан дать код 1.
//
// Код возврата: 0 — сошлось; 1 — расхождения; 2 — ключи или среда.
static class ElectronSectionFormProbeA305
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;

    static int failures;
    static string outPath;

    static void Say(string s)
    {
        Console.WriteLine(s);
        if (outPath != null)
        {
            File.AppendAllText(outPath, s + Environment.NewLine, new UTF8Encoding(false));
        }
    }

    static void Check(bool ok, string what)
    {
        if (!ok) failures++;
        Say((ok ? "   OK  " : "   ОТКАЗ ") + what);
    }

    static string Fmt(string format, params object[] args)
    {
        return string.Format(CultureInfo.InvariantCulture, format, args);
    }

    // ------------------------------------------------------------------
    // Окно без показа
    // ------------------------------------------------------------------

    sealed class Win : IDisposable
    {
        public readonly ResponseMatrixForm Form;
        public int MaxBar;
        public int BackSteps;
        public int LastBar;
        public int BarAtFirstSection = -1;
        public double FirstSectionSeconds = -1;
        public int SectionLabels;
        public string LastSectionLabel;
        public int FullBarsBeforeEnd;
        public double Seconds;
        readonly Stopwatch clock = new Stopwatch();

        public Win(EfficiencyConfigData config)
        {
            this.Form = new ResponseMatrixForm(config);
        }

        public object Get(string name)
        {
            FieldInfo f = typeof(ResponseMatrixForm).GetField(name, F);
            if (f == null) throw new MissingFieldException("ResponseMatrixForm", name);
            return f.GetValue(this.Form);
        }

        public NumericUpDown Box(string name) { return (NumericUpDown)this.Get(name); }
        public ResponseMatrix Computed { get { return (ResponseMatrix)this.Get("computed"); } }
        public string Details { get { return (string)this.Get("detailsText"); } }
        public bool Busy { get { return (bool)this.Get("busy"); } }
        public string ProgressText { get { return ((Label)this.Get("progressLabel")).Text; } }
        public string StateText { get { return ((Label)this.Get("stateLabel")).Text; } }
        public string VersionsText { get { return ((Label)this.Get("versionsLabel")).Text; } }
        public ProgressBar Bar { get { return (ProgressBar)this.Get("progressBar"); } }
        public bool SaveEnabled { get { return ((Button)this.Get("saveButton")).Enabled; } }
        public bool ComputeEnabled { get { return ((Button)this.Get("computeButton")).Enabled; } }

        public bool UseGpu
        {
            get { return (bool)typeof(ResponseMatrixForm).GetProperty("UseGpu", F).GetValue(this.Form, null); }
        }

        public void Call(string method)
        {
            typeof(ResponseMatrixForm).GetMethod(method, F).Invoke(this.Form, new object[] { null, EventArgs.Empty });
        }

        public void Quick(int nodes, int histories, int threads)
        {
            this.Box("minEnergyBox").Value = 30;
            this.Box("maxEnergyBox").Value = 3000;
            this.Box("nodesBox").Value = nodes;
            this.Box("binBox").Value = 2;
            this.Box("historiesBox").Value = histories;
            this.Box("threadsBox").Value = threads;
        }

        /// <summary>«Посчитать» и прокачка до конца; <paramref name="cancelWhen"/> вернул истину — «Отмена».</summary>
        public bool Compute(double limitSeconds, Func<Win, bool> cancelWhen)
        {
            if (!(SynchronizationContext.Current is WindowsFormsSynchronizationContext))
            {
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            }

            this.MaxBar = 0;
            this.BackSteps = 0;
            this.LastBar = 0;
            this.BarAtFirstSection = -1;
            this.FirstSectionSeconds = -1;
            this.SectionLabels = 0;
            this.LastSectionLabel = null;
            this.FullBarsBeforeEnd = 0;
            this.clock.Restart();
            this.Call("ComputeClick");
            bool cancelled = false;
            string lastLabel = null;
            while (this.Busy)
            {
                if (this.clock.Elapsed.TotalSeconds > limitSeconds)
                {
                    Say(Fmt("   счёт не кончился за {0:F0} с", limitSeconds));
                    return false;
                }

                Application.DoEvents();
                if (!this.Busy) break;
                int bar = this.Bar.Value;
                if (bar < this.LastBar) this.BackSteps++;
                this.LastBar = bar;
                this.MaxBar = Math.Max(this.MaxBar, bar);
                if (bar >= this.Bar.Maximum) this.FullBarsBeforeEnd++;
                string label = this.ProgressText;
                if (!string.Equals(label, lastLabel, StringComparison.Ordinal))
                {
                    lastLabel = label;
                    if (IsSectionLabel(label))
                    {
                        if (this.BarAtFirstSection < 0)
                        {
                            this.BarAtFirstSection = bar;
                            this.FirstSectionSeconds = this.clock.Elapsed.TotalSeconds;
                        }

                        this.SectionLabels++;
                        this.LastSectionLabel = label;
                    }
                }

                if (!cancelled && cancelWhen != null && cancelWhen(this))
                {
                    cancelled = true;
                    this.Call("CancelClick");
                }

                Thread.Sleep(5);
            }

            Application.DoEvents();
            this.Seconds = this.clock.Elapsed.TotalSeconds;
            return true;
        }

        public void Dispose() { this.Form.Dispose(); }
    }

    static bool IsSectionLabel(string label)
    {
        return label != null && (label.IndexOf("lectron section", StringComparison.Ordinal) >= 0
                                 || label.IndexOf("аздел электрон", StringComparison.Ordinal) >= 0);
    }

    static List<ROIEfficiencyData> StubCurve()
    {
        return new List<ROIEfficiencyData>
        {
            new ROIEfficiencyData { Energy = 5, Efficiency = 0.5, ErrorPercent = 1 },
            new ROIEfficiencyData { Energy = 662, Efficiency = 0.1, ErrorPercent = 1 },
            new ROIEfficiencyData { Energy = 3000, Efficiency = 0.02, ErrorPercent = 1 },
        };
    }

    static void SetGpu(bool on)
    {
        GlobalConfigManager manager = GlobalConfigManager.GetInstance();
        // В памяти и в той же позиции, что у галки: окно не запишет то, что не изменилось.
        manager.GlobalConfig.UseGpuForResponseMatrix = on;
    }

    static string NewGuid(string tag)
    {
        return "a305-" + tag + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
    }

    // ------------------------------------------------------------------
    // Сверка разделов
    // ------------------------------------------------------------------

    sealed class RowStats
    {
        public int Rows, Equal;
        public long Bins, DiffBins;
        public double MaxBinRel, MaxSumRel;
    }

    static void CompareRow(float[] a, float[] b, RowStats s)
    {
        s.Rows++;
        int n = Math.Max(a != null ? a.Length : 0, b != null ? b.Length : 0);
        bool equal = true;
        double sa = 0.0, sb = 0.0, top = 0.0;
        for (int i = 0; i < n; i++)
        {
            double va = a != null && i < a.Length ? a[i] : 0.0;
            double vb = b != null && i < b.Length ? b[i] : 0.0;
            sa += va;
            sb += vb;
            top = Math.Max(top, Math.Max(Math.Abs(va), Math.Abs(vb)));
        }

        for (int i = 0; i < n; i++)
        {
            float va = a != null && i < a.Length ? a[i] : 0f;
            float vb = b != null && i < b.Length ? b[i] : 0f;
            s.Bins++;
            if (!va.Equals(vb))
            {
                equal = false;
                s.DiffBins++;
                if (top > 0.0) s.MaxBinRel = Math.Max(s.MaxBinRel, Math.Abs(va - vb) / top);
            }
        }

        if (equal) s.Equal++;
        double big = Math.Max(Math.Abs(sa), Math.Abs(sb));
        if (big > 0.0) s.MaxSumRel = Math.Max(s.MaxSumRel, Math.Abs(sa - sb) / big);
    }

    static RowStats Print(string name, RowStats s)
    {
        Say(Fmt("   {0,-14} строк {1,4}, побитно равных {2,4}; бинов {3,8}, различных {4,6}; худший бин {5:E2} от вершины строки; худшая сумма строки {6:E2}",
                name, s.Rows, s.Equal, s.Bins, s.DiffBins, s.MaxBinRel, s.MaxSumRel));
        return s;
    }

    /// <summary>Раздел A против раздела B строка к строке. Истина — совпали все общие строки побитно.</summary>
    static bool CompareSections(ResponseElectronSection a, ResponseElectronSection b, double sumTolerance)
    {
        Say("   клеймо A: " + a.Stamp);
        Say("   клеймо B: " + b.Stamp);
        Say(Fmt("   движок A {0}, B {1}; свет на кэВ A {2:R}, B {3:R}; узлов {4}/{5}, линий {6}/{7}, ветвей {8}/{9}",
                a.Engine, b.Engine, a.LightPerKev, b.LightPerKev, a.NodeCount, b.NodeCount, a.LineCount, b.LineCount, a.BranchCount, b.BranchCount));
        Check(a.NodeCount == b.NodeCount, "число узлов то же");
        var electron = new RowStats();
        var positron = new RowStats();
        for (int i = 0; i < Math.Min(a.NodeCount, b.NodeCount); i++)
        {
            Check2(a.Energies[i] == b.Energies[i], "энергия узла " + i);
            CompareRow(a.Electron[i], b.Electron[i], electron);
            CompareRow(a.Positron != null ? a.Positron[i] : null, b.Positron != null ? b.Positron[i] : null, positron);
        }

        var lines = new RowStats();
        int linesAlone = 0;
        for (int j = 0; j < a.LineCount; j++)
        {
            int k = b.LineEnergies != null ? Array.IndexOf(b.LineEnergies, a.LineEnergies[j]) : -1;
            if (k < 0) { linesAlone++; continue; }
            CompareRow(a.LineElectron[j], b.LineElectron[k], lines);
        }

        var events = new RowStats();
        var gammas = new RowStats();
        int branchesAlone = 0;
        for (int j = 0; j < a.BranchCount; j++)
        {
            int k = b.BranchIndex(a.BranchNuclides[j], a.BranchEndpoints[j]);
            if (k < 0) { branchesAlone++; continue; }
            CompareRow(a.BranchEvent[j], b.BranchEvent[k], events);
            CompareRow(a.BranchGamma[j], b.BranchGamma[k], gammas);
        }

        Print("e-", electron);
        Print("e+", positron);
        Print("линии", lines);
        Print("ветви, событие", events);
        Print("ветви, каскад", gammas);
        Say(Fmt("   общих линий {0}, только в A {1}, только в B {2}; общих ветвей {3}, только в A {4}, только в B {5}",
                lines.Rows, linesAlone, b.LineCount - lines.Rows, events.Rows, branchesAlone, b.BranchCount - events.Rows));
        bool bitwise = electron.Rows == electron.Equal && positron.Rows == positron.Equal && lines.Rows == lines.Equal
                       && events.Rows == events.Equal && gammas.Rows == gammas.Equal;
        double worst = Math.Max(Math.Max(electron.MaxSumRel, positron.MaxSumRel), Math.Max(lines.MaxSumRel, Math.Max(events.MaxSumRel, gammas.MaxSumRel)));
        Say(Fmt("   итог разделов: {0}; худшая сумма строки {1:E2}", bitwise ? "ВСЕ ОБЩИЕ СТРОКИ ПОБИТНО РАВНЫ" : "есть различия в битах", worst));
        Check(bitwise || worst <= sumTolerance, Fmt("строки равны побитно или суммы строк сходятся до {0:E1}", sumTolerance));
        return bitwise;
    }

    /// <summary>
    /// Фотонные строки двух матриц по каналам. Счёт GPU складывает float параллельно, и порядок
    /// сложения от запуска к запуску разный: биты младшего разряда расходятся и у двух запусков одной
    /// сборки. Судится относительная разница суммы строки и бина, а не отпечаток.
    /// </summary>
    static void ComparePhotonRows(ResponseMatrix a, ResponseMatrix b)
    {
        Say(Fmt("   фотонная часть: каналов {0}/{1}, узлов {2}/{3}; историй потрачено {4}/{5}; шум конт. взвеш. {6:R}/{7:R}",
                a.ChannelRows.Length, b.ChannelRows.Length, a.NodeCount, b.NodeCount, a.HistoriesSpent, b.HistoriesSpent,
                a.ContinuumWeightedError, b.ContinuumWeightedError));
        Check(a.NodeCount == b.NodeCount && a.ChannelRows.Length == b.ChannelRows.Length, "сетка и каналы фотонной части те же");
        var all = new RowStats();
        for (int c = 0; c < Math.Min(a.ChannelRows.Length, b.ChannelRows.Length); c++)
        {
            var s = new RowStats();
            for (int i = 0; i < Math.Min(a.NodeCount, b.NodeCount); i++)
            {
                CompareRow(a.ChannelRows[c][i], b.ChannelRows[c][i], s);
                CompareRow(a.ChannelRows[c][i], b.ChannelRows[c][i], all);
            }

            Print("канал " + ((EfficiencySimulator.ResponseChannel)c), s);
        }

        Print("всего", all);
        // (`AMBER311`) Строка обратного рассеяния — так же; у прежнего склада её нет.
        if (a.HasBackscatter && b.HasBackscatter)
        {
            var hump = new RowStats();
            for (int i = 0; i < Math.Min(a.NodeCount, b.NodeCount); i++)
            {
                CompareRow(a.BackscatterRows[i], b.BackscatterRows[i], hump);
            }

            Print("обр. рассеяние", hump);
            Check(hump.MaxSumRel <= 1e-5, Fmt("суммы строк обратного рассеяния сходятся до 1.0E-005 (худшая {0:E2})", hump.MaxSumRel));
        }
        Check(all.MaxSumRel <= 1e-5, Fmt("суммы фотонных строк сходятся до 1.0E-005 (худшая {0:E2})", all.MaxSumRel));
    }

    static void Check2(bool ok, string what)
    {
        if (!ok) Check(false, what);
    }

    // ------------------------------------------------------------------
    // Режимы
    // ------------------------------------------------------------------

    /// <summary>Окно над фотонной частью склада: всё как у человека, матрица против склада.</summary>
    static void ModeWindow(string scene, string storePath, bool cpu, int threads)
    {
        Say("== окно против склада: " + Path.GetFileName(storePath) + (cpu ? ", ЦП" : ", GPU"));
        GeometryModel geometry = GeometryModel.Load(scene);
        MatrixRefusal refusal;
        int format;
        ResponseMatrix store = ResponseMatrix.Load(storePath, out refusal, out format);
        if (store == null) throw new InvalidOperationException("склад не прочитан: " + refusal);
        Say(Fmt("   склад: формат {0}, годен для сцены {1}; клеймо {2}", format, store.IsValidFor(geometry), store.Stamp));
        Check(store.IsValidFor(geometry) && store.ElectronSection != null, "склад годен для сцены и несёт раздел");

        // Прежняя матрица окна — фотонная часть склада, раздел снят: так у пользователя; поля окна — её.
        string guid = NewGuid("win");
        ResponseMatrix photon = ResponseMatrix.Load(storePath, out refusal, out format);
        photon.ElectronSection = null;
        photon.Save(ResponseMatrixStore.PathOf(guid, ResponseMatrixSource.Store));

        SetGpu(!cpu);
        var config = new EfficiencyConfigData("проба AMBER305") { Guid = guid, Geometry = geometry, Curve = StubCurve() };
        using (var w = new Win(config))
        {
            Check(w.UseGpu == !cpu, "счёт идёт там, где заказано (GPU " + w.UseGpu + ")");
            if (threads > 0) w.Box("threadsBox").Value = threads;
            Say(Fmt("   поля окна: {0}…{1} кэВ, узлов {2}, бин {3}, историй {4}, потоков {5}",
                    w.Box("minEnergyBox").Value, w.Box("maxEnergyBox").Value, w.Box("nodesBox").Value, w.Box("binBox").Value,
                    w.Box("historiesBox").Value, w.Box("threadsBox").Value));
            Check(w.Compute(3600, null), "счёт дошёл до конца");
            ResponseMatrix m = w.Computed;
            Say("   строка хода после счёта: " + w.ProgressText);
            Say(Fmt("   всего {0:F1} с; раздел начался на {1:F1} с при полосе {2} из {3}; подписей раздела {4}, последняя «{5}»; шагов полосы назад {6}",
                    w.Seconds, w.FirstSectionSeconds, w.BarAtFirstSection, w.Bar.Maximum, w.SectionLabels, w.LastSectionLabel, w.BackSteps));
            Check(m != null, "матрица посчитана");
            if (m == null) return;
            Check(w.BackSteps == 0, "полоса не пятится");
            Check(w.SectionLabels > 0, "фаза раздела названа в строке хода");
            Check(m.ElectronSection != null, "у матрицы окна ЕСТЬ раздел электрона");
            if (m.ElectronSection == null) return;
            Check(string.Equals(m.Stamp, store.Stamp, StringComparison.Ordinal), "клеймо фотонной части = склад");
            w.Call("SaveClick");
            string pending = ResponseMatrixStore.PathOf(guid, ResponseMatrixSource.Pending);
            ResponseMatrix back = ResponseMatrix.Load(pending, out refusal, out format);
            Check(back != null && format == ResponseMatrix.FormatVersion, "записанная матрица читается, формат " + format);
            if (back == null) return;
            ElectronSectionRefusal why;
            Check(back.ElectronSectionFor(geometry, out why) != null, "разбор берёт раздел записанной матрицы (" + why + ")");
            Say(Fmt("   отпечаток тела: окно {0}, склад {1}", back.BodyFingerprint, store.BodyFingerprint));
            Say(string.Equals(back.BodyFingerprint, store.BodyFingerprint, StringComparison.Ordinal) ? "   фотонное тело = склад побитно" : "   фотонное тело ≠ склад в битах: разница — ниже, по строкам");
            Say(Fmt("   длины файлов: окно {0} байт, склад {1} байт", new FileInfo(pending).Length, new FileInfo(storePath).Length));
            ComparePhotonRows(back, store);
            CompareSections(back.ElectronSection, store.ElectronSection, 1e-4);
            Check(string.Equals(back.ElectronSection.Stamp, store.ElectronSection.Stamp, StringComparison.Ordinal), "клеймо раздела = склад");
        }
    }

    /// <summary>Малая фотонная часть и раздел в штатных историях: время и, по ключу, матрица на диск.</summary>
    static void ModeSmall(string scene, bool cpu, int threads, string keep)
    {
        Say("== малая фотонная часть, полный раздел: " + Path.GetFileName(scene) + (cpu ? ", ЦП" : ", GPU"));
        SetGpu(!cpu);
        var config = new EfficiencyConfigData("проба AMBER305") { Guid = NewGuid("small"), Geometry = GeometryModel.Load(scene), Curve = StubCurve() };
        using (var w = new Win(config))
        {
            Check(w.UseGpu == !cpu, "счёт идёт там, где заказано");
            w.Quick(10, 20000, threads > 0 ? threads : 8);
            long ticksAtStart = Stopwatch.GetTimestamp();
            int hiccups = 0;
            double worst = 0.0;
            long last = ticksAtStart;
            bool done = w.Compute(3600, _ =>
            {
                long now = Stopwatch.GetTimestamp();
                double gap = (double)(now - last) / Stopwatch.Frequency;
                last = now;
                worst = Math.Max(worst, gap);
                if (gap > 1.0) hiccups++;
                return false;
            });
            Check(done, "счёт дошёл до конца");
            ResponseMatrix m = w.Computed;
            Check(m != null && m.ElectronSection != null, "у матрицы есть раздел");
            Say(Fmt("   всего {0:F1} с, раздел начался на {1:F1} с (потоков {2}), раздел {3:F1} с; строк {4}+{5}+{6}; подписей {7}; шагов полосы назад {8}",
                    w.Seconds, w.FirstSectionSeconds, w.Box("threadsBox").Value, w.Seconds - w.FirstSectionSeconds,
                    m != null && m.ElectronSection != null ? m.ElectronSection.NodeCount : 0,
                    m != null && m.ElectronSection != null ? m.ElectronSection.LineCount : 0,
                    m != null && m.ElectronSection != null ? m.ElectronSection.BranchCount : 0, w.SectionLabels, w.BackSteps));
            Say(Fmt("   поток окна: худший промежуток прокачки {0:F3} с, дольше секунды {1} раз", worst, hiccups));
            Check(worst < 2.0, "окно не зависает: поток окна свободен (промежутки прокачки меньше 2 с)");
            Check(w.BackSteps == 0, "полоса не пятится");
            Say("   строка хода: " + w.ProgressText);
            if (keep != null && m != null)
            {
                m.Save(keep);
                Say("   записано: " + keep);
            }
        }
    }

    static void ModeCarry(string scene, bool cpu)
    {
        Say("== перенос: только раздел того же клейма" + (cpu ? ", ЦП" : ", GPU"));
        ElectronSectionRecipe.Histories = 2000;
        SetGpu(!cpu);
        GeometryModel geometry = GeometryModel.Load(scene);
        string guid = NewGuid("carry");
        var config = new EfficiencyConfigData("проба AMBER305") { Guid = guid, Geometry = geometry, Curve = StubCurve() };
        ResponseElectronSection first;
        using (var w = new Win(config))
        {
            w.Quick(10, 20000, 8);
            Check(w.Compute(1800, null) && w.Computed != null && w.Computed.ElectronSection != null, "(а) первый счёт: раздел посчитан");
            first = w.Computed.ElectronSection;
            Check(w.SectionLabels > 0, "(а) раздел шёл счётом");
            Check(w.Compute(1800, null) && w.Computed != null, "(б) повторный счёт в том же окне");
            Check(object.ReferenceEquals(w.Computed.ElectronSection, first), "(б) несохранённый раздел того же клейма взят без счёта");
            Check(w.SectionLabels == 0, "(б) строк раздела не считалось");
            w.Call("SaveClick");
        }

        using (var w = new Win(config))
        {
            w.Quick(10, 20000, 8);
            Check(w.Compute(1800, null) && w.Computed != null && w.Computed.ElectronSection != null, "(в) новое окно, раздел с диска");
            Check(w.Computed.ElectronSection.CreatedUtc == first.CreatedUtc && w.SectionLabels == 0, "(в) раздел с диска того же клейма взят без счёта");
        }

        // (е) то же клеймо матрицы, иное число историй рецепта: прежний раздел не переносится.
        ElectronSectionRecipe.Histories = 3000;
        using (var w = new Win(config))
        {
            w.Quick(10, 20000, 8);
            Check(w.Compute(1800, null) && w.Computed != null && w.Computed.ElectronSection != null, "(е) иное n рецепта");
            Check(w.SectionLabels > 0 && w.Computed.ElectronSection.StampField("n") == "3000", "(е) раздел посчитан заново, n=" + w.Computed.ElectronSection.StampField("n"));
        }

        ElectronSectionRecipe.Histories = 2000;

        // (г) другая сетка фотонной части — другое клеймо матрицы: раздел негоден.
        using (var w = new Win(config))
        {
            w.Quick(12, 20000, 8);
            Check(w.Compute(1800, null) && w.Computed != null && w.Computed.ElectronSection != null, "(г) другое клеймо матрицы");
            Check(w.SectionLabels > 0 && w.Computed.ElectronSection.ForMatrixStamp == w.Computed.Stamp, "(г) раздел посчитан заново и для новой матрицы");
        }

        // (д) то же клеймо матрицы, но другая капсула: фотонное клеймо её не видит, прежний перенос
        // по `ForMatrixStamp` взял бы чужой раздел. Сцена с капсулой — только точечная.
        if (geometry.SourceType == GeometrySourceType.Point)
        {
            GeometryModel other = geometry.Clone();
            other.SourceCapsuleThickness = other.SourceCapsuleThickness > 0.0 ? other.SourceCapsuleThickness * 2.0 : 0.1;
            var config2 = new EfficiencyConfigData("проба AMBER305") { Guid = guid, Geometry = other, Curve = StubCurve() };
            using (var w = new Win(config2))
            {
                w.Quick(10, 20000, 8);
                Check(w.Compute(1800, null) && w.Computed != null && w.Computed.ElectronSection != null, "(д) другая капсула");
                ElectronSectionRefusal why;
                Check(w.Computed.ElectronSectionFor(other, out why) != null, "(д) разбор берёт раздел для новой капсулы (" + why + ")");
                Say("   капсула: была «" + first.Capsule + "», стала «" + w.Computed.ElectronSection.Capsule + "»; строк раздела считалось: " + w.SectionLabels);
            }
        }
    }

    static void ModeCancel(string scene, bool cpu)
    {
        Say("== отмена посреди раздела" + (cpu ? ", ЦП" : ", GPU"));
        SetGpu(!cpu);
        var config = new EfficiencyConfigData("проба AMBER305") { Guid = NewGuid("cancel"), Geometry = GeometryModel.Load(scene), Curve = StubCurve() };
        using (var w = new Win(config))
        {
            w.Quick(10, 20000, 8);
            double cancelledAt = -1.0;
            var clock = Stopwatch.StartNew();
            Check(w.Compute(1800, x =>
            {
                string label = x.ProgressText;
                if (!IsSectionLabel(label) || x.Bar.Value <= x.Bar.Maximum / 2) return false;
                cancelledAt = clock.Elapsed.TotalSeconds;
                return true;
            }), "(а) счёт кончился");
            double delay = clock.Elapsed.TotalSeconds - cancelledAt;
            Say(Fmt("   отмена на {0:F1} с («{1}»), окно свободно через {2:F2} с; строка хода «{3}»", cancelledAt, w.LastSectionLabel, delay, w.ProgressText));
            Check(cancelledAt > 0, "(а) отмена пришлась на раздел");
            Check(w.Computed == null, "(а) матрицы нет: отмена не оставила половины");
            Check(w.Bar.Value == 0 && !w.SaveEnabled && w.ComputeEnabled, "(а) полоса 0, «Сохранить» погашена, «Посчитать» живая");
            Check(w.ProgressText == Resources.ResponseMatrixCancelled, "(а) строка хода говорит об отмене");
            Check(delay < 30.0, "(а) отмена срабатывает быстро");

            // (б) прежний результат возвращается (П291, Н6): раздел другого числа историй — другое клеймо.
            ElectronSectionRecipe.Histories = 2000;
            Check(w.Compute(1800, null) && w.Computed != null && w.Computed.ElectronSection != null, "(б) быстрый счёт: результат есть");
            ResponseMatrix kept = w.Computed;
            ElectronSectionRecipe.Histories = ElectronSectionRecipe.StoreHistories;
            cancelledAt = -1.0;
            clock.Restart();
            Check(w.Compute(1800, x =>
            {
                if (!IsSectionLabel(x.ProgressText)) return false;
                cancelledAt = clock.Elapsed.TotalSeconds;
                return true;
            }), "(б) счёт кончился");
            Check(cancelledAt > 0, "(б) отмена пришлась на раздел");
            Check(object.ReferenceEquals(w.Computed, kept) && w.SaveEnabled, "(б) прежний несохранённый результат на месте, «Сохранить» живая");
        }
    }

    static void ModeOld(string scene, string storePath)
    {
        Say("== чтение форматов 11 и 12");
        GeometryModel geometry = GeometryModel.Load(scene);
        MatrixRefusal refusal;
        int format;
        ResponseMatrix store = ResponseMatrix.Load(storePath, out refusal, out format);
        Check(store != null && format == 12, "склад читается форматом 12");
        string guid12 = NewGuid("f12"), guid11 = NewGuid("f11");
        File.Copy(storePath, ResponseMatrixStore.PathOf(guid12, ResponseMatrixSource.Store));
        store.ElectronSection = null;
        store.Save(ResponseMatrixStore.PathOf(guid11, ResponseMatrixSource.Store));
        int headerFormat, headerPhysics;
        ResponseMatrix.PeekVersions(ResponseMatrixStore.PathOf(guid11, ResponseMatrixSource.Store), out headerFormat, out headerPhysics);
        Check(headerFormat == 11, "матрица без раздела пишется форматом 11");
        foreach (string guid in new[] { guid11, guid12 })
        {
            var config = new EfficiencyConfigData("проба AMBER305") { Guid = guid, Geometry = geometry, Curve = StubCurve() };
            using (var w = new Win(config))
            {
                Say("   " + guid + ": состояние «" + w.StateText + "»; версии «" + w.VersionsText + "»");
                string[] lines = w.Details.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                string last = lines[lines.Length - 1];
                Say("     последняя строка подробностей: «" + last + "»");
                MatrixRefusal r;
                int f;
                ResponseMatrix read = ResponseMatrix.Load(ResponseMatrixStore.PathOf(guid, ResponseMatrixSource.Store), out r, out f);
                ElectronSectionRefusal why;
                bool usable = read.ElectronSectionFor(geometry, out why) != null;
                Say("     разбор: раздел " + (usable ? "годен" : "не взят, " + why));
                string needs = Resources.ResourceManager.GetString("ResponseMatrixStateNeedsSection", Resources.Culture);
                Check(w.StateText == (usable ? Resources.ResponseMatrixStateValid : needs), "окно и разбор говорят одно: " + (usable ? "годна" : "нужен пересчёт"));
                Check(usable == (last.IndexOf("80", StringComparison.Ordinal) >= 0), "строка подробностей о том же");
                Check(w.ComputeEnabled, "«Пересчитать» доступна");
            }
        }

        // Сторож зовёт режим в каталоге проб: своих файлов там не оставлять.
        File.Delete(ResponseMatrixStore.PathOf(guid11, ResponseMatrixSource.Store));
        File.Delete(ResponseMatrixStore.PathOf(guid12, ResponseMatrixSource.Store));
    }

    static void ModeIso(string scene)
    {
        Say("== сцена изотропного поля: раздела нет, отказа нет");
        SetGpu(false);
        GeometryModel g = GeometryModel.Load(scene);
        GeometryScenes.Iso(g);
        var config = new EfficiencyConfigData("проба AMBER305") { Guid = NewGuid("iso"), Geometry = g, Curve = StubCurve() };
        using (var w = new Win(config))
        {
            w.Quick(10, 20000, 8);
            Check(w.Compute(1800, null) && w.Computed != null, "матрица посчитана");
            Say("   строка хода: " + w.ProgressText);
            Check(w.Computed.ElectronSection == null && w.SectionLabels == 0, "раздела нет и он не считался");
            Check(w.ProgressText.IndexOf("ection", StringComparison.Ordinal) < 0 && w.ProgressText.IndexOf("аздел", StringComparison.Ordinal) < 0, "об отказе ни слова");
            Check(w.Bar.Value == w.Bar.Maximum, "полоса полна");
            Check(w.Details.IndexOf("lectron section", StringComparison.Ordinal) < 0 && w.Details.IndexOf("аздел электрон", StringComparison.Ordinal) < 0, "в подробностях строки о разделе нет");
            Check(w.Computed.WrittenFormatVersion == 11, "формат 11");
        }
    }

    static void ModeFailure(string scene)
    {
        Say("== отказ раздела не снимает фотонную матрицу");
        SetGpu(false);
        MethodInfo add = typeof(ResponseMatrixForm).GetMethod("AddElectronSection", BindingFlags.Static | BindingFlags.NonPublic);
        Check(add != null, "AddElectronSection найден");
        if (add == null) return;
        GeometryModel g = GeometryModel.Load(scene);
        var options = new ResponseMatrixOptions { MinEnergyKev = 30, MaxEnergyKev = 3000, NodeCount = 8, BinKev = 2, Histories = 20000, Threads = 8, ContinuumErrorTarget = 0.0 };
        ResponseMatrix m = ResponseMatrixBuilder.Build(g, options, null, CancellationToken.None);
        string stamp = m.Stamp;
        // Инъекция: клеймо матрицы не сходится со сценой — построитель раздела отказывает словами.
        ResponseMatrixOptions saved = m.Options;
        m.Stamp = stamp + "-поддельное";
        object reason = add.Invoke(null, new object[] { g, m, null, "a305-none", ResponseMatrixSource.Store, null, 4, null, CancellationToken.None });
        m.Stamp = stamp;
        Say("   причина: " + reason);
        Check(reason != null, "отказ назван причиной, а не брошен наружу");
        Check(m.ElectronSection == null && m.Stamp == stamp && m.Options == saved, "фотонная матрица не тронута, раздела нет");
        var cts = new CancellationTokenSource();
        cts.Cancel();
        try
        {
            add.Invoke(null, new object[] { g, m, null, "a305-none", ResponseMatrixSource.Store, null, 4, null, cts.Token });
            Check(false, "отмена не дошла");
        }
        catch (TargetInvocationException ex)
        {
            Check(ex.InnerException is OperationCanceledException, "отмена — исключением отмены: " + ex.InnerException.GetType().Name);
        }
    }

    /// <summary>
    /// `Н7` на фазе раздела: отказ устройства (`BQ_GPU_LAUNCH=0,2048`, ключ читает конструктор
    /// `RmGpu`) — текст на языке окна с советом снять галку, внутренний текст библиотеки только в журнале.
    /// </summary>
    static void ModeGpuFail(string scene, string ui)
    {
        Say("== отказ GPU на фазе раздела, язык " + ui);
        MethodInfo add = typeof(ResponseMatrixForm).GetMethod("AddElectronSection", BindingFlags.Static | BindingFlags.NonPublic);
        GeometryModel g = GeometryModel.Load(scene);
        var options = new ResponseMatrixOptions { MinEnergyKev = 30, MaxEnergyKev = 3000, NodeCount = 8, BinKev = 2, Histories = 20000, Threads = 8, ContinuumErrorTarget = 0.0 };
        ResponseMatrix m = ResponseMatrixBuilder.Build(g, options, null, CancellationToken.None);
        string stamp = m.Stamp;
        Environment.SetEnvironmentVariable("BQ_GPU_LAUNCH", "0,2048");
        object reason;
        try
        {
            using (var gpu = new RmGpu(RmGpu.DefaultPath, RmGpu.DefaultStackBytes))
            {
                reason = add.Invoke(null, new object[] { g, m, null, "a305-none", ResponseMatrixSource.Store, gpu, 8, null, CancellationToken.None });
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("BQ_GPU_LAUNCH", null);
        }

        string text = reason as string ?? "";
        Say("   причина в строке хода: «" + text + "»");
        string want = string.Format(CultureInfo.InvariantCulture, Resources.ResponseMatrixGpuFailed, Resources.ResponseMatrixUseGpu);
        Check(text == want, "текст окна с советом снять галку «" + Resources.ResponseMatrixUseGpu + "»");
        bool cyrillic = false;
        foreach (char c in text) cyrillic |= c >= '\u0400' && c <= '\u04FF';
        Check(cyrillic == (ui == "ru"), "на языке окна, внутреннего русского текста нет");
        Check(text.IndexOf("rm_", StringComparison.Ordinal) < 0, "имён библиотеки в окне нет");
        Check(m.ElectronSection == null && m.Stamp == stamp, "фотонная матрица на месте, раздела нет");
        string shown = string.Format(CultureInfo.InvariantCulture, Resources.ResponseMatrixDone, "2:17") + " "
                       + string.Format(CultureInfo.InvariantCulture, Resources.ResourceManager.GetString("ResponseMatrixSectionFailed", Resources.Culture), text);
        using (var form = new Form())
        {
            Size need = TextRenderer.MeasureText(shown, form.Font, new Size(536, int.MaxValue), TextFormatFlags.WordBreak);
            Check(need.Height <= 48, Fmt("влезает в строку хода: {0} из 48 px: «{1}»", need.Height, shown));
        }
    }

    static void ModeRecipe(string scene, string storePath)
    {
        Say("== рецепт окна против клейма склада");
        GeometryModel geometry = GeometryModel.Load(scene);
        MatrixRefusal refusal;
        int format;
        ResponseMatrix store = ResponseMatrix.Load(storePath, out refusal, out format);
        var clock = Stopwatch.StartNew();
        ElectronSectionRecipe recipe = ElectronSectionRecipe.FromLibrary(null);
        Say(Fmt("   рецепт собран за {0:F2} с; линий {1}, ветвей {2}", clock.Elapsed.TotalSeconds, recipe.Lines.Length, recipe.Branches.Count));
        clock.Restart();
        ElectronSectionRecipe.FromLibrary(null);
        Say(Fmt("   повторно, с кэшем: {0:F2} с", clock.Elapsed.TotalSeconds));
        string mine = recipe.StampFor(geometry, store);
        Say("   рецепт: " + mine);
        Say("   склад : " + store.ElectronSection.Stamp);
        foreach (string f in new[] { "esec", "phys", "for", "grid", "n", "seed", "e+", "bin", "lines", "br", "cap" })
        {
            var a = new ResponseElectronSection { Stamp = mine };
            string va = a.StampField(f), vb = store.ElectronSection.StampField(f);
            Say(Fmt("   {0,-6} {1}  рецепт {2}  склад {3}", f, va == vb ? "=" : "≠", va, vb));
        }

        Check(string.Equals(mine, store.ElectronSection.Stamp, StringComparison.Ordinal), "клеймо рецепта = клеймо склада");
    }

    /// <summary>Пять строк раздела на языке окна (`--ui=`): ключ есть, числа подставляются, текст влезает.</summary>
    static void ModeTexts(string ui)
    {
        Say("== строки раздела, язык " + ui);
        bool ru = ui == "ru";
        var form = new Form();
        var rows = new[]
        {
            new { Key = "ResponseMatrixStateNeedsSection", Args = new object[0], Height = 0 },
            new { Key = "ResponseMatrixSectionRows", Args = new object[] { 80, 85, 61 }, Height = 0 },
            new { Key = "ResponseMatrixSectionMissing", Args = new object[0], Height = 0 },
            new { Key = "ResponseMatrixSectionFailed", Args = new object[] { "InvalidOperationException: reason" }, Height = 48 },
            new { Key = "ResponseMatrixSectionProgress", Args = new object[] { 337, 337 }, Height = 48 },
            new { Key = "ResponseMatrixSectionGpuProgress", Args = new object[] { 337, 337 }, Height = 48 },
        };
        foreach (var r in rows)
        {
            string text = Resources.ResourceManager.GetString(r.Key, Resources.Culture);
            Check(!string.IsNullOrEmpty(text), r.Key + " есть");
            if (string.IsNullOrEmpty(text)) continue;
            string shown = string.Format(CultureInfo.InvariantCulture, text, r.Args);
            bool cyrillic = false;
            foreach (char c in shown) cyrillic |= c >= '\u0400' && c <= '\u04FF';
            Check(cyrillic == ru, (ru ? "по-русски: «" : "по-английски: «") + shown + "»");
            // Ширина окна 560 − 2·12; подробности растут сами, строка хода — 48 px.
            Size need = TextRenderer.MeasureText(shown, form.Font, new Size(536, int.MaxValue), TextFormatFlags.WordBreak);
            Say(Fmt("      высота {0} px при ширине 536", need.Height));
            if (r.Height > 0) Check(need.Height <= r.Height, Fmt("влезает в строку хода: {0} из {1} px", need.Height, r.Height));
        }

        form.Dispose();
    }

    static void ModeDiff(string pathA, string pathB)
    {
        MatrixRefusal ra, rb;
        int fa, fb;
        ResponseMatrix a = ResponseMatrix.Load(pathA, out ra, out fa);
        ResponseMatrix b = ResponseMatrix.Load(pathB, out rb, out fb);
        Check(a != null && b != null && a.ElectronSection != null && b.ElectronSection != null, "оба файла читаются и несут раздел");
        if (a == null || b == null || a.ElectronSection == null || b.ElectronSection == null) return;
        Say(Fmt("   A {0}: формат {1}, клеймо {2}, тело {3}", pathA, fa, a.Stamp, a.BodyFingerprint));
        Say(Fmt("   B {0}: формат {1}, клеймо {2}, тело {3}", pathB, fb, b.Stamp, b.BodyFingerprint));
        ComparePhotonRows(a, b);
        bool bitwise = CompareSections(a.ElectronSection, b.ElectronSection, 1e-4);
        Check(bitwise, "разделы побитно равны");
    }

    static void ModeConfig(string device, string curve, bool cpu)
    {
        Say("== окно над кривой конфигурации: " + device + " / " + curve);
        SetGpu(!cpu);
        DeviceConfigInfo found = null;
        foreach (DeviceConfigInfo info in DeviceConfigManager.GetInstance().DeviceConfigList)
        {
            if (string.Equals(info.Name, device, StringComparison.Ordinal)) found = info;
        }

        if (found == null) throw new InvalidOperationException("нет прибора " + device);
        EfficiencyConfigData eff = null;
        foreach (EfficiencyConfigData e in found.EfficiencyConfigs)
        {
            if (string.Equals(e.Name, curve, StringComparison.Ordinal)) eff = e;
        }

        if (eff == null) throw new InvalidOperationException("нет кривой " + curve);
        using (var w = new Win(eff))
        {
            Check(w.UseGpu == !cpu, "счёт идёт там, где заказано");
            Say("   прежде: «" + w.StateText + "»; " + w.VersionsText);
            Say(Fmt("   поля окна: {0}…{1} кэВ, узлов {2}, бин {3}, историй {4}, потоков {5}",
                    w.Box("minEnergyBox").Value, w.Box("maxEnergyBox").Value, w.Box("nodesBox").Value, w.Box("binBox").Value,
                    w.Box("historiesBox").Value, w.Box("threadsBox").Value));
            Check(w.Compute(3600, null), "счёт дошёл до конца");
            ResponseMatrix m = w.Computed;
            Check(m != null && m.ElectronSection != null, "у матрицы окна есть раздел");
            if (m == null || m.ElectronSection == null) return;
            Say(Fmt("   всего {0:F1} с; раздел начался на {1:F1} с; раздел {2:F1} с; строка хода «{3}»", w.Seconds, w.FirstSectionSeconds, w.Seconds - w.FirstSectionSeconds, w.ProgressText));
            w.Call("SaveClick");
            ResponseMatrixStore.CommitPending(new[] { eff.Guid });
            MatrixRefusal refusal;
            int format;
            ResponseMatrix back = ResponseMatrix.Load(ResponseMatrixStore.PathOf(eff.Guid, ResponseMatrixSource.Store), out refusal, out format);
            ElectronSectionRefusal why = ElectronSectionRefusal.Missing;
            Check(back != null && format == 12 && back.ElectronSectionFor(eff.Geometry, out why) != null, "на складе копии — формат 12, раздел годен (" + why + ")");
            Say("   клеймо раздела: " + m.ElectronSection.Stamp);
        }
    }

    // ------------------------------------------------------------------

    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string mode = null, scene = null, store = null, keep = null, pathA = null, pathB = null, device = null, curve = null, ui = "en";
        bool cpu = false;
        int threads = 0;
        foreach (string a in args)
        {
            if (a.StartsWith("--mode=", StringComparison.Ordinal)) mode = a.Substring(7);
            else if (a.StartsWith("--scene=", StringComparison.Ordinal)) scene = a.Substring(8);
            else if (a.StartsWith("--store=", StringComparison.Ordinal)) store = a.Substring(8);
            else if (a.StartsWith("--keep=", StringComparison.Ordinal)) keep = a.Substring(7);
            else if (a.StartsWith("--a=", StringComparison.Ordinal)) pathA = a.Substring(4);
            else if (a.StartsWith("--b=", StringComparison.Ordinal)) pathB = a.Substring(4);
            else if (a.StartsWith("--device=", StringComparison.Ordinal)) device = a.Substring(9);
            else if (a.StartsWith("--curve=", StringComparison.Ordinal)) curve = a.Substring(8);
            else if (a.StartsWith("--ui=", StringComparison.Ordinal)) ui = a.Substring(5);
            else if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
            else if (a.StartsWith("--threads=", StringComparison.Ordinal)) threads = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
            else if (a == "--cpu") cpu = true;
            else { Console.WriteLine("не знаю ключа: " + a); return 2; }
        }

        if (mode == null)
        {
            Console.WriteLine("нужен --mode=");
            return 2;
        }

        var culture = new CultureInfo(ui == "ru" ? "ru-RU" : "en-US");
        Thread.CurrentThread.CurrentUICulture = culture;
        Resources.Culture = culture;
        Application.EnableVisualStyles();
        GlobalConfigManager.GetInstance();
        string dir = ResponseMatrixStore.Directory;
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Say("exe:   " + typeof(ResponseMatrixForm).Assembly.Location);
        Say("склад: " + dir + ";  физика " + ResponseMatrix.PhysicsVersion + ", формат " + ResponseMatrix.FormatVersion);
        if (dir.StartsWith(appData, StringComparison.OrdinalIgnoreCase))
        {
            Say("⛔ склад пробы — настоящий %AppData%: проба нужна в портативном каталоге");
            return 2;
        }

        Directory.CreateDirectory(dir);
        try
        {
            switch (mode)
            {
                case "window": ModeWindow(scene, store, cpu, threads); break;
                case "small": ModeSmall(scene, cpu, threads, keep); break;
                case "carry": ModeCarry(scene, cpu); break;
                case "cancel": ModeCancel(scene, cpu); break;
                case "old": ModeOld(scene, store); break;
                case "iso": ModeIso(scene); break;
                case "failure": ModeFailure(scene); break;
                case "recipe": ModeRecipe(scene, store); break;
                case "diff": ModeDiff(pathA, pathB); break;
                case "texts": ModeTexts(ui); break;
                case "gpufail": ModeGpuFail(scene, ui); break;
                case "config": ModeConfig(device, curve, cpu); break;
                default: Console.WriteLine("не знаю режима: " + mode); return 2;
            }
        }
        catch (Exception ex)
        {
            failures++;
            Say("⛔ проба оборвалась: " + ex);
        }

        Say(failures == 0 ? "ВСЕ СОШЛИСЬ" : "⛔ РАСХОЖДЕНИЙ: " + failures.ToString(CultureInfo.InvariantCulture));
        return failures == 0 ? 0 : 1;
    }
}
