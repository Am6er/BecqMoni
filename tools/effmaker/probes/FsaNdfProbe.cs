using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace FsaNdfProbe
{
    /// <summary>
    /// (`A299`) Знаменатель ОТЧЁТНОГО остатка — точный след
    /// `tr(W₀(I−H)V₀(I−H)ᵀ)`, а не `n − активных`.
    ///
    /// ⛔ ЗАЧЕМ ОТДЕЛЬНАЯ ПРОБА, А НЕ КОРПУСНЫЙ ПРОГОН. На поставочных
    /// умолчаниях (штраф гладкости 0, веса решателя равны отчётным) верное
    /// число и приближение СОВПАДАЮТ — корпус этой правки не видит вовсе.
    /// Расходятся они там, где хубер подрезал каналы или включён составной шум
    /// `S43`, и величину расхождения корпусом не измерить: оно внутри
    /// знаменателя, а не в невязке. Поэтому проверка счётная, на разобранном
    /// контрпримере с известным ответом.
    ///
    /// Три случая, и каждый нужен:
    ///
    ///   1. **веса РАЗОШЛИСЬ** — тот самый контрпример разбора: ответ `20/9`,
    ///      прежний код давал `2`. Это плечо РАЗЛИЧАЕТ старое и новое;
    ///   2. **веса совпали, штраф есть** — формула обязана свестись к
    ///      `EffectiveNdf` (`n − 2trS + trS²`), то есть `A291` не потеряна;
    ///   3. **веса совпали, штрафа нет** — обязана свестись к `n − активных`,
    ///      то есть исходная договорённость тоже не потеряна.
    ///
    /// Плечи 2 и 3 — отрицательный контроль: если бы правка ломала вырождение,
    /// они бы и показали. Проверяется через отражение: `ReportNdf` и `FitResult`
    /// закрыты, и открывать их наружу ради пробы было бы хуже.
    ///
    ///     FsaNdfProbe.exe
    /// </summary>
    static class Program
    {
        static int failed;

        static Type analyzer;
        static Type resultType;
        static Type columnType;
        static MethodInfo reportNdf;

        static void Say(string format, params object[] args)
        {
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, format, args));
        }

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            foreach (string a in args)
            {
                // (`A263`) неизвестный ключ — отказ, а не молчание.
                Console.WriteLine("не знаю ключа: " + a);
                return 2;
            }

            analyzer = typeof(FsaAnalyzer);
            resultType = analyzer.GetNestedType("FitResult", BindingFlags.NonPublic);
            columnType = analyzer.GetNestedType("FitColumn", BindingFlags.NonPublic);
            reportNdf = analyzer.GetMethod("ReportNdf",
                                           BindingFlags.NonPublic | BindingFlags.Static);
            if (resultType == null || columnType == null || reportNdf == null)
            {
                Say("⛔ ОТКАЗ: закрытых FitResult/FitColumn/ReportNdf в сборке нет —"
                    + " проба мерила бы пустоту (FitResult {0}, FitColumn {1}, метод {2})",
                    resultType != null, columnType != null, reportNdf != null);
                return 1;
            }

            Say("ЗНАМЕНАТЕЛЬ ОТЧЁТНОГО ОСТАТКА (A299)");
            Say("");
            Say("  случай                          ждём     вышло   n−активных     итог");

            // Три наблюдения, одна колонка-константа. Грам решателя A = Σw,
            // штрафованная G = A + λ.
            double[] ones = { 1.0, 1.0, 1.0 };

            // 1. Контрпример разбора: веса решателя diag(1, 1, ¼), отчётные —
            //    единичные. Строки H равны (4/9, 4/9, 1/9), точный след 20/9.
            Case("веса разошлись (контрпример)", ones,
                 new[] { 1.0, 1.0, 0.25 }, new[] { 1.0, 1.0, 1.0 }, 0.0,
                 20.0 / 9.0, true);

            // 2. Веса совпали, штраф есть: A = 3, λ = 1, G = 4.
            //    trS = ¾, trS² = 9/16 → 3 − 1.5 + 0.5625 = 2.0625.
            Case("веса совпали, штраф есть", ones,
                 new[] { 1.0, 1.0, 1.0 }, new[] { 1.0, 1.0, 1.0 }, 1.0,
                 2.0625, false);

            // 3. Веса совпали, штрафа нет: обязано выйти ровно n − активных.
            Case("веса совпали, штрафа нет", ones,
                 new[] { 1.0, 1.0, 1.0 }, new[] { 1.0, 1.0, 1.0 }, 0.0,
                 2.0, false);

            // 4. Отказ считать: у канала полосы нет положительного отчётного
            //    веса — ожидание отчётной суммы не определено, и подставлять
            //    ноль молча нельзя.
            Case("отчётный вес канала нулевой", ones,
                 new[] { 1.0, 1.0, 1.0 }, new[] { 1.0, 0.0, 1.0 }, 0.0,
                 0.0, false);

            Say("");
            Say(failed == 0 ? "ВСЕ СОШЛИСЬ" : "ПРОВАЛОВ: " + failed);
            return failed == 0 ? 0 : 1;
        }

        /// <summary>
        /// Один случай: собрать `FitResult` отражением, позвать закрытый
        /// `ReportNdf` и сверить с независимо посчитанным ожиданием.
        ///
        /// `separates` — плечо, которое РАЗЛИЧАЕТ новое и прежнее (`n − p`).
        /// Отмечено явно: случай, где старое и новое дают одно, проверяет лишь
        /// что код не сломан вовсе.
        /// </summary>
        static void Case(string name, double[] template, double[] solver,
                         double[] report, double lambda, double want, bool separates)
        {
            int n = template.Length;

            // A = Σ w·x², G = A + λ (одна колонка, поэтому всё скалярное).
            double gram = 0.0;
            for (int i = 0; i < n; i++)
            {
                gram += solver[i] * template[i] * template[i];
            }

            double penalized = gram + lambda;

            object column = Activator.CreateInstance(columnType, true);
            columnType.GetField("Values").SetValue(column, template);

            Type listType = typeof(List<>).MakeGenericType(columnType);
            object columns = Activator.CreateInstance(listType);
            listType.GetMethod("Add").Invoke(columns, new[] { column });

            object fit = Activator.CreateInstance(resultType, true);
            resultType.GetField("Columns").SetValue(fit, columns);
            resultType.GetField("Weights").SetValue(fit, solver);
            resultType.GetField("ActiveIndices").SetValue(fit, new List<int> { 0 });
            resultType.GetField("ActiveInverse").SetValue(fit,
                new double[,] { { 1.0 / penalized } });

            object raw = reportNdf.Invoke(null, new object[] { fit, report, 0, n - 1 });
            double got = (double)raw;

            // Прежнее приближение — для столбца сравнения.
            double old = Math.Max(1.0, n - 1);

            bool ok = Math.Abs(got - want) <= 1.0E-9 * (1.0 + Math.Abs(want));
            if (!ok)
            {
                failed++;
            }

            Say("  {0,-30} {1,8:F5} {2,9:F5} {3,12:F5}   {4}{5}",
                name, want, got, old, ok ? "СОШЛОСЬ" : "⛔ ПРОВАЛ",
                separates ? "   — РАЗЛИЧАЕТ" : "");
        }
    }
}
