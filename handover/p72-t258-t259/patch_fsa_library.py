# -*- coding: utf-8 -*-
"""П72 (T259): правка `FsaSampleLibrary.cs` — обрыв подряда по периоду (`EquilibriumMembers`),
`IsDecaying`/`TryHalfLifeSeconds`, кэш; фильтр в `CollectChain`. Скрипт хранится как запись того,
что именно вставлено; применяется один раз к основному дереву.

    python handover/p72-t258-t259/patch_fsa_library.py <путь к FsaSampleLibrary.cs>
"""
import sys

p = sys.argv[1]
raw = open(p, 'rb').read()
bom = raw.startswith(b'\xef\xbb\xbf')
crlf = b'\r\n' in raw
t = raw.decode('utf-8-sig').replace('\r\n', '\n')

old_cache = """        /// <summary>Кэш глубин членов ряда по корню (`S65`).</summary>
        static readonly Dictionary<string, Dictionary<string, int>> DepthCache =
            new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
"""
new_cache = old_cache + """
        /// <summary>Кэш подрядов равновесия по корню (`T259`).</summary>
        static readonly Dictionary<string, HashSet<string>> EquilibriumCache =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
"""
assert t.count(old_cache) == 1
t = t.replace(old_cache, new_cache)

old_collect = """            Dictionary<string, double> members = ChainBranches(chain.Root, report);
            foreach (KeyValuePair<string, double> member in members)
            {
                if (chain.Only.Count > 0 && !chain.Only.Contains(member.Key))
                {
                    continue;
                }
"""
new_collect = """            Dictionary<string, double> members = ChainBranches(chain.Root, report);
            // (`T259`) Ряд — ОТ КОРНЯ ВНИЗ, ПОКА РАВНОВЕСИЕ ВОЗМОЖНО: член с
            // периодом длиннее корня (и всё под ним) в подряд не входит. У голов
            // рядов такого члена нет, и множество равно всему обходу; у «Rn-222»
            // оно обрывается на Pb-210. Что выброшено — в отчёт, не молча.
            HashSet<string> reachable = EquilibriumMembers(chain.Root, report);
            var cut = new List<string>();
            foreach (KeyValuePair<string, double> member in members)
            {
                if (chain.Only.Count > 0 && !chain.Only.Contains(member.Key))
                {
                    continue;
                }

                if (!reachable.Contains(member.Key))
                {
                    cut.Add(member.Key);
                    continue;
                }
"""
assert t.count(old_collect) == 1
t = t.replace(old_collect, new_collect)

old_tail = """                Remember(branch, owner, member.Key, member.Value, chain.Root);
            }
        }

        /// <summary>
        /// {nucid → накопленная доля ветвления от корня}, только основные
"""
new_tail = """                Remember(branch, owner, member.Key, member.Value, chain.Root);
            }

            if (cut.Count > 0)
            {
                cut.Sort(StringComparer.OrdinalIgnoreCase);
                report.Notes.Add("ряд от " + chain.Root + ": вне равновесия с корнем (период длиннее его) — "
                                 + string.Join(", ", cut) + "; в подряд не взяты (T259)");
            }
        }

        /// <summary>
        /// Распадается ли нуклид по базе: есть строка `nuclides` с числовым
        /// `half_life_sec`. Стабильный (`STABLE`, период пуст) и неизвестный базе
        /// — <c>false</c>. Нужен <see cref="FsaSampleChain.FromLabel"/>: корень
        /// подряда обязан распадаться, иначе «ряд» — пустое множество линий.
        /// </summary>
        public static bool IsDecaying(string nucid)
        {
            if (string.IsNullOrEmpty(nucid))
            {
                return false;
            }

            double seconds;
            return TryHalfLifeSeconds(nucid, out seconds) && seconds > 0.0;
        }

        /// <summary>
        /// Период полураспада в секундах из `nuclides.half_life_sec` — по
        /// самому нижнему уровню `l_seqno` (имя в таблице не уникально:
        /// `144TBm` — три строки). <c>false</c> — нуклида нет или он стабилен.
        /// </summary>
        static bool TryHalfLifeSeconds(string nucid, out double seconds)
        {
            seconds = 0.0;
            using (SqliteConnection connection = OpenRead(NuclideDatabasePath()))
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    "select half_life_sec from nuclides where nucid = $n"
                    + " and half_life_sec is not null order by l_seqno limit 1";
                command.Parameters.AddWithValue("$n", nucid);
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    return reader.Read() && TryNumber(reader, 0, out seconds);
                }
            }
        }

        /// <summary>
        /// Члены ряда, С КОТОРЫМИ КОРЕНЬ МОЖЕТ БЫТЬ В РАВНОВЕСИИ (`T259`): обход
        /// `decay_chain` от корня вниз тем же ребром, что у
        /// <see cref="ChainBranches"/>, но дочерний открывается, только если его
        /// период КОРОЧЕ периода корня; член длиннее корня не входит сам и
        /// закрывает всё, что под ним (оно питается только через него).
        /// Стабильные концы (период пуст) пропускаются как есть — у них нет ни
        /// распада, ни линий, и <see cref="ChainBranches"/> их держит.
        ///
        /// Физика: переходное равновесие возможно лишь при T½(дочь) короче
        /// T½(родитель); Pb-210 (22.2 г) за часы съёмки радона (3.82 сут) не
        /// нарастает вовсе (A/A₀ ≈ λ·t ~ 10⁻⁵), и предъявлять его 46.5 кэВ одной
        /// амплитудой с Pb-214/Bi-214 значит навязать пробе то, чего в ней нет
        /// (П64 §6: со связкой на угле Ra-226 2 %, Pb-210 1.5 % — оба ложные).
        /// У голов рядов и Th-228 члена длиннее корня нет, множество равно всему
        /// обходу, и состав от правила не меняется — измерено побитово.
        /// </summary>
        internal static HashSet<string> EquilibriumMembers(string root, Report report)
        {
            lock (Gate)
            {
                HashSet<string> cached;
                if (EquilibriumCache.TryGetValue(root, out cached))
                {
                    return cached;
                }
            }

            var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
            try
            {
                double rootSeconds;
                if (!TryHalfLifeSeconds(root, out rootSeconds))
                {
                    // Периода у корня нет — обрывать не по чему: берётся весь обход,
                    // как до `T259`, и об этом сказано.
                    report.Notes.Add("ряд от " + root + ": периода корня в nuclides нет — подряд не обрывается");
                    foreach (string member in ChainBranches(root, report).Keys)
                    {
                        reachable.Add(member);
                    }
                }
                else
                {
                    var order = new List<string> { root };
                    using (SqliteConnection connection = OpenRead(NuclideDatabasePath()))
                    using (SqliteCommand edges = connection.CreateCommand())
                    using (SqliteCommand life = connection.CreateCommand())
                    {
                        edges.CommandText =
                            "select daughter_nucid, perc from decay_chain d"
                            + " where nucid = $n and perc not null"
                            + DecayParentRule.ChainLevelClause;
                        edges.Parameters.AddWithValue("$n", root);
                        life.CommandText =
                            "select half_life_sec from nuclides where nucid = $n"
                            + " and half_life_sec is not null order by l_seqno limit 1";
                        life.Parameters.AddWithValue("$n", root);
                        for (int i = 0; i < order.Count && order.Count <= MaxChainNodes; i++)
                        {
                            string current = order[i];
                            edges.Parameters["$n"].Value = current;
                            var daughters = new List<string>();
                            using (SqliteDataReader reader = edges.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    string daughter = reader.IsDBNull(0) ? null : reader.GetString(0);
                                    double percent;
                                    if (string.IsNullOrEmpty(daughter)
                                        || string.Equals(daughter, current, StringComparison.OrdinalIgnoreCase)
                                        || !TryNumber(reader, 1, out percent) || !(percent > 0.0)
                                        || reachable.Contains(daughter))
                                    {
                                        continue;
                                    }

                                    daughters.Add(daughter);
                                }
                            }

                            foreach (string daughter in daughters)
                            {
                                life.Parameters["$n"].Value = daughter;
                                double seconds = 0.0;
                                bool decays;
                                using (SqliteDataReader reader = life.ExecuteReader())
                                {
                                    decays = reader.Read() && TryNumber(reader, 0, out seconds);
                                }

                                if (decays && !(seconds < rootSeconds))
                                {
                                    // Длиннее корня (или равен ему): не в равновесии,
                                    // и всё под ним закрыто.
                                    continue;
                                }

                                if (reachable.Add(daughter) && decays)
                                {
                                    order.Add(daughter);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception error)
            {
                report.Notes.Add("ряд от " + root + ": отказ базы при обходе равновесия — " + error.Message);
                foreach (string member in ChainBranches(root, report).Keys)
                {
                    reachable.Add(member);
                }
            }

            lock (Gate)
            {
                EquilibriumCache[root] = reachable;
            }

            return reachable;
        }

        /// <summary>
        /// {nucid → накопленная доля ветвления от корня}, только основные
"""
assert t.count(old_tail) == 1, t.count(old_tail)
t = t.replace(old_tail, new_tail)
out = t.replace('\n', '\r\n') if crlf else t
open(p, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + out.encode('utf-8'))
print('ok bom=%s crlf=%s' % (bom, crlf))
