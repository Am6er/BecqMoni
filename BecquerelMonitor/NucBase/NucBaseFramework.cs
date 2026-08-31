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

        /// <summary>
        /// ЗАПРОС НЕ БЫЛ ЗАДАН ВОВСЕ: ни родителя, ни диапазона, ни порогов
        /// (`D44`). Пустой ответ при этом признаке значит «не о чем
        /// спрашивать», а не «в базе такого нет», и сказать это человеку
        /// разными словами — единственная причина, по которой признак заведён.
        ///
        /// ⚠ Читатель у него один и настоящий: <c>NucBase.DoSearch</c>, строка
        /// состояния под таблицами. Сбрасывается в начале каждого запроса, как
        /// и <see cref="LastError"/>.
        /// </summary>
        public bool LastNoCriteria { get; private set; }

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
                // Имя — ПАРАМЕТРОМ, а не склейкой (`D45`): апостроф в нём
                // закрывал литерал и ронял запрос. Имя параметра `$n` — то же,
                // что у `CascadeAtomicData`, `FsaSampleLibrary` и
                // `DecayParentRule`; второго соглашения быть не должно.
                SqliteDataReader reader = db.ReadData("select z, n, ifnull(half_life, '?'), ifnull(half_life_unit, ''), ifnull(half_life_sec, 0), ifnull(abundance, 0) from nuclides where nucid = $n and half_life not null",
                                                      DataBase.Param("$n", nucname));
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

                reader = db.ReadData("select daughter_nucid, ifnull(perc, '?'), dec_type from decay_chain where nucid = $n",
                                     DataBase.Param("$n", nucname));
                while (reader.Read())
                {
                    Decay dec = new Decay();
                    dec.NucName = reader.GetString(0);
                    dec.DecayPercent = reader.GetString(1);
                    dec.DecayType = Convert.ToInt32(reader.GetString(2));
                    nuc.Daughters.Add(dec);
                }

                reader = db.ReadData("select nucid, ifnull(perc, '?'), dec_type from decay_chain where daughter_nucid = $n",
                                     DataBase.Param("$n", nucname));
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
        /// строки против 77.5 с на те же 49965.
        ///
        /// ⛔ ПЕРИОД БЕРЁТСЯ ОТДЕЛЬНЫМ ЗАПРОСОМ, А НЕ СОЕДИНЕНИЕМ (`D43`).
        /// Соединение шло по `dr.parent_nucid = nuc.nucid`, а `nucid` в
        /// `nuclides` НЕ УНИКАЛЕН: у `144TBm` там три строки (`l_seqno` 4, 6, 7
        /// — единственный такой случай на всю таблицу, `D41`), и каждая строка
        /// излучения множилась на три. Меряно 31.08.2026: 8 различных `dr_pk`
        /// давали 24 строки, и у трёх копий стоял СВОЙ период — 4.25 с,
        /// 2.8 мкс, 0.67 мкс. В редакторе и в конструкторе ROI линии изомера
        /// троились, каждая со своим периодом. Правило
        /// <see cref="DecayParentRule"/> этот случай переживает (зажим стоит
        /// через `exists`), а соединение — нет: оно про `nuclides`, а зажим про
        /// `decay_radiations.parent_l_seqno`.
        ///
        /// Строка периода выбирается ЯВНО: по `l_seqno`, равному
        /// `dr.parent_l_seqno` — то есть периодом уровня, который эту линию и
        /// испускает, — а если строки такого уровня в `nuclides` нет, берётся
        /// строка с наименьшим `l_seqno`. Запасная ветвь нужна ровно одному
        /// родителю: у `123CSm2` в `nuclides` только `l_seqno` 8, а излучения
        /// стоят на уровне 5 (меряно там же; без запасной ветви его 9 линий
        /// пропали бы целиком). На всех остальных выбор совпадает с прежним
        /// соединением построчно — проверено полным прогоном.
        ///
        /// ⚠ Родитель, которого в `nuclides` нет вовсе, прежде терял линии
        /// молча (соединение внутреннее); теперь они остаются с периодом 0 —
        /// тем же нулём «не измерено», что ниже. Сегодня таких родителей НОЛЬ
        /// (меряно 31.08.2026), так что на числах это не сказывается.
        ///
        /// ⚠ Имя и числа уходят ПАРАМЕТРАМИ (`D45`), а не склейкой: склейка
        /// числа шла через `double.ToString()` текущей культуры, и запятая
        /// разделителя рвала запрос (в приложении разделитель подменяет
        /// `MainForm`, вне его — нет).
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
        public List<DecayRad> getDecayRad(string nucname, double intensity = 0.0, double lowEnergy = 0.0, double highEnergy = 0.0, double half_life_sec = 0)
        {
            this.LastError = null;
            this.LastNoCriteria = false;
            nucname = nucname ?? "";

            // ⛔ БЕЗ ЕДИНОГО УСЛОВИЯ ОТБОРА ЗАПРОС НЕ ИДЁТ (`D44`, решение Amber
            // 28.08.2026). Правило родителя существует, чтобы подтянуть распады
            // дочерних продуктов НАЗВАННОГО родителя; родителя не назвали —
            // подтягивать нечего, расширение не применяется, и остаются только
            // остальные условия. Если не задано ничего, то и показывать нечего:
            // прежде такой вызов выгружал в таблицу редактора ВСЮ таблицу
            // излучений — меряно 31.08.2026, 50054 строки, окно набивалось
            // секундами и человеку не показывало ничего.
            //
            // Пустота при этом обязана читаться не как «в базе нет»: признак
            // <see cref="LastNoCriteria"/> отделяет «не о чем спрашивать» от
            // «спросили, не нашлось», и редактор говорит об этом строкой
            // состояния (`T92`).
            //
            // ⚠ Ноль у границы означает «границу не задавали» — так её и
            // читает сам запрос ниже (`highEnergy != 0.0`), и так её шлёт
            // редактор при пустом поле. Поэтому ЛЮБАЯ ненулевая граница
            // считается вопросом, даже широкая.
            //
            // ⛔ Прежде здесь стояло `highEnergy < 3000`, и это ЛГАЛО
            // человеку: набравший в верхнем поле 4000 получал строку «не
            // задано ни имени, ни диапазона», хотя диапазон он задал.
            // Найдено встречной проверкой 28.08.2026 замером: низ 0 верх
            // 5000 давал 0 строк и признак «не о чем спрашивать», а выше
            // 3000 кэВ в базе лежит 1081 строка — случай не выдуманный.
            bool named = nucname.Length > 0;
            bool banded = lowEnergy > 0.0 || highEnergy > 0.0;
            bool thresholded = intensity > 0.0 || half_life_sec > 0.0;
            if (!named && !banded && !thresholded)
            {
                this.LastNoCriteria = true;
                return new List<DecayRad>();
            }

            // Соединение создаётся ВНУТРИ `try` ниже — довод при `getNuclude` (`D46`).
            DataBase db = null;
            List<SqliteParameter> args = new List<SqliteParameter>();
            // ⛔ Соединения с `nuclides` здесь БОЛЬШЕ НЕТ — оно троило линии
            // изомера (`D43`, довод в примечании к методу). Период приезжает
            // отдельным запросом, а уровень строки нужен, чтобы его выбрать.
            string sql = "select dr.parent_nucid, dr.energy_num, dr.intensity_num, dr.type_a,"
                       + " dr.type_c, dr.dec_type, dr.parent_l_seqno"
                       + " from decay_radiations as dr where dr.type_a in ('G', 'X')";
            if (named)
            {
                sql += " and dr.parent_nucid = $n";
                // ОДНО правило на проект: тот же зажим по уровню родителя, что у
                // `CascadeAtomicData` и `FsaSampleLibrary`, и из того же места.
                // Имя правило ждёт под `$n` — и теперь получает его НАСТОЯЩИМ
                // параметром, как у обоих соседей, а не склейкой (`D45`).
                sql += DecayParentRule.LevelClause;
                args.Add(DataBase.Param("$n", nucname));
            }
            if (intensity >= 0.0)
            {
                // Условие остаётся при нулевом пороге НАРОЧНО: `intensity_num`
                // бывает NULL, и `cast(NULL as float) >= 0` такую строку
                // снимает. Сегодня таких строк ноль, но правило отбора менять
                // здесь нечем — это не про `D44`.
                sql += " and cast(dr.intensity_num as float) >= $i";
                args.Add(DataBase.Param("$i", intensity));
            }
            if (lowEnergy != 0.0)
            {
                sql += " and cast(dr.energy_num as float) >= $lo";
                args.Add(DataBase.Param("$lo", lowEnergy));
            }
            if (highEnergy != 0.0)
            {
                sql += " and cast(dr.energy_num as float) <= $hi";
                args.Add(DataBase.Param("$hi", highEnergy));
            }

            List<DecayRad> decayRads = new List<DecayRad>();
            try
            {
                db = new DataBase();
                // Период — ОТДЕЛЬНЫМ запросом, до строк излучения: читателю на
                // одном соединении нужен один читатель зараз.
                Dictionary<string, List<HalfLifeRow>> halfLives = ReadHalfLives(db, named ? nucname : null);
                SqliteDataReader reader = db.ReadData(sql, args.ToArray());
                while (reader.Read())
                {
                    // Столбцы читаются с проверкой на NULL — все, а не
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

                    HalfLifeRow life = PickHalfLife(halfLives, decrad.Name,
                                                    reader.IsDBNull(6) ? int.MinValue : reader.GetInt32(6));
                    if (half_life_sec > 0 && !(life.Seconds > half_life_sec))
                    {
                        // Тот же отбор, что стоял в запросе условием
                        // `cast(nuc.half_life_sec as float) > …`: неизмеренный
                        // период (NULL, здесь ноль) его не проходит, ровно как
                        // прежде не проходило сравнение с NULL.
                        continue;
                    }

                    decrad.HalfLife = Number(life.Text);
                    decrad.HalfLifeUnit = life.Unit;
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
        /// Строка периода полураспада из <c>nuclides</c>: уровень, период
        /// текстом с единицей и он же в секундах.
        /// </summary>
        sealed class HalfLifeRow
        {
            public int Level;
            public string Text = "";
            public string Unit = "";
            public double Seconds;
        }

        /// <summary>Пустой период — «не измерено» (`D42`), тем же нулём.</summary>
        static readonly HalfLifeRow NoHalfLife = new HalfLifeRow();

        /// <summary>
        /// Периоды из <c>nuclides</c>: {nucid -&gt; строки по возрастанию
        /// уровня}. Отдельный запрос вместо соединения — довод в примечании к
        /// <see cref="getDecayRad"/> (`D43`).
        ///
        /// ⚠ Названный родитель тянет ТОЛЬКО свои строки (обычно одну); имени
        /// нет — читается вся таблица, 4429 строк. Соединение с
        /// `decay_radiations` вместо этого было бы коррелированным подзапросом
        /// на таблице без единого индекса — то самое, что меряно 77.5 с
        /// против 0.09 с (`D44`).
        /// </summary>
        static Dictionary<string, List<HalfLifeRow>> ReadHalfLives(DataBase db, string nucname)
        {
            var lives = new Dictionary<string, List<HalfLifeRow>>(StringComparer.Ordinal);
            string sql = "select nucid, l_seqno, half_life, half_life_unit, half_life_sec from nuclides";
            SqliteParameter[] args = new SqliteParameter[0];
            if (nucname != null)
            {
                sql += " where nucid = $n";
                args = new[] { DataBase.Param("$n", nucname) };
            }

            // Порядок — по уровню: запасной выбор берёт ПЕРВУЮ строку списка.
            sql += " order by nucid, l_seqno";
            SqliteDataReader reader = db.ReadData(sql, args);
            while (reader.Read())
            {
                string nucid = Text(reader, 0);
                List<HalfLifeRow> rows;
                if (!lives.TryGetValue(nucid, out rows))
                {
                    rows = new List<HalfLifeRow>();
                    lives[nucid] = rows;
                }

                rows.Add(new HalfLifeRow
                {
                    Level = reader.IsDBNull(1) ? int.MinValue : reader.GetInt32(1),
                    Text = Text(reader, 2),
                    Unit = Text(reader, 3),
                    Seconds = reader.IsDBNull(4) ? 0.0 : reader.GetDouble(4)
                });
            }

            reader.Close();
            return lives;
        }

        /// <summary>
        /// Период ТОЙ строки `nuclides`, что испускает линию: по уровню
        /// излучения, а нет такого уровня — по наименьшему (`D43`).
        /// </summary>
        static HalfLifeRow PickHalfLife(Dictionary<string, List<HalfLifeRow>> lives, string nucid, int level)
        {
            List<HalfLifeRow> rows;
            if (!lives.TryGetValue(nucid, out rows) || rows.Count == 0)
            {
                return NoHalfLife;
            }

            foreach (HalfLifeRow row in rows)
            {
                if (row.Level == level)
                {
                    return row;
                }
            }

            return rows[0];
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
                    // Имя — параметром: оно приходит из базы и из поля ввода, а
                    // апостроф в нём закрывал литерал и ронял обход (`D45`).
                    SqliteDataReader reader = db.ReadData(
                        "select daughter_nucid, perc from decay_chain d where nucid = $n" +
                        " and perc not null and l_seqno = (select min(l_seqno) from decay_chain x " +
                        "where x.nucid = d.nucid and x.daughter_nucid = d.daughter_nucid " +
                        "and x.dec_type = d.dec_type and x.perc not null)",
                        DataBase.Param("$n", current));
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
