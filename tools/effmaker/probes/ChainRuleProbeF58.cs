using BecquerelMonitor.FullSpectrumAnalysis;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// `A218` (остаток `T78`): зажим по уровню в `decay_chain` — ОДНО правило на
    /// приложение, `DecayParentRule.ChainLevelClause`.
    ///
    /// ⛔ ЧТО ИМЕННО ПОВЕРЯЕТСЯ. Три вещи, и ни одна из них не «печать чисел»:
    ///
    ///   1. **СВОДИМОСТЬ.** По дереву приложения не должно остаться НИ ОДНОЙ
    ///      второй записи зажима — ни `min(l_seqno)`, ни `l_seqno = 0`, — а
    ///      обращений к `DecayParentRule.ChainLevelClause` должно быть ровно
    ///      столько, сколько запросов к `decay_chain` с зажимом (4). Подмена
    ///      ОДНОГО места этим и ловится: копий станет 1, обращений 3.
    ///   2. **КОНТРАКТ** с `tools/CORPUS/scripts/chains.py`: выражение
    ///      начинается с ` and `, зовёт внешнюю таблицу алиасом `d`, параметров
    ///      кроме `$n` не несёт. Разойдись он — корпусные скрипты читают у
    ///      приложения мусор и молчат.
    ///   3. **ЦЕНА ПЕРЕХОДА C → A** для `CascadeAtomicData`, числом: у скольких
    ///      родителей меняется дочерний атом, у скольких его не было вовсе, и
    ///      сколько петель `daughter = nucid` в выборку приходит (их потребитель
    ///      обязан снимать САМ — правило их не снимает и не должно).
    ///
    /// ⚠ СНЯТОЕ правило C (`l_seqno = 0`) живёт здесь КОПИЕЙ НАРОЧНО: это
    /// «было», с которым сравнивается «стало». Второго соглашения оно не
    /// заводит — из приложения оно удалено, и раздел 1 за этим следит.
    ///
    /// ⚠ Правило берётся ОТРАЖЕНИЕМ, а не прямой ссылкой, чтобы один и тот же
    /// исходник пробы собирался и против сборки ДО правки (поля нет), и против
    /// сборки ПОСЛЕ. Иначе «до» померить нечем.
    ///
    /// Положительные контроли:
    ///   * `--clause=<заведомо неверный текст>` — правило подменяется, числа
    ///     раздела 3 обязаны разойтись с живым `CascadeAtomicData`, и проба
    ///     ОТКАЗЫВАЕТ (раздел 4, «сверка эмуляции с приложением»);
    ///   * `--src=<копия дерева с возвращённой копией>` — раздел 1 отказывает.
    ///
    /// Запуск: ChainRuleProbeF58.exe [--src=&lt;корень&gt;] [--db=&lt;nucdb&gt;]
    ///                              [--clause=&lt;подмена&gt;] [--top=N]
    /// Ожидание: «ВСЁ СОШЛОСЬ» и код 0.
    /// </summary>
    static class ChainRuleProbeF58
    {
        /// <summary>Снятое правило C — только как «было». В приложении его нет.</summary>
        const string RetiredClauseC = " and l_seqno = 0";

        /// <summary>
        /// Текст A на день правки — запасной, если поля в сборке ещё нет
        /// (то есть проба гоняется против сборки ДО правки).
        /// </summary>
        const string FallbackClauseA =
            " and l_seqno = (select min(l_seqno) from decay_chain x"
            + " where x.nucid = d.nucid and x.daughter_nucid = d.daughter_nucid"
            + " and x.dec_type = d.dec_type)";

        /// <summary>Сколько обращений к правилу ждём в приложении.</summary>
        const int ExpectedUses = 4;

        static int failures;
        static int top = 12;

        static void Fail(string text)
        {
            failures++;
            Console.WriteLine("  ⛔ ОТКАЗ: " + text);
        }

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            string src = null, db = null, clauseOverride = null;
            foreach (string arg in args)
            {
                if (arg.StartsWith("--src=", StringComparison.Ordinal)) src = arg.Substring(6);
                else if (arg.StartsWith("--db=", StringComparison.Ordinal)) db = arg.Substring(5);
                else if (arg.StartsWith("--clause=", StringComparison.Ordinal)) clauseOverride = arg.Substring(9);
                else if (arg.StartsWith("--top=", StringComparison.Ordinal))
                    top = int.Parse(arg.Substring(6), CultureInfo.InvariantCulture);
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + arg);
                    return 2;
                }
            }

            if (string.IsNullOrEmpty(db))
            {
                db = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
            }

            if (!File.Exists(db))
            {
                Console.Error.WriteLine("нет nucdb.sqlite: " + db);
                return 2;
            }

            if (string.IsNullOrEmpty(src))
            {
                src = FindRepo();
            }

            Console.WriteLine("A218 / T78 — зажим по уровню в decay_chain");
            Console.WriteLine("  дерево: {0}", src ?? "(не найдено)");
            Console.WriteLine("  база:   {0}", db);
            Console.WriteLine();

            // ----------------------------------------------------------------
            string live = LiveClause();
            string clause = clauseOverride ?? live ?? FallbackClauseA;
            Console.WriteLine("РАЗДЕЛ 1. СВОДИМОСТЬ ПО ДЕРЕВУ ПРИЛОЖЕНИЯ");
            Scan(src);
            Console.WriteLine();

            Console.WriteLine("РАЗДЕЛ 2. КОНТРАКТ ПРАВИЛА");
            Console.WriteLine("  источник: {0}",
                              clauseOverride != null ? "ПОДМЕНА ключом --clause"
                              : live != null ? "DecayParentRule.ChainLevelClause (отражением)"
                              : "поля в сборке НЕТ — запасной текст A (сборка ДО правки)");
            Console.WriteLine("  текст:    «{0}»", Squeeze(clause));
            Contract(clause, live != null && clauseOverride == null);
            Console.WriteLine();

            Console.WriteLine("РАЗДЕЛ 3. ЧИСЛА ПО БАЗЕ: правило против снятого C");
            List<string> changed = Numbers(db, clause);
            Console.WriteLine();

            Console.WriteLine("РАЗДЕЛ 4. ПРИЛОЖЕНИЕ: CascadeAtomicData");
            Application(db, clause, changed);
            Console.WriteLine();

            Console.WriteLine(failures == 0 ? "ВСЁ СОШЛОСЬ" : "ОТКАЗОВ: " + failures);
            return failures == 0 ? 0 : 1;
        }

        // ====================================================================
        // Раздел 1: сколько копий зажима осталось в дереве
        // ====================================================================

        static readonly Regex CopyMin = new Regex(@"min\s*\(\s*l_seqno", RegexOptions.IgnoreCase);
        static readonly Regex CopyZero = new Regex(@"l_seqno\s*=\s*0\b", RegexOptions.IgnoreCase);
        static readonly Regex UseRule = new Regex(@"DecayParentRule\s*\.\s*ChainLevelClause");
        static readonly Regex Declare = new Regex(@"const\s+string\s+ChainLevelClause\s*=");
        static readonly Regex FromChain = new Regex(@"from\s+decay_chain", RegexOptions.IgnoreCase);

        static void Scan(string repo)
        {
            if (repo == null)
            {
                Fail("корень дерева не найден — сводимость проверить нечем (ключ --src=)");
                return;
            }

            string app = Path.Combine(repo, "BecquerelMonitor");
            if (!Directory.Exists(app))
            {
                Fail("нет каталога приложения: " + app);
                return;
            }

            int copies = 0, uses = 0, declarations = 0, unclamped = 0;
            var where = new List<string>();
            var unclampedWhere = new List<string>();

            foreach (string file in Directory.GetFiles(app, "*.cs", SearchOption.AllDirectories))
            {
                string rel = file.Substring(repo.Length).TrimStart('\\', '/');
                if (rel.IndexOf("\\bin\\", StringComparison.OrdinalIgnoreCase) >= 0
                    || rel.IndexOf("\\obj\\", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                bool isRule = string.Equals(Path.GetFileName(file), "DecayParentRule.cs",
                                            StringComparison.OrdinalIgnoreCase);
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    // Комментарии в счёт не идут: разбор `T78` про SQL, а не про
                    // разговоры о нём — иначе объяснение, почему копий больше
                    // нет, само считалось бы копией.
                    string code = StripComment(line);
                    if (Declare.IsMatch(code)) { declarations++; continue; }

                    if (UseRule.IsMatch(code)) uses++;

                    if (isRule) continue;

                    if (CopyMin.IsMatch(code) || CopyZero.IsMatch(code))
                    {
                        copies++;
                        where.Add(string.Format(CultureInfo.InvariantCulture, "{0}:{1}", rel, i + 1));
                    }

                    if (FromChain.IsMatch(code) && !HasClampNearby(lines, i))
                    {
                        unclamped++;
                        unclampedWhere.Add(string.Format(CultureInfo.InvariantCulture, "{0}:{1}", rel, i + 1));
                    }
                }
            }

            Console.WriteLine("  объявлений ChainLevelClause: {0} (ждём 1)", declarations);
            Console.WriteLine("  обращений к правилу:         {0} (ждём {1})", uses, ExpectedUses);
            Console.WriteLine("  копий зажима в приложении:   {0} (ждём 0){1}", copies,
                              copies == 0 ? "" : " — " + string.Join(", ", where.ToArray()));
            Console.WriteLine("  чтений decay_chain БЕЗ зажима: {0}{1}", unclamped,
                              unclamped == 0 ? "" : " — " + string.Join(", ", unclampedWhere.ToArray()));
            Console.WriteLine("      (это показ «как есть» в карточке NucBase, не копия правила;");
            Console.WriteLine("       у них зажима нет вовсе, и `l_seqno` в них не встречается)");

            if (declarations != 1) Fail("объявление ChainLevelClause должно быть РОВНО одно");
            if (copies != 0) Fail("в приложении осталась вторая запись зажима");
            if (uses != ExpectedUses)
                Fail("обращений к правилу " + uses + ", а запросов с зажимом " + ExpectedUses
                     + " — одно место разошлось с общим правилом");
        }

        /// <summary>
        /// Есть ли у запроса, начатого на строке `i`, обращение к правилу.
        /// Смотрим окно в шесть строк: текст запроса склеен из литералов и
        /// довесок стоит следующей строкой или через одну.
        /// </summary>
        static bool HasClampNearby(string[] lines, int i)
        {
            for (int k = Math.Max(0, i - 3); k < Math.Min(lines.Length, i + 6); k++)
            {
                string code = StripComment(lines[k]);
                if (UseRule.IsMatch(code) || CopyMin.IsMatch(code) || CopyZero.IsMatch(code))
                {
                    return true;
                }
            }

            return false;
        }

        static string StripComment(string line)
        {
            int at = line.IndexOf("//", StringComparison.Ordinal);
            string code = at >= 0 ? line.Substring(0, at) : line;
            return code.TrimStart().StartsWith("///", StringComparison.Ordinal) ? "" : code;
        }

        // ====================================================================
        // Раздел 2: контракт
        // ====================================================================

        static string LiveClause()
        {
            FieldInfo field = typeof(DecayParentRule).GetField(
                "ChainLevelClause", BindingFlags.Public | BindingFlags.Static);
            if (field == null || field.FieldType != typeof(string))
            {
                return null;
            }

            return (string)(field.IsLiteral ? field.GetRawConstantValue() : field.GetValue(null));
        }

        static void Contract(string clause, bool fromApp)
        {
            string flat = Squeeze(clause);
            if (!clause.StartsWith(" and ", StringComparison.Ordinal))
                Fail("выражение обязано начинаться с ` and ` — chains.py склеивает его прямо в where");
            if (flat.IndexOf("l_seqno", StringComparison.Ordinal) < 0)
                Fail("в выражении нет l_seqno — это не зажим по уровню");
            if (flat.IndexOf("d.", StringComparison.Ordinal) < 0)
                Fail("выражение не зовёт внешнюю таблицу алиасом `d` — договор с chains.py нарушен");
            foreach (char bad in new[] { '?', ':', '@' })
            {
                if (flat.IndexOf(bad) >= 0)
                    Fail("в выражении появился параметр «" + bad + "» — связать его нечем");
            }

            foreach (Match m in Regex.Matches(flat, @"\$\w+"))
            {
                if (m.Value != "$n") Fail("параметр «" + m.Value + "»: договор допускает только $n");
            }

            if (!fromApp)
            {
                Console.WriteLine("  ⚠ правило взято НЕ из сборки — раздел 1 скажет, есть ли оно там вовсе");
            }
        }

        static string Squeeze(string text)
        {
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        // ====================================================================
        // Раздел 3: числа по базе
        // ====================================================================

        sealed class Row
        {
            public string Daughter;
            public string Perc;
        }

        static List<Row> Rows(SqliteConnection cn, string clause, string nucid, bool percNotNull)
        {
            var found = new List<Row>();
            using (SqliteCommand command = cn.CreateCommand())
            {
                command.CommandText = "select daughter_nucid, perc from decay_chain d where nucid = $n"
                                      + (percNotNull ? " and perc not null" : "") + clause;
                command.Parameters.AddWithValue("$n", nucid);
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        found.Add(new Row
                        {
                            Daughter = reader.IsDBNull(0) ? null : reader.GetString(0),
                            Perc = reader.IsDBNull(1) ? null : reader.GetString(1)
                        });
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// Дочерний глазами `CascadeAtomicData`: одна ветвь с наибольшим `perc`,
        /// непрочитанное число считается нулём, начальное «лучшее» = −1.
        /// `dropLoops` повторяет явное снятие петель у потребителя.
        /// </summary>
        static string Best(List<Row> rows, string nucid, bool dropLoops)
        {
            double best = -1.0;
            string name = null;
            foreach (Row row in rows)
            {
                if (string.IsNullOrEmpty(row.Daughter)) continue;
                if (dropLoops && string.Equals(row.Daughter, nucid, StringComparison.OrdinalIgnoreCase)) continue;
                double perc;
                if (!double.TryParse(row.Perc ?? "", NumberStyles.Float, CultureInfo.InvariantCulture, out perc))
                {
                    perc = 0.0;
                }

                if (perc > best) { best = perc; name = row.Daughter; }
            }

            return name;
        }

        static readonly Dictionary<string, string> After = new Dictionary<string, string>(StringComparer.Ordinal);
        static readonly List<string> Parents = new List<string>();

        static List<string> Numbers(string db, string clause)
        {
            var changed = new List<string>();
            using (var cn = new SqliteConnection("Data Source=" + db + ";Mode=ReadOnly;"))
            {
                cn.Open();
                using (SqliteCommand command = cn.CreateCommand())
                {
                    command.CommandText = "select distinct nucid from decay_chain order by nucid";
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read()) Parents.Add(reader.GetString(0));
                    }
                }

                Console.WriteLine("  родителей в decay_chain: {0}", Parents.Count);

                int setsDiffer = 0, loopParents = 0, loopWins = 0;
                int beforeNone = 0, afterNone = 0, bothDiffer = 0;
                var examples = new List<string>();
                var loopNames = new List<string>();

                foreach (string parent in Parents)
                {
                    List<Row> a = Rows(cn, clause, parent, true);
                    List<Row> c = Rows(cn, RetiredClauseC, parent, true);
                    if (!SameSet(a, c)) setsDiffer++;

                    // Потребитель: `CascadeAtomicData` фильтра `perc not null` не
                    // ставит — числа читаются как есть, поэтому здесь тоже без него.
                    List<Row> aFull = Rows(cn, clause, parent, false);
                    List<Row> cFull = Rows(cn, RetiredClauseC, parent, false);
                    string was = Best(cFull, parent, false);          // как было в коде
                    string now = Best(aFull, parent, true);           // как стало
                    After[parent] = now;

                    bool loop = false, loopWin = false;
                    foreach (Row row in aFull)
                    {
                        if (row.Daughter != null
                            && string.Equals(row.Daughter, parent, StringComparison.OrdinalIgnoreCase))
                        {
                            loop = true;
                        }
                    }

                    if (loop)
                    {
                        loopParents++;
                        loopNames.Add(parent);
                        string naive = Best(aFull, parent, false);
                        if (naive != null && string.Equals(naive, parent, StringComparison.OrdinalIgnoreCase))
                        {
                            loopWin = true;
                            loopWins++;
                        }
                    }

                    if (!string.Equals(was ?? "", now ?? "", StringComparison.OrdinalIgnoreCase))
                    {
                        changed.Add(parent);
                        if (was == null) beforeNone++;
                        else if (now == null) afterNone++;
                        else
                        {
                            bothDiffer++;
                            if (examples.Count < top)
                                examples.Add(string.Format("{0}: {1} → {2}", parent, was, now));
                        }
                    }

                    if (loopWin && examples.Count < top)
                    {
                        // Петля, победившая бы без явного снятия, — самая дорогая
                        // из ловушек перехода: дочерним атомом стал бы сам родитель.
                    }
                }

                Console.WriteLine("  наборы дочек (с perc not null) расходятся у {0} родителей", setsDiffer);
                Console.WriteLine("  ГЛАЗАМИ ПОТРЕБИТЕЛЯ (одна дочка с наибольшим perc):");
                Console.WriteLine("    меняют дочерний атом:        {0}", changed.Count);
                Console.WriteLine("      из них C не находил вовсе: {0}", beforeNone);
                Console.WriteLine("      из них A не находит вовсе: {0}", afterNone);
                Console.WriteLine("      обе есть, но разные:       {0}", bothDiffer);
                foreach (string e in examples) Console.WriteLine("        {0}", e);
                Console.WriteLine("  петли daughter = nucid в выборке правила: у {0} родителей,", loopParents);
                Console.WriteLine("    и у {0} из них петля ПОБЕДИЛА БЫ по perc, не сними её потребитель", loopWins);
                if (loopNames.Count > 0)
                {
                    Console.WriteLine("    например: {0}",
                                      string.Join(", ", loopNames.GetRange(0, Math.Min(6, loopNames.Count)).ToArray()));
                }

                if (loopWins > 0)
                {
                    Console.WriteLine("    ⚠ правило петли НЕ снимает и не должно: у ряда они");
                    Console.WriteLine("      значат изомерный переход. Снимает потребитель, явно.");
                }

                // Сторож текста B: меняет ли `x.perc not null` внутри min хоть что-то.
                using (SqliteCommand command = cn.CreateCommand())
                {
                    command.CommandText =
                        "select count(*) from (select d.nucid, d.daughter_nucid, d.dec_type"
                        + " from decay_chain d where d.perc is null"
                        + "  and d.l_seqno = (select min(l_seqno) from decay_chain x where x.nucid = d.nucid"
                        + "                   and x.daughter_nucid = d.daughter_nucid and x.dec_type = d.dec_type)"
                        + "  and exists (select 1 from decay_chain y where y.nucid = d.nucid"
                        + "              and y.daughter_nucid = d.daughter_nucid and y.dec_type = d.dec_type"
                        + "              and y.perc not null and y.l_seqno > d.l_seqno)"
                        + " group by 1, 2, 3)";
                    long guard = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
                    Console.WriteLine("  троек, где довесок `x.perc not null` (текст B) что-то менял бы: {0}", guard);
                    if (guard != 0)
                    {
                        Console.WriteLine("    ⚠ на этой поставке он был бы НЕ косметическим — смотреть A218");
                    }
                }
            }

            return changed;
        }

        static bool SameSet(List<Row> a, List<Row> b)
        {
            var x = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var y = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Row r in a) if (r.Daughter != null) x.Add(r.Daughter);
            foreach (Row r in b) if (r.Daughter != null) y.Add(r.Daughter);
            return x.SetEquals(y);
        }

        // ====================================================================
        // Раздел 4: что видит приложение
        // ====================================================================

        static void Application(string db, string clause, List<string> changed)
        {
            FieldInfo daughterField = typeof(CascadeAtomicData).GetField(
                "Daughter", BindingFlags.Public | BindingFlags.Instance);

            int built = 0, withOmega = 0, withK = 0, withAnnihilation = 0;
            long xrayPairs = 0, annihilationPairs = 0;
            int mismatch = 0;
            var mismatchWhere = new List<string>();

            foreach (string parent in Parents)
            {
                CascadeAtomicData atomic;
                try
                {
                    atomic = CascadeAtomicData.Of(parent);
                }
                catch (Exception error)
                {
                    Fail("CascadeAtomicData.Of(" + parent + ") бросил: " + error.Message);
                    continue;
                }

                if (atomic == null) continue;
                built++;
                if (atomic.OmegaK > 0.0) withOmega++;
                if (atomic.KIntensityPct > 0.0) withK++;
                if (atomic.AnnihilationQuanta > 0.0) withAnnihilation++;

                int gammas = atomic.GammaIntensity != null ? atomic.GammaIntensity.Count : 0;
                if (atomic.OmegaK > 0.0 && atomic.KIntensityPct > 0.0 && atomic.KLines != null)
                {
                    xrayPairs += (long)atomic.KLines.Count * gammas;
                }

                if (atomic.AnnihilationQuanta > 0.0)
                {
                    annihilationPairs += gammas;
                }

                if (daughterField != null)
                {
                    string live = (string)daughterField.GetValue(atomic);
                    string expect;
                    After.TryGetValue(parent, out expect);
                    if (!string.Equals(live ?? "", expect ?? "", StringComparison.OrdinalIgnoreCase))
                    {
                        mismatch++;
                        if (mismatchWhere.Count < top)
                            mismatchWhere.Add(parent + ": приложение «" + (live ?? "—")
                                              + "», правило «" + (expect ?? "—") + "»");
                    }
                }
            }

            Console.WriteLine("  CascadeAtomicData.Of дал данные у {0} родителей из {1}", built, Parents.Count);
            Console.WriteLine("    из них с ω_K дочернего атома:  {0}", withOmega);
            Console.WriteLine("    с K-рентгеном (I_K > 0):       {0}", withK);
            Console.WriteLine("    с аннигиляцией:                {0}", withAnnihilation);
            Console.WriteLine("  АТОМНЫХ КАСКАДНЫХ ПАР (γ × носитель):");
            Console.WriteLine("    γ × K-линия:        {0}", xrayPairs);
            Console.WriteLine("    γ × 511:            {0}", annihilationPairs);
            Console.WriteLine("    всего:              {0}", xrayPairs + annihilationPairs);
            Console.WriteLine("  родителей, у кого правило сменило дочерний атом: {0}", changed.Count);

            // Поимённо — те самые, у кого что-то поменялось: без этого «пар стало
            // на 68 больше» не назвать по имени, а значит и не проверить.
            Console.WriteLine("  из них с атомными данными (Of дал не null):");
            int named = 0;
            foreach (string parent in changed)
            {
                CascadeAtomicData atomic = CascadeAtomicData.Of(parent);
                if (atomic == null) continue;
                named++;
                string live = daughterField != null ? (string)daughterField.GetValue(atomic) : "?";
                Console.WriteLine("      {0,-10} дочь {1,-10} ω_K {2} K-линий {3} γ-линий {4}",
                                  parent, live ?? "—",
                                  atomic.OmegaK.ToString("F4", CultureInfo.InvariantCulture),
                                  atomic.KLines != null ? atomic.KLines.Count : 0,
                                  atomic.GammaIntensity != null ? atomic.GammaIntensity.Count : 0);
            }

            if (named == 0) Console.WriteLine("      (ни у одного — атомных данных у них нет вовсе)");

            if (daughterField == null)
            {
                Console.WriteLine("  ⚠ поля CascadeAtomicData.Daughter в сборке нет — сверить эмуляцию");
                Console.WriteLine("    с приложением нечем (сборка ДО правки).");
                return;
            }

            Console.WriteLine("  сверка эмуляции с приложением: расхождений {0} из {1}", mismatch, built);
            foreach (string w in mismatchWhere) Console.WriteLine("      {0}", w);
            if (mismatch != 0)
            {
                Fail("приложение выбирает дочерний атом НЕ тем правилом, которым меряет проба");
            }
        }

        // ====================================================================

        static string FindRepo()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName,
                        "BecquerelMonitor" + Path.DirectorySeparatorChar + "BecquerelMonitor.csproj")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            return null;
        }
    }
}
