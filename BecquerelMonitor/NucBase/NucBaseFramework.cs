using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.Sqlite;
using System.Linq;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using BecquerelMonitor.Properties;

namespace BecquerelMonitor.NucBase
{
    public class NucBaseFramework
    {

        public NucBaseFramework()
        {

        }

        /// <summary>
        /// ПРИЧИНА ПОСЛЕДНЕГО ОТКАЗА, словами исключения. Пусто, когда запрос
        /// отказом не кончился (`T92`).
        ///
        /// ⛔ Отказ у этого класса — ЗНАЧЕНИЕ, А НЕ ДИАЛОГ (`D42`): модальное
        /// окно вставало насмерть в безоконном запуске, пробу приходилось
        /// убивать. Но одного признака-значения мало: `null` от
        /// <see cref="getDecayRad"/> и <see cref="getNuclude"/> не отличает
        /// «запрос упал» от «в базе такого нет», и редактор молчал одинаково в
        /// обоих случаях. Здесь лежит причина — чтобы вызывающий сказал
        /// человеку РАЗНЫМИ словами разные вещи.
        ///
        /// ⚠ Свойство сбрасывается В НАЧАЛЕ каждого запроса, поэтому читать его
        /// надо сразу после вызова, до следующего. Так и читают потребители:
        /// <c>NucBase.DoSearch</c> (линии и ряд) и <c>NucBase.ShowCardFor</c>
        /// (карточка нуклида).
        /// </summary>
        public string LastError { get; private set; }

        public Nuclide getNuclude(string nucname)
        {
            this.LastError = null;
            // ⛔ СОЕДИНЕНИЕ СОЗДАЁТСЯ ВНУТРИ `try`, И ЭТО НЕ ОПРЯТНОСТЬ (`D46`).
            // Стояло оно строкой ВЫШЕ, и всякий бросок конструктора — а с ним и
            // бросок ИНИЦИАЛИЗАТОРА ТИПА поставщика — уходил наружу мимо
            // <see cref="LastError"/>, мимо строки состояния, мимо всего, что
            // завела `T92`. Измерено 28.08.2026: прогон без `<проба>.exe.config`
            // рядом (нет перенаправления версий `SQLitePCLRaw.core`) давал
            // `TypeInitializationException` из `SqliteConnection`, пролетавший
            // НАСКВОЗЬ через `getDecayRad` в `DoSearch`, — процесс умирал кодом
            // −532462766 без единого слова человеку. Правка `T87` прикрыла
            // только отказ `Open()`.
            DataBase db = null;
            Nuclide nuc = new Nuclide();
            try
            {
                db = new DataBase();
                SqliteDataReader reader = db.ReadData("select z, n, ifnull(half_life, '?'), ifnull(half_life_unit, ''), ifnull(half_life_sec, 0), ifnull(abundance, 0) from nuclides where nucid = '" + nucname + "' and half_life not null");
                if (!reader.Read())
                {
                    // Пусто — не ошибка: такого имени в таблице нет, либо период
                    // полураспада у него НЕ ИЗМЕРЕН (`half_life` = NULL, 181
                    // нуклид из 4429 — меряно 27.08.2026), и отбор его снял.
                    // Чтение без этой проверки бросало и показывало окно ошибки.
                    //
                    // ⚠ Стабильные сюда НЕ попадают: у них период есть строкой
                    // `STABLE` (244 нуклида), отбор их пропускает, и карточка у
                    // них показывается. Прежде здесь стояло обратное — снято
                    // чтением базы 27.08.2026 (`D42`).
                    db.Close();
                    return null;
                }
                nuc.Z = reader.GetInt32(0);
                nuc.N = reader.GetInt32(1);
                nuc.HalfLife = reader.GetString(2);
                nuc.HalfLifeUOM = reader.GetString(3);
                nuc.HalfLife_Sec = reader.GetDouble(4);
                nuc.Abundance = reader.GetDouble(5);

                reader = db.ReadData("select daughter_nucid, ifnull(perc, '?'), dec_type from decay_chain where nucid = '" + nucname + "'");
                while (reader.Read())
                {
                    Decay dec = new Decay();
                    dec.NucName = reader.GetString(0);
                    dec.DecayPercent = reader.GetString(1);
                    dec.DecayType = Convert.ToInt32(reader.GetString(2));
                    nuc.Daughters.Add(dec);
                }

                reader = db.ReadData("select nucid, ifnull(perc, '?'), dec_type from decay_chain where daughter_nucid = '" + nucname + "'");
                while (reader.Read())
                {
                    Decay dec = new Decay();
                    dec.NucName = reader.GetString(0);
                    dec.DecayPercent = reader.GetString(1);
                    dec.DecayType = Convert.ToInt32(reader.GetString(2));
                    nuc.Parents.Add(dec);
                }
            }
            catch (Exception ex)
            {
                // ⛔ ОТКАЗ — ЗНАЧЕНИЕ, А НЕ ДИАЛОГ (`D42`): здесь стояло
                // модальное окно, а метод зовётся в том числе из `DoSearch`,
                // который гоняет безоконная проба (`ChainProbe.CheckSearch`).
                // Причина уезжает в <see cref="LastError"/>, и редактор
                // показывает её строкой состояния (`T92`).
                Trace.WriteLine("getNuclude(" + nucname + "): " + ex.GetType().Name + ": " + ex.Message);
                this.LastError = ex.GetType().Name + ": " + ex.Message;
                nuc = null;
            }

            // Соединения может не быть вовсе — конструктор до него не дошёл
            // (`D46`). Закрывать нечего, и падать на уборке нельзя: исключение
            // уборки подменило бы причину отказа.
            if (db != null)
            {
                db.Close();
            }

            return nuc;
        }

        /// <summary>
        /// Линии запрошенного родителя для редактора нуклидных сетов и
        /// конструктора ROI.
        ///
        /// ⚠ Набор зажат по уровню родителя ОДНИМ на проект правилом
        /// (<see cref="DecayParentRule.LevelClause"/>, `S89`/`S94`). Без зажима
        /// запрос склеивал наборы разных состояний одного имени — ровно то
        /// двоение распада, которое закрыла `S89` у двух других читателей
        /// (`D39`). Меряно чтением базы 26.08.2026: трогает это РОВНО четырёх
        /// родителей из 2655, у которых строки лежат более чем на одном
        /// `parent_l_seqno`, — `118INm2` 26 → 11 строк, `190Wm2` 25 → 13,
        /// `116AGm2` 82 → 44, `70CUm2` 34 → 10; на `176LU`, `234PAm1`, `137CS`,
        /// `144TBm`, `123CSm2`, `208TL`, `40K` число строк не сдвинулось.
        ///
        /// ⛔ Зажим ставится ТОЛЬКО когда родитель назван. При пустом имени
        /// запрос — не «излучения родителя X», а выгрузка всей таблицы, и
        /// запрошенного родителя у него нет вовсе; подстановка `dr.parent_nucid`
        /// вместо имени делает подзапросы правила коррелированными, а индексов
        /// у `decay_radiations` нет ни одного — меряно там же: 0.09 с на 50054
        /// строки против 77.5 с на те же 49965. Что выгрузка всей таблицы в
        /// таблицу редактора сама по себе бессмысленна — отдельная строка.
        ///
        /// ⚠ Имя подставляется в текст запроса, а не параметром, потому что
        /// <see cref="DataBase.ReadData"/> параметров не принимает вовсе; вся
        /// строка собирается конкатенацией по той же причине. Отдельная строка.
        ///
        /// ⚠ ПЕРИОД ПОЛУРАСПАДА РОДИТЕЛЯ БЫВАЕТ НЕ ИЗМЕРЕН, и это законно
        /// (`D42`): в `nuclides` тогда NULL сразу в трёх колонках —
        /// `half_life`, `half_life_unit`, `half_life_sec`. Меряно чтением базы
        /// 27.08.2026: среди строк типа `G` и `X` таких родителей СЕМЬ —
        /// `126INm`, `148EUm1`, `154TBm`, `156HOm`, `160TMm1`, `200BIm`,
        /// `216FRm`, всего 145 строк. `GetString` на NULL не отдаёт пустую
        /// строку, а бросает, и одна такая строка роняла ВСЮ выборку: поиск по
        /// этим семи именам не возвращал ничего, а выгрузка без имени умирала
        /// на середине таблицы.
        ///
        /// Столбцы поэтому читаются с проверкой на NULL, а неизвестный период
        /// уезжает НУЛЁМ — тем же, каким уходит период у характеристического
        /// рентгена элемента (<see cref="GetFluorescence"/>) и который
        /// потребители уже отличают от настоящего: поправка на распад ставится
        /// только при `HalfLife &gt; 0` (`MeasurementResultManager`).
        ///
        /// ⛔ Отсечь таких родителей условием `nuc.half_life not null`, как в
        /// <see cref="getNuclude"/>, — НЕ то же самое. Там период и ЕСТЬ ответ
        /// метода (карточка нуклида), без него показывать нечего; здесь он
        /// один столбец из восьми, а ответ — ЛИНИИ родителя, и они на месте.
        /// С отсечкой поиск по этим семи именам остался бы мёртвым, только
        /// молча — исключение сменилось бы пустым списком.
        /// </summary>
        public List<DecayRad> getDecayRad(string nucname, double intensity = 0.0, double lowEnergy = 0.0, double highEnergy = 3000.0, double half_life_sec = 0)
        {
            this.LastError = null;
            // Соединение создаётся ВНУТРИ `try` ниже — довод при `getNuclude` (`D46`).
            DataBase db = null;
            string sql = "select dr.parent_nucid, dr.energy_num, dr.intensity_num, dr.type_a, dr.type_c, dr.dec_type, nuc.half_life, nuc.half_life_unit from decay_radiations as dr, nuclides nuc where dr.parent_nucid = nuc.nucid and dr.type_a in ('G', 'X') and ";
            if (nucname.Length > 0)
            {
                sql += "dr.parent_nucid = " + SqlText(nucname) + " and ";
            }
            if (intensity >= 0.0)
            {
                sql += "cast(dr.intensity_num as float) >= " + intensity + " and ";
            }
            if (highEnergy == 0.0 && lowEnergy == 0.0)
            {
                sql += " 1=1 and ";
            } else
            {
                if (lowEnergy == 0.0)
                {
                    sql += "cast(dr.energy_num as float) <= " + highEnergy + " and ";
                }
                if (highEnergy == 0.0)
                {
                    sql += "cast(dr.energy_num as float) >= " + lowEnergy + " and ";
                }

                if (highEnergy > 0.0 && lowEnergy > 0.0)
                {
                    sql += "cast(dr.energy_num as float) >= " + lowEnergy + " and  cast(dr.energy_num as float) <= " + highEnergy + " and ";
                }
            }
            if (half_life_sec > 0)
            {
                sql += "cast(nuc.half_life_sec as float) > " + half_life_sec + " and ";
            }
            sql += " 1=1";
            if (nucname.Length > 0)
            {
                // ОДНО правило на проект: тот же зажим по уровню родителя, что у
                // `CascadeAtomicData` и `FsaSampleLibrary`, и из того же места.
                // Имя правило ждёт под `$n`; параметров у `ReadData` нет, поэтому
                // подставляется текстом — см. примечание к методу.
                sql += DecayParentRule.LevelClause.Replace("$n", SqlText(nucname));
            }

            List<DecayRad> decayRads = new List<DecayRad>();
            try
            {
                db = new DataBase();
                SqliteDataReader reader = db.ReadData(sql);
                while (reader.Read())
                {
                    // Столбцы читаются с проверкой на NULL — все восемь, а не
                    // только те, где NULL нашёлся сегодня (`D42`). Так же
                    // читают `decay_radiations` оба других читателя проекта,
                    // `CascadeAtomicData` и `FsaSampleLibrary`; третьего
                    // соглашения о чтении здесь быть не должно.
                    DecayRad decrad = new DecayRad();
                    decrad.Name = Text(reader, 0);
                    decrad.Energy = reader.IsDBNull(1) ? 0.0 : reader.GetDouble(1);
                    string intensitystr = Text(reader, 2);
                    if (intensitystr.IndexOf("(") != -1)
                    {
                        intensitystr = intensitystr.Replace("(", "").Replace(")", "").Trim();
                    }
                    decrad.Intensity = Number(intensitystr);
                    decrad.DecayLine = Text(reader, 3);
                    decrad.XrayType = Text(reader, 4);
                    decrad.DecayType = Integer(Text(reader, 5));
                    decrad.HalfLife = Number(Text(reader, 6));
                    decrad.HalfLifeUnit = Text(reader, 7);
                    decayRads.Add(decrad);
                }
            } catch (Exception ex)
            {
                // ⛔ ОТКАЗ — ЗНАЧЕНИЕ, А НЕ ДИАЛОГ (`D42`). Здесь стояло
                // модальное окно, и на нём насмерть вставал любой безоконный
                // запуск: проба ждала кнопки, которую некому нажать — меряно
                // 27.08.2026, процесс с окном «Ошибка!» пришлось убивать.
                // Признак отказа у метода прежний и единственный — `null`, и
                // читают его оба вызывающих (`NucBase.DoSearch`,
                // `ChainProbe`). Причина при этом больше не теряется: она
                // уезжает в <see cref="LastError"/>, а редактор показывает её
                // строкой состояния под таблицами (`T92`) — до этого отказ был
                // неотличим от «линий нет».
                Trace.WriteLine("getDecayRad: " + ex.GetType().Name + ": " + ex.Message
                                + Environment.NewLine + sql);
                this.LastError = ex.GetType().Name + ": " + ex.Message;
                decayRads = null;
            }

            // Соединения может не быть вовсе — см. `getNuclude` (`D46`).
            if (db != null)
            {
                db.Close();
            }

            MarkRedundantKSeries(decayRads);
            return decayRads;
        }

        /// <summary>
        /// Строковая константа для текста запроса: в кавычках, с удвоением
        /// апострофа. В идентификаторах нуклидов апострофа не бывает, но имя
        /// сюда приходит из поля ввода, а <see cref="DataBase.ReadData"/>
        /// параметров не принимает вовсе — значит имя попадает прямо в текст,
        /// и апостроф в нём закрыл бы литерал.
        /// </summary>
        static string SqlText(string value)
        {
            return "'" + value.Replace("'", "''") + "'";
        }

        /// <summary>
        /// Текст столбца, в котором NULL законен (`D42`).
        ///
        /// ⚠ <c>SqliteDataReader.GetString</c> на NULL не отдаёт ни пустой
        /// строки, ни <c>null</c>, а бросает
        /// <c>InvalidOperationException: The data is NULL at ordinal N</c>, —
        /// и одна такая строка роняет всю выборку целиком, а не только себя.
        /// </summary>
        static string Text(SqliteDataReader reader, int column)
        {
            return reader.IsDBNull(column) ? "" : reader.GetString(column);
        }

        /// <summary>
        /// Число из текста столбца; пусто и нечисло дают 0, а не исключение.
        ///
        /// ⚠ Культура — ТЕКУЩАЯ, ровно как у прежнего <c>Convert.ToDouble</c>:
        /// числа базы приходят с точкой, и приложение подменяет разделитель
        /// при запуске (<c>MainForm</c>). Инвариантная культура здесь была бы
        /// не «правильнее», а расхождением с остальными разборами этой базы.
        /// </summary>
        static double Number(string text)
        {
            double value;
            double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
            return value;
        }

        /// <summary>Целое из текста столбца; пусто и нечисло дают 0.</summary>
        static int Integer(string text)
        {
            int value;
            int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value);
            return value;
        }

        /// <summary>
        /// ЛОВУШКА K-СЕРИИ В РЕДАКТОРЕ (`D33`). В `decay_radiations` Kβ лежит
        /// ДВАЖДЫ: итогом `KB` и разложением `KpB1` + `KpB2`. В таблице они
        /// стоят рядом как равноправные линии, и ничто не говорило, что
        /// складывать их вместе нельзя, — а наивная сумма всех `K*` завышает
        /// K-выход: на Lu-176 40.53 % вместо 33.49 %, в 1.21 раза.
        ///
        /// Здесь у лишней при сложении половины снимается галочка и в колонке
        /// серии появляется пометка. Прятать строку нельзя: она в базе есть, и
        /// взять именно её человек вправе — но взять ОБЕ он теперь может только
        /// нарочно, и при ввозе ему об этом скажут.
        ///
        /// Какая половина лишняя, решает <see cref="KSeriesRule"/> — то же
        /// правило, что у разбора и у суммирователя совпадений; трёх
        /// соглашений о Kβ в проекте быть не должно.
        ///
        /// ⚠ Набор здесь — «родитель + тип распада», без уровня, и этого хватает:
        /// уровень уже зажат САМИМ ЗАПРОСОМ (`D39`, <see cref="DecayParentRule"/>),
        /// так что до сюда доезжают строки одного уровня. Двух уровней сразу в
        /// списке больше нет — кроме выгрузки без имени родителя, где зажима нет
        /// по цене запроса и где сложение K-серии всё равно идёт по родителю.
        /// </summary>
        static void MarkRedundantKSeries(List<DecayRad> lines)
        {
            if (lines == null)
            {
                return;
            }

            var sets = new Dictionary<string, List<DecayRad>>(StringComparer.Ordinal);
            foreach (DecayRad line in lines)
            {
                line.Redundant = false;
                if (line.DecayLine != "X" || !KSeriesRule.IsSeries(line.XrayType))
                {
                    continue;
                }

                string key = line.Name + "\u0001" + line.DecayType.ToString(CultureInfo.InvariantCulture);
                List<DecayRad> set;
                if (!sets.TryGetValue(key, out set))
                {
                    set = new List<DecayRad>();
                    sets[key] = set;
                }

                set.Add(line);
            }

            foreach (KeyValuePair<string, List<DecayRad>> pair in sets)
            {
                var split = new List<DecayRad>();
                var total = new List<DecayRad>();
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (DecayRad line in pair.Value)
                {
                    if (KSeriesRule.IsBetaTotal(line.XrayType))
                    {
                        total.Add(line);
                    }
                    else if (KSeriesRule.IsBetaSplit(line.XrayType))
                    {
                        split.Add(line);
                        names.Add(line.XrayType);
                    }
                }

                if (split.Count == 0 || total.Count == 0)
                {
                    continue;           // одна половина — выбирать не из чего
                }

                List<double[]> splitPairs = Numbers(split);
                List<double[]> chosen = KSeriesRule.Beta(splitPairs, Numbers(total), names.Count);
                List<DecayRad> loser = ReferenceEquals(chosen, splitPairs) ? total : split;
                foreach (DecayRad line in loser)
                {
                    line.Redundant = true;
                }
            }
        }

        static List<double[]> Numbers(List<DecayRad> lines)
        {
            var pairs = new List<double[]>();
            foreach (DecayRad line in lines)
            {
                pairs.Add(new[] { line.Energy, line.Intensity });
            }

            return pairs;
        }

        /// <summary>
        /// Характеристический рентген ЭЛЕМЕНТА: «W», «Pb» — символ без
        /// массового числа. Это не распад: атом отвечает квантом на дырку в
        /// K-оболочке, откуда бы та ни взялась — от фотопоглощения в электроде,
        /// в свинцовом домике, в корпусе. Поэтому и берётся не из
        /// <c>decay_radiations</c>, а из <c>xray_fluorescence</c>
        /// (<see cref="MaterialDatabase"/>): энергии Kα1, Kα2 и Kβ посчитаны по
        /// краям поглощения XCOM, веса — доли внутри K-серии.
        ///
        /// Выход на распад у таких линий не определён вовсе, поэтому в колонке
        /// интенсивности стоит доля внутри серии, в сумме 100 %.
        ///
        /// Пустой список — про этот элемент в таблице ничего нет: она
        /// заполнена от Z = 30, у более лёгких нет пары краёв L2/L3, по разности
        /// с которыми считаются энергии линий.
        /// </summary>
        public List<DecayRad> GetFluorescence(string symbol, double intensity = 0.0,
                                              double lowEnergy = 0.0, double highEnergy = 0.0)
        {
            List<DecayRad> lines = new List<DecayRad>();
            int z = MaterialDatabase.ZOf(symbol);
            MaterialDatabase.Fluorescence fluorescence = z > 0 ? MaterialDatabase.FluorescenceOf(z) : null;
            if (fluorescence == null)
            {
                return lines;
            }

            string[] labels = { "KA1", "KA2", "KB" };
            for (int i = 0; i < fluorescence.LineKev.Length && i < labels.Length; i++)
            {
                double energy = fluorescence.LineKev[i];
                double percent = fluorescence.LineWeight[i] * 100.0;
                if (energy <= 0.0 || percent < intensity
                    || (lowEnergy > 0.0 && energy < lowEnergy)
                    || (highEnergy > 0.0 && energy > highEnergy))
                {
                    continue;
                }

                lines.Add(new DecayRad
                {
                    Name = symbol,
                    Energy = energy,
                    Intensity = percent,
                    DecayLine = FluorescenceLine,
                    XrayType = labels[i],
                    // Периода полураспада у элемента нет: светит он не сам, а в
                    // ответ на облучение. Ноль здесь и означает «не применимо» —
                    // и с ним же уходит в определение при ввозе.
                    HalfLife = 0.0,
                    HalfLifeUnit = "s",
                    DecayTypeText = Resources.NucBase_Fluorescence
                });
            }

            return lines;
        }

        /// <summary>
        /// Метка строки характеристического рентгена в колонке типа излучения.
        /// По ней же ввоз узнаёт такую строку: у неё нет ни родителя, ни ряда,
        /// ни периода полураспада.
        /// </summary>
        public const string FluorescenceLine = "XF";

        /// <summary>
        /// Ряд от корня: {нуклид -> накопленная доля ветвления}, у корня 1.0.
        ///
        /// Нужна для выходов НА РАСПАД РОДИТЕЛЯ РЯДА. В базе выход линии дан на
        /// распад своего нуклида: у Tl-208 линия 2614 кэВ стоит 99.75 %, но сам
        /// Tl-208 получается лишь из 35.94 % распадов Bi-212, и на распад Th-232
        /// та же линия даёт 35.85 %. Векового равновесия иначе не посчитать —
        /// именно на распад родителя даны все выходы, которыми пользуется и
        /// конструктор кривой, и разложение спектра.
        ///
        /// Две тонкости, без которых числа врут:
        ///
        /// * идти только по НИЖНЕМУ уровню родителя (`l_seqno`): строки с
        ///   большим номером описывают распад возбуждённого уровня и дублируют
        ///   тот же переход с другим ветвлением — у Bi-212 на Tl-208 есть
        ///   35.94 % при уровне 0 и 67 % при уровне 5. Изомер, если он живёт
        ///   сам по себе, имеет собственный nucid (234PAM1);
        /// * пропускать петлю на себя: у 238U в базе есть такая строка.
        /// </summary>
        public Dictionary<string, double> GetChainBranches(string rootNucid, double minFraction = 1e-6)
        {
            this.LastError = null;
            Dictionary<string, double> fraction = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            // Обход разносит ДЕЛЬТЫ, а не посещает нуклиды по разу: пути разной
            // длины сходятся в один нуклид (234U из 234Th через 234mPa и через
            // IT-ветку 234Pa), и вклад, пришедший к уже пройденному узлу,
            // раньше терялся для его потомков. Каждая запись очереди — «этому
            // нуклиду добавилось столько-то», и добавка проходит вниз ровно
            // один раз независимо от порядка обхода. Отсечка minFraction гасит
            // и хвосты, и циклы грязных данных; ограничение длины очереди —
            // страховка от цикла со 100-процентной веткой.
            List<KeyValuePair<string, double>> queue = new List<KeyValuePair<string, double>>();
            fraction[rootNucid] = 1.0;
            queue.Add(new KeyValuePair<string, double>(rootNucid, 1.0));

            // Соединение создаётся ВНУТРИ `try` — довод при `getNuclude` (`D46`).
            DataBase db = null;
            try
            {
                db = new DataBase();
                for (int i = 0; i < queue.Count && queue.Count <= 1000; i++)
                {
                    string current = queue[i].Key;
                    double share = queue[i].Value;
                    List<KeyValuePair<string, string>> rows = new List<KeyValuePair<string, string>>();
                    // Строки вычитываются целиком до следующего запроса: обходу
                    // нужен ещё один читатель на том же соединении.
                    // Минимальный l_seqno ищется среди строк С ЧИСЛОМ: если у
                    // самой ранней записи perc пуст, дочка бралась бы из неё и
                    // выпадала из ряда целиком, хотя число есть строкой ниже.
                    SqliteDataReader reader = db.ReadData(
                        "select daughter_nucid, perc from decay_chain d where nucid = '" + current +
                        "' and perc not null and l_seqno = (select min(l_seqno) from decay_chain x " +
                        "where x.nucid = d.nucid and x.daughter_nucid = d.daughter_nucid " +
                        "and x.dec_type = d.dec_type and x.perc not null)");
                    while (reader.Read())
                    {
                        rows.Add(new KeyValuePair<string, string>(reader.GetString(0), reader.GetString(1)));
                    }

                    reader.Close();

                    foreach (KeyValuePair<string, string> row in rows)
                    {
                        double percent;
                        if (string.Equals(row.Key, current, StringComparison.OrdinalIgnoreCase)
                            || !double.TryParse(row.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out percent))
                        {
                            continue;
                        }

                        double add = share * percent / 100.0;
                        if (add < minFraction)
                        {
                            continue;
                        }

                        if (fraction.ContainsKey(row.Key))
                        {
                            fraction[row.Key] += add;
                        }
                        else
                        {
                            fraction[row.Key] = add;
                        }

                        queue.Add(new KeyValuePair<string, double>(row.Key, add));
                    }
                }
            }
            catch (Exception ex)
            {
                // ⛔ Тот же разбор, что у соседей по классу: отказ — значение,
                // а не диалог (`D42`). Ряд при этом возвращается ОБОРВАННЫМ, и
                // молчать об этом нельзя: недостающие члены выглядят как
                // «их в ряду нет». Причина уезжает в <see cref="LastError"/>,
                // редактор говорит о ней строкой состояния (`T92`).
                Trace.WriteLine("GetChainBranches(" + rootNucid + "): " + ex.GetType().Name + ": " + ex.Message);
                this.LastError = ex.GetType().Name + ": " + ex.Message;
            }

            // Соединения может не быть вовсе — см. `getNuclude` (`D46`).
            if (db != null)
            {
                db.Close();
            }

            return fraction;
        }

    }
}
