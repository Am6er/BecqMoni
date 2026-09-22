using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CascadeExclusiveProbe
{
    /// <summary>
    /// ИСКЛЮЧАЮЩИЕ ПАРТНЁРЫ В ВЫНОСЕ ИЗ ПИКА И ВЫХОД СЛИТОГО КЛЮЧА Kα
    /// (`AMBER59`, `AMBER60`, `AMBER61`; полоса П121, 22.09.2026).
    ///
    ///     cascadeexclusiveprobe --matrix=&lt;файл .rmx&gt; [--scint=NaI:Tl]
    ///                           [--nuclides=I-125,Ba-133] [--lines=I-125:27.2;Na-22:1274.5]
    ///                           [--angcorr=0|1]
    ///
    /// Три раздела, на ОДНОЙ матрице сцены (умолчание — то, что видит
    /// приложение: κ_pT в выносе ВКЛ, угловые корреляции — ключом; арбитр
    /// Geant4 гонялся без `corr`, потому умолчание здесь 0):
    ///
    /// **1. Выход слитого ключа (`AMBER59`).** У Kα1/Kα2 ближе 0.3 кэВ
    /// (Z ≲ 53: I-125 27.202/27.473, In-111 22.983/23.173, Sr-85 13.336/13.396)
    /// оба носителя ложатся под ОДИН ключ таблицы выходов, и обратная условная
    /// P(γ | Kα) = P(Kα | γ)·I(γ)/I(ключа) делится на выход ключа. Печатается
    /// выход ключа против суммы выходов линий K-серии, сошедшихся к нему, и
    /// партнёры ключа. Судится равенство: выход ключа = Σ линий под ним (до
    /// 1e-9 относительных). Положительный контроль — нуклид, у которого Kα1 и
    /// Kα2 НЕ сливаются (Ba-133: Cs Kα 30.625/30.973, разница 0.348): у него
    /// каждая линия — свой ключ, и равенство тривиально верно.
    ///
    /// **2. Два кванта 511 (`AMBER60`).** Для β⁺-излучателя печатается CF и
    /// вынос линии из <see cref="FsaCascadeSummer.LineNote"/>, ε_T(511) матрицы
    /// и два ожидания потери при q квантах-пар на событие: независимое
    /// `q·(1 − (1 − ε)²)` и несовместное (спина к спине, источник вне
    /// кристалла) `q·min(1, 2ε)`; какое из них счёт воспроизводит — видно
    /// числом. Ожидания считаются БЕЗ κ_pT — при точечном источнике κ(1274,
    /// 511) ≈ 1, и разница напечатана рядом.
    ///
    /// **3. Линии K-серии как партнёры (`AMBER61`).** Для названной линии
    /// печатается таблица партнёров с ε_T и три выживания: как считает код
    /// (<see cref="FsaCascadeSummer.LineNote.Loss"/>), произведение по всем
    /// партнёрам без κ и корреляций (реплика формулы `S144`) и то же, но K-линии
    /// одной серии сложены как исключающие (`1 − Σ pᵢεᵢ` по серии, остальные
    /// партнёры множителями). Разность двух последних и есть цена вопроса
    /// строки на ЭТОЙ сцене.
    ///
    /// Код возврата: 0 — раздел 1 сошёлся у всех названных; 1 — выход ключа
    /// не равен сумме линий под ним (дефект `AMBER59` или его рецидив);
    /// 2 — мерить нечем.
    /// </summary>
    static class Program
    {
        static void Say(string format, params object[] args)
        {
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, format, args));
        }

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string matrixFile = null;
            string scint = "NaI:Tl";
            int angcorr = 0;
            var nuclides = new List<string>();
            var lines = new List<string>();
            foreach (string a in args)
            {
                if (a.StartsWith("--matrix=", StringComparison.Ordinal)) matrixFile = a.Substring(9);
                else if (a.StartsWith("--scint=", StringComparison.Ordinal)) scint = a.Substring(8);
                else if (a == "--angcorr=0") angcorr = 0;
                else if (a == "--angcorr=1") angcorr = 1;
                else if (a.StartsWith("--nuclides=", StringComparison.Ordinal))
                {
                    nuclides.AddRange(a.Substring(11).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                }
                else if (a.StartsWith("--lines=", StringComparison.Ordinal))
                {
                    lines.AddRange(a.Substring(8).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            if (matrixFile == null || !File.Exists(matrixFile))
            {
                Console.Error.WriteLine("нужен --matrix=<файл .rmx> (копия сцены корпуса, не живой склад)");
                return 2;
            }

            if (nuclides.Count == 0)
            {
                nuclides.AddRange(new[] { "I-125", "I-123", "In-111", "Sr-85", "Ba-133", "Cs-137" });
            }

            if (lines.Count == 0)
            {
                lines.AddRange(new[] { "I-125:27.2", "Na-22:1274.537", "Ba-133:356.0129", "Ba-133:80.9979" });
            }

            MatrixRefusal refusal;
            int format;
            ResponseMatrix matrix = ResponseMatrix.Load(matrixFile, out refusal, out format);
            if (matrix == null)
            {
                Console.Error.WriteLine("матрица не прочитана: " + refusal);
                return 2;
            }

            Say("матрица: {0} (узлов {1}, историй {2})", Path.GetFileName(matrixFile),
                matrix.Energies.Length, matrix.Histories);
            Say("клеймо: {0}", matrix.Stamp);
            Say("сетка узлов: {0:F3} … {1:F1} кэВ; кристалл {2}; угловые корреляции {3}",
                matrix.Energies[0], matrix.Energies[matrix.Energies.Length - 1], scint,
                angcorr == 1 ? "ВКЛ" : "ВЫКЛ");
            Console.WriteLine();

            int bad = 0;
            bad += MergedKeys(matrix, scint, angcorr, nuclides);
            LinesReport(matrix, scint, angcorr, lines);

            if (!string.IsNullOrEmpty(FsaCascadeSummer.Notes))
            {
                Say("ПРИМЕЧАНИЕ БАЗЫ: {0}", FsaCascadeSummer.Notes);
            }

            if (!string.IsNullOrEmpty(FsaCascadeSummer.Failure))
            {
                Say("ОТКАЗ БАЗЫ: {0}", FsaCascadeSummer.Failure);
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "раздел 1: выход каждого ключа K равен сумме линий под ним — СОШЛОСЬ"
                                       : "⛔ раздел 1: выход ключа НЕ равен сумме линий под ним — расхождений " + bad);
            return bad == 0 ? 0 : 1;
        }

        /// <summary>
        /// Суммирователь с настройками приложения: окно совпадения умолчания
        /// (1 мкс), рентген и аннигиляция ВКЛ, изомеры ВКЛ, гейт по времени —
        /// ВЕРОЯТНОСТЬЮ (`A289`, как `FsaAnalyzer.CascadeDecayTimeProbability`
        /// = true; ступенька отсекла бы Sr-85 — уровень Rb 514 живёт 1.015 мкс),
        /// κ_pT в выносе ВКЛ, корреляции — ключом.
        /// </summary>
        static FsaCascadeSummer Summer(ResponseMatrix matrix, string scint, int angcorr)
        {
            FsaCascadeSummer summer = FsaCascadeSummer.Create(matrix, scint, 0.0, true, true, true, true);
            if (summer == null)
            {
                return null;
            }

            summer.LossJointFactor = true;
            summer.AngularCorrelations = angcorr == 1;
            summer.AngularQk = matrix.AngularQk;
            return summer;
        }

        /// <summary>
        /// Образ нуклида: все гамма-линии распада и линии K-серии из
        /// <see cref="CascadeAtomicData"/> — чтобы CF считался и у рентгена.
        /// </summary>
        static FsaComponent Build(string name, out CascadeAtomicData atomic)
        {
            atomic = null;
            string key = FsaCascadeSummer.ParentKey(name);
            if (string.IsNullOrEmpty(key) || key.StartsWith("sandia:", StringComparison.Ordinal))
            {
                return null;
            }

            atomic = CascadeAtomicData.Of(key);
            if (atomic == null)
            {
                return null;
            }

            FsaComponent component = new FsaComponent(name, FsaComponentKind.Single);
            foreach (CascadeAtomicData.GammaLine line in atomic.GammaIntensity)
            {
                component.Lines.Add(new FsaLine(name, line.EnergyKev, line.IntensityPct));
            }

            foreach (double[] k in atomic.KLines)
            {
                component.Lines.Add(new FsaLine(name, k[0], k[1]));
            }

            component.Lines.Sort((a, b) => a.Energy.CompareTo(b.Energy));
            return component;
        }

        static FsaCascadeSummer.LineNote NoteOf(FsaCascadeSummer.Correction correction, double energyKev)
        {
            if (correction == null || correction.Notes == null)
            {
                return null;
            }

            foreach (FsaCascadeSummer.LineNote note in correction.Notes)
            {
                if (Math.Abs(note.EnergyKev - energyKev) < 0.0005)
                {
                    return note;
                }
            }

            return null;
        }

        // ------------------------------------------------------------------
        // Раздел 1
        // ------------------------------------------------------------------

        static int MergedKeys(ResponseMatrix matrix, string scint, int angcorr, List<string> nuclides)
        {
            Console.WriteLine("1. Выход ключа K-серии против суммы линий под ним (AMBER59)");
            Console.WriteLine();
            int bad = 0;
            foreach (string name in nuclides)
            {
                CascadeAtomicData atomic;
                FsaComponent component = Build(name, out atomic);
                FsaCascadeSummer summer = Summer(matrix, scint, angcorr);
                if (component == null || summer == null)
                {
                    Say("   {0}: атомных данных нет либо суммирователь не построился — пропущен", name);
                    Console.WriteLine();
                    continue;
                }

                FsaCascadeSummer.Correction correction = summer.For(component);
                Say("   {0}: линий K-серии {1}, ω_K {2:F3}, полный выход K {3:F2} %",
                    name, atomic.KLines.Count, atomic.OmegaK, atomic.KIntensityPct);
                Say("      K-линия    выход, %   ключ        выход ключа  Σ линий под ключом   CF линии   вынос");
                var seenKeys = new HashSet<double>();
                foreach (double[] k in atomic.KLines)
                {
                    double key, intensity;
                    List<double[]> partners = summer.PartnerTable(name, k[0], out key, out intensity);
                    FsaCascadeSummer.LineNote note = NoteOf(correction, k[0]);
                    if (double.IsNaN(key))
                    {
                        Say("      {0,8:F3}  {1,9:F3}   — ключа нет (носитель ниже сетки матрицы?)", k[0], k[1]);
                        continue;
                    }

                    // Сумма выходов ВСЕХ линий K-серии, сошедшихся к этому ключу.
                    double under = 0.0;
                    foreach (double[] other in atomic.KLines)
                    {
                        double otherKey, otherIntensity;
                        summer.PartnerTable(name, other[0], out otherKey, out otherIntensity);
                        if (!double.IsNaN(otherKey) && otherKey == key)
                        {
                            under += other[1];
                        }
                    }

                    bool ok = Math.Abs(intensity - under) <= 1e-9 * Math.Max(under, 1e-300);
                    if (!ok && seenKeys.Add(key))
                    {
                        bad++;
                    }

                    Say("      {0,8:F3}  {1,9:F3}   {2,8:F3}  {3,11:F3}  {4,11:F3} {5}  {6}  {7}",
                        k[0], k[1], key, intensity, under, ok ? "ok         " : "⛔ РАСХОЖДЕНИЕ",
                        note != null ? note.Cf.ToString("F5", CultureInfo.InvariantCulture) : "   —   ",
                        note != null ? note.Loss.ToString("F5", CultureInfo.InvariantCulture) : "—");
                    if (seenKeys.Add(key) || true)
                    {
                        foreach (double[] p in partners)
                        {
                            Say("            партнёр {0,9:F3} кэВ  P(партнёр|ключ) = {1:F6}  кратность {2:F0}  ε_T = {3:F6}",
                                p[0], p[1], p[2], p[3]);
                        }
                    }
                }

                Console.WriteLine();
            }

            return bad;
        }

        // ------------------------------------------------------------------
        // Разделы 2 и 3
        // ------------------------------------------------------------------

        static void LinesReport(ResponseMatrix matrix, string scint, int angcorr, List<string> lines)
        {
            Console.WriteLine("2–3. Вынос из пика названных линий: партнёры, ε_T, три выживания (AMBER60, AMBER61)");
            Console.WriteLine();
            foreach (string item in lines)
            {
                string[] parts = item.Split(':');
                if (parts.Length != 2)
                {
                    Say("   --lines= ждёт Нуклид:E_кэВ; получено «{0}»", item);
                    continue;
                }

                string name = parts[0];
                double energy = double.Parse(parts[1], CultureInfo.InvariantCulture);
                CascadeAtomicData atomic;
                FsaComponent component = Build(name, out atomic);
                FsaCascadeSummer summer = Summer(matrix, scint, angcorr);
                if (component == null || summer == null)
                {
                    Say("   {0} {1:F3}: атомных данных нет либо суммирователь не построился — пропущен", name, energy);
                    Console.WriteLine();
                    continue;
                }

                FsaCascadeSummer.Correction correction = summer.For(component);
                double key, intensity;
                List<double[]> partners = summer.PartnerTable(name, energy, out key, out intensity);
                if (double.IsNaN(key))
                {
                    Say("   {0} {1:F3}: линии нет в таблице выходов", name, energy);
                    Console.WriteLine();
                    continue;
                }

                FsaCascadeSummer.LineNote note = null;
                foreach (FsaLine line in component.Lines)
                {
                    if (Math.Abs(line.Energy - key) < 0.3)
                    {
                        FsaCascadeSummer.LineNote candidate = NoteOf(correction, line.Energy);
                        if (candidate != null && (note == null || Math.Abs(line.Energy - energy) < 0.0005))
                        {
                            note = candidate;
                        }
                    }
                }

                Say("   {0} {1:F3} кэВ → ключ {2:F3}, выход {3:F3} %; ε_p = {4:F6}, ε_T = {5:F6}",
                    name, energy, key, intensity, summer.PeakEfficiency(key), summer.TotalEfficiency(key));
                if (note != null)
                {
                    Say("      счёт приложения: CF = {0:F5}, вынос = {1:F6}, влёт = {2:F6}",
                        note.Cf, note.Loss, note.InShare);
                }
                else
                {
                    Say("      счёт приложения: строки CF нет (линия не сошлась с образом)");
                }

                // Реплики выживания без κ и корреляций: произведение (S144) и
                // произведение с K-серией как исключающими.
                Say("      партнёр, кэВ      P(партнёр|ключ)  кратность    ε_T        p·ε (1−(1−ε)^m·p)   вид");
                double productAll = 1.0;
                double kSum = 0.0;
                double othersProduct = 1.0;
                double annihilationP = 0.0, annihilationEps = 0.0, annihilationQuanta = 0.0;
                foreach (double[] p in partners)
                {
                    double eps = p[3];
                    double quanta = p[2];
                    double share = quanta > 1.0 ? p[1] / quanta : p[1];
                    if (share > 1.0) share = 1.0;
                    double term = share * (1.0 - Math.Pow(1.0 - eps, quanta));
                    string kind;
                    if (quanta > 1.0)
                    {
                        kind = "аннигиляция";
                        annihilationP = p[1];
                        annihilationEps = eps;
                        annihilationQuanta = quanta;
                    }
                    else if (IsKLine(atomic, p[0]))
                    {
                        kind = "K-серия";
                        kSum += term;
                    }
                    else
                    {
                        kind = "гамма";
                    }

                    if (kind != "K-серия")
                    {
                        othersProduct *= 1.0 - term;
                    }

                    productAll *= 1.0 - term;
                    Say("      {0,10:F3}      {1,12:F6}  {2,6:F0}     {3,9:F6}  {4,11:F6}   {5}",
                        p[0], p[1], quanta, eps, term, kind);
                }

                double exclusiveK = othersProduct * (1.0 - Math.Min(1.0, kSum));
                Say("      реплика без κ: выживание Π по всем = {0:F6} (вынос {1:F6});"
                    + " K-серия как исключающая = {2:F6} (вынос {3:F6}); Σ pᵢεᵢ по K = {4:F6}",
                    productAll, 1.0 - productAll, exclusiveK, 1.0 - exclusiveK, kSum);
                Say("      CF по репликам: Π → {0:F5}; K исключающая → {1:F5}",
                    1.0 / productAll, exclusiveK > 0.0 ? 1.0 / exclusiveK : double.PositiveInfinity);

                // Точнее: исключающие ответы КАЖДОЙ вакансии порознь, вакансии
                // разных источников (захват, конверсия соседей) — независимы;
                // третья реплика ещё связывает γ_T с вакансией от конверсии T
                // (при конверсии γ_T нет): один множитель на переход.
                List<double[]> sources = summer.VacancyTable(name, energy);
                double kTotal = 0.0, kMeanEps = 0.0;
                foreach (double[] p in partners)
                {
                    if (p[2] <= 1.0 && IsKLine(atomic, p[0]))
                    {
                        kTotal += p[1];
                        kMeanEps += p[1] * p[3];
                    }
                }

                if (sources.Count > 0 && kTotal > 0.0)
                {
                    kMeanEps /= kTotal;                       // ε̄_K по долям серии
                    double sumSources = 0.0;
                    double perSource = 1.0;                   // Π_v (1 − P_v·ε̄_K)
                    double perTransition = 1.0;               // то же, но γ_T и K(T) — исключающие
                    var linked = new HashSet<double>();
                    var sb = new StringBuilder();
                    foreach (double[] v in sources)
                    {
                        sumSources += v[1];
                        perSource *= 1.0 - Math.Min(1.0, v[1] * kMeanEps);
                        double gammaTerm = 0.0;
                        if (v[0] > 0.0)
                        {
                            foreach (double[] p in partners)
                            {
                                if (p[2] <= 1.0 && !IsKLine(atomic, p[0]) && Math.Abs(p[0] - v[0]) < 0.3)
                                {
                                    gammaTerm = Math.Min(1.0, p[1]) * p[3];
                                    linked.Add(p[0]);
                                }
                            }
                        }

                        perTransition *= 1.0 - Math.Min(1.0, v[1] * kMeanEps + gammaTerm);
                        sb.AppendFormat(CultureInfo.InvariantCulture, " {0}:{1:F4}",
                                        v[0] > 0.0 ? v[0].ToString("F1", CultureInfo.InvariantCulture) : "захват", v[1]);
                    }

                    // Остальные гаммы (не связанные с источником вакансии) — множителями.
                    foreach (double[] p in partners)
                    {
                        if (p[2] <= 1.0 && !IsKLine(atomic, p[0]) && !linked.Contains(p[0]))
                        {
                            double term = Math.Min(1.0, p[1]) * p[3];
                            perSource *= 1.0 - term;
                            perTransition *= 1.0 - term;
                        }
                        else if (p[2] <= 1.0 && !IsKLine(atomic, p[0]))
                        {
                            perSource *= 1.0 - Math.Min(1.0, p[1]) * p[3];
                        }
                    }

                    // Аннигиляция (если есть) — тем же множителем, что в Π.
                    if (annihilationQuanta > 1.0)
                    {
                        double q = annihilationP / annihilationQuanta;
                        double term = q * (1.0 - Math.Pow(1.0 - annihilationEps, annihilationQuanta));
                        perSource *= 1.0 - term;
                        perTransition *= 1.0 - term;
                    }

                    Say("      источники K-вакансии (·ω_K):{0}; Σ = {1:F4} (Σ долей K в партнёрах {2:F4}); ε̄_K = {3:F6}",
                        sb.ToString(), sumSources, kTotal, kMeanEps);
                    Say("      реплика по источникам: выживание {0:F6} (CF {1:F5}); с связкой γ_T↔K(T): {2:F6} (CF {3:F5})",
                        perSource, 1.0 / perSource, perTransition, 1.0 / perTransition);
                }
                if (annihilationQuanta > 1.0)
                {
                    double q = annihilationP / annihilationQuanta;
                    double independent = q * (1.0 - Math.Pow(1.0 - annihilationEps, annihilationQuanta));
                    double exclusive = q * Math.Min(1.0, annihilationQuanta * annihilationEps);
                    Say("      511: q = {0:F5} событий-пар, ε_T(511) = {1:F6}; потеря независимо {2:F6} (CF {3:F5}),"
                        + " спина к спине {4:F6} (CF {5:F5}); κ_pT(ключ,511) = {6:F5}",
                        q, annihilationEps, independent, 1.0 / (1.0 - independent),
                        exclusive, 1.0 / (1.0 - exclusive), JointOf(matrix, key, 511.0));
                }

                Console.WriteLine();
            }
        }

        static bool IsKLine(CascadeAtomicData atomic, double energyKev)
        {
            foreach (double[] k in atomic.KLines)
            {
                if (Math.Abs(k[0] - energyKev) < 0.3)
                {
                    return true;
                }
            }

            foreach (CascadeAtomicData.Branch branch in atomic.Branches)
            {
                foreach (double[] k in branch.KLines)
                {
                    if (Math.Abs(k[0] - energyKev) < 0.3)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        static double JointOf(ResponseMatrix matrix, double e1, double e2)
        {
            try
            {
                return matrix.JointFactor(e1, e2);
            }
            catch (Exception)
            {
                return double.NaN;
            }
        }
    }
}
