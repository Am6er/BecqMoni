// Читатель полосы П23, 10.09.2026: ИСПОЛНЕНО ЛИ РЕШЕНИЕ AMBER по `AMBER14`
// (находка 2) — «Предупреждать при вывозе» в N42, — числом и из СОБРАННОЙ
// сборки, без единого окна.
//
// ⛔ Окно `BecqMoni` не поднимается. Само предупреждение показывается через
// `AppUi.Report`, а решение «говорить или молчать» вынесено в отдельный
// безоконный метод `DocumentManager.N42ExportWarning(ResultDataFile)` — именно
// затем, чтобы положительный контроль снимался ЧИСЛОМ, а не нажатием «ОК».
//
// Четыре раздела:
//
//  §1 ДВА ПЛЕЧА ПРЕДУПРЕЖДЕНИЯ. Документ БЕЗ кривой — метод обязан вернуть
//     `null` (терять нечего, предупреждение молчит); документ С кривой —
//     обязан вернуть текст, и в тексте обязаны стоять ЧИСЛА «сколько из
//     скольких». ⚠ Плечи названы именно так, а не наоборот: предупреждение
//     говорит о ПОТЕРЕ, а у документа без кривой терять нечего.
//
//  §2 ТЕКСТ НАЗЫВАЕТ НЕ ТОЛЬКО КРИВУЮ. Довод Amber при решении: через
//     `ResultData.Efficiency` приходят ещё `Geometry` и привязка к матрице
//     отклика, поэтому файл теряет РАЗОМ активность выделения, активность
//     зон, FSA и нормировку. Раздел требует, чтобы все шесть предметов были
//     названы, и требует этого В ОБЕИХ КУЛЬТУРАХ — нейтральной и `ru`
//     (правило проекта: строка заводится ПАРОЙ).
//
//  §3 ПРЕДУПРЕЖДЕНИЕ ПРИВИНЧЕНО К ВЫВОЗУ. Разбор IL тела
//     `DocumentManager.ExportDocumentN42`: оно обязано звать и
//     `N42ExportWarning`, и `AppUi.Report`. Метод, который никто не зовёт, —
//     это признак без читателя, и такой в этом дереве уже находили
//     (`DoseRateSpectrumChoice.Efficiency`, 0 чтений при 1 записи).
//
//  §4 ПОСЫЛКА РЕШЕНИЯ ВЕРНА: схема N42 кривую не несёт. Отражением по
//     `BecquerelMonitor.N42`: членов со словом `Efficiency` обязано быть 0,
//     положительный контроль того же скана — `EnergyCalibration`, совпадений
//     обязано быть больше нуля.
//
//   n42exportwarningp23probe
//   n42exportwarningp23probe --sabotage=curve|nocurve|wire|n42   (ждёт ОТКАЗ)
//
// ⛔ Приёмка, которая проходит всегда, не мерит ничего. `--sabotage` портит
// РОВНО ОДНУ вещь и требует, чтобы свой раздел ОТКАЗАЛ; коды у него
// перевёрнуты: 0 — отказ получен (читатель смотрит), 1 — не получен (слеп).
//
//   curve   — в плечо «БЕЗ кривой» подсовывается документ С кривой:
//             предупреждение обязано заговорить там, где ждали тишины;
//   nocurve — в плечо «С кривой» подсовывается документ БЕЗ кривой:
//             предупреждение обязано промолчать там, где ждали слов;
//   wire    — в IL вывоза ищется вызов метода, которого там нет вовсе;
//   n42     — запретным словом скана объявляется `EnergyCalibration`,
//             которое в схеме N42 ЕСТЬ.
//
// Коды возврата обычного прогона: 0 — все проверки прошли, 1 — есть
// непрошедшие, 2 — отказ оснастки.

using BecquerelMonitor;
using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace N42ExportWarningP23Probe
{
    static class Program
    {
        static int failed;
        static int checks;
        static string sabotage;

        /// <summary>
        /// Что предупреждение ОБЯЗАНО назвать, по-английски и по-русски. Список
        /// не вкусовой: это дословный довод Amber при решении 10.09.2026 —
        /// теряется не «кривая», а кривая + геометрия + матрица, и с ними
        /// четыре возможности разом.
        /// </summary>
        static readonly string[] MustNameNeutral =
        {
            "efficiency curve", "geometry", "response matrix",
            "activity of the selection", "activity of the ROIs",
            "full-spectrum analysis", "normalization by efficiency"
        };

        static readonly string[] MustNameRu =
        {
            "кривая эффективности", "геометрия детектора", "матрице отклика",
            "активности выделения", "активности зон",
            "полноспектрального разбора", "нормировки по эффективности"
        };

        static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch (IOException) { }
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            foreach (string a in args)
            {
                if (a.StartsWith("--sabotage=", StringComparison.Ordinal)) sabotage = a.Substring(11);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (sabotage != null && sabotage != "curve" && sabotage != "nocurve"
                && sabotage != "wire" && sabotage != "n42")
            {
                Console.Error.WriteLine("--sabotage= принимает curve, nocurve, wire или n42");
                return 2;
            }

            Console.WriteLine("ЧИТАТЕЛЬ П23: предупреждение при вывозе в N42 (AMBER14, находка 2)");
            Console.WriteLine("сборка под рукой: " + typeof(DocumentManager).Assembly.Location);
            if (sabotage != null)
            {
                Console.WriteLine("⚠ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: испорчено «" + sabotage + "», ждём ОТКАЗ");
            }
            Console.WriteLine();

            try
            {
                TwoArms();
                TextNamesEverything();
                WiredToExport();
                SchemeCarriesNoCurve();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ОТКАЗ ОСНАСТКИ: " + ex);
                return 2;
            }

            Console.WriteLine();
            Console.WriteLine("ИТОГ: проверок {0}, не прошло {1}",
                              checks.ToString(CultureInfo.InvariantCulture),
                              failed.ToString(CultureInfo.InvariantCulture));

            if (sabotage != null)
            {
                bool refused = failed > 0;
                Console.WriteLine(refused
                    ? "ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: отказ ПОЛУЧЕН — читатель смотрит"
                    : "ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: отказа НЕТ — читатель СЛЕП");
                return refused ? 0 : 1;
            }

            return failed > 0 ? 1 : 0;
        }

        // ==================================================================
        // §1. Два плеча предупреждения
        // ==================================================================

        static void TwoArms()
        {
            Head("§1. Два плеча: без кривой — молчит, с кривой — говорит");

            // Плечо «БЕЗ кривой»: три спектра, ни у одного Efficiency нет.
            ResultDataFile silent = MakeFile(3, 0);
            // Плечо «С кривой»: три спектра, у двух кривая есть.
            ResultDataFile speaking = MakeFile(3, 2);

            if (sabotage == "curve") silent = MakeFile(3, 1);
            if (sabotage == "nocurve") speaking = MakeFile(3, 0);

            string saidNothing = Warning(silent);
            Check("документ БЕЗ кривой — предупреждение молчит",
                  saidNothing == null,
                  saidNothing == null ? "вернулся null" : "сказано: " + Flat(saidNothing));

            string said = Warning(speaking);
            Check("документ С кривой — предупреждение говорит",
                  !string.IsNullOrEmpty(said),
                  said == null ? "вернулся null — МОЛЧИТ" : "сказано " + said.Length.ToString(CultureInfo.InvariantCulture) + " знаков");

            if (!string.IsNullOrEmpty(said))
            {
                Console.WriteLine("      текст: " + Flat(said));
                // Числа «сколько из скольких» — из них человек понимает охват.
                // ⚠ Ищутся ОТДЕЛЬНЫЕ числовые слова, а не подстроки: «N42»
                // подстрокой даёт и «2», и «42», и проверка на подстроку
                // проходила бы всегда, то есть не мерила бы ничего.
                var nums = new HashSet<string>(
                    System.Text.RegularExpressions.Regex.Matches(said, @"\b\d+\b")
                        .Cast<System.Text.RegularExpressions.Match>().Select(m => m.Value),
                    StringComparer.Ordinal);
                bool hasCounts = nums.Contains("2") && nums.Contains("3");
                Check("предупреждение называет ЧИСЛА (2 спектра из 3)", hasCounts,
                      "числовые слова текста: " + (nums.Count == 0 ? "(нет)" : string.Join(", ", nums.OrderBy(x => x, StringComparer.Ordinal))));
            }

            // Границы: пустой список и null — тоже молчание, а не отказ.
            Check("пустой документ — молчит", Warning(MakeFile(0, 0)) == null, "");
            Check("документа нет вовсе (null) — молчит", Warning(null) == null, "");
        }

        /// <summary>
        /// Файл из <paramref name="total"/> спектров, у первых
        /// <paramref name="withCurve"/> из них проставлена кривая.
        /// </summary>
        static ResultDataFile MakeFile(int total, int withCurve)
        {
            ResultDataFile f = new ResultDataFile();
            f.ResultDataList = new List<ResultData>();
            for (int i = 0; i < total; i++)
            {
                ResultData d = new ResultData();
                if (i < withCurve)
                {
                    EfficiencyConfigData eff = new EfficiencyConfigData("П23");
                    eff.Curve = new List<ROIEfficiencyData>();
                    d.Efficiency = eff;
                }
                f.ResultDataList.Add(d);
            }
            return f;
        }

        static string Warning(ResultDataFile file)
        {
            MethodInfo m = typeof(DocumentManager).GetMethod("N42ExportWarning",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (m == null)
            {
                throw new InvalidOperationException(
                    "DocumentManager.N42ExportWarning не найден — предупреждения в сборке НЕТ");
            }
            return (string)m.Invoke(null, new object[] { file });
        }

        // ==================================================================
        // §2. Текст называет всё, что теряется, и в обеих культурах
        // ==================================================================

        static void TextNamesEverything()
        {
            Head("§2. Предупреждение называет ВСЕ шесть потерь, в обеих культурах");

            ResultDataFile file = MakeFile(2, 1);

            CultureInfo saved = Resources.Culture;
            try
            {
                Resources.Culture = CultureInfo.InvariantCulture;
                Names("нейтральная", Warning(file), MustNameNeutral);

                Resources.Culture = new CultureInfo("ru");
                Names("ru", Warning(file), MustNameRu);
            }
            finally
            {
                Resources.Culture = saved;
            }
        }

        static void Names(string culture, string text, string[] must)
        {
            if (string.IsNullOrEmpty(text))
            {
                Check("культура " + culture + ": текст есть", false, "пусто");
                return;
            }
            Console.WriteLine("      [{0}] {1}", culture, Flat(text));
            var missing = must.Where(w => text.IndexOf(w, StringComparison.OrdinalIgnoreCase) < 0).ToArray();
            Check("культура " + culture + ": названы все " + must.Length.ToString(CultureInfo.InvariantCulture) + " предметов",
                  missing.Length == 0,
                  missing.Length == 0 ? "все на месте" : "НЕ названо: " + string.Join(", ", missing));
        }

        // ==================================================================
        // §3. Предупреждение привинчено к вывозу
        // ==================================================================

        static void WiredToExport()
        {
            Head("§3. Вывоз ExportDocumentN42 действительно зовёт предупреждение");

            MethodInfo export = typeof(DocumentManager).GetMethod("ExportDocumentN42",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (export == null)
            {
                Check("DocumentManager.ExportDocumentN42 найден", false, "метода нет");
                return;
            }

            string[] wanted = sabotage == "wire"
                ? new[] { "N42ExportWarningЧегоНетВовсе" }
                : new[] { "N42ExportWarning", "Report" };

            HashSet<string> called = CalleesOf(export);
            Console.WriteLine("      вызовов в теле вывоза: {0}",
                              called.Count.ToString(CultureInfo.InvariantCulture));
            foreach (string w in wanted)
            {
                Check("вывоз зовёт " + w, called.Contains(w),
                      called.Contains(w) ? "есть" : "в IL тела такого вызова НЕТ");
            }
        }

        /// <summary>
        /// Имена методов, которые зовёт тело. Байтовый проход по `call`/`callvirt`:
        /// случайное совпадение байтов не резолвится в метод и отсеивается само.
        /// </summary>
        static HashSet<string> CalleesOf(MethodBase mb)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            MethodBody body = mb.GetMethodBody();
            if (body == null) return names;
            byte[] il = body.GetILAsByteArray();
            if (il == null) return names;

            Module mod = mb.Module;
            for (int i = 0; i + 4 < il.Length; i++)
            {
                byte op = il[i];
                if (op != 0x28 && op != 0x6F) continue;
                MethodBase callee;
                try { callee = mod.ResolveMethod(BitConverter.ToInt32(il, i + 1)); }
                catch (Exception) { continue; }
                if (callee == null) continue;
                names.Add(callee.Name);
            }
            return names;
        }

        // ==================================================================
        // §4. Схема N42 кривую не несёт (посылка решения Amber)
        // ==================================================================

        static void SchemeCarriesNoCurve()
        {
            Head("§4. Посылка: в схеме N42 эффективности нет вовсе");

            string forbidden = sabotage == "n42" ? "EnergyCalibration" : "Efficiency";
            const string control = "EnergyCalibration";

            Assembly asm = typeof(DocumentManager).Assembly;
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }

            Type[] n42 = types.Where(t => t.Namespace != null
                                          && t.Namespace.StartsWith("BecquerelMonitor.N42", StringComparison.Ordinal))
                              .ToArray();
            Console.WriteLine("      типов в BecquerelMonitor.N42: {0}",
                              n42.Length.ToString(CultureInfo.InvariantCulture));

            var hitsForbidden = new List<string>();
            var hitsControl = new List<string>();
            foreach (Type t in n42)
            {
                Scan(t.Name, t.FullName + " (тип)", forbidden, control, hitsForbidden, hitsControl);
                MemberInfo[] members;
                try
                {
                    members = t.GetMembers(BindingFlags.Instance | BindingFlags.Static
                                           | BindingFlags.Public | BindingFlags.NonPublic
                                           | BindingFlags.DeclaredOnly);
                }
                catch (Exception) { continue; }
                foreach (MemberInfo m in members)
                {
                    Scan(m.Name, t.Name + "." + m.Name, forbidden, control, hitsForbidden, hitsControl);
                }
            }

            Console.WriteLine("      членов со словом «{0}»: {1}", control,
                              hitsControl.Count.ToString(CultureInfo.InvariantCulture));
            Check("положительный контроль скана: «" + control + "» в схеме ЕСТЬ",
                  hitsControl.Count > 0,
                  hitsControl.Count > 0 ? "первое: " + hitsControl[0] : "скан не видит НИЧЕГО — он слеп");

            Check("членов со словом «" + forbidden + "» — ноль",
                  hitsForbidden.Count == 0,
                  hitsForbidden.Count == 0
                      ? "0"
                      : hitsForbidden.Count.ToString(CultureInfo.InvariantCulture) + ": "
                        + string.Join(", ", hitsForbidden.Take(5)));
        }

        static void Scan(string name, string label, string forbidden, string control,
                         List<string> hitsForbidden, List<string> hitsControl)
        {
            if (name.IndexOf(forbidden, StringComparison.OrdinalIgnoreCase) >= 0) hitsForbidden.Add(label);
            if (name.IndexOf(control, StringComparison.OrdinalIgnoreCase) >= 0) hitsControl.Add(label);
        }

        // ==================================================================

        static void Head(string title)
        {
            Console.WriteLine();
            Console.WriteLine("=== " + title + " ===");
        }

        static void Check(string what, bool ok, string detail)
        {
            checks++;
            if (!ok) failed++;
            Console.WriteLine("  [{0}] {1}{2}", ok ? "ok" : "НЕТ", what,
                              string.IsNullOrEmpty(detail) ? "" : " — " + detail);
        }

        static string Flat(string s)
        {
            if (s == null) return "";
            return s.Replace("\r", " ").Replace("\n", " ").Replace("  ", " ").Trim();
        }
    }
}
