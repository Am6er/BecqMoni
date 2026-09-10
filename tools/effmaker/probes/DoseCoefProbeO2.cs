using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml.Serialization;

namespace DoseCoefProbeO2
{
    /// <summary>
    /// Строка `A198` — таблица h*(10)/K_air в `DoseRateCoefficients`.
    ///
    /// Решение Amber 05.09.2026: принять ICRP 74, источник прежних вшитых чисел
    /// не разыскивать, редакцию назвать в коде, расхождение с прежними версиями
    /// записать в журнал. Проба меряет то, чем решение НЕ покрыто:
    ///
    ///  1. **ВСЕ узлы**, а не шесть перечисленных в строке: сетка и значения
    ///     таблицы, лежащей в приложении, сверяются с опубликованными ICRP 74
    ///     (1996), приложение A, h*(10)/K_air, Зв/Гр — двадцать пять узлов.
    ///     ⚠ Таблица читается ОТРАЖЕНИЕМ из сборки приложения, а не копией:
    ///     проверка обязана судить то, что уедет людям.
    ///
    ///  2. **Положительный контроль.** Сверка, которая проходит всегда, ничего
    ///     не меряет. Один узел портится отражением (800 кэВ ×1.05, затем
    ///     30 кэВ ×0.95) — проверка обязана ОТКАЗАТЬ, назвав узел и величину;
    ///     после восстановления — ни одной ложной тревоги.
    ///
    ///  3. **Интерполяция МЕЖДУ узлами.** Названа вслух — линейная по значению,
    ///     логарифмическая по энергии — и сверяется с независимой записью той же
    ///     формулы; отрицательный контроль: линейная по энергии обязана
    ///     разойтись, иначе проверка не различает схемы. Плюс сравнение с той
    ///     схемой, которой пользовался ПРЕЖНИЙ код (монотонный кубический сплайн
    ///     по шестнадцати узлам «RToSv»), — узловая сверка `C4` этого не видела.
    ///
    ///  4. **Воздух — независимая привязка к той же публикации.** μ_en/ρ в
    ///     расчёт входит наравне с h*(10)/K_air, и его источник другой (XCOM).
    ///     ICRP 119 (сводка коэффициентов по ICRP 60/74), приложение I,
    ///     таблица I.1, колонка K_a/Φ даёт керму воздуха на единицу флюенса —
    ///     это ровно E·μ_tr/ρ. Сверка ловит перекос в ТОЙ половине множителя,
    ///     которую узловая сверка `A198` не трогает.
    ///
    ///  5. **Цена для показаний на Co-60 и K-40.** Развёртка по ОДНОЙ величине:
    ///     сетка, μ_en/ρ, кривая эффективности, эталон и спектр — общие, меняется
    ///     только таблица h*(10)/K_air (прежняя против ICRP 74). Прибор
    ///     Gamma-1S UDS-GC 63x63, эталон `G1S16_Cs137_P5` с объявленной дозой
    ///     1.000 мкЗв/ч, измеряются `G1S16_Co60_P5` и `G1S16_K40_Mar` — все
    ///     одного прибора, чтобы шкала не подменяла ответ.
    ///
    ///   dosecoefprobeo2 [--dir=&lt;корпус&gt;] [--csv=&lt;каталог&gt;]
    ///   dosecoefprobeo2 --tamper=&lt;кэВ&gt;:&lt;множитель&gt;   (ждёт ОТКАЗ)
    ///
    /// ⛔ С 06.09.2026 (`T201`) эта проба — ЕДИНСТВЕННЫЙ судья таблицы
    /// h*(10)/K_air: `DoseRateProbe.AmbientAgreement` (шестнадцать узлов против
    /// вшитого «RToSv», допуск 5 %) снята, двух судей одной таблицы с разными
    /// допусками быть не должно. Ключ `--tamper=` — положительный контроль
    /// оставшегося судьи снаружи, а не только внутри прогона: узел портится
    /// отражением на ВЕСЬ прогон, и сверка узлов обязана отказать, назвав узел.
    /// Коды возврата у `--tamper` перевёрнуты: 0 — отказ получен и узел назван
    /// (судья смотрит), 1 — не получен (судья слеп).
    /// </summary>
    static class Program
    {
        static int failed;
        static int checks;
        static string corpusDir = @"tools\CORPUS\corpus";
        static string csvDir;

        /// <summary>Порча узла на весь прогон, «кэВ:множитель»; null — нет.</summary>
        static string tamper;

        /// <summary>Расхождения, названные сверкой узлов в последнем прогоне.</summary>
        static List<string> lastNodeDiffs = new List<string>();

        // ------------------------------------------------------------------
        // Опубликованное: ICRP 74 (1996), приложение A, h*(10)/K_air, Зв/Гр.
        // Двадцать пять узлов, 10 кэВ … 10 МэВ.
        // ------------------------------------------------------------------
        static readonly double[] Icrp74EnergyKev =
        {
            10, 15, 20, 30, 40, 50, 60, 80, 100, 150, 200, 300, 400, 500, 600,
            800, 1000, 1500, 2000, 3000, 4000, 5000, 6000, 8000, 10000,
        };

        static readonly double[] Icrp74Conversion =
        {
            0.008, 0.26, 0.61, 1.10, 1.47, 1.67, 1.74, 1.72, 1.65, 1.49, 1.40,
            1.31, 1.26, 1.23, 1.21, 1.19, 1.17, 1.15, 1.14, 1.13, 1.12, 1.11,
            1.11, 1.11, 1.10,
        };

        // ------------------------------------------------------------------
        // Прежнее вшитое, дословно из `DeviceConfigForm.CalculateDoseRateConfig`
        // до правки 05.09.2026. Величина — h*(10)/K_air, умноженная на 0.876,
        // то есть бэр/Р; для сравнения делится обратно.
        // ------------------------------------------------------------------
        static readonly double[] OldEnergies =
            { 40, 50, 60, 80, 100, 150, 200, 300, 400, 500, 600, 800, 1000, 1500, 2000, 3000 };

        static readonly double[] OldRToSv =
            { 1.29, 1.46, 1.52, 1.51, 1.44, 1.31, 1.22, 1.15, 1.10, 1.07, 1.04, 1.02, 1.01,
              0.99, 0.99, 0.98 };

        // ------------------------------------------------------------------
        // ICRP 119 (2012), приложение I, таблица I.1, колонка K_a/Φ, пГр·см².
        // Взято сводкой ICRP по Publication 74; это E·μ_tr/ρ воздуха.
        //
        // ✅ СВЕРЕНО С САМОЙ ПУБЛИКАЦИЕЙ 06.09.2026 (полоса F65, `A224`) —
        // прежде числа стояли транскрипцией без источника на руках.
        // Файл: icrp.org, «p 119 jaicrp 41(s) compendium of dose coefficients
        // based on icrp publication 60.pdf», 655 207 байт,
        // sha256 5b891dd8a17a85fbfd60324eea6bb32e552284ae48b70eebabeb251c6c679516;
        // таблица — страница 125 файла (печатная 123), колонка ВТОРАЯ, по
        // заголовку «Ka/Φ (pGy cm2)» (первая — «Photon energy (MeV)», дальше
        // идут шесть колонок E/Ka по геометриям AP…ISO).
        //
        // Все 23 числа и все 23 узла совпали ДОСЛОВНО, четырьмя разборами PDF
        // (`pdftotext` по умолчанию, `-layout`, `-table`, `-raw`) — в том числе
        // спорный узел 70 кэВ: у публикации там 2.97E−01, то есть 0.297, а не
        // 0.2918. ⚠ Разбор PDF при этом НЕ безошибочен, и это видно на той же
        // странице: в колонке LLAT на 0.03 МэВ все четыре способа теряют знак
        // порядка и печатают «9.08E+00» вместо 9.08E−02 (сосед RLAT — 9.04E−02).
        // Поэтому колонка проверена не только чтением: K_a/Φ = E·μ_en/ρ, и на
        // КАЖДОМ узле, который есть в сетке NIST, она воспроизводит табличную
        // μ_en/ρ сухого воздуха до третьей значащей — 30 кэВ 0.1537, 40 кэВ
        // 0.06833, 50 кэВ 0.04098, 60 кэВ 0.03041, 80 кэВ 0.02407, 100 кэВ
        // 0.02325, 150 кэВ 0.02496 см²/г. Узел 70 кэВ — единственный вне сетки
        // NIST, и своей опоры такого рода у него нет.
        // ------------------------------------------------------------------
        static readonly double[] KaPhiEnergyKev =
            { 10, 15, 20, 30, 40, 50, 60, 70, 80, 100, 150, 200, 300, 400, 500,
              600, 800, 1000, 2000, 4000, 6000, 8000, 10000 };

        static readonly double[] KaPhiPGyCm2 =
            { 7.60, 3.21, 1.73, 0.739, 0.438, 0.328, 0.292, 0.297, 0.308, 0.372,
              0.600, 0.856, 1.38, 1.89, 2.38, 2.84, 3.69, 4.47, 7.51, 12.0,
              15.8, 19.5, 23.2 };

        /// <summary>пГр·см² на (кэВ · см²/г): 1.602176634e-16 Дж/кэВ × 1e15.</summary>
        const double KaPhiUnit = 0.1602176634;

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            // ⛔ (`T247`) Культура ЦЕЛИКОМ инвариантная, приказ Amber 05.09.2026. Проба
            //    не ставила её ВОВСЕ, и на русской машине часть её чисел шла с ЗАПЯТОЙ
            //    (замер 10.09.2026, полоса П8: мест без поставщика культуры — 4).
            //    Инвариант ЦЕЛИКОМ, а не клон с подменённым разделителем: клон
            //    чинит печать и оставляет РАЗБОР системным (`T245`).
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            foreach (string a in args)
            {
                if (a.StartsWith("--dir=", StringComparison.Ordinal))
                {
                    corpusDir = a.Substring(6);
                }
                else if (a.StartsWith("--csv=", StringComparison.Ordinal))
                {
                    csvDir = a.Substring(6);
                }
                else if (a.StartsWith("--tamper=", StringComparison.Ordinal))
                {
                    tamper = a.Substring(9);
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            double tamperKev = 0.0, tamperFactor = 1.0;
            if (tamper != null)
            {
                string[] parts = tamper.Split(':');
                if (parts.Length != 2
                    || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out tamperKev)
                    || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out tamperFactor))
                {
                    Console.Error.WriteLine("--tamper= ждёт «кэВ:множитель» с точкой, дано: " + tamper);
                    return 2;
                }

                double[] energy = Field("AmbientEnergyKev");
                int at = Array.IndexOf(energy, tamperKev);
                if (at < 0)
                {
                    Console.Error.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "--tamper=: в таблице приложения нет узла {0} кэВ — портить нечего", tamperKev));
                    return 2;
                }

                // Порча ЖИВОЙ таблицы приложения отражением, без восстановления:
                // всё, что ниже, судит испорченное.
                Field("AmbientConversion")[at] *= tamperFactor;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "⛔ ПОРЧА НА ВЕСЬ ПРОГОН: узел {0:f0} кэВ × {1:f3} (ждётся отказ сверки узлов)",
                    tamperKev, tamperFactor));
                Console.WriteLine();
            }

            try
            {
                Nodes();
                TamperControl();
                Interpolation();
                AirCrossCheck();
                Price();
            }
            catch (Exception ex)
            {
                Console.WriteLine("!! проба сорвалась: " + ex);
                return 3;
            }

            Console.WriteLine();
            if (tamper != null)
            {
                string node = tamperKev.ToString("f0", CultureInfo.InvariantCulture) + " кэВ";
                bool named = false;
                foreach (string d in lastNodeDiffs)
                {
                    if (d.StartsWith(node, StringComparison.Ordinal))
                    {
                        named = true;
                    }
                }

                bool caught = failed > 0 && named;
                Console.WriteLine(caught
                    ? string.Format(CultureInfo.InvariantCulture,
                        "ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ «{0} ×{1:f3}»: отказ получен ({2} из {3}), узел назван сверкой узлов",
                        node, tamperFactor, failed, checks)
                    : string.Format(CultureInfo.InvariantCulture,
                        "ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ «{0} ×{1:f3}»: ОТКАЗА НЕТ — судья слеп (провалов {2} из {3}, узел назван: {4})",
                        node, tamperFactor, failed, checks, named ? "да" : "нет"));
                return caught ? 0 : 1;
            }

            Console.WriteLine(failed == 0
                ? string.Format("ВСЁ СОШЛОСЬ: {0} проверок", checks)
                : string.Format("ПРОВАЛОВ {0} из {1}", failed, checks));
            return failed == 0 ? 0 : 1;
        }

        static void Ok(bool condition, string what)
        {
            checks++;
            if (!condition)
            {
                failed++;
            }

            Console.WriteLine("  {0} {1}", condition ? "ok  " : "ПРОВАЛ", what);
        }

        // ==================================================================
        // Доступ к таблице приложения ОТРАЖЕНИЕМ
        // ==================================================================

        static double[] Field(string name)
        {
            FieldInfo f = typeof(DoseRateCoefficients).GetField(
                name, BindingFlags.NonPublic | BindingFlags.Static);
            if (f == null)
            {
                throw new InvalidOperationException(
                    "в DoseRateCoefficients нет поля " + name + " — проба смотрит не туда");
            }

            return (double[])f.GetValue(null);
        }

        // ==================================================================
        // 1. Все узлы таблицы
        // ==================================================================

        /// <summary>
        /// Сверка узлов. Возвращает список расхождений словами — им же
        /// пользуется положительный контроль, поэтому портить нечего: судит
        /// один и тот же код.
        /// </summary>
        static List<string> NodeDiffs(out double worstPercent)
        {
            double[] energy = Field("AmbientEnergyKev");
            double[] value = Field("AmbientConversion");
            var diffs = new List<string>();
            worstPercent = 0.0;

            if (energy.Length != Icrp74EnergyKev.Length || value.Length != Icrp74Conversion.Length)
            {
                diffs.Add(string.Format(CultureInfo.InvariantCulture,
                    "длина таблицы {0} узлов против {1} опубликованных",
                    energy.Length, Icrp74EnergyKev.Length));
                return diffs;
            }

            for (int i = 0; i < energy.Length; i++)
            {
                if (Math.Abs(energy[i] - Icrp74EnergyKev[i]) > 1e-9)
                {
                    diffs.Add(string.Format(CultureInfo.InvariantCulture,
                        "узел {0}: энергия {1} кэВ против опубликованной {2} кэВ",
                        i, energy[i], Icrp74EnergyKev[i]));
                    continue;
                }

                double p = 100.0 * (value[i] - Icrp74Conversion[i]) / Icrp74Conversion[i];
                if (Math.Abs(p) > Math.Abs(worstPercent))
                {
                    worstPercent = p;
                }

                // Опубликованные числа даны двумя-тремя значащими; таблица
                // обязана совпасть с ними ДОСЛОВНО, а не «в пределах».
                if (Math.Abs(value[i] - Icrp74Conversion[i]) > 5e-13)
                {
                    diffs.Add(string.Format(CultureInfo.InvariantCulture,
                        "{0:f0} кэВ: в коде {1:g6}, ICRP 74 {2:g6}, расхождение {3:+0.00;-0.00} %",
                        energy[i], value[i], Icrp74Conversion[i], p));
                }
            }

            return diffs;
        }

        static void Nodes()
        {
            Console.WriteLine("== ВСЕ узлы h*(10)/K_air: таблица приложения против ICRP 74 ==");
            double[] energy = Field("AmbientEnergyKev");
            double[] value = Field("AmbientConversion");

            var csv = new StringBuilder();
            csv.AppendLine("E_keV;code;ICRP74;ratio_code_over_ICRP74;diff_percent;"
                           + "public_entry;old_RToSv_over_0876;code_over_old_percent");
            Console.WriteLine("   E, кэВ    в коде   ICRP 74   расх., %   прежнее/0.876   новое/прежнее, %");

            for (int i = 0; i < energy.Length; i++)
            {
                double published = i < Icrp74Conversion.Length ? Icrp74Conversion[i] : double.NaN;
                double diff = 100.0 * (value[i] - published) / published;

                // То же значение, но через ПУБЛИЧНЫЙ вход: узел обязан
                // возвращаться таблицей, а не интерполяцией мимо неё.
                double entry = DoseRateCoefficients.AmbientDoseConversion(energy[i]);

                double old = double.NaN;
                double oldDiff = double.NaN;
                int k = Array.IndexOf(OldEnergies, energy[i]);
                if (k >= 0)
                {
                    old = OldRToSv[k] / DoseRateCoefficients.RemPerRoentgenFactor;
                    oldDiff = 100.0 * (value[i] - old) / old;
                }

                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,7:f0}   {1,7:f3}   {2,7:f3}   {3,8:+0.00;-0.00}   {4,13}   {5,14}",
                    energy[i], value[i], published, diff,
                    double.IsNaN(old) ? "—" : old.ToString("f4", CultureInfo.InvariantCulture),
                    double.IsNaN(oldDiff) ? "—" : oldDiff.ToString("+0.00;-0.00", CultureInfo.InvariantCulture)));

                csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:g6};{1:g6};{2:g6};{3:g8};{4:g6};{5:g6};{6};{7}",
                    energy[i], value[i], published, value[i] / published, diff, entry,
                    double.IsNaN(old) ? "" : old.ToString("g6", CultureInfo.InvariantCulture),
                    double.IsNaN(oldDiff) ? "" : oldDiff.ToString("g6", CultureInfo.InvariantCulture)));

                Ok(Math.Abs(entry - value[i]) < 1e-12, string.Format(CultureInfo.InvariantCulture,
                    "{0,7:f0} кэВ: публичный вход отдаёт узел таблицы ({1:g6})", energy[i], entry));
            }

            double worst;
            List<string> diffs = NodeDiffs(out worst);
            lastNodeDiffs = diffs;
            foreach (string d in diffs)
            {
                Console.WriteLine("   расхождение: " + d);
            }

            Ok(diffs.Count == 0, string.Format(CultureInfo.InvariantCulture,
                "все {0} узлов совпали с ICRP 74 дословно (худшее расхождение {1:g3} %)",
                energy.Length, worst));

            Write("o2-a198-icrp74-nodes.csv", csv.ToString());
        }

        // ==================================================================
        // 2. Положительный контроль сверки
        // ==================================================================

        static void TamperControl()
        {
            Console.WriteLine();
            Console.WriteLine("== положительный контроль: подсунуть заведомо неверную таблицу ==");

            double[] value = Field("AmbientConversion");
            double[] energy = Field("AmbientEnergyKev");

            // Плечо 0 — нетронутая таблица. Ложной тревоги быть не должно.
            double worst0;
            Ok(NodeDiffs(out worst0).Count == 0, "нетронутая таблица: ложной тревоги нет");

            // Плечо 1 — узел 800 кэВ завышен на 5 %.
            Tamper(value, energy, 800.0, 1.05);
            // Плечо 2 — узел 30 кэВ занижен на 5 %: чтобы отказ не оказался
            // свойством одного места таблицы.
            Tamper(value, energy, 30.0, 0.95);

            // Обе порчи разом — сверка обязана назвать ОБА узла.
            int i800 = Array.IndexOf(energy, 800.0);
            int i30 = Array.IndexOf(energy, 30.0);
            double keep800 = value[i800];
            double keep30 = value[i30];
            value[i800] *= 1.05;
            value[i30] *= 0.95;
            double worstBoth;
            List<string> both = NodeDiffs(out worstBoth);
            value[i800] = keep800;
            value[i30] = keep30;
            Ok(both.Count == 2, string.Format(CultureInfo.InvariantCulture,
                "две порчи разом дают ровно два отказа (получено {0})", both.Count));

            // И снова нетронутая — порча не осталась в живой таблице.
            double worst1;
            Ok(NodeDiffs(out worst1).Count == 0,
               "таблица восстановлена, ложной тревоги нет");
        }

        static void Tamper(double[] value, double[] energy, double atKev, double factor)
        {
            int i = Array.IndexOf(energy, atKev);
            if (i < 0)
            {
                Ok(false, string.Format(CultureInfo.InvariantCulture,
                    "в таблице нет узла {0:f0} кэВ — портить нечего", atKev));
                return;
            }

            double keep = value[i];
            value[i] = keep * factor;
            double worst;
            List<string> diffs = NodeDiffs(out worst);
            value[i] = keep;

            bool named = diffs.Count == 1
                         && diffs[0].IndexOf(atKev.ToString("f0", CultureInfo.InvariantCulture)
                                             + " кэВ", StringComparison.Ordinal) >= 0;
            double expected = 100.0 * (factor - 1.0);
            bool sized = Math.Abs(worst - expected) < 0.01;

            Console.WriteLine("     " + (diffs.Count > 0 ? diffs[0] : "(отказа нет)"));
            Ok(named && sized, string.Format(CultureInfo.InvariantCulture,
                "узел {0:f0} кэВ ×{1:f2}: сверка ОТКАЗАЛА, назвала узел и величину {2:+0.00;-0.00} %",
                atKev, factor, worst));
        }

        // ==================================================================
        // 3. Интерполяция МЕЖДУ узлами
        // ==================================================================

        /// <summary>
        /// Независимая запись объявленной схемы: значение линейно, энергия —
        /// логарифмически. Своя, а не вызов приложения: иначе проверка
        /// сравнивала бы код сам с собой.
        /// </summary>
        static double LogLinear(double[] x, double[] y, double e)
        {
            int n = x.Length;
            if (e <= x[0]) return y[0];
            if (e >= x[n - 1]) return y[n - 1];
            int lo = 0;
            while (lo + 1 < n && x[lo + 1] <= e) lo++;
            int hi = lo + 1;
            double t = (Math.Log(e) - Math.Log(x[lo])) / (Math.Log(x[hi]) - Math.Log(x[lo]));
            return y[lo] + t * (y[hi] - y[lo]);
        }

        /// <summary>Отрицательный контроль схемы: линейная и по энергии тоже.</summary>
        static double PlainLinear(double[] x, double[] y, double e)
        {
            int n = x.Length;
            if (e <= x[0]) return y[0];
            if (e >= x[n - 1]) return y[n - 1];
            int lo = 0;
            while (lo + 1 < n && x[lo + 1] <= e) lo++;
            int hi = lo + 1;
            double t = (e - x[lo]) / (x[hi] - x[lo]);
            return y[lo] + t * (y[hi] - y[lo]);
        }

        // ==================================================================
        // ФОРМА, А НЕ КРИВАЯ ЭФФЕКТИВНОСТИ (`A258`)
        // ==================================================================

        /// <summary>
        /// Табличная кривая ТЕМ ЖЕ монотонным сплайном, каким её строит
        /// приложение, — но для величины, которая эффективностью НЕ ЯВЛЯЕТСЯ.
        ///
        /// ⛔ Зачем понадобилось разводить. С 06.09.2026 (~~`A222`~~)
        /// `DoseRateEstimator.CurveOf` отказывает на точке ε &gt; 1:
        /// эффективность — доля испущенных квантов, попавших в пик, и больше
        /// единицы быть не может. Граница ВЕРНА и здесь НЕ ОСЛАБЛЯЕТСЯ. А
        /// проба гоняет через тот же сплайн совсем другие величины: h*(10)/K_air
        /// в Зв/Гр (0.008…1.74) и прежнюю таблицу «RToSv»/0.876 (1.47…1.74).
        /// Они нужны ей одной лишь ФОРМОЙ — сравнить схему интерполяции со
        /// схемой, — и единицей не ограничены ничем. Дефект был у пробы: она
        /// называла кривой эффективности то, что ею не является.
        ///
        /// ⚠ Разведено НОРМИРОВКОЙ, и она стоит ровно ноль: значения делятся на
        /// наименьшую степень двойки, не меньшую наибольшего из них, а
        /// <see cref="At"/> умножает обратно. Обе операции в двоичной плавающей
        /// точке точны, а монотонный сплайн однороден по значениям первой
        /// степени, — так что кривая выходит прежней ДО ПОСЛЕДНЕГО БИТА, а не
        /// «в пределах допуска». Это не рассуждение: `DoseBoundProbeF65`
        /// меряет обе половины — что граница ε ≤ 1 по-прежнему отвергает
        /// НАСТОЯЩУЮ кривую, и что нормировка не двигает чисел.
        /// </summary>
        sealed class ShapeCurve
        {
            readonly DoseRateCurve curve;
            readonly double scale;

            ShapeCurve(DoseRateCurve curve, double scale)
            {
                this.curve = curve;
                this.scale = scale;
            }

            public double MinKev { get { return this.curve.MinKev; } }

            public double MaxKev { get { return this.curve.MaxKev; } }

            public double At(double energyKev)
            {
                return this.curve.At(energyKev) * this.scale;
            }

            /// <summary>Наименьшая степень двойки, не меньшая наибольшего значения.</summary>
            public static double ScaleFor(double[] y)
            {
                double max = 0.0;
                for (int i = 0; i < y.Length; i++)
                {
                    if (y[i] > max)
                    {
                        max = y[i];
                    }
                }

                double scale = 1.0;
                while (scale < max)
                {
                    scale *= 2.0;
                }

                return scale;
            }

            public static ShapeCurve Of(double[] x, double[] y)
            {
                double scale = ScaleFor(y);
                var points = new List<ROIEfficiencyData>();
                for (int i = 0; i < x.Length; i++)
                {
                    points.Add(new ROIEfficiencyData
                    {
                        Energy = x[i],
                        Efficiency = y[i] / scale,
                        ErrorPercent = 1.0,
                    });
                }

                return new ShapeCurve(DoseRateEstimator.CurveOf(points), scale);
            }
        }

        /// <summary>Прежняя таблица «RToSv», приведённая к Зв/Гр.</summary>
        static double[] OldSvPerGy()
        {
            var y = new double[OldRToSv.Length];
            for (int i = 0; i < y.Length; i++)
            {
                y[i] = OldRToSv[i] / DoseRateCoefficients.RemPerRoentgenFactor;
            }

            return y;
        }

        static void Interpolation()
        {
            Console.WriteLine();
            Console.WriteLine("== интерполяция МЕЖДУ узлами ==");

            double[] x = Field("AmbientEnergyKev");
            double[] y = Field("AmbientConversion");

            // 3.1 Схема названа и та же, какой сверялись узлы.
            double worstSelf = 0.0, atSelf = 0.0;
            double worstPlain = 0.0, atPlain = 0.0;
            for (int i = 0; i <= 400; i++)
            {
                double e = 10.0 * Math.Pow(1000.0, i / 400.0);   // 10 … 10000 кэВ
                double app = DoseRateCoefficients.AmbientDoseConversion(e);
                double mine = LogLinear(x, y, e);
                double plain = PlainLinear(x, y, e);
                if (Math.Abs(app - mine) > worstSelf) { worstSelf = Math.Abs(app - mine); atSelf = e; }
                double d = Math.Abs(app - plain) / app;
                if (d > worstPlain) { worstPlain = d; atPlain = e; }
            }

            Ok(worstSelf < 1e-12, string.Format(CultureInfo.InvariantCulture,
                "объявленная схема (линейно по значению, логарифмически по энергии)"
                + " воспроизведена независимо: худшее |Δ| = {0:e2} на {1:f0} кэВ", worstSelf, atSelf));

            // Отрицательный контроль: если проверка не отличает схему от
            // соседней, она не проверяет схему.
            Ok(worstPlain > 1e-3, string.Format(CultureInfo.InvariantCulture,
                "линейная по ЭНЕРГИИ схема расходится с приложением на {0:f2} % ({1:f0} кэВ)"
                + " — проверка схемы не слепая", 100.0 * worstPlain, atPlain));

            // 3.2 Опубликованные опоры МЕЖДУ узлами.
            double cs137 = DoseRateCoefficients.AmbientDoseConversion(661.657);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  Cs-137, 661.657 кэВ: {0:f4} Зв/Гр (опубликованная опора ICRP 74 — 1.20…1.21)", cs137));
            Ok(cs137 > 1.195 && cs137 < 1.215,
               "опора Cs-137 между узлами 600 и 800 кэВ выдержана");

            // 3.3 Чем схема отличается от той, которой считал ПРЕЖНИЙ код.
            // ⚠ Узловая сверка `C4` этого не видела вовсе: она сравнивала
            // значения В УЗЛАХ, а прежний код между ними гнул монотонный
            // кубический сплайн по шестнадцати точкам.
            // ⚠ Это ФОРМА, а не кривая эффективности: величина — Зв/Гр,
            // 1.47…1.74. Через `ShapeCurve`, а не через `CurveOf` напрямую
            // (`A258`); граница ε ≤ 1 остаётся на месте.
            ShapeCurve oldCurve = ShapeCurve.Of(OldEnergies, OldSvPerGy());

            // Тот же сплайн, но по НОВЫМ двадцати пяти узлам: показывает,
            // сколько стоит САМА схема, отдельно от смены чисел. Тоже форма:
            // h*(10)/K_air доходит до 1.74 Зв/Гр.
            ShapeCurve newSpline = ShapeCurve.Of(x, y);

            var csv = new StringBuilder();
            csv.AppendLine("E_keV;icrp74_loglinear;icrp74_spline;old_RToSv_spline;"
                           + "loglin_over_spline_percent;new_over_old_percent");
            double worstScheme = 0.0, atScheme = 0.0;
            double worstOld = 0.0, atOld = 0.0;
            for (int i = 0; i <= 400; i++)
            {
                double e = 40.0 * Math.Pow(3000.0 / 40.0, i / 400.0);   // область прежней таблицы
                double loglin = DoseRateCoefficients.AmbientDoseConversion(e);
                double spline = newSpline.At(e);
                double old = oldCurve.At(e);
                double dScheme = 100.0 * (loglin - spline) / spline;
                double dOld = 100.0 * (loglin - old) / old;
                if (Math.Abs(dScheme) > Math.Abs(worstScheme)) { worstScheme = dScheme; atScheme = e; }
                if (Math.Abs(dOld) > Math.Abs(worstOld)) { worstOld = dOld; atOld = e; }
                csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:g6};{1:g6};{2:g6};{3:g6};{4:g4};{5:g4}",
                    e, loglin, spline, old, dScheme, dOld));
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  схема против схемы на ОДНИХ узлах (лог-линейная против сплайна):"
                + " худшее {0:+0.00;-0.00} % на {1:f0} кэВ", worstScheme, atScheme));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  ICRP 74 против ПРЕЖНЕГО кода целиком (числа + схема):"
                + " худшее {0:+0.00;-0.00} % на {1:f0} кэВ", worstOld, atOld));
            Ok(Math.Abs(worstScheme) < 1.0, string.Format(CultureInfo.InvariantCulture,
                "выбор схемы стоит меньше 1 % ({0:+0.00;-0.00} %) — числа в узлах решают, а не схема",
                worstScheme));

            Write("o2-a198-interpolation.csv", csv.ToString());
        }

        // ==================================================================
        // 4. Воздух: независимая привязка к ICRP 119 / Publication 74
        // ==================================================================

        static void AirCrossCheck()
        {
            Console.WriteLine();
            Console.WriteLine("== K_a/Φ воздуха: XCOM приложения против ICRP 119, табл. I.1 ==");
            Console.WriteLine("   E, кэВ   ICRP 119, пГр·см²   приложение   расх., %");

            var csv = new StringBuilder();
            csv.AppendLine("E_keV;icrp119_Ka_over_phi_pGycm2;app_Ka_over_phi_pGycm2;diff_percent;"
                           + "app_mu_en_rho_m2kg");
            double worst = 0.0, at = 0.0;              // по всей шкале
            double worstLow = 0.0, atLow = 0.0;        // ниже 3 МэВ, где код заявляет g < 0.2 %
            for (int i = 0; i < KaPhiEnergyKev.Length; i++)
            {
                double e = KaPhiEnergyKev[i];
                double mu = DoseRateCoefficients.MassEnergyAbsorptionAir(e);   // м²/кг
                double mine = e * (mu * 10.0) * KaPhiUnit;                     // пГр·см²
                double d = 100.0 * (mine - KaPhiPGyCm2[i]) / KaPhiPGyCm2[i];
                if (Math.Abs(d) > Math.Abs(worst)) { worst = d; at = e; }
                if (e <= 3000.0 && Math.Abs(d) > Math.Abs(worstLow)) { worstLow = d; atLow = e; }
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,7:f0}   {1,17:g4}   {2,10:g4}   {3,8:+0.00;-0.00}",
                    e, KaPhiPGyCm2[i], mine, d));
                csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:g6};{1:g6};{2:g6};{3:g4};{4:g6}", e, KaPhiPGyCm2[i], mine, d, mu));
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  худшая точка по всей шкале {0:f0} кэВ: {1:+0.00;-0.00} %;"
                + " ниже 3 МэВ {2:f0} кэВ: {3:+0.00;-0.00} %", at, worst, atLow, worstLow));

            // ⚠ Это сторож ВТОРОЙ половины множителя, а не приёмка `A198`.
            // Он судит одно: осталась ли величина той же величиной того же
            // воздуха. Порог 5 % назван ДО замера и ловит промах в составе, в
            // единицах и в канале сечения — такие промахи стоят десятки
            // процентов и разы, а не единицы.
            Ok(Math.Abs(worst) < 5.0, string.Format(CultureInfo.InvariantCulture,
                "μ_en/ρ приложения остаётся той же величиной, что у ICRP (худшее {0:+0.00;-0.00} %)",
                worst));

            // ⛔ Положительный контроль этого сторожа: та же сверка на заведомо
            // неверной единице (см²/г вместо м²/кг) обязана ОТКАЗАТЬ. Без него
            // «сошлось» ничего не значит.
            double worstBad = 0.0;
            for (int i = 0; i < KaPhiEnergyKev.Length; i++)
            {
                double e = KaPhiEnergyKev[i];
                double bad = e * DoseRateCoefficients.MassEnergyAbsorptionAir(e) * KaPhiUnit;
                double d = 100.0 * (bad - KaPhiPGyCm2[i]) / KaPhiPGyCm2[i];
                if (Math.Abs(d) > Math.Abs(worstBad)) worstBad = d;
            }

            Ok(Math.Abs(worstBad) >= 5.0, string.Format(CultureInfo.InvariantCulture,
                "подставленная неверная единица (см²/г вместо м²/кг) сторожа ПРОБИВАЕТ:"
                + " {0:+0.0;-0.0} %", worstBad));

            // ------------------------------------------------------------------
            // ⚠ НАХОДКИ по воздуху. Обе — не про `A198` (там таблица ICRP 74
            // сошлась дословно), а про вторую половину множителя. Идут строками
            // реестра, чинить здесь нечего.
            // ------------------------------------------------------------------
            Console.WriteLine("  ⚠ НАХОДКА 1: выше 3 МэВ расхождение растёт монотонно"
                              + " (+1.16 / +2.38 / +3.25 / +4.02 % на 4/6/8/10 МэВ) —"
                              + " это невычтенные радиационные потери g, названные в коде.");

            // ⚠ Выброс на 70 кэВ. Ниже НЕ проверяется монотонность μ_en/ρ: в
            // этой области она ПАДАЕТ (минимум воздуха около 100 кэВ), и
            // «обязана расти» было бы выдумкой. Растёт произведение E·μ, то
            // есть сама K_a/Φ, и у неё минимум — у ICRP на 60 кэВ, у
            // приложения на 65. Мерится не форма, а ЛОКАЛЬНОСТЬ выброса:
            // насколько 70 кэВ хуже своих соседей.
            double d60 = Deviation(60.0), d70 = Deviation(70.0), d80 = Deviation(80.0);
            double excess = Math.Abs(d70) - Math.Max(Math.Abs(d60), Math.Abs(d80));
            Console.WriteLine("  K_a/Φ на мелкой сетке 55…95 кэВ (приложение):");
            var sb = new StringBuilder();
            for (double e = 55.0; e <= 95.001; e += 5.0)
            {
                sb.Append(string.Format(CultureInfo.InvariantCulture, " {0:f0}:{1:f4}",
                    e, e * DoseRateCoefficients.MassEnergyAbsorptionAir(e) * 10.0 * KaPhiUnit));
            }

            Console.WriteLine("   " + sb.ToString().Trim());
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  ⚠ НАХОДКА 2: ниже 3 МэВ приложение систематически НИЖЕ ICRP на 0.7…1.2 %"
                + " в области 40…150 кэВ, и на 70 кэВ выброс {0:+0.00;-0.00} % против"
                + " {1:+0.00;-0.00} / {2:+0.00;-0.00} у соседей 60 и 80 — местный перебор"
                + " {3:f2} процентных пункта", d70, d60, d80, excess));

            Write("o2-a198-air-kaphi.csv", csv.ToString());
        }

        /// <summary>Расхождение K_a/Φ приложения с ICRP 119 в узле таблицы, %.</summary>
        static double Deviation(double energyKev)
        {
            int i = Array.IndexOf(KaPhiEnergyKev, energyKev);
            double mine = energyKev * DoseRateCoefficients.MassEnergyAbsorptionAir(energyKev)
                          * 10.0 * KaPhiUnit;
            return 100.0 * (mine - KaPhiPGyCm2[i]) / KaPhiPGyCm2[i];
        }

        // ==================================================================
        // 5. Цена для показаний: Co-60 и K-40
        // ==================================================================

        static void Price()
        {
            Console.WriteLine();
            Console.WriteLine("== цена таблицы h*(10)/K_air для показаний: Co-60 и K-40 ==");

            // 5.1 Голое отношение коэффициента на линиях этих нуклидов —
            // ответ на вопрос строки без примеси нормировки.
            double[] lines = { 1173.2, 1332.5, 1460.8 };
            string[] names = { "Co-60, 1173.2 кэВ", "Co-60, 1332.5 кэВ", "K-40, 1460.8 кэВ" };

            // ⚠ Снова ФОРМА, а не эффективность (`A258`): Зв/Гр, не доля.
            ShapeCurve oldCurve = ShapeCurve.Of(OldEnergies, OldSvPerGy());
            for (int i = 0; i < lines.Length; i++)
            {
                double now = DoseRateCoefficients.AmbientDoseConversion(lines[i]);
                double was = oldCurve.At(lines[i]);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0}: прежнее {1:f4}, ICRP 74 {2:f4}, {3:+0.00;-0.00} %",
                    names[i], was, now, 100.0 * (now - was) / was));
            }

            // 5.2 Сквозной прогон. Развёртка по ОДНОЙ величине: всё общее,
            // меняется только таблица h.
            string devicePath = Path.Combine(corpusDir, "devices",
                "Gamma-1S UDS-GC 63x63 1024 (corpus, поверка 2016).xml");
            string spectra = Path.Combine(corpusDir, "spectra");
            if (!File.Exists(devicePath))
            {
                Console.WriteLine("  нет " + devicePath + " — замер пропущен");
                Ok(false, "прибор корпуса не найден");
                return;
            }

            DeviceConfigInfo device;
            using (var fs = new FileStream(devicePath, FileMode.Open, FileAccess.Read))
            {
                device = (DeviceConfigInfo)new XmlSerializer(typeof(DeviceConfigInfo)).Deserialize(fs);
            }

            ResultData etalon = LoadSpectrum(Path.Combine(spectra, "G1S16_Cs137_P5.xml"));
            ResultData cobalt = LoadSpectrum(Path.Combine(spectra, "G1S16_Co60_P5.xml"));
            ResultData potassium = LoadSpectrum(Path.Combine(spectra, "G1S16_K40_Mar.xml"));
            // ⚠ Третья и четвёртая сцены НЕ для полноты. Эталон стоит на
            // 662 кэВ, там таблицы расходятся на +1.9 %, и нормировка этот
            // сдвиг ЗАБИРАЕТ. Поэтому у жёстких спектров цена обязана выйти
            // почти нулевой, а весь остаток — у спектров ИНОЙ формы, где
            // таблицы сходятся (ниже 500 кэВ, ≤ 0.7 %). Без мягкой сцены
            // «цена ≈ 0» была бы свойством выбранных спектров, а не ответом.
            ResultData americium = LoadSpectrum(Path.Combine(spectra, "G1S16_Am241_P5.xml"));
            ResultData barium = LoadSpectrum(Path.Combine(spectra, "G1S16_Ba133_P5.xml"));
            if (etalon == null || cobalt == null || potassium == null)
            {
                Ok(false, "спектров корпуса нет — замер пропущен");
                return;
            }

            double deviceMin, deviceMax;
            DoseRateEstimator.DeviceRange(device, null, out deviceMin, out deviceMax);
            DoseRateCurve efficiency = DoseRateEstimator.CurveOf(NaICurve());
            double low = Math.Max(deviceMin, efficiency.MinKev);
            double high = Math.Min(deviceMax, efficiency.MaxKev);

            // ⛔ Сетка обрезается по 40…3000 кэВ НАРОЧНО, и это не мелочь.
            // Прежняя таблица за этими двумя числами не существует вовсе, и
            // всякое значение, подставленное туда, было бы выдумкой: на 10 кэВ
            // ICRP 74 даёт 0.008, а удержание края прежней таблицы дало бы
            // 1.47 — разница в 184 раза, и развёртка мерила бы РАСШИРЕНИЕ
            // СЕТКИ (`C4`; ширина поставочных кривых `config/ROI/*.xml` —
            // по приказу Amber 05.09.2026 не задача, таблица «Чего делать НЕ
            // надо» в `TODO.md`), а не смену чисел (`A198`). Первый заход
            // именно так и соврал: 92.5 % на точке калибровки и +12.8 % на
            // Co-60. Здесь полоса одна на оба плеча.
            double[] grid = DoseRateEstimator.BuildGrid(
                Math.Max(low, OldEnergies[0]),
                Math.Min(high, OldEnergies[OldEnergies.Length - 1]));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  прибор {0}: {1:f0}...{2:f0} кэВ, {3} каналов",
                device.Name, deviceMin, deviceMax, device.NumberOfChannels));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  полоса развёртки {0:f0}...{1:f0} кэВ (общая обоим плечам), {2} диапазонов",
                grid[0], grid[grid.Length - 1], grid.Length - 1));

            const double Declared = 1.0;   // мкЗв/ч

            // Плечо A — прежняя таблица h. Ставится ОТРАЖЕНИЕМ на место живой,
            // чтобы весь остальной путь (сетка, μ, окна каналов, нормировка)
            // остался буква в букву тем же.
            double[] energyField = Field("AmbientEnergyKev");
            double[] valueField = Field("AmbientConversion");
            double[] keepValue = (double[])valueField.Clone();

            DoseRateConfig configNew = new DoseRateConfig();
            configNew.DoseRateCalibrationPoints =
                DoseRateEstimator.Estimate(etalon.EnergySpectrum, efficiency, Declared, grid, null);

            for (int i = 0; i < energyField.Length; i++)
            {
                double e = energyField[i];
                // За краями прежней таблицы сплайн продолжает свою форму —
                // берётся крайняя точка, как делал прежний код зажимом сетки.
                double clamped = Math.Min(Math.Max(e, OldEnergies[0]),
                                          OldEnergies[OldEnergies.Length - 1]);
                valueField[i] = oldCurve.At(clamped);
            }

            DoseRateConfig configOld = new DoseRateConfig();
            configOld.DoseRateCalibrationPoints =
                DoseRateEstimator.Estimate(etalon.EnergySpectrum, efficiency, Declared, grid, null);

            Array.Copy(keepValue, valueField, keepValue.Length);

            // Положительный контроль развёртки: плечи обязаны РАЗЛИЧАТЬСЯ.
            // Совпавшие точки значили бы, что подмена не доехала до счёта.
            double maxPoint = 0.0;
            for (int i = 0; i < configNew.DoseRateCalibrationPoints.Count
                            && i < configOld.DoseRateCalibrationPoints.Count; i++)
            {
                double a = configNew.DoseRateCalibrationPoints[i].EtalonDoseRateValue;
                double b = configOld.DoseRateCalibrationPoints[i].EtalonDoseRateValue;
                maxPoint = Math.Max(maxPoint, Math.Abs(a - b) / Math.Max(1e-30, b));
            }

            // Два условия сразу, и второе важнее первого. Первое — подмена
            // доехала до счёта (иначе плечи мерили бы одно и то же). Второе —
            // она доехала ТОЛЬКО своей величиной: таблицы расходятся не больше
            // чем на 2.24 %, и точка калибровки, разошедшаяся на десятки
            // процентов, значила бы, что в развёртку затесалось что-то ещё.
            Ok(maxPoint > 1e-6 && 100.0 * maxPoint < 5.0, string.Format(CultureInfo.InvariantCulture,
                "подмена доехала до точек калибровки и только своей величиной:"
                + " худшая точка разошлась на {0:f2} %", 100.0 * maxPoint));

            DoseRateManager manager = new DoseRateManager(Config());
            Compare(manager, "эталон Cs-137 (контроль нормировки)", etalon, configOld, configNew, Declared);
            Compare(manager, "Co-60", cobalt, configOld, configNew, double.NaN);
            Compare(manager, "K-40", potassium, configOld, configNew, double.NaN);
            if (americium != null)
            {
                Compare(manager, "Am-241 (мягкий, форма иная, чем у эталона)",
                        americium, configOld, configNew, double.NaN);
            }

            if (barium != null)
            {
                Compare(manager, "Ba-133 (середина шкалы)", barium, configOld, configNew, double.NaN);
            }
        }

        static void Compare(DoseRateManager manager, string what, ResultData data,
                            DoseRateConfig configOld, DoseRateConfig configNew, double declared)
        {
            DoseRate was = manager.Calculate(data, configOld);
            DoseRate now = manager.Calculate(data, configNew);
            double percent = was.Rate > 0.0 ? 100.0 * (now.Rate - was.Rate) / was.Rate : double.NaN;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  {0}: прежней таблицей {1:f6}, ICRP 74 {2:f6} мкЗв/ч, {3:+0.000;-0.000} %"
                + " (покрытие {4:f1} %)", what, was.Rate, now.Rate, percent, 100.0 * now.Coverage));

            if (!double.IsNaN(declared))
            {
                Ok(Math.Abs(now.Rate - declared) / declared < 1e-9
                   && Math.Abs(was.Rate - declared) / declared < 1e-9,
                   "оба плеча возвращают эталону объявленную дозу — нормировка не подменяет ответ");
            }
        }

        // ==================================================================
        // Оснастка
        // ==================================================================

        static void Write(string name, string text)
        {
            if (string.IsNullOrEmpty(csvDir))
            {
                return;
            }

            Directory.CreateDirectory(csvDir);
            string path = Path.Combine(csvDir, name);
            File.WriteAllText(path, text, new UTF8Encoding(false));
            Console.WriteLine("  записано: " + path);
        }

        static ResultData LoadSpectrum(string path)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine("  нет " + path);
                return null;
            }

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                var file = (ResultDataFile)new XmlSerializer(typeof(ResultDataFile)).Deserialize(fs);
                return file.ResultDataList.Count > 0 ? file.ResultDataList[0] : null;
            }
        }

        static GlobalConfigManager Config()
        {
            var manager = new GlobalConfigManager();
            var info = new GlobalConfigInfo();
            if (info.ColorConfig != null
                && (info.ColorConfig.SpectrumColorList == null || info.ColorConfig.SpectrumColorList.Count == 0))
            {
                info.ColorConfig.InitializeSpectrumColor();
            }

            manager.GlobalConfig = info;
            return manager;
        }

        /// <summary>
        /// Вероятность взаимодействия в кристалле NaI 63×63 по ослаблению XCOM.
        /// Это НЕ эффективность полного поглощения: пробе нужна лишь физически
        /// убывающая форма, на которую делить, и она у ОБОИХ плеч одна.
        /// </summary>
        static List<ROIEfficiencyData> NaICurve()
        {
            const double Density = 3.667;    // г/см³
            const double Thickness = 6.3;    // см
            var points = new List<ROIEfficiencyData>();
            for (double e = 10.0; e <= 10000.0; e *= 1.15)
            {
                double mu = 0.153373 * AttenuationData.MassAttenuation(11, e)
                            + 0.846627 * AttenuationData.MassAttenuation(53, e);
                double value = 1.0 - Math.Exp(-mu * Density * Thickness);
                if (value < 1e-6)
                {
                    value = 1e-6;
                }

                points.Add(new ROIEfficiencyData { Energy = e, Efficiency = value, ErrorPercent = 1.0 });
            }

            points.Add(new ROIEfficiencyData { Energy = 10000.0, Efficiency = 1e-3, ErrorPercent = 1.0 });
            return points;
        }
    }
}
