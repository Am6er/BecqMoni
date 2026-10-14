using BecquerelMonitor;
using BecquerelMonitor.NucBase;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// (П296, 14.10.2026) Карточка NucBase не показывает дочерние и родительские строки с долей
    /// ровно 0 — отражением, без показа окон. Решение Amber 14.10.2026 вопросником, дословно:
    /// «Скрывать строки с долей 0 в карточке (Рекомендую)»; описание: «База не меняется; карточка
    /// NucBase не показывает дочерей с долей 0 (и эти три, и 25 из поставки)».
    ///
    /// Для КАЖДОГО нуклида базы с карточкой (`getNuclude`) читается «сырой» список (`Nuclide.Daughters` /
    /// `Parents` — данные, правилом ряда, как до правки) и ТАБЛИЦЫ карточки после `ShowIsotopeCard`
    /// (`DaughtersDataGridView`, `ParentsDataGridView`). Требуется:
    ///   * таблица = сырой список БЕЗ строк, у которых доля — число 0 (разбор — своя проба,
    ///     `InvariantCulture`: «0», «0.», «0.0», «0E0»); порядок и тексты остальных строк — побайтно;
    ///   * в таблице нет ни одной строки с долей 0;
    ///   * «?» и «LT …» (числа нет) — НЕ нули, остаются.
    /// Положительный контроль: в сырых списках нули ЕСТЬ (три оставленных расщеплением П294 — `130SN`, `52FE`,
    /// `85Y` — и строки поставки); ПРОБА НА СБОРКЕ ДО ПРАВКИ ОБЯЗАНА КРАСНЕТЬ — таблица там равна сырому списку.
    /// Окно «как у человека» — второй путь: `IsotopeTextBox` + `DoSearch` для трёх имён.
    ///
    /// Ключ: --all — печатать каждую строку с нулём. Коды: 0 — сошлось; 1 — нет; 2 — базы нет.
    /// </summary>
    static class NucBaseZeroShareProbeP296
    {
        static int bad;
        static bool printAll;
        const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");   // разбор обязан быть безразличен к культуре
            printAll = args.Contains("--all");
            string db = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
            if (!File.Exists(db))
            {
                Console.WriteLine("нет базы: " + db);
                return 2;
            }

            Console.WriteLine("база: " + db);
            Console.WriteLine("sha256 базы: " + Sha(File.ReadAllBytes(db)));
            Console.WriteLine("сборка приложения: " + Sha(File.ReadAllBytes(typeof(NucBaseFramework).Assembly.Location)).Substring(0, 16));

            List<string> ids = AllNucids(db);
            Console.WriteLine("нуклидов (nuclides, родители и дочери decay_chain): " + ids.Count.ToString(CultureInfo.InvariantCulture));

            var cards = new Dictionary<string, Card>(StringComparer.Ordinal);
            using (var form = new NucBase.NucBase())
            {
                Type t = typeof(NucBase.NucBase);
                MethodInfo show = t.GetMethod("ShowIsotopeCard", Any);
                var dgrid = (DataGridView)t.GetField("DaughtersDataGridView", Any).GetValue(form);
                var pgrid = (DataGridView)t.GetField("ParentsDataGridView", Any).GetValue(form);

                foreach (string id in ids)
                {
                    Nuclide nuc = new NucBaseFramework().getNuclude(id);
                    if (nuc == null) continue;
                    var c = new Card();
                    c.RawD = nuc.Daughters.Select(Row).ToList();
                    c.RawP = nuc.Parents.Select(Row).ToList();
                    show.Invoke(form, new object[] { id, nuc });
                    c.ShownD = Rows(dgrid);
                    c.ShownP = Rows(pgrid);
                    cards[id] = c;
                }

                Console.WriteLine("карточек прочитано: " + cards.Count.ToString(CultureInfo.InvariantCulture));
                Whole(cards);
                Zeros(db, cards);
                Named(cards);
                Untouched(cards);
                Emptied(cards);
                Window(form, t);
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "СОШЛОСЬ" : "НЕ СОШЛОСЬ: " + bad.ToString(CultureInfo.InvariantCulture));
            return bad == 0 ? 0 : 1;
        }

        sealed class Card
        {
            public List<string> RawD, RawP, ShownD, ShownP;
        }

        static void Check(string what, bool ok, string detail)
        {
            if (!ok) bad++;
            Console.WriteLine("  {0} {1}: {2}", ok ? "ок" : "!!", what, detail);
        }

        static string Row(Decay d)
        {
            return d.NucName + "|" + d.DecayTypeString + "|" + d.DecayPercent;
        }

        static List<string> Rows(DataGridView grid)
        {
            var list = new List<string>();
            foreach (DataGridViewRow r in grid.Rows)
            {
                if (!r.IsNewRow)
                {
                    list.Add(r.Cells[0].Value + "|" + r.Cells[1].Value + "|" + r.Cells[2].Value);
                }
            }

            return list;
        }

        /// <summary>Доля строки — число, равное нулю (своя проба, не код приложения).</summary>
        static bool IsZero(string row)
        {
            string percent = row.Substring(row.LastIndexOf('|') + 1);
            double v;
            return double.TryParse(percent, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v == 0.0;
        }

        static string Sha(byte[] data)
        {
            using (var h = SHA256.Create())
            {
                return string.Concat(h.ComputeHash(data).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
            }
        }

        static List<string> AllNucids(string db)
        {
            var list = new List<string>();
            var b = new SqliteConnectionStringBuilder { DataSource = db, Mode = SqliteOpenMode.ReadOnly };
            using (var c = new SqliteConnection(b.ToString()))
            {
                c.Open();
                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = "select nucid from nuclides group by nucid union select nucid from decay_chain "
                                      + "union select daughter_nucid from decay_chain where daughter_nucid is not null";
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read()) list.Add(r.GetString(0));
                    }
                }
            }

            list.Sort(StringComparer.Ordinal);
            return list;
        }

        // ---------------------------------------------------------------- все карточки
        static void Whole(Dictionary<string, Card> cards)
        {
            Console.WriteLine("--- 1. все карточки: таблица = сырой список без строк с долей 0");
            var sbRaw = new StringBuilder();
            var sbWant = new StringBuilder();
            var sbShown = new StringBuilder();
            int cardsWithZero = 0, zeroRowsRaw = 0, zeroRowsShown = 0, mismatched = 0, zerosD = 0, zerosP = 0;
            var examples = new List<string>();
            foreach (string id in cards.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                Card c = cards[id];
                List<string> wantD = c.RawD.Where(r => !IsZero(r)).ToList();
                List<string> wantP = c.RawP.Where(r => !IsZero(r)).ToList();
                int zd = c.RawD.Count - wantD.Count, zp = c.RawP.Count - wantP.Count;
                zerosD += zd;
                zerosP += zp;
                if (zd + zp > 0) cardsWithZero++;
                zeroRowsRaw += zd + zp;
                zeroRowsShown += c.ShownD.Count(IsZero) + c.ShownP.Count(IsZero);
                bool same = c.ShownD.SequenceEqual(wantD, StringComparer.Ordinal) && c.ShownP.SequenceEqual(wantP, StringComparer.Ordinal);
                if (!same)
                {
                    mismatched++;
                    if (examples.Count < 6) examples.Add(id);
                }

                sbRaw.Append(id).Append("\nD:").Append(string.Join(";", c.RawD)).Append("\nP:").Append(string.Join(";", c.RawP)).Append('\n');
                sbWant.Append(id).Append("\nD:").Append(string.Join(";", wantD)).Append("\nP:").Append(string.Join(";", wantP)).Append('\n');
                sbShown.Append(id).Append("\nD:").Append(string.Join(";", c.ShownD)).Append("\nP:").Append(string.Join(";", c.ShownP)).Append('\n');
            }

            string dRaw = Sha(Encoding.UTF8.GetBytes(sbRaw.ToString())).Substring(0, 16);
            string dWant = Sha(Encoding.UTF8.GetBytes(sbWant.ToString())).Substring(0, 16);
            string dShown = Sha(Encoding.UTF8.GetBytes(sbShown.ToString())).Substring(0, 16);
            Console.WriteLine("    сырых строк с долей 0: дочерних {0}, родительских {1}, карточек с нулём {2}",
                              zerosD, zerosP, cardsWithZero);
            Console.WriteLine("    свёртка: сырой список {0}, ожидаемая (без нулей) {1}, таблицы карточки {2}", dRaw, dWant, dShown);
            Check("контроль: в сырых списках нули ЕСТЬ (иначе проба нуля не увидела бы)", zeroRowsRaw > 0,
                  zeroRowsRaw.ToString(CultureInfo.InvariantCulture) + " строк");
            Check("в таблицах карточки строк с долей 0 нет", zeroRowsShown == 0,
                  zeroRowsShown.ToString(CultureInfo.InvariantCulture) + " строк");
            Check("таблицы = сырой список без нулей, побайтно и в том же порядке (все карточки)", mismatched == 0,
                  mismatched.ToString(CultureInfo.InvariantCulture) + " расхождений" + (examples.Count > 0 ? " (напр. " + string.Join(", ", examples) + ")" : ""));
            Check("свёртка таблиц = свёртка ожидаемого", dShown == dWant, dShown + " против " + dWant);
        }

        // ---------------------------------------------------------------- строки поставки и оставленные
        static void Zeros(string db, Dictionary<string, Card> cards)
        {
            Console.WriteLine("--- 2. нулевые строки decay_chain (поставка + три оставленных расщеплением) и их судьба в карточке");
            var rows = new List<string[]>();
            var b = new SqliteConnectionStringBuilder { DataSource = db, Mode = SqliteOpenMode.ReadOnly };
            using (var c = new SqliteConnection(b.ToString()))
            {
                c.Open();
                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = "select nucid, l_seqno, daughter_nucid, perc from decay_chain where perc is not null";
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            string perc = Convert.ToString(r.GetValue(3), CultureInfo.InvariantCulture);
                            double v;
                            if (double.TryParse(perc, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v == 0.0)
                            {
                                rows.Add(new[] { r.GetString(0), Convert.ToString(r.GetValue(1), CultureInfo.InvariantCulture), r.GetString(2), perc });
                            }
                        }
                    }
                }
            }

            int onCard = 0, notOnCard = 0, stillShown = 0;
            foreach (string[] z in rows.OrderBy(x => x[0], StringComparer.Ordinal))
            {
                Card card;
                bool had = false, now = false;
                if (cards.TryGetValue(z[0], out card))
                {
                    had = card.RawD.Any(r => r.StartsWith(z[2] + "|", StringComparison.Ordinal) && IsZero(r));
                    now = card.ShownD.Any(r => r.StartsWith(z[2] + "|", StringComparison.Ordinal) && IsZero(r));
                }

                if (had) onCard++; else notOnCard++;
                if (now) stillShown++;
                if (printAll || had)
                {
                    Console.WriteLine("    {0,-8} ур.{1,-2} → {2,-8} perc «{3}»: в списке данных {4}, в таблице карточки {5}",
                                      z[0], z[1], z[2], z[3], had ? "ЕСТЬ" : "нет (отбор правила ряда)", now ? "ЕСТЬ" : "нет");
                }
            }

            Console.WriteLine("    нулевых строк decay_chain: {0}; из них попадают в карточку по правилу ряда: {1}, не попадают: {2}",
                              rows.Count, onCard, notOnCard);
            Check("контроль: среди них есть те, что карточка показывала бы (иначе проверять нечего)", onCard > 0,
                  onCard.ToString(CultureInfo.InvariantCulture));
            Check("ни одна нулевая строка decay_chain в таблице карточки не осталась", stillShown == 0,
                  stillShown.ToString(CultureInfo.InvariantCulture));
        }

        // ---------------------------------------------------------------- именные случаи
        static void Named(Dictionary<string, Card> cards)
        {
            Console.WriteLine("--- 3. именные случаи: три оставленных нуля П294 и Sn-126");
            foreach (string[] e in new[]
            {
                new[] { "130SN", "130SB", "130SBm" },
                new[] { "52FE", "52MN", "52MNm" },
                new[] { "85Y", "85SR", "85SRm1" }
            })
            {
                Card c;
                if (!cards.TryGetValue(e[0], out c))
                {
                    Check(e[0] + ": карточка", false, "не прочитана");
                    continue;
                }

                string zero = c.RawD.FirstOrDefault(r => r.StartsWith(e[1] + "|", StringComparison.Ordinal) && IsZero(r));
                Check(e[0] + ": контроль — в списке данных «" + e[1] + " 0» ЕСТЬ", zero != null, zero ?? "нет");
                Check(e[0] + ": в таблице «" + e[1] + " 0» нет", !c.ShownD.Any(r => r.StartsWith(e[1] + "|", StringComparison.Ordinal)),
                      string.Join(", ", c.ShownD));
                Check(e[0] + ": строка изомера «" + e[2] + " 100» осталась",
                      c.ShownD.Any(r => r.StartsWith(e[2] + "|", StringComparison.Ordinal) && r.EndsWith("|100", StringComparison.Ordinal)),
                      string.Join(", ", c.ShownD));
            }

            // Родительская сторона: те же три ребра глазами дочери.
            foreach (string[] e in new[] { new[] { "130SB", "130SN" }, new[] { "52MN", "52FE" }, new[] { "85SR", "85Y" } })
            {
                Card c;
                if (!cards.TryGetValue(e[0], out c)) { Check(e[0] + ": карточка", false, "не прочитана"); continue; }
                string zero = c.RawP.FirstOrDefault(r => r.StartsWith(e[1] + "|", StringComparison.Ordinal) && IsZero(r));
                Console.WriteLine("    {0}: родители в данных: {1}; в таблице: {2}", e[0], string.Join(", ", c.RawP), string.Join(", ", c.ShownP));
                Check(e[0] + ": родитель «" + e[1] + " 0» — в таблице нет (то же ребро с другой стороны)",
                      zero == null || !c.ShownP.Any(r => r.StartsWith(e[1] + "|", StringComparison.Ordinal) && IsZero(r)), zero ?? "в данных нуля нет");
            }

            Card sn;
            if (cards.TryGetValue("126SN", out sn))
            {
                Console.WriteLine("    126SN в таблице: " + string.Join(", ", sn.ShownD));
                Check("Sn-126: «126SBm1 100» как после П294",
                      sn.ShownD.Any(r => r.StartsWith("126SBm1|", StringComparison.Ordinal) && r.EndsWith("|100", StringComparison.Ordinal)),
                      string.Join(", ", sn.ShownD));
                Check("Sn-126: строки «126SB …» нет", !sn.ShownD.Any(r => r.StartsWith("126SB|", StringComparison.Ordinal)),
                      string.Join(", ", sn.ShownD));
            }
            else
            {
                Check("Sn-126: карточка", false, "не прочитана");
            }

            // Числа нет — не нуль: «?» и пределы остаются.
            int unknownRaw = 0, unknownShown = 0;
            foreach (Card c in cards.Values)
            {
                unknownRaw += c.RawD.Count(r => r.EndsWith("|?", StringComparison.Ordinal)) + c.RawP.Count(r => r.EndsWith("|?", StringComparison.Ordinal));
                unknownShown += c.ShownD.Count(r => r.EndsWith("|?", StringComparison.Ordinal)) + c.ShownP.Count(r => r.EndsWith("|?", StringComparison.Ordinal));
            }

            Check("строки с долей «?» (числа нет) — все остались", unknownRaw == unknownShown,
                  unknownRaw.ToString(CultureInfo.InvariantCulture) + " в данных, " + unknownShown.ToString(CultureInfo.InvariantCulture) + " в таблицах");
        }

        // ---------------------------------------------------------------- десять без нулей
        static void Untouched(Dictionary<string, Card> cards)
        {
            Console.WriteLine("--- 4. десять случайных карточек без нулей — таблицы побайтно как данные (зерно 296)");
            List<string> clean = cards.Where(p => (p.Value.RawD.Count + p.Value.RawP.Count) > 0
                                                  && !p.Value.RawD.Any(IsZero) && !p.Value.RawP.Any(IsZero))
                                      .Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
            var rng = new Random(296);
            var pick = new List<string>();
            while (pick.Count < 10 && clean.Count > 0)
            {
                string id = clean[rng.Next(clean.Count)];
                clean.Remove(id);
                pick.Add(id);
            }

            foreach (string id in pick)
            {
                Card c = cards[id];
                bool same = c.ShownD.SequenceEqual(c.RawD, StringComparer.Ordinal) && c.ShownP.SequenceEqual(c.RawP, StringComparer.Ordinal);
                Check(id, same, "дочерние: " + string.Join(", ", c.ShownD) + "; родители: " + string.Join(", ", c.ShownP));
            }
        }

        // ---------------------------------------------------------------- у кого таблица опустела
        static void Emptied(Dictionary<string, Card> cards)
        {
            Console.WriteLine("--- 5а. карточки, у которых после скрытия опустела таблица (в данных строки были) — следствие решения, не отказ");
            int emptyD = 0, emptyP = 0;
            foreach (string id in cards.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                Card c = cards[id];
                bool d = c.RawD.Count > 0 && c.ShownD.Count == 0;
                bool p = c.RawP.Count > 0 && c.ShownP.Count == 0;
                if (d) emptyD++;
                if (p) emptyP++;
                if (d || p)
                {
                    Console.WriteLine("    {0,-8} {1}{2}{3}", id,
                                      d ? "дочерние пусты (в данных: " + string.Join(", ", c.RawD) + ") " : "",
                                      p ? "родители пусты (в данных: " + string.Join(", ", c.RawP) + ")" : "", "");
                }
            }

            Console.WriteLine("    пустая таблица дочерних у {0} карточек, родителей — у {1}", emptyD, emptyP);
        }

        // ---------------------------------------------------------------- окно: IsotopeTextBox + DoSearch
        static void Window(NucBase.NucBase form, Type t)
        {
            Console.WriteLine("--- 5. окно как у человека: IsotopeTextBox + DoSearch (форма не показывается)");
            var box = (TextBox)t.GetField("IsotopeTextBox", Any).GetValue(form);
            var dgrid = (DataGridView)t.GetField("DaughtersDataGridView", Any).GetValue(form);
            var pgrid = (DataGridView)t.GetField("ParentsDataGridView", Any).GetValue(form);
            MethodInfo search = t.GetMethod("DoSearch", Any);
            foreach (string q in new[] { "Sn-130", "Fe-52", "Y-85", "Sn-126", "Bi-212" })
            {
                box.Text = q;
                search.Invoke(form, null);
                List<string> d = Rows(dgrid), p = Rows(pgrid);
                Console.WriteLine("    «{0}»: дочерние: {1}; родители: {2}", q, string.Join(", ", d), string.Join(", ", p));
                Check("«" + q + "»: в окне строк с долей 0 нет", !d.Any(IsZero) && !p.Any(IsZero),
                      "нулей " + (d.Count(IsZero) + p.Count(IsZero)).ToString(CultureInfo.InvariantCulture) + " из "
                      + d.Count.ToString(CultureInfo.InvariantCulture) + " + " + p.Count.ToString(CultureInfo.InvariantCulture) + " строк");
            }
        }
    }
}
