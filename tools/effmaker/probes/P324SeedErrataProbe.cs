using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml.Serialization;

namespace P324SeedErrataProbe
{
    /// <summary>
    /// П324 (15.10.2026): ТРИ ОПЕЧАТКИ ТАБЛИЦЫ ЛСРМ в засеве веществ конструктора
    /// геометрий и их ПЕРЕНОС на библиотеку пользователя (поколение 11) — ДО/ПОСЛЕ.
    ///
    ///     p324seederrataprobe [--lib=<копия GeometryMaterials.xml> [--dump=<файл.csv>]]
    ///
    /// Решение Amber 15.10.2026 вопросником, дословно: «Патч целиком (Рекомендую)».
    /// Три записи: «Aniline» (доля C 0.775838 -> 0.773838), «Skin (icrp)» (доля N
    /// 0.04602 -> 0.04642), «Krypton, liquid» (плотность 0.002418 -> 2.418).
    ///
    /// Разделы:
    ///
    /// 1. ЗАСЕВ: <see cref="GeometryMaterialLibrary.Seed"/> и строки
    ///    <see cref="GeometryMaterialSeed.Rows"/> несут нынешние значения; доли
    ///    сходятся к единице; жидкий криптон тяжелее газообразного.
    /// 2. ПЕРЕНОС: файл библиотеки = ВЕСЬ прежний засев (три записи с прежними
    ///    числами) в поколениях 8, 9, 10; после загрузки он обязан совпасть с
    ///    нынешним засевом ВСЕМИ записями побитово — то есть изменились ровно три.
    /// 3. КОНТРОЛЬ: запись, изменённая человеком хоть в одной цифре (плотность,
    ///    сокращение, доля, свой криптон), НЕ тронута, а её соседки по файлу
    ///    перенесены — иначе «не тронута» мерила бы пустоту.
    /// 4. БЕЗ ЗАПИСЕЙ: три записи удалены человеком (<c>Removed</c>) — не
    ///    возвращаются; свои записи с теми же числами под другим именем — целы.
    /// 5. РАЗ: загрузка не пишет файл; повторная загрузка ничего не меняет; после
    ///    Save файл поколения 11, повторное сохранение побайтно то же; файл
    ///    поколения 11 с прежними числами (человек набрал их сам) не переносится.
    /// 6. СЛОТЫ готовых геометрий (<see cref="GeometryMaterialStore.MigrateGeometry"/>):
    ///    прежние доли побитово -> новые; чужая доля -> не тронуто.
    /// 7. КОПИЯ ЖИВОГО КОНФИГА (<c>--lib=</c>): файл копируется в файл склада,
    ///    грузится; пять опечаток (две прежние + три новые) сверяются с сырым
    ///    файлом: перенесена ровно та запись, что побитово прежняя. <c>--dump=</c>
    ///    пишет ВСЕ загруженные записи («R»), и дампы ДО и ПОСЛЕ сличаются построчно.
    ///
    /// Проба пишет в файл склада (<c>config\GeometryMaterials.xml</c> рядом с exe) и
    /// ВОЗВРАЩАЕТ его, как был; файл вне каталога пробы не трогает. Поток живёт
    /// под ru-RU нарочно: всё, что разбирает приложение, обязано идти инвариантной
    /// культурой (правило Amber 05.09.2026).
    ///
    /// Код возврата: 0 — дефектов нет; 1 — есть дефект (перечислен; ДО правки так и
    /// должно быть); 2 — файл склада вне каталога пробы или нечем меряться.
    /// </summary>
    static class Program
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly List<string> Defects = new List<string>();
        static string path;

        sealed class Erratum
        {
            public string Name;
            public string OldFractions;
            public double OldDensity;
            public string NewFractions;
            public double NewDensity;
        }

        // Прежнее и нынешнее — ЧИСЛАМИ, не из засева: проба обязана мерить оба
        // состояния одним кодом, а засев ДО правки несёт прежние.
        static readonly Erratum[] Three =
        {
            new Erratum
            {
                Name = "Aniline",
                OldFractions = "1:0.075759 6:0.775838 7:0.150403", OldDensity = 1.024,
                NewFractions = "1:0.075759 6:0.773838 7:0.150403", NewDensity = 1.024,
            },
            new Erratum
            {
                Name = "Skin (icrp)",
                OldFractions = "1:0.100588 6:0.22825 7:0.04602 8:0.619002 11:7e-05 12:6e-05 15:0.00033 16:0.00159 17:0.00267 19:0.00085 20:0.00015 26:1e-05 30:1e-05",
                OldDensity = 1.1,
                NewFractions = "1:0.100588 6:0.22825 7:0.04642 8:0.619002 11:7e-05 12:6e-05 15:0.00033 16:0.00159 17:0.00267 19:0.00085 20:0.00015 26:1e-05 30:1e-05",
                NewDensity = 1.1,
            },
            new Erratum
            {
                Name = "Krypton, liquid",
                OldFractions = "36:1", OldDensity = 0.002418,
                NewFractions = "36:1", NewDensity = 2.418,
            },
        };

        // Две опечатки поколения 9 (AMBER107): нынешнее берётся из самого засева.
        static readonly Erratum[] Earlier =
        {
            new Erratum { Name = "Freon-13ii", OldFractions = "6:0.061309 9:0.290924 55:0.647767" },
            new Erratum { Name = "Lanthanum oxysulfide", OldFractions = "8:0.049097 16:0.098383 57:0.85252" },
        };

        static string R(double x)
        {
            return x.ToString("R", Inv);
        }

        static void Say(string text)
        {
            Console.WriteLine(text);
        }

        static void Check(bool ok, string what, string detail)
        {
            Say(string.Format(Inv, "   {0,-6} {1}{2}", ok ? "верно" : "ДЕФЕКТ", what,
                              string.IsNullOrEmpty(detail) ? "" : "  — " + detail));
            if (!ok)
            {
                Defects.Add(what);
            }
        }

        static GeometryMaterialLibrary.Entry Find(List<GeometryMaterialLibrary.Entry> list, string name)
        {
            foreach (GeometryMaterialLibrary.Entry e in list)
            {
                if (string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return e;
                }
            }

            return null;
        }

        static void Set(GeometryMaterialLibrary.Entry e, string fractions, double density)
        {
            e.ElementFractions.Clear();
            foreach (KeyValuePair<int, double> pair in GeometryMaterialSeed.Fractions(fractions))
            {
                e.ElementFractions[pair.Key] = pair.Value;
            }

            e.Density = density;
        }

        /// <summary>Засев приложения, глубоко скопированный; три записи переведены в прежнее или нынешнее состояние.</summary>
        static List<GeometryMaterialLibrary.Entry> SeedWith(bool previous)
        {
            List<GeometryMaterialLibrary.Entry> list = new List<GeometryMaterialLibrary.Entry>();
            foreach (GeometryMaterialLibrary.Entry e in GeometryMaterialLibrary.Seed())
            {
                list.Add(e.Clone());
            }

            foreach (Erratum x in Three)
            {
                Set(Find(list, x.Name), previous ? x.OldFractions : x.NewFractions,
                    previous ? x.OldDensity : x.NewDensity);
            }

            return list;
        }

        static bool Same(GeometryMaterialLibrary.Entry a, GeometryMaterialLibrary.Entry b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            if (!string.Equals(a.Name ?? "", b.Name ?? "", StringComparison.Ordinal)
                || !string.Equals(a.Abbr ?? "", b.Abbr ?? "", StringComparison.Ordinal)
                || !string.Equals(a.Formula ?? "", b.Formula ?? "", StringComparison.Ordinal)
                || a.Density != b.Density
                || a.Kind != b.Kind
                || a.ElementFractions.Count != b.ElementFractions.Count
                || a.Components.Count != b.Components.Count)
            {
                return false;
            }

            foreach (KeyValuePair<int, double> pair in a.ElementFractions)
            {
                double other;
                if (!b.ElementFractions.TryGetValue(pair.Key, out other) || other != pair.Value)
                {
                    return false;
                }
            }

            for (int i = 0; i < a.Components.Count; i++)
            {
                if (!string.Equals(a.Components[i].Material ?? "", b.Components[i].Material ?? "", StringComparison.Ordinal)
                    || a.Components[i].Weight != b.Components[i].Weight)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Имена, по которым два списка различаются (есть только в одном, или поля не равны).</summary>
        static List<string> Differences(List<GeometryMaterialLibrary.Entry> got, List<GeometryMaterialLibrary.Entry> want)
        {
            List<string> names = new List<string>();
            foreach (GeometryMaterialLibrary.Entry w in want)
            {
                if (!Same(Find(got, w.Name), w))
                {
                    names.Add(w.Name);
                }
            }

            foreach (GeometryMaterialLibrary.Entry g in got)
            {
                if (Find(want, g.Name) == null)
                {
                    names.Add("+" + g.Name);
                }
            }

            return names;
        }

        static GeometryMaterialRecord[] Records(List<GeometryMaterialLibrary.Entry> list)
        {
            List<GeometryMaterialRecord> records = new List<GeometryMaterialRecord>();
            foreach (GeometryMaterialLibrary.Entry e in list)
            {
                GeometryMaterialRecord r = new GeometryMaterialRecord
                {
                    Name = e.Name ?? "",
                    Abbr = e.Abbr ?? "",
                    Formula = e.Formula ?? "",
                    Density = e.Density,
                    Kind = e.Kind,
                };
                if (e.Components.Count > 0)
                {
                    r.Components = e.Components.ToArray();
                }

                if (e.ElementFractions.Count > 0)
                {
                    List<GeometryElementFraction> f = new List<GeometryElementFraction>();
                    foreach (KeyValuePair<int, double> pair in e.ElementFractions)
                    {
                        f.Add(new GeometryElementFraction { Z = pair.Key, Fraction = pair.Value });
                    }

                    f.Sort((a, b) => a.Z.CompareTo(b.Z));
                    r.Fractions = f.ToArray();
                }

                records.Add(r);
            }

            return records.ToArray();
        }

        static void WriteFile(List<GeometryMaterialLibrary.Entry> list, int generation, params string[] removed)
        {
            GeometryMaterialConfig config = new GeometryMaterialConfig
            {
                SeedVersion = generation,
                Materials = Records(list),
                Removed = removed,
            };
            XmlSerializer serializer = new XmlSerializer(typeof(GeometryMaterialConfig));
            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                serializer.Serialize(stream, config);
            }
        }

        static List<GeometryMaterialLibrary.Entry> Load()
        {
            GeometryMaterialStore.Reload();
            return new List<GeometryMaterialLibrary.Entry>(GeometryMaterialStore.Entries);
        }

        static string Hash()
        {
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
            }
        }

        static int FileGeneration()
        {
            XmlSerializer serializer = new XmlSerializer(typeof(GeometryMaterialConfig));
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                return ((GeometryMaterialConfig)serializer.Deserialize(stream)).SeedVersion;
            }
        }

        static string Show(GeometryMaterialLibrary.Entry e)
        {
            if (e == null)
            {
                return "(нет)";
            }

            StringBuilder sb = new StringBuilder("ρ=" + R(e.Density));
            List<int> zs = new List<int>(e.ElementFractions.Keys);
            zs.Sort();
            foreach (int z in zs)
            {
                if (z == 6 && e.Name == "Aniline" || z == 7 && e.Name == "Skin (icrp)" || z == 36)
                {
                    sb.Append(" Z" + z.ToString(Inv) + "=" + R(e.ElementFractions[z]));
                }
            }

            return sb.ToString();
        }

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");
            string lib = null;
            string dump = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--lib=", StringComparison.Ordinal))
                {
                    lib = a.Substring(6).Trim().Trim('"');
                }
                else if (a.StartsWith("--dump=", StringComparison.Ordinal))
                {
                    dump = a.Substring(7).Trim().Trim('"');
                }
                else
                {
                    Console.Error.WriteLine("не знаю ключа: " + a);
                    return 2;
                }
            }

            path = GeometryMaterialStore.FilePath;
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            Say("поколение засева в коде (CurrentSeedVersion): " + GeometryMaterialStore.CurrentSeedVersion.ToString(Inv));
            Say("файл склада: " + path);
            if (!Path.GetFullPath(path).StartsWith(Path.GetFullPath(appDir), StringComparison.OrdinalIgnoreCase))
            {
                Say("файл склада НЕ в каталоге пробы — писать туда нельзя, проба отказывается");
                return 2;
            }

            bool hadFile = File.Exists(path);
            byte[] backup = hadFile ? File.ReadAllBytes(path) : null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (lib != null)
                {
                    LiveCopy(lib, dump);
                }
                else
                {
                    Seed();
                    Migration();
                    Controls();
                    Absent();
                    Once();
                    Slots();
                }
            }
            finally
            {
                if (hadFile)
                {
                    File.WriteAllBytes(path, backup);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }

                GeometryMaterialStore.Reload();
            }

            Say("");
            if (Defects.Count == 0)
            {
                Say("ИТОГ: дефектов нет");
                return 0;
            }

            Say("ИТОГ: дефектов " + Defects.Count.ToString(Inv) + ":");
            foreach (string d in Defects)
            {
                Say("   - " + d);
            }

            return 1;
        }

        // 1 ------------------------------------------------------------------
        static void Seed()
        {
            Say("");
            Say("== 1. ЗАСЕВ ==");
            List<GeometryMaterialLibrary.Entry> seed = GeometryMaterialLibrary.Seed();
            List<GeometryMaterialLibrary.Entry> want = SeedWith(false);
            foreach (Erratum x in Three)
            {
                GeometryMaterialLibrary.Entry got = Find(seed, x.Name);
                Check(Same(got, Find(want, x.Name)), "засев «" + x.Name + "» несёт нынешнее значение", Show(got));
                string[] row = null;
                foreach (string[] r in GeometryMaterialSeed.Rows)
                {
                    if (r[0] == x.Name)
                    {
                        row = r;
                    }
                }

                Check(row != null && double.Parse(row[2], NumberStyles.Float, Inv) == x.NewDensity
                      && row[3] == x.NewFractions, "строка таблицы «" + x.Name + "»",
                      row == null ? "строки нет" : "ρ " + row[2] + ", доли " + row[3]);
            }

            List<string> differs = Differences(seed, want);
            Check(differs.Count == 0, "остальные записи засева совпадают с ожидаемым (" + seed.Count.ToString(Inv) + ")",
                  differs.Count == 0 ? "" : string.Join("; ", differs));

            foreach (Erratum x in Three)
            {
                double sum = 0.0;
                foreach (double w in Find(seed, x.Name).ElementFractions.Values)
                {
                    sum += w;
                }

                Check(Math.Abs(sum - 1.0) < 5e-6, "сумма долей «" + x.Name + "» единица (до 5e-6)", "сумма " + R(sum));
            }

            GeometryMaterialLibrary.Entry gas = Find(seed, "Krypton, gaseous");
            GeometryMaterialLibrary.Entry liquid = Find(seed, "Krypton, liquid");
            Check(liquid.Density > 100.0 * gas.Density, "жидкий криптон тяжелее газообразного",
                  "жидкий " + R(liquid.Density) + ", газ " + R(gas.Density));
        }

        static string PackOf(GeometryMaterialLibrary.Entry e)
        {
            List<int> zs = new List<int>(e.ElementFractions.Keys);
            zs.Sort();
            StringBuilder sb = new StringBuilder();
            foreach (int z in zs)
            {
                if (sb.Length > 0)
                {
                    sb.Append(' ');
                }

                sb.Append(z.ToString(Inv)).Append(':').Append(R(e.ElementFractions[z]));
            }

            return sb.ToString();
        }

        // 2 ------------------------------------------------------------------
        static void Migration()
        {
            Say("");
            Say("== 2. ПЕРЕНОС: файл = весь ПРЕЖНИЙ засев, три записи с прежними числами ==");
            List<GeometryMaterialLibrary.Entry> want = SeedWith(false);
            foreach (int generation in new[] { 8, 9, 10 })
            {
                WriteFile(SeedWith(true), generation);
                List<GeometryMaterialLibrary.Entry> got = Load();
                List<string> differs = Differences(got, want);
                Check(differs.Count == 0, "поколение " + generation.ToString(Inv) + ": загружено = нынешний засев всеми "
                      + want.Count.ToString(Inv) + " записями",
                      differs.Count == 0 ? "" : "расходятся: " + string.Join("; ", differs));
                foreach (Erratum x in Three)
                {
                    Say("          " + x.Name + ": " + Show(Find(got, x.Name)));
                }
            }

            Say("   -- в одном проходе вместе с двумя опечатками поколения 9 (файл поколения 8)");
            List<GeometryMaterialLibrary.Entry> five = SeedWith(true);
            foreach (Erratum x in Earlier)
            {
                Set(Find(five, x.Name), x.OldFractions, Find(five, x.Name).Density);
            }

            WriteFile(five, 8);
            List<GeometryMaterialLibrary.Entry> loaded = Load();
            List<string> diff5 = Differences(loaded, want);
            Check(diff5.Count == 0, "пять прежних записей -> пять нынешних, остальное без изменений",
                  diff5.Count == 0 ? "" : "расходятся: " + string.Join("; ", diff5));
        }

        // 3 ------------------------------------------------------------------
        static void Controls()
        {
            Say("");
            Say("== 3. КОНТРОЛЬ: запись, изменённая человеком хоть в одной цифре, НЕ тронута ==");
            // имя записи, что меняем, и как; ждём: эта запись как записана, две соседки перенесены
            object[][] cases =
            {
                new object[] { "Aniline", "плотность 1.024 -> 1.025", (Action<GeometryMaterialLibrary.Entry>)(e => e.Density = 1.025) },
                new object[] { "Aniline", "сокращение «» -> «Ani»", (Action<GeometryMaterialLibrary.Entry>)(e => e.Abbr = "Ani") },
                new object[] { "Aniline", "доля H 0.075759 -> 0.075760", (Action<GeometryMaterialLibrary.Entry>)(e => e.ElementFractions[1] = 0.07576) },
                new object[] { "Skin (icrp)", "доля O 0.619002 -> 0.619003", (Action<GeometryMaterialLibrary.Entry>)(e => e.ElementFractions[8] = 0.619003) },
                new object[] { "Skin (icrp)", "плотность 1.1 -> 1.10001", (Action<GeometryMaterialLibrary.Entry>)(e => e.Density = 1.10001) },
                new object[] { "Krypton, liquid", "плотность 0.002418 -> 0.0024181", (Action<GeometryMaterialLibrary.Entry>)(e => e.Density = 0.0024181) },
                new object[] { "Krypton, liquid", "плотность 0.002418 -> 2.41 (своя)", (Action<GeometryMaterialLibrary.Entry>)(e => e.Density = 2.41) },
                new object[] { "Krypton, liquid", "вид Other -> Source", (Action<GeometryMaterialLibrary.Entry>)(e => e.Kind = GeometryMaterialLibrary.MaterialKind.Source) },
            };
            foreach (object[] c in cases)
            {
                string name = (string)c[0];
                List<GeometryMaterialLibrary.Entry> file = SeedWith(true);
                GeometryMaterialLibrary.Entry mine = Find(file, name);
                ((Action<GeometryMaterialLibrary.Entry>)c[2])(mine);
                GeometryMaterialLibrary.Entry asWritten = mine.Clone();
                WriteFile(file, 10);
                List<GeometryMaterialLibrary.Entry> got = Load();
                bool untouched = Same(Find(got, name), asWritten);
                bool siblings = true;
                string siblingText = "";
                List<GeometryMaterialLibrary.Entry> now = SeedWith(false);
                foreach (Erratum x in Three)
                {
                    if (x.Name == name)
                    {
                        continue;
                    }

                    bool moved = Same(Find(got, x.Name), Find(now, x.Name));
                    siblings &= moved;
                    siblingText += (siblingText.Length > 0 ? ", " : "") + x.Name + (moved ? " перенесена" : " НЕ перенесена");
                }

                Check(untouched, "«" + name + "», " + (string)c[1] + ": не тронута", Show(Find(got, name)));
                Check(siblings, "   контроль имеет силу — соседки по тому же файлу", siblingText);
            }
        }

        // 4 ------------------------------------------------------------------
        static void Absent()
        {
            Say("");
            Say("== 4. БЕЗ ЭТИХ ЗАПИСЕЙ ==");
            List<GeometryMaterialLibrary.Entry> now = SeedWith(false);

            List<GeometryMaterialLibrary.Entry> without = SeedWith(true);
            List<string> names = new List<string>();
            foreach (Erratum x in Three)
            {
                without.Remove(Find(without, x.Name));
                names.Add(x.Name);
            }

            WriteFile(without, 10, names.ToArray());
            List<GeometryMaterialLibrary.Entry> got = Load();
            bool none = true;
            foreach (Erratum x in Three)
            {
                none &= Find(got, x.Name) == null;
            }

            Check(none, "три записи удалены человеком (Removed) — не возвращаются", "записей " + got.Count.ToString(Inv));
            List<GeometryMaterialLibrary.Entry> rest = new List<GeometryMaterialLibrary.Entry>();
            foreach (GeometryMaterialLibrary.Entry e in now)
            {
                if (!names.Contains(e.Name))
                {
                    rest.Add(e);
                }
            }

            List<string> differs = Differences(got, rest);
            Check(differs.Count == 0, "остальные записи не тронуты (" + rest.Count.ToString(Inv) + ")",
                  differs.Count == 0 ? "" : string.Join("; ", differs));

            // Не удалены, а просто отсутствуют в файле: довоз из засева, и значения нынешние.
            WriteFile(without, 10);
            List<GeometryMaterialLibrary.Entry> brought = Load();
            bool fresh = true;
            foreach (Erratum x in Three)
            {
                fresh &= Same(Find(brought, x.Name), Find(now, x.Name));
            }

            Check(fresh, "нет в файле и не удалены — довозятся из засева нынешними", Show(Find(brought, "Krypton, liquid")));

            // Свои записи с теми же числами под другим именем: перенос идёт по ИМЕНИ.
            List<GeometryMaterialLibrary.Entry> own = SeedWith(true);
            GeometryMaterialLibrary.Entry myAniline = Find(own, "Aniline").Clone();
            myAniline.Name = "p324 свой анилин";
            GeometryMaterialLibrary.Entry myKrypton = Find(own, "Krypton, liquid").Clone();
            myKrypton.Name = "p324 свой криптон";
            own.Add(myAniline);
            own.Add(myKrypton);
            WriteFile(own, 10);
            List<GeometryMaterialLibrary.Entry> ownGot = Load();
            Check(Same(Find(ownGot, myAniline.Name), myAniline) && Same(Find(ownGot, myKrypton.Name), myKrypton),
                  "свои записи с прежними числами под другим именем целы", Show(Find(ownGot, myKrypton.Name)));
        }

        // 5 ------------------------------------------------------------------
        static void Once()
        {
            Say("");
            Say("== 5. РАЗ: поколение поднимается один раз ==");
            List<GeometryMaterialLibrary.Entry> now = SeedWith(false);
            WriteFile(SeedWith(true), 10);
            string h0 = Hash();
            List<GeometryMaterialLibrary.Entry> first = Load();
            Check(Hash() == h0, "загрузка файл НЕ пишет (перенос живёт в памяти)", "поколение файла " + FileGeneration().ToString(Inv));
            List<GeometryMaterialLibrary.Entry> second = Load();
            Check(Differences(second, first).Count == 0 && Differences(first, now).Count == 0,
                  "повторная загрузка даёт то же", "");

            GeometryMaterialStore.Save(GeometryMaterialStore.Entries);
            int generation = FileGeneration();
            Check(generation == 11, "после Save поколение файла = 11", "поколение " + generation.ToString(Inv));
            string h1 = Hash();
            List<GeometryMaterialLibrary.Entry> third = Load();
            Check(Differences(third, now).Count == 0, "файл поколения 11 читается тем же составом", "");
            GeometryMaterialStore.Save(GeometryMaterialStore.Entries);
            Check(Hash() == h1, "повторный Save побайтно тот же файл", "");

            // Человек набрал прежние числа УЖЕ на поколении 11: перенос не вправе их тронуть.
            WriteFile(SeedWith(true), 11);
            List<GeometryMaterialLibrary.Entry> mine = Load();
            List<GeometryMaterialLibrary.Entry> old = SeedWith(true);
            bool kept = true;
            foreach (Erratum x in Three)
            {
                kept &= Same(Find(mine, x.Name), Find(old, x.Name));
            }

            Check(kept, "файл поколения 11 с прежними числами не переносится", Show(Find(mine, "Aniline")));
        }

        // 6 ------------------------------------------------------------------
        static void Slots()
        {
            Say("");
            Say("== 6. СЛОТЫ готовых геометрий (MigrateGeometry) ==");
            List<GeometryMaterialLibrary.Entry> oldSeed = SeedWith(true);
            List<GeometryMaterialLibrary.Entry> newSeed = SeedWith(false);
            // пустая библиотека склада: слоты мерятся сами по себе
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            GeometryMaterialStore.Reload();

            GeometryModel model = new GeometryModel();
            model.Source = GeometryMaterialLibrary.Make(Find(oldSeed, "Aniline"), 0.0, n => null);
            model.Gap = GeometryMaterialLibrary.Make(Find(oldSeed, "Skin (icrp)"), 0.0, n => null);
            model.Cladding = GeometryMaterialLibrary.Make(Find(oldSeed, "Krypton, liquid"), 0.0, n => null);
            model.Reflector = GeometryMaterialLibrary.Make(Find(oldSeed, "Aniline"), 0.0, n => null);
            model.Reflector.Fractions[6] = model.Reflector.Fractions[6] * (1.0 + 1e-9);   // чужая доля
            Dictionary<int, double> reflectorBefore = new Dictionary<int, double>(model.Reflector.Fractions);
            double kryptonRho = model.Cladding.Density;
            GeometryMaterialStore.MigrateGeometry(model);

            GeometryMaterial wantAniline = GeometryMaterialLibrary.Make(Find(newSeed, "Aniline"), 0.0, n => null);
            GeometryMaterial wantSkin = GeometryMaterialLibrary.Make(Find(newSeed, "Skin (icrp)"), 0.0, n => null);
            Check(SameFractions(model.Source.Fractions, wantAniline.Fractions), "слот «Aniline» с прежними долями -> нынешние",
                  "C " + R(model.Source.Fractions[6]) + " (нынешняя " + R(wantAniline.Fractions[6]) + ")");
            Check(SameFractions(model.Gap.Fractions, wantSkin.Fractions), "слот «Skin (icrp)» с прежними долями -> нынешние",
                  "N " + R(model.Gap.Fractions[7]) + " (нынешняя " + R(wantSkin.Fractions[7]) + ")");
            Check(SameFractions(model.Reflector.Fractions, reflectorBefore), "слот «Aniline» с чужой долей (1e-9) не тронут",
                  "C " + R(model.Reflector.Fractions[6]));
            Say("   (по замыслу AMBER107) плотность слота не трогается, у «Krypton, liquid» она и есть опечатка: слот "
                + R(kryptonRho) + " -> " + R(model.Cladding.Density) + " г/см³");
            Check(model.Cladding.Density == kryptonRho, "плотность слота не тронута (замысел AMBER107, а не дефект)", "");
        }

        static bool SameFractions(Dictionary<int, double> a, Dictionary<int, double> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            foreach (KeyValuePair<int, double> pair in a)
            {
                double other;
                if (!b.TryGetValue(pair.Key, out other) || other != pair.Value)
                {
                    return false;
                }
            }

            return true;
        }

        // 7 ------------------------------------------------------------------
        static void LiveCopy(string lib, string dump)
        {
            Say("");
            Say("== 7. КОПИЯ ЖИВОГО КОНФИГА: " + lib + " ==");
            if (!File.Exists(lib))
            {
                Say("   файла нет");
                Defects.Add("файла --lib нет");
                return;
            }

            GeometryMaterialConfig raw;
            XmlSerializer serializer = new XmlSerializer(typeof(GeometryMaterialConfig));
            using (FileStream stream = new FileStream(lib, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                raw = (GeometryMaterialConfig)serializer.Deserialize(stream);
            }

            Say("   поколение файла: " + raw.SeedVersion.ToString(Inv) + ", записей "
                + (raw.Materials == null ? 0 : raw.Materials.Length).ToString(Inv)
                + ", Removed " + (raw.Removed == null ? 0 : raw.Removed.Length).ToString(Inv));
            File.Copy(lib, path, true);
            List<GeometryMaterialLibrary.Entry> got = Load();
            Say("   загружено записей: " + got.Count.ToString(Inv) + ", отказ загрузки: "
                + (GeometryMaterialStore.LoadError ?? "нет"));

            List<GeometryMaterialLibrary.Entry> rawEntries = new List<GeometryMaterialLibrary.Entry>();
            if (raw.Materials != null)
            {
                foreach (GeometryMaterialRecord r in raw.Materials)
                {
                    GeometryMaterialLibrary.Entry e = new GeometryMaterialLibrary.Entry
                    {
                        Name = r.Name ?? "",
                        Abbr = r.Abbr ?? "",
                        Formula = r.Formula ?? "",
                        Density = r.Density,
                        Kind = r.Kind,
                    };
                    if (r.Fractions != null)
                    {
                        foreach (GeometryElementFraction f in r.Fractions)
                        {
                            e.ElementFractions[f.Z] = f.Fraction;
                        }
                    }

                    if (r.Components != null)
                    {
                        e.Components.AddRange(r.Components);
                    }

                    rawEntries.Add(e);
                }
            }

            int generation = GeometryMaterialStore.CurrentSeedVersion;
            List<GeometryMaterialLibrary.Entry> previous = SeedWith(true);
            List<GeometryMaterialLibrary.Entry> current = SeedWith(false);
            foreach (Erratum x in Three)
            {
                GeometryMaterialLibrary.Entry inFile = Find(rawEntries, x.Name);
                GeometryMaterialLibrary.Entry after = Find(got, x.Name);
                if (inFile == null)
                {
                    Say("   «" + x.Name + "»: в файле нет; после загрузки " + (after == null ? "нет" : Show(after)));
                    continue;
                }

                bool wasPrevious = Same(inFile, Find(previous, x.Name));
                bool isNow = Same(after, Find(current, x.Name));
                bool unchanged = Same(after, inFile);
                bool expect = raw.SeedVersion < generation && wasPrevious ? isNow : unchanged;
                Check(expect,
                      "«" + x.Name + "»: " + (wasPrevious ? "в файле побитово прежняя" : "в файле НЕ прежняя (правлена/иная)")
                      + (wasPrevious ? " -> " + (isNow ? "перенесена" : "осталась прежней")
                                     : " -> " + (unchanged ? "не тронута" : "ИЗМЕНЕНА")),
                      "в файле " + Show(inFile) + "; после загрузки " + Show(after));
            }

            if (dump != null)
            {
                StringBuilder sb = new StringBuilder();
                List<GeometryMaterialLibrary.Entry> sorted = new List<GeometryMaterialLibrary.Entry>(got);
                sorted.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
                foreach (GeometryMaterialLibrary.Entry e in sorted)
                {
                    sb.Append(e.Name).Append(';').Append(e.Abbr ?? "").Append(';').Append(e.Formula ?? "").Append(';')
                      .Append(R(e.Density)).Append(';').Append(e.Kind.ToString()).Append(';').Append(PackOf(e)).Append(';');
                    foreach (GeometryMaterialComponent c in e.Components)
                    {
                        sb.Append(c.Material).Append('=').Append(R(c.Weight)).Append(',');
                    }

                    sb.Append('\n');
                }

                File.WriteAllText(dump, sb.ToString(), new UTF8Encoding(false));
                Say("   дамп всех загруженных записей («R»): " + dump);
            }
        }
    }
}
