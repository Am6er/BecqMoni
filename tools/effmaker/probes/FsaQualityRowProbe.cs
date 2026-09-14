using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;

namespace FsaQualityRowProbe
{
    /// <summary>
    /// СТОРОЖ СТРОКИ КАЧЕСТВА отчёта разбора (`A96`, переписан под `A145`
    /// этап 2).
    ///
    /// ЗАЧЕМ. Хвост пометок этой строки — единственное место, где приложение
    /// говорит, чем именно посчитано разложение: с матрицей отклика, без неё
    /// или со СТАРОЙ (`A50`, пометка «· старая матрица»); подавлен ли состав
    /// (`S104`); сработало ли суммирование; есть ли кривая; упёрся ли дрейф в
    /// край сетки. До `A145` строка жила в отрисовке легенды на графике и
    /// урезалась по ширине панели, а сторож мерил её ПИКСЕЛЯМИ. Теперь текст
    /// строки — ДАННЫЕ модели (<see cref="FsaReportRow"/>), те же, что читает
    /// XPTable окна отчёта, и меряется он как данные: в обеих культурах, все
    /// пометки, без многоточия.
    ///
    /// ЧТО МЕРЯЕТСЯ:
    ///
    ///   1. ТЕКСТ. <see cref="FsaPresentationBuilder.QualityText"/> на всех
    ///      четырёх сочетаниях «матрица использована × формат старый»: ровно
    ///      ОДНА из трёх матричных пометок, и та, какая положена. Оба языка.
    ///   2. ЦЕПЬ. Признак ставится настоящему <see cref="FsaAnalysisSession"/>
    ///      полем, читается его открытым свойством и доезжает до строки
    ///      модели, собранной так, как её собирает вид, — без окна.
    ///   3. ПОЛНОТА. Самая тяжёлая сцена — `подавлен` + `старая матрица` +
    ///      `суммирование` + `без кривой` + край дрейфа — в обеих культурах:
    ///      строка модели равна собранному тексту, содержит КАЖДУЮ пометку и
    ///      не содержит многоточия; число χ²/ndf лежит в своей колонке.
    ///
    /// ⛔ И ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ, четвёртым разделом: те же проверки на
    /// заведомо испорченном входе ОБЯЗАНЫ отказать.
    ///
    ///     fsaqualityrowprobe
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ».
    /// </summary>
    static class Program
    {
        /// <summary>χ²/ndf сцены — то же число, на котором мерен `S104`.</summary>
        const double Chi2 = 2.94;

        static int bad;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            // ⛔ ОБЕ карты примитивов ROI — ДО ЛЮБОГО менеджера-одиночки
            // (`T60`): ресурсы и палитра менеджеров не трогают, но сеанс —
            // класс приложения, и дорога к одиночкам у него есть.
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();

            Text();
            Chain();
            Completeness();
            Control();

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------
        // 1. ТЕКСТ
        // ------------------------------------------------------------------

        static void Text()
        {
            Console.WriteLine();
            Console.WriteLine("=== 1. текст строки качества: какая матричная пометка ===");

            foreach (string lang in new[] { "ru-RU", "en-US" })
            {
                Language(lang);
                string old = Mark("FSAOldMatrixMark");
                string none = Mark("FSANoMatrixMark");
                string with = Mark("FSAMatrixMark");

                // Три пометки обязаны быть РАЗНЫМИ строками: слейся две — и
                // проверки ниже сошлись бы, ничего не проверив.
                Same(lang + ": три матричные пометки различны", 3, DistinctCount(old, none, with));

                foreach (bool used in new[] { false, true })
                {
                    foreach (bool oldFormat in new[] { false, true })
                    {
                        FsaResult result = Scene(used, false, true, false, false);
                        string text = FsaPresentationBuilder.QualityText(result, oldFormat);
                        string want = used ? with : (oldFormat ? old : none);
                        string scene = string.Format("{0}: матрица={1} старая={2}",
                                                     lang, used ? "да" : "нет", oldFormat ? "да" : "нет");

                        // Одна и только одна: две пометки в строке значили бы,
                        // что человеку сказали два разных ответа сразу.
                        int found = (Has(text, old) ? 1 : 0) + (Has(text, none) ? 1 : 0) + (Has(text, with) ? 1 : 0);
                        Same(scene + " — пометок ровно одна", 1, found);
                        Same(scene + " — она «" + want.Trim() + "»", true, Has(text, want));
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // 2. ЦЕПЬ: признак сеанса -> текст -> строка модели
        // ------------------------------------------------------------------

        static void Chain()
        {
            Console.WriteLine();
            Console.WriteLine("=== 2. цепь: FsaAnalysisSession.ResponseMatrixOldFormat -> строка модели ===");
            Language("ru-RU");
            string old = Mark("FSAOldMatrixMark");
            string none = Mark("FSANoMatrixMark");

            FsaResult result = Scene(false, false, true, false, false);
            var session = new FsaAnalysisSession();

            SetOldFlag(session, true);
            Same("признак поднят — открытое свойство сеанса это подтверждает", true, session.ResponseMatrixOldFormat);
            FsaReportRow on = QualityRow(FsaPresentationBuilder.Build(result, FsaGrouping.Daughters, session.ResponseMatrixOldFormat));
            Same("строка модели — та, что собрал построитель", FsaPresentationBuilder.QualityText(result, true), on.Name);
            Same("в ней есть «старая матрица»", true, Has(on.Name, old));
            Same("и нет «без матрицы»", false, Has(on.Name, none));
            Same("число χ²/ndf — в колонке значения", Chi2.ToString("f2", CultureInfo.InvariantCulture), on.Value);

            SetOldFlag(session, false);
            Same("признак снят — открытое свойство сеанса это подтверждает", false, session.ResponseMatrixOldFormat);
            FsaReportRow off = QualityRow(FsaPresentationBuilder.Build(result, FsaGrouping.Daughters, session.ResponseMatrixOldFormat));
            Same("«старой матрицы» в строке НЕТ", false, Has(off.Name, old));
            Same("а «без матрицы» есть", true, Has(off.Name, none));

            // ⛔ И ОБРАТНАЯ ПРОВЕРКА: строки с поднятым и снятым признаком
            // ОБЯЗАНЫ различаться, иначе сверки выше прошли бы на построителе,
            // который признак не читает.
            Denies("строки с поднятым и снятым признаком НЕ совпадают", on.Name == off.Name);

            // Строка одна и последняя — таблица читает её по роду, не по тексту.
            FsaPresentation both = FsaPresentationBuilder.Build(result, FsaGrouping.Daughters, true);
            int qualityRows = 0;
            foreach (FsaReportRow row in both.Rows)
            {
                if (row.Kind == FsaReportRowKind.Quality) qualityRows++;
            }

            Same("строка качества в модели одна", 1, qualityRows);
            Same("и она последняя", FsaReportRowKind.Quality, both.Rows[both.Rows.Count - 1].Kind);
        }

        // ------------------------------------------------------------------
        // 3. ПОЛНОТА: все пометки, обе культуры, без многоточия
        // ------------------------------------------------------------------

        static void Completeness()
        {
            Console.WriteLine();
            Console.WriteLine("=== 3. полнота: все пометки в обеих культурах, без многоточия ===");

            foreach (string lang in new[] { "ru-RU", "en-US" })
            {
                Language(lang);
                foreach (Scenery s in Sceneries())
                {
                    FsaResult result = Scene(false, s.Suppressed, s.Efficiency, s.Summing, s.DriftEdge);
                    FsaPresentation presentation = FsaPresentationBuilder.Build(result, FsaGrouping.Daughters, true);
                    FsaReportRow row = QualityRow(presentation);
                    string full = FsaPresentationBuilder.QualityText(result, true);
                    string scene = lang + " · " + s.Name;

                    Console.WriteLine("  {0,-40} «{1}» | {2}", scene, row.Name, row.Value);
                    Same(scene + " — строка модели равна собранному тексту", full, row.Name);
                    Same(scene + " — без многоточия", false, Trimmed(row.Name));
                    Same(scene + " — начинается с χ²/ndf", true, row.Name.StartsWith("χ²/ndf", StringComparison.Ordinal));
                    Same(scene + " — «старая матрица» есть", true, Has(row.Name, Mark("FSAOldMatrixMark")));
                    Same(scene + " — «подавлен» " + (s.Suppressed ? "есть" : "нет"), s.Suppressed, Has(row.Name, Mark("FSASuppressedMark")));
                    Same(scene + " — «суммирование» " + (s.Summing ? "есть" : "нет"), s.Summing, Has(row.Name, Mark("FSACascadeMark")));
                    Same(scene + " — «без кривой» " + (s.Efficiency ? "нет" : "есть"), !s.Efficiency, Has(row.Name, Mark("FSANoEfficiencyMark")));
                    Same(scene + " — край дрейфа " + (s.DriftEdge ? "есть" : "нет"), s.DriftEdge, Has(row.Name, Mark("FSADriftEdgeMark")));
                    Same(scene + " — значение χ²/ndf в своей колонке", Chi2.ToString("f2", CultureInfo.InvariantCulture), row.Value);
                    Same(scene + " — число НЕ в тексте пометок", false, Has(row.Name, row.Value));
                }
            }
        }

        struct Scenery
        {
            public string Name;
            public bool Suppressed;
            public bool Efficiency;
            public bool Summing;
            public bool DriftEdge;
        }

        /// <summary>
        /// Сцены хвоста, от голой к самой тяжёлой. Каскадное суммирование при
        /// «старой матрице» на экране не бывает (без матрицы его нет вовсе),
        /// но критерий 6 `A145` требует ПОЛНОЙ строки со ВСЕМИ пометками —
        /// модель обязана нести её без усечения, и это здесь меряется.
        /// </summary>
        static IEnumerable<Scenery> Sceneries()
        {
            yield return new Scenery { Name = "голая", Efficiency = true };
            yield return new Scenery { Name = "+ подавлен", Suppressed = true, Efficiency = true };
            yield return new Scenery { Name = "+ подавлен, без кривой", Suppressed = true };
            yield return new Scenery { Name = "+ подавлен, без кривой, край", Suppressed = true, DriftEdge = true };
            yield return new Scenery
            {
                Name = "ВСЕ: подавлен, суммирование, без кривой, край",
                Suppressed = true,
                Summing = true,
                DriftEdge = true
            };
        }

        // ------------------------------------------------------------------
        // 4. ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ
        // ------------------------------------------------------------------

        /// <summary>
        /// ⛔ Сторож обязан УМЕТЬ ОТКАЗАТЬ. Здесь ему подсовывают то, что он
        /// призван ловить, и каждая проверка обязана сказать «плохо».
        /// </summary>
        static void Control()
        {
            Console.WriteLine();
            FoldedLimitsSection();
            BackscatterRowSection();

            Console.WriteLine("=== 4. положительный контроль: сторож на заведомо плохом входе ===");
            Language("ru-RU");
            string old = Mark("FSAOldMatrixMark");
            FsaResult result = Scene(false, true, false, true, true);
            string full = FsaPresentationBuilder.QualityText(result, true);

            // (а) пометку вырезали из текста — сторож видит пропажу.
            Denies("пометку вырезали из текста — сторож видит пропажу", Has(full.Replace(old, string.Empty), old));

            // (б) текст урезан многоточием, как это делала старая легенда, —
            //     сторож полноты обязан отказать.
            string cut = full.Substring(0, Math.Max(1, full.IndexOf(old, StringComparison.Ordinal))) + "…";
            Same("урезанный текст действительно короче", true, cut.Length < full.Length);
            Denies("урезанный многоточием текст сторож НЕ принимает", !Trimmed(cut));
            Denies("и пометки в нём не находит", Has(cut, old));

            // (в) ОТРИЦАТЕЛЬНЫЙ конец: полный текст сторож принимает.
            Same("полный текст — без многоточия", false, Trimmed(full));
            Same("и пометка в нём цела", true, Has(full, old));

            // (г) ПОДМЕНА: строка модели с лишним знаком не равна тексту.
            FsaReportRow row = QualityRow(FsaPresentationBuilder.Build(result, FsaGrouping.Daughters, true));
            Denies("строка с лишним знаком НЕ равна собранному тексту", row.Name + "x" == full);
        }

        // ------------------------------------------------------------------
        // Сцена и отражение
        // ------------------------------------------------------------------

        /// <summary>
        /// Разложение, у которого заполнено ровно то, что читает строка
        /// качества. Состав пуст нарочно: строк состава от этого нет, и в
        /// модели остаются невязка и качество.
        /// </summary>
        static FsaResult Scene(bool matrixUsed, bool suppressed, bool efficiency, bool summing, bool driftEdge)
        {
            FsaResult result = new FsaResult
            {
                Chi2Ndf = Chi2,
                BackgroundUsed = true,
                ResponseMatrixUsed = matrixUsed,
                EfficiencyUsed = efficiency,
                CascadeSummingUsed = summing,
                GainOnGridEdge = driftEdge
            };

            if (suppressed)
            {
                // `SuppressorName` ставит только сам разбор; вердикт читается
                // из него, и подделать сцену иначе нечем.
                FieldInfo f = typeof(FsaResult).GetField("<SuppressorName>k__BackingField",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (f == null)
                {
                    Console.WriteLine("  ⛔ у FsaResult нет поля вердикта — сцену «подавлен» не собрать");
                    bad++;
                }
                else
                {
                    f.SetValue(result, "Backscatter");
                }

                if (!result.CompositionSuppressed)
                {
                    Console.WriteLine("  ⛔ сцена «подавлен» не собралась — проверки по ней пусты");
                    bad++;
                }
            }

            return result;
        }

        static FsaReportRow QualityRow(FsaPresentation presentation)
        {
            foreach (FsaReportRow row in presentation.Rows)
            {
                if (row.Kind == FsaReportRowKind.Quality)
                {
                    return row;
                }
            }

            Console.WriteLine("  ⛔ в модели нет строки качества");
            bad++;
            return new FsaReportRow { Name = string.Empty, Value = string.Empty };
        }

        /// <summary>Признак — тем же закрытым полем, каким его ставит снимок сеанса.</summary>
        static void SetOldFlag(FsaAnalysisSession session, bool value)
        {
            FieldInfo f = typeof(FsaAnalysisSession).GetField("matrixOldFormat",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (f == null)
            {
                throw new InvalidOperationException("нет поля FsaAnalysisSession.matrixOldFormat — проба смотрит не туда");
            }

            f.SetValue(session, value);
        }

        // ------------------------------------------------------------------
        // Мелочь
        // ------------------------------------------------------------------

        static bool Trimmed(string text)
        {
            return text.IndexOf('…') >= 0 || text.IndexOf("...", StringComparison.Ordinal) >= 0;
        }

        static bool Has(string text, string mark)
        {
            return text.IndexOf(mark, StringComparison.Ordinal) >= 0;
        }

        static int DistinctCount(params string[] items)
        {
            var seen = new List<string>();
            foreach (string item in items)
            {
                if (!seen.Contains(item)) seen.Add(item);
            }

            return seen.Count;
        }

        static string Mark(string name)
        {
            PropertyInfo p = typeof(BecquerelMonitor.Properties.Resources).GetProperty(
                name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (p == null)
            {
                Console.WriteLine("  ⛔ нет ресурса {0}", name);
                bad++;
                return " ";
            }

            return (string)p.GetValue(null, null);
        }

        /// <summary>Язык МЕНЯЕТСЯ ЦЕЛИКОМ — и надписи, и разделитель числа.</summary>
        static void Language(string name)
        {
            CultureInfo culture = new CultureInfo(name);
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
        }

        /// <summary>
        /// (`AMBER6`) СВЁРНУТАЯ СТРОКА ПРЕДЕЛОВ НАЗЫВАЕТ СВЁРНУТЫХ — в
        /// подсказке, раз в самой строке места нет.
        ///
        /// Вопрос Amber 08.09.2026: «Выключено равновесие у Ra-226 цепи. Где
        /// радон?» На `Чароит в домике` разбор радон СУДИТ и предел ему
        /// считает (1.58E+003 1/с, то есть заведомо не ограничивающий), но в
        /// таблицу он не попадает: гамма-выход у Rn-222 ниже порога, и свёртка
        /// (`S69`, решение Amber `S74`) кладёт его в безымянное
        /// «не определяются (3)» вместе с Po-214 и Po-210. Свёртка верна —
        /// отдельный предел был бы обещанием измерения, которого нет, — но
        /// человек, спросивший «где радон», обязан получить ответ.
        ///
        /// Положительный контроль здесь ОБЯЗАТЕЛЕН и он второй строкой: у
        /// НАЗВАННОГО кандидата подсказки быть не должно, иначе «подсказка
        /// есть» ничего не отличает.
        /// </summary>
        static void FoldedLimitsSection()
        {
            Console.WriteLine();
            Console.WriteLine("=== 5. свёрнутая строка пределов называет свёрнутых (`AMBER6`) ===");

            var result = new FsaResult { Chi2Ndf = Chi2, EfficiencyUsed = true };
            result.CharacteristicLimits.Add(new FsaCharacteristicLimit
            {
                Name = "Ra-226", Kind = FsaComponentKind.Single, Detected = false,
                DetectionLimitRate = 1.0, DetectionLimitPeakCounts = 100.0,
                TotalYieldPercent = 100.0
            });
            result.CharacteristicLimits.Add(new FsaCharacteristicLimit
            {
                Name = "Rn-222", Kind = FsaComponentKind.Single, Detected = false,
                DetectionLimitRate = 1.0, DetectionLimitPeakCounts = 100.0,
                TotalYieldPercent = 0.08
            });
            result.CharacteristicLimits.Add(new FsaCharacteristicLimit
            {
                Name = "Po-214", Kind = FsaComponentKind.Single, Detected = false,
                DetectionLimitRate = 1.0, DetectionLimitPeakCounts = 100.0,
                TotalYieldPercent = 0.01
            });

            FsaPresentation view = FsaPresentationBuilder.Build(result, FsaGrouping.Daughters, false);
            string folded = null, named = "(строки нет)";
            int foldedRows = 0;
            foreach (FsaReportRow row in view.Rows)
            {
                if (row.Kind == FsaReportRowKind.UndetectedFolded)
                {
                    folded = row.Hint;
                    foldedRows++;
                }
                else if (row.Kind == FsaReportRowKind.Undetected && row.Name == "Ra-226")
                {
                    named = row.Hint ?? "(пусто)";
                }
            }

            Console.WriteLine("  свёрнутых строк {0}, подсказка «{1}»; у названного «{2}»",
                              foldedRows, folded ?? "(нет)", named);
            Same("свёрнутая строка одна", 1, foldedRows);
            Same("она называет обоих свёрнутых", "Rn-222, Po-214", folded);
            Same("у названного кандидата подсказки нет", "(пусто)", named);
        }

        /// <summary>
        /// (`AMBER5`) ПРЕДЪЯВЛЕННОЕ И НЕ ВЫДЕЛИВШЕЕСЯ ОБРАТНОЕ РАССЕЯНИЕ
        /// НАЗЫВАЕТСЯ. Вопрос Amber 08.09.2026: «Где обратное рассеивание?» —
        /// на `Чароит в домике` строки не было вовсе при стоящем
        /// переключателе.
        ///
        /// Физика: рассеянный назад квант возвращается широким горбом около
        /// 200 кэВ; подложка модели тоже гладкая, и без матрицы её форма
        /// свободна. Измерено на том же спектре: в полосе 120…280 кэВ подложка
        /// держит 796 338 отсчётов из 872 542, образу достаётся ноль. Значение
        /// строки — СЛОВО, а не число, по тому же доводу, по какому у
        /// выделившегося рассеяния печатается «есть» (~~`S85`~~).
        ///
        /// Положительный контроль второй строкой: когда рассеяние ВЫДЕЛИЛОСЬ,
        /// второй строки быть не должно — иначе одно и то же окажется в
        /// таблице дважды.
        /// </summary>
        static void BackscatterRowSection()
        {
            Console.WriteLine();
            Console.WriteLine("=== 6. предъявленное и не выделившееся рассеяние названо (`AMBER5`) ===");

            var dropped = new FsaResult { Chi2Ndf = Chi2, EfficiencyUsed = true };
            dropped.SuppressedImages.Add(new FsaSuppressedImage
            {
                Name = FsaResult.BackscatterLayerName,
                Kind = FsaComponentKind.Nuisance,
                Z = 0.0
            });

            int rows = 0;
            string value = null, hint = null;
            foreach (FsaReportRow row in FsaPresentationBuilder.Build(dropped, FsaGrouping.Daughters, false).Rows)
            {
                if (row.Name == FsaResult.BackscatterLayerName)
                {
                    rows++;
                    value = row.Value;
                    hint = row.Hint;
                }
            }

            Console.WriteLine("  строк рассеяния {0}, значение «{1}», подсказка {2}",
                              rows, value ?? "(нет)", string.IsNullOrEmpty(hint) ? "(нет)" : "есть");
            Same("строка появилась ровно одна", 1, rows);
            Same("значение — слово, а не доля", BecquerelMonitor.Properties.Resources.FSANotResolvedNoShare, value);
            Same("причина в подсказке есть", true, !string.IsNullOrEmpty(hint));

            // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: та же сцена БЕЗ отсеянного рассеяния —
            // строки быть не должно. Без него «строка появилась» неотличимо от
            // «строка появляется всегда», и сторож слеп.
            var quiet = new FsaResult { Chi2Ndf = Chi2, EfficiencyUsed = true };
            quiet.SuppressedImages.Add(new FsaSuppressedImage
            {
                Name = "pile-up",
                Kind = FsaComponentKind.Nuisance,
                Z = 0.0
            });

            int again = 0;
            foreach (FsaReportRow row in FsaPresentationBuilder.Build(quiet, FsaGrouping.Daughters, false).Rows)
            {
                if (row.Name == FsaResult.BackscatterLayerName) again++;
            }

            Console.WriteLine("  контроль: без отсеянного рассеяния строк {0}", again);
            Same("на чужой отсеянный образ строка не заводится", 0, again);
        }

        static void Same(string what, object expected, object got)
        {
            bool ok = Equals(expected, got);
            Console.WriteLine("  {0} {1,-72} {2}{3}", ok ? "ok  " : "⛔ ", what, got,
                              ok ? string.Empty : "  вместо " + expected);
            if (!ok) bad++;
        }

        static void Denies(string what, bool found)
        {
            Console.WriteLine("  {0} {1,-72} {2}", found ? "⛔ " : "ok  ", what,
                              found ? "СТОРОЖ ПРОМОЛЧАЛ" : "отказал, как и должен");
            if (found) bad++;
        }
    }
}
