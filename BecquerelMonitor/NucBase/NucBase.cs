using System;
using System.Collections.Generic;
using System.Windows.Forms;
using BecquerelMonitor.Properties;
using System.Text.RegularExpressions;
using System.Threading;
using System.Globalization;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;

namespace BecquerelMonitor.NucBase
{
    public partial class NucBase : Form
    {
        private const int CheckedColumnIdx = 0;
        private const int NameColumnIdx = 1;
        private const int LineColumnIdx = 2;
        private const int EnergyColumnIdx = 3;
        private const int IntencityColumnIdx = 4;
        private const int SeriesColumnIdx = 5;
        private const int DecayTypeColumnIdx = 6;
        private const int HalfLifeColumnIdx = 7;

        /// <summary>
        /// Хвост подписи у характеристического рентгена элемента: «W x-ray».
        /// Не переводится: подпись стоит в файле определений и читается тем же
        /// разбором на обоих языках.
        /// </summary>
        private const string XrayNameSuffix = "x-ray";

        /// <summary>
        /// Ключ строки состояния «условия отбора не заданы» (`D44`) в
        /// СОБСТВЕННОМ <c>resx</c> этой формы.
        ///
        /// ⚠ Строка живёт в паре <c>NucBase.resx</c> / <c>NucBase.ru.resx</c>,
        /// а не в общем <c>Properties/Resources</c>: она принадлежит только
        /// этому окну и читается тем же <see cref="ComponentResourceManager"/>,
        /// которым конструктор берёт подписи его же контролов. Пара
        /// проверяется <c>tools/check_resx.py</c> наравне с подписями.
        ///
        /// ⛔ Ключ НЕ ИМЕЕТ ВИДА «контрол.свойство» и конструктору формы
        /// неизвестен: при перезаписи <c>resx</c> конструктором WinForms его
        /// легко потерять. Потеря видна сразу — строка состояния показывает
        /// само имя ключа (см. <see cref="OwnText"/>), а не пустоту.
        /// </summary>
        private const string NoCriteriaKey = "NucBase_NoCriteria";

        static readonly ComponentResourceManager OwnResources = new ComponentResourceManager(typeof(NucBase));

        /// <summary>
        /// Строка из собственного <c>resx</c> формы. Ключ вместо пропажи:
        /// пустая строка состояния неотличима от «сказать нечего», а признак
        /// без читателя — не работа.
        /// </summary>
        static string OwnText(string key)
        {
            return OwnResources.GetString(key) ?? key;
        }

        private string SearchedIsotope;

        public NucBase()
        {
            InitializeComponent();
        }

        public NucBase(Form mainForm)
        {
            InitializeComponent();
            this.mainForm = mainForm;
            this.Icon = Resources.becqmoni;
            this.IncludeDecayChainCheckBox.Enabled = false;
            this.comboBoxNameFormat.SelectedIndex = 1;
        }

        private void SearchButton_Click(object sender, EventArgs e)
        {
            DoSearch();
        }

        private void DoSearch()
        {
            string isotopeTextBox = this.IsotopeTextBox.Text.Trim().Replace("-", "");
            Match isomerRegex = Regex.Match(isotopeTextBox, @"[m]\d{0,1}$");
            string isomer = "";
            string isotope = isotopeTextBox.ToUpper();
            if (isomerRegex.Index + isomerRegex.Length == isotopeTextBox.Length) 
            {
                isomer = isomerRegex.Value;
                isotope = isotopeTextBox.Substring(0, isomerRegex.Index).ToUpper();
            }
            string isotope_number = Regex.Match(isotope, @"\d+").Value;
            string isotope_name = Regex.Match(isotope, @"[a-zA-Z]+").Value;
            isotope = isotope_number + isotope_name + isomer;
            this.SearchedIsotope = isotope;
            bool incDecayChain = this.IncludeDecayChainCheckBox.Checked;
            // TryParse instead of Convert.ToDouble: non-numeric input used to throw
            // FormatException out of the search button handler.
            double lowEnergy = 0.0;
            if (this.LowEnrgTextBox.Text.Length != 0)
            {
                UserNumber.TryParseDouble(this.LowEnrgTextBox.Text, out lowEnergy);
            }
            double highEnergy = 0.0;
            if (this.HighEnrgTextBox.Text.Length != 0)
            {
                UserNumber.TryParseDouble(this.HighEnrgTextBox.Text, out highEnergy);
            }
            double intensity = 0.0;
            if (this.IntencityTextBox.Text.Length != 0)
            {
                UserNumber.TryParseDouble(this.IntencityTextBox.Text, out intensity);
            }
            double half_life = -1;
            if (this.HalfLifeUOMComboBox.Text.Length > 0 && this.HalfLifeTextBox.Text.Length > 0)
            {
                double halfLifeValue;
                if (UserNumber.TryParseDouble(this.HalfLifeTextBox.Text, out halfLifeValue))
                {
                    half_life = ConvertHalfLifeToSeconds(halfLifeValue, this.HalfLifeUOMComboBox.Text);
                }
            }

            NucBaseFramework fw = new NucBaseFramework();

            // ⛔ ИТОГ ЗАПРОСА ГОВОРИТСЯ СЛОВАМИ, И СОБИРАЕТСЯ ОН ЗАНОВО (`T92`).
            // Пока строки состояния не было, отказ базы выглядел ровно как
            // «линий нет»: список молча оставался таким, каким был. Части
            // складываются сюда и уезжают в строку одним куском в конце — так
            // от прошлого запроса не остаётся ни одного слова.
            //
            // ⚠ Части и ПРИЧИНЫ к ним лежат врозь (<see cref="StatusNote"/>,
            // `A92`): причина у всех частей одного отказа одна, а складывалась
            // она в метку столько раз, сколько было частей.
            StatusNote status = new StatusNote();

            // Символ элемента без массового числа («W», «Pb») — запрос не про
            // распад, а про характеристический рентген: чем светит вольфрам
            // электрода или свинец домика, когда в нём выбило K-электрон. Ряда
            // и родителей у такого запроса нет, поэтому ветка своя и короткая.
            // ⛔ Отказ БАЗЫ ВЕЩЕСТВ здесь — не «это не элемент», и уходить с ним
            // в поиск нуклида нельзя (`A25`): по «W» там не найдётся ничего, и
            // человек прочитал бы «Ничего не найдено» — то есть неправду. Ответа
            // на вопрос у нас в этом случае нет вовсе, и так и говорится.
            string elementError;
            string element = ElementSymbol(isotopeTextBox, out elementError);
            if (elementError != null)
            {
                this.ResultDataGridView.Rows.Clear();
                ShowIsotopeCard("", null);
                ShowCardNote(null);
                status.AddFailure(Resources.NucBase_ElementDataFetchError, elementError);
                SetSearchStatus(status);
                UpdateNuclideDefinitionControlsState();
                return;
            }

            if (element != null)
            {
                this.SearchedIsotope = element;
                List<DecayRad> fluorescence = fw.GetFluorescence(
                    element, intensity: intensity, lowEnergy: lowEnergy, highEnergy: highEnergy);
                this.ResultDataGridView.Rows.Clear();
                foreach (DecayRad line in fluorescence)
                {
                    AddRow(line);
                }

                RestoreSorting();
                // Карточка нуклида у элемента пустая: нуклида нет вовсе, есть
                // только номер элемента — его `ShowIsotopeCard` и оставит.
                ShowIsotopeCard(element, null);
                // Заметка о карточке от ПРОШЛОГО запроса к этому элементу не
                // относится, и остаться на экране не должна (`T96`).
                ShowCardNote(null);
                if (fw.LastError != null)
                {
                    // ⛔ Отказ базы ВЕЩЕСТВ на полпути (`A25`): таблица пуста
                    // потому, что посмотреть не удалось, — а не потому, что у
                    // элемента нет линий. Слова у этих двух случаев разные, как
                    // и у отказа поиска нуклида ниже.
                    status.AddFailure(Resources.NucBase_ElementDataFetchError, fw.LastError);
                }
                else if (fluorescence.Count == 0)
                {
                    // ⛔ ЗДЕСЬ СТОЯЛО МОДАЛЬНОЕ ОКНО, И ОНО ВЕШАЛО БЕЗОКОННЫЙ
                    // ПРОГОН НАСМЕРТЬ (`T98`). Измерено 27–28.08.2026:
                    // `DoSearch("Na")` не возвращался вовсе — процесс убивали по
                    // сроку; сторожем окон поймано и названо поимённо: класс
                    // `#32770`, заголовок «Характеристический рентген».
                    // Положительный контроль `DoSearch("W")` и до, и после даёт
                    // «Найдено линий: 3» и ни одного окна.
                    //
                    // ⚠ «Провести через `AppUi`» было бы половиной дела: окно
                    // осталось бы, а СЛОВА в строке состояния остались бы
                    // короткими и неверными по существу — «ни одна линия не
                    // подошла под условия отбора» неправда, отбор ни при чём,
                    // в таблице нет самого элемента. Поэтому длинное объяснение
                    // переехало В СТРОКУ СОСТОЯНИЯ, а окна не стало вовсе: метка
                    // видна всегда, кнопки не требует и читается прогоном так же,
                    // как глазами (`T92`).
                    status.Add(string.Format(Resources.NucBase_NoFluorescence, element));
                }
                else
                {
                    status.Add(string.Format(CultureInfo.InvariantCulture, Resources.NucBase_SearchFound, fluorescence.Count));
                }

                SetSearchStatus(status);
                UpdateNuclideDefinitionControlsState();
                return;
            }

            // ⛔ Таблица чистится ДО запроса, а не после удачного (`T92`). При
            // отказе очистка не выполнялась вовсе, и на экране оставались
            // строки ПРОШЛОГО нуклида — читались они как строки нового.
            this.ResultDataGridView.Rows.Clear();

            if (!incDecayChain)
            {
                List<DecayRad> decayRads = fw.getDecayRad(isotope, intensity: intensity, lowEnergy: lowEnergy, highEnergy: highEnergy, half_life_sec: half_life);
                if (decayRads == null)
                {
                    status.AddFailure(Resources.NucBase_LinesFetchError, fw.LastError, isotope);
                }
                else
                {
                    foreach (DecayRad decrad in decayRads)
                    {
                        AddRow(decrad);
                    }
                    RestoreSorting();
                    // ⛔ «Ничего не найдено» и «не о чем спрашивать» — РАЗНЫЕ
                    // слова (`D44`). Пустой запрос — без имени родителя, без
                    // диапазона и без порогов — прежде выгружал в эту таблицу
                    // всю базу излучений (50054 строки, меряно 31.08.2026), а
                    // теперь не идёт вовсе; без отдельных слов такая пустота
                    // читалась бы как «в базе такого нет».
                    status.Add(fw.LastNoCriteria
                        ? OwnText(NoCriteriaKey)
                        : decayRads.Count == 0
                            ? Resources.NucBase_SearchEmpty
                            : string.Format(CultureInfo.InvariantCulture, Resources.NucBase_SearchFound, decayRads.Count));
                }
            }
            else
            {
                // Выбран ряд — значит и выходы показываются НА РАСПАД КОРНЯ
                // ряда, а не на распад своего нуклида: у Tl-208 в ториевом ряду
                // это 35.85 % вместо 99.75 %. Ровно эти числа и ввозятся, и
                // ровно их ждёт всё, что стоит на вековом равновесии, —
                // конструктор кривой и разложение спектра.
                Dictionary<string, double> branches = fw.GetChainBranches(isotope);
                if (fw.LastError != null)
                {
                    // Обход ряда оборвался на полпути: часть членов до списка
                    // не доехала, и без этих слов их отсутствие выглядит как
                    // «их в ряду нет».
                    status.AddFailure(Resources.NucBase_DaughtersFetchError, fw.LastError, isotope);
                }

                int shown = 0;
                int refusedMembers = 0;
                string firstReason = null;
                // Ряд без корня — тот же пустой запрос, что и без ряда (`D44`):
                // корня нет, членов ряда нет, и спрашивать не о чем.
                bool noCriteria = false;
                foreach (KeyValuePair<string, double> member in branches.OrderByDescending(m => m.Value))
                {
                    // Порог выхода прикладывается к ПОКАЗАННОМУ числу, а не
                    // к базовому: иначе «не ниже 1 %» отсеивало бы по
                    // величине, которой на экране нет.
                    List<DecayRad> decayRads = fw.getDecayRad(member.Key, intensity: 0.0, lowEnergy: lowEnergy, highEnergy: highEnergy, half_life_sec: half_life);
                    if (decayRads == null)
                    {
                        // Отказ по одному члену ряда не отменяет остальных, но
                        // и потеряться молча не должен: он считается и будет
                        // назван вместе с причиной.
                        refusedMembers++;
                        if (firstReason == null)
                        {
                            firstReason = fw.LastError;
                        }

                        continue;
                    }

                    noCriteria |= fw.LastNoCriteria;
                    foreach (DecayRad decrad in decayRads)
                    {
                        decrad.Intensity *= member.Value;
                        if (decrad.Intensity < intensity)
                        {
                            continue;
                        }

                        AddRow(decrad);
                        shown++;
                    }
                }

                RestoreSorting();
                if (refusedMembers > 0)
                {
                    status.AddFailure(Resources.NucBase_ChainLinesRefused, firstReason, refusedMembers);
                }

                if (shown > 0)
                {
                    status.Add(string.Format(CultureInfo.InvariantCulture, Resources.NucBase_SearchFound, shown));
                }
                else if (refusedMembers == 0)
                {
                    // ⛔ «Ничего не найдено» говорится только когда запросы и
                    // правда прошли. Рядом с отказом эти слова противоречат
                    // ему же — а разводить отказ и пустоту разными словами и
                    // есть смысл строки (`T92`). Пустой запрос (`D44`) —
                    // третий случай, и слова у него тоже свои.
                    status.Add(noCriteria ? OwnText(NoCriteriaKey) : Resources.NucBase_SearchEmpty);
                }
            }

            if (isotope.Length == 0)
            {
                // Имени нет — карточке взяться неоткуда, и прошлая на экране
                // остаться не должна. Вместе с ней уходит и заметка о ней.
                ShowIsotopeCard("", null);
                ShowCardNote(null);
            }
            else
            {
                // Заметка о карточке — ВТОРОЙ слот строки состояния, а не часть
                // итога поиска (`T96`). Прежде она подклеивалась сюда, в `status`,
                // и та же заметка от щелчка по строке затирала весь итог целиком.
                ShowCardNote(ShowCardFor(isotope));
            }

            SetSearchStatus(status);
            UpdateNuclideDefinitionControlsState();
        }

        /// <summary>
        /// Символ элемента, если в запросе нет массового числа: «w» -&gt; «W»,
        /// «PB» -&gt; «Pb». Иначе null — искать надо нуклид, как и раньше.
        ///
        /// Регистр приводится здесь, а не в поиске нуклида: тот всё поднимает в
        /// верхний («137CS»), а символ элемента пишется «Pb», и по «PB» в
        /// таблице ничего не найдётся.
        /// </summary>
        public static string ElementSymbol(string query)
        {
            string unusedError;
            return ElementSymbol(query, out unusedError);
        }

        /// <summary>
        /// То же, но с причиной отказа БАЗЫ ВЕЩЕСТВ (`A25`).
        ///
        /// ⛔ Различить «это не элемент» и «спросить было не у кого» обязан
        /// вызывающий, и без выходного параметра он этого сделать не может:
        /// оба случая дают <c>null</c>. Молча свалиться в ветку поиска нуклида
        /// при отказе базы нельзя — по «W» там не найдётся ничего, и человек
        /// прочитает «Ничего не найдено» вместо правды.
        ///
        /// Перегрузка без параметра оставлена ради читателей, которым причина не
        /// нужна (<c>XrayLinesProbe</c> сверяет только разбор).
        /// </summary>
        public static string ElementSymbol(string query, out string error)
        {
            error = null;
            string letters = Regex.Match(query ?? "", @"^[a-zA-Z]{1,2}$").Value;
            if (letters.Length == 0)
            {
                return null;
            }

            string symbol = letters.Substring(0, 1).ToUpperInvariant()
                            + letters.Substring(1).ToLowerInvariant();
            return NucBaseFramework.ElementNumber(symbol, out error) > 0 ? symbol : null;
        }

        /// <summary>
        /// Карточка нуклида: заполнить прочитанным или ОЧИСТИТЬ (nuc = null).
        ///
        /// ⛔ Метод один на все три места, где карточку показывают (`T92`).
        /// Раньше карточку ЗАПОЛНЯЛИ в трёх местах, и ни одно из этих трёх её
        /// не очищало: когда нуклид не читался, каждое просто выходило — и на
        /// экране оставался ПРЕДЫДУЩИЙ нуклид, чьи Z, N, период, родители и
        /// дочки выглядели принадлежащими выбранному. Пустой список хотя бы
        /// виден; чужая карточка выглядит настоящей.
        ///
        /// ⚠ Очистка в дереве БЫЛА — отдельным методом `ClearIsotopeCard`, но
        /// звали его только из ветки запроса про элемент, а не с путей отказа.
        /// Он снят, и его довод переехал сюда: карточка — про распад, а у
        /// элемента распада нет; оставленная от прошлого поиска, она подписала
        /// бы рентген вольфрама периодом полураспада того, кого искали до него.
        /// </summary>
        private void ShowIsotopeCard(string isotope, Nuclide nuc)
        {
            this.lastElementError = null;
            this.IsotopeNameLabel.Text = isotope ?? "";
            this.ParentsDataGridView.Rows.Clear();
            this.DaughtersDataGridView.Rows.Clear();
            if (nuc == null)
            {
                // Номер элемента остаётся и у пустой карточки — ради запроса о
                // характеристическом рентгене («W»): нуклида там нет вовсе, а
                // Z есть. У ненайденного нуклида (`232TH`) `ZOf` даёт 0, и
                // тогда в поле пусто: ноль в графе «Z» — неправда, а не пробел.
                //
                // ⛔ И ЗДЕСЬ ЖЕ УМИРАЛ ВЕСЬ ЗАПРОС (`A25`). Строкой ниже стоял
                // прямой `MaterialDatabase.ZOf`, а он тянет чтение базы веществ:
                // измерено 03.09.2026 на каталоге без `<проба>.exe.config` —
                // `TypeInitializationException` из поставщика SQLite уходил
                // отсюда наружу через `ShowCardFor` и `DoSearch`, процесс умирал
                // кодом −532462766. Причина теперь не бросок, а значение
                // (`NucBaseFramework.ElementNumber`), и её договаривает
                // <see cref="ShowCardFor"/> — в ту же строку состояния.
                int z = NucBaseFramework.ElementNumber(isotope ?? "", out this.lastElementError);
                this.IsotopeZLabel.Text = z > 0 ? z.ToString(CultureInfo.InvariantCulture) : "";
                this.IsotopeNLabel.Text = "";
                this.IsotopeHLLabel.Text = "";
                this.IsotopeSpecActivity.Text = "";
                this.IsotopeAbundance.Text = "";
                return;
            }

            this.IsotopeZLabel.Text = nuc.Z.ToString(CultureInfo.InvariantCulture);
            this.IsotopeNLabel.Text = nuc.N.ToString(CultureInfo.InvariantCulture);
            // ⛔ ПОДПИСЬ СКЛАДЫВАЕТ `NucBaseFramework`, А НЕ ЭТА СТРОКА
            // (`A304`): у 81 нуклида из 4429 период в поставке — не измерение,
            // а граница сверху, и перед ним обязан стоять знак «>» (решение
            // Amber 10.09.2026). Правило показа вынесено туда, чтобы его
            // проверял безоконный читатель: окно `BecqMoni` в проверке не
            // поднимают.
            this.IsotopeHLLabel.Text = NucBaseFramework.HalfLifeCaption(nuc);
            // ⛔ И ЗДЕСЬ ПОДПИСЬ СКЛАДЫВАЕТ `NucBaseFramework`, А НЕ ЭТА СТРОКА
            // (`A304`, решение Amber 10.09.2026: «Ставить „<“ тем же
            // признаком»). Знак ПРОТИВОПОЛОЖЕН знаку строкой выше: период
            // стоит в знаменателе активности, поэтому граница снизу у периода
            // — это граница СВЕРХУ у активности.
            this.IsotopeSpecActivity.Text = NucBaseFramework.SpecificActivityCaption(nuc);
            this.IsotopeAbundance.Text = nuc.Abundance.ToString(CultureInfo.InvariantCulture) + " %";

            foreach (Decay parent in nuc.Parents)
            {
                this.ParentsDataGridView.Rows.Add(parent.NucName, parent.DecayTypeString, parent.DecayPercent);
            }

            foreach (Decay daughter in nuc.Daughters)
            {
                this.DaughtersDataGridView.Rows.Add(daughter.NucName, daughter.DecayTypeString, daughter.DecayPercent);
            }
        }

        /// <summary>
        /// Прочитать нуклид и показать его карточку — или очистить её, если
        /// показывать нечего.
        ///
        /// Возвращает то, что надо СКАЗАТЬ человеку, когда карточки не будет, и
        /// ПУСТУЮ заметку, когда она показана. Слова у двух причин РАЗНЫЕ
        /// (`T92`): «в таблице такой строки нет либо период не измерен» — это
        /// законный ответ базы, а «прочитать не удалось» — отказ, и признак
        /// <see cref="StatusNote.Failed"/> взводится только у второго.
        /// </summary>
        private StatusNote ShowCardFor(string isotope)
        {
            NucBaseFramework fw = new NucBaseFramework();
            Nuclide nuc = fw.getNuclude(isotope);
            // ⚠ `ShowIsotopeCard` взводит `lastElementError`, поэтому читать его
            // надо ПОСЛЕ вызова и до следующего — так же, как `LastError`
            // у самого `NucBaseFramework`.
            ShowIsotopeCard(isotope, nuc);
            string elementError = this.lastElementError;

            StatusNote note = new StatusNote();
            if (nuc == null)
            {
                if (fw.LastError != null)
                {
                    note.AddFailure(Resources.NucBase_IsotopeFetchError, fw.LastError, isotope);
                }
                else
                {
                    note.Add(string.Format(Resources.NucBase_CardEmpty, isotope));
                }
            }

            if (elementError != null)
            {
                // ⛔ Отказ базы ВЕЩЕСТВ — своя причина и свои слова (`A25`): графа
                // «Z» осталась пустой не потому, что такого элемента нет, а потому,
                // что справиться было негде. Прежде этот отказ вылетал наружу
                // броском и убивал весь запрос.
                note.AddFailure(Resources.NucBase_ElementDataFetchError, elementError);
            }

            return note;
        }

        /// <summary>
        /// Строка состояния под таблицами — ЕДИНСТВЕННОЕ место, где редактор
        /// говорит человеку, чем кончился запрос (`T92`).
        ///
        /// ⛔ Диалогом отказ здесь не показывают: `DoSearch` гоняет безоконная
        /// проба (`ChainProbe.CheckSearch`), и модальное окно вставало бы в ней
        /// насмерть — этим уже заплачено (`D42`, `T98`). Метка видна всегда,
        /// ждать кнопки не заставляет и читается прогоном так же, как глазами.
        ///
        /// Цвет — вторая подсказка, не первая: отказ отличается СЛОВАМИ, а
        /// краснота только помогает его заметить.
        ///
        /// ⛔ У метки ДВА СЛОТА, И ЭТО НЕ УКРАШЕНИЕ (`T96`). Сообщения у неё два
        /// и приходят они с разных путей: итог поиска — из <see cref="DoSearch"/>,
        /// заметка о карточке — ещё и из <see cref="ResultDataGridView_CellClick"/>
        /// с <see cref="ResultDataGridView_CellEnter"/>, то есть с КАЖДОЙ ячейки,
        /// по которой прошли стрелками. Пока слот был один, второе сообщение
        /// затирало первое целиком — измерено 27.08.2026: после «Найдено линий: 5.»
        /// щелчок по строке `148EUm1` оставлял в метке только «Карточки для
        /// 148EUm1 нет: …», и итог не возвращался до следующего поиска. Терялся
        /// он ровно тогда, когда сказать было что ОБОИМ.
        /// </summary>
        private void SetSearchStatus(StatusNote note)
        {
            this.searchNote = note ?? new StatusNote();
            RenderStatus();
        }

        /// <summary>
        /// Второй слот той же метки: заметка о карточке нуклида. Пустая строка
        /// или <c>null</c> слот ОЧИЩАЮТ — заметка о прошлой строке таблицы к
        /// новой не относится.
        /// </summary>
        private void SetCardStatus(StatusNote note)
        {
            this.cardNote = note ?? new StatusNote();
            RenderStatus();
        }

        /// <summary>
        /// Собрать метку из двух слотов. Пустой слот места не занимает — иначе
        /// у сообщения появлялась бы пустая строка сверху или снизу.
        ///
        /// ⛔ ПРИЧИНА НАЗЫВАЕТСЯ РОВНО ОДИН РАЗ (`A92`). Отказ базы виден сразу
        /// нескольким наблюдателям — обходу ряда, сбору линий его членов и
        /// карточке нуклида, — и каждый говорил о нём СВОИМИ словами, доклеивая
        /// к ним ПОЛНЫЙ текст одной и той же причины. Измерено 04.09.2026 на
        /// сцене `F_a25_noconf` (каталог без <c>&lt;проба&gt;.exe.config</c>,
        /// поставщик SQLite не поднимается): в метке стояло 4699 знаков, и
        /// 1160 из них — два лишних повтора причины про <c>nucdb.sqlite</c>.
        /// Читать такую метку нельзя: перечень отказавшего тонет в трёх копиях
        /// одного абзаца.
        ///
        /// Поэтому слоты держат ЧАСТИ и ПРИЧИНЫ врозь (<see cref="StatusNote"/>):
        /// сперва идёт перечень того, что отказало, затем — каждая РАЗЛИЧНАЯ
        /// причина по одному разу, подписью <c>ERRFailureReason</c>. Ни одно
        /// слово из состава сообщения при этом не теряется.
        /// </summary>
        private void RenderStatus()
        {
            List<string> lines = new List<string>();
            lines.AddRange(this.searchNote.Parts);
            lines.AddRange(this.cardNote.Parts);

            List<string> reasons = new List<string>();
            foreach (string reason in this.searchNote.Reasons.Concat(this.cardNote.Reasons))
            {
                // Слоты заполняются с РАЗНЫХ путей (`T96`), и одинаковая причина
                // приходит в них по отдельности: сверять надо после слияния, а
                // не внутри каждого.
                if (!reasons.Contains(reason))
                {
                    reasons.Add(reason);
                }
            }

            foreach (string reason in reasons)
            {
                lines.Add(string.Format(Resources.ERRFailureReason, reason));
            }

            this.SearchStatusLabel.Text = string.Join(Environment.NewLine, lines);
            this.SearchStatusLabel.ForeColor = this.searchNote.Failed || this.cardNote.Failed
                ? System.Drawing.Color.Firebrick
                : System.Drawing.SystemColors.ControlText;
        }

        /// <summary>Итог последнего поиска и был ли он отказом (`T96`).</summary>
        private StatusNote searchNote = new StatusNote();

        /// <summary>Заметка о последней показанной карточке (`T96`).</summary>
        private StatusNote cardNote = new StatusNote();

        /// <summary>
        /// Сообщение строки состояния: ЧТО отказало (<see cref="Parts"/>) и
        /// ПОЧЕМУ (<see cref="Reasons"/>) — ВРОЗЬ (`A92`).
        ///
        /// ⛔ Порознь они лежат затем, что причина у нескольких частей одного
        /// отказа ОДНА, а складывалась она в метку столько раз, сколько было
        /// частей. Резать метку по длине было бы лечением следствия: длинна она
        /// не потому, что причина длинная, а потому, что причина повторена.
        ///
        /// ⚠ Признак <see cref="Failed"/> взводится САМОЙ укладкой отказа
        /// (<see cref="AddFailure"/>), а не отдельным присваиванием: прежде это
        /// были две строки в шести местах, и забыть вторую ничего не стоило.
        /// Прежнее поле `lastCardFailed` снято — оно держало ровно этот признак
        /// у карточки, и теперь он едет вместе с самим сообщением.
        /// </summary>
        private sealed class StatusNote
        {
            /// <summary>Что отказало (или чем кончился удачный запрос) — без причин.</summary>
            public readonly List<string> Parts = new List<string>();

            /// <summary>Причины, каждая по одному разу и в порядке появления.</summary>
            public readonly List<string> Reasons = new List<string>();

            /// <summary>Был ли среди частей отказ: от него краснота метки.</summary>
            public bool Failed;

            public void Add(string text)
            {
                if (!string.IsNullOrEmpty(text))
                {
                    this.Parts.Add(text);
                }
            }

            /// <summary>
            /// Сложить сообщение об отказе: слова — в <see cref="Parts"/>,
            /// причину — в <see cref="Reasons"/>, и только если её там ещё нет.
            ///
            /// Причина у шаблона всегда ПОСЛЕДНИЙ довод, поэтому она и стоит
            /// вторым параметром: остальные доводы (<paramref name="args"/>)
            /// уходят в текст, как уходили.
            /// </summary>
            public void AddFailure(string template, string reason, params object[] args)
            {
                this.Failed = true;
                Add(WithoutReason(template, args));
                if (!string.IsNullOrEmpty(reason) && !this.Reasons.Contains(reason))
                {
                    this.Reasons.Add(reason);
                }
            }
        }

        /// <summary>
        /// Текст сообщения БЕЗ причины: шаблон обрезается по месту, где в нём
        /// стоит причина (`A92`).
        ///
        /// ⛔ Обрезается ШАБЛОН, а не готовая строка, и это не изящество:
        /// причина приходит от платформы и содержит что угодно — точки,
        /// двоеточия, пути, чужие сообщения, — а искать её в готовом тексте
        /// значило бы искать иголку, которую сам же туда и положил. Место
        /// причины известно точно: она ПОСЛЕДНИЙ довод шаблона, то есть
        /// «{N}», где N — число остальных доводов.
        ///
        /// ⚠ Вводное предложение, у которого отобрали то, что оно вводит,
        /// снимается целиком: «Дочерние нуклиды {0} прочитаны не все.
        /// Сообщение:» без сообщения — не текст. Признак вводного — ХВОСТОВОЕ
        /// двоеточие; режется по ближайшей точке слева, и правило это работает
        /// в обеих культурах («… Message:» у того же ключа по-английски).
        /// </summary>
        static string WithoutReason(string template, params object[] args)
        {
            int given = args == null ? 0 : args.Length;
            string text = template ?? "";
            int cut = text.IndexOf("{" + given.ToString(CultureInfo.InvariantCulture) + "}",
                                   StringComparison.Ordinal);
            if (cut >= 0)
            {
                text = text.Substring(0, cut);
            }

            text = text.TrimEnd();
            if (text.EndsWith(":", StringComparison.Ordinal))
            {
                int stop = text.LastIndexOfAny(SentenceEnd);
                text = stop >= 0
                    ? text.Substring(0, stop + 1)
                    : text.Substring(0, text.Length - 1).TrimEnd();
            }

            return given == 0 ? text : string.Format(CultureInfo.InvariantCulture, text, args);
        }

        static readonly char[] SentenceEnd = { '.', '!', '?' };

        private void UpdateNuclideDefinitionControlsState()
        {
            bool hasRows = this.ResultDataGridView.Rows.Count > 0;

            buttonImportDef.Enabled = hasRows;
            checkBoxOverwriteDef.Enabled = hasRows;
            checkBoxAppendRootName.Enabled = IncludeDecayChainCheckBox.Checked;
            checkBoxAppendRootName.Checked = IncludeDecayChainCheckBox.Checked;
            comboBoxNameFormat.Enabled = hasRows;
            labelNameFormat.Enabled = hasRows;
        }

        /// <summary>
        /// Подпись определения для линии характеристического рентгена: «W» -&gt;
        /// «W x-ray». Массового числа в ней нет и быть не может — по этому и
        /// отличают рентген от нуклида те, кто читает файл определений
        /// (см. <see cref="NuclideDefinition.IsElementXrayName"/>).
        /// </summary>
        public static string XrayDefinitionName(string symbol)
        {
            return (symbol ?? "").Trim() + " " + XrayNameSuffix;
        }

        /// <summary>
        /// Период полураспада в годах из ячейки таблицы вида «5.75(Y)».
        ///
        /// Вынесено из обработчика ввоза вместе с <see cref="XrayDefinitionName"/>:
        /// форму можно собрать и без главного окна, но ввоз кончается модальным
        /// сообщением, и проба на нём повисла бы. У рентгена периода нет вовсе —
        /// в ячейке ноль, и разбор обязан его пережить, а не уронить весь ввоз.
        /// </summary>
        public static double HalfLifeYearsFromCell(string cell)
        {
            string[] parts = (cell ?? "").Split('(');
            double value;
            // ⛔ ИНВАРИАНТ (`A242`): ячейку пишет `AddDecayRadRow` выше, тоже
            // инвариантом. Пара «печать → разбор» обязана меняться разом, иначе
            // период полураспада меняется молча — измерено 05.09.2026: на
            // `ru-RU` разбор ОТКАЗЫВАЕТ и оставляет ноль, на `de-DE` точка
            // сходит за разделитель тысяч и «5.75» становится 575.
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            // Take the full unit, not the first character: Substring(0,1) turned
            // "ms" into "m" (minutes, a x60000 error) and "us"/"ns" into unknown units.
            string unit = parts.Length > 1 ? parts[1].TrimEnd(')') : "s";
            return ConvertHalfLifeToSeconds(value, unit) / 31536000;
        }

        private static double ConvertHalfLifeToSeconds(double value, string unit)
        {
            double coeff;

            switch (unit)
            {
                case "s":
                    coeff = 1;
                    break;
                case "m":
                    coeff = 60;
                    break;
                case "h":
                    coeff = 3600;
                    break;
                case "d":
                    coeff = 86400;
                    break;
                case "Y":
                    coeff = 31536000;
                    break;
                case "ms":
                    coeff = 1.0 / 1000.0;
                    break;
                case "us":
                    coeff = 1.0 / 1000000.0;
                    break;
                case "ns":
                    coeff = 1.0 / 1000000000.0;
                    break;
                default:
                    coeff = 1.0;
                    break;
            }
            
            return coeff * value;
        }

        private void AddRow(DecayRad decrad)
        {
            // TODO: use data binding?
            // Галочка стоит у того, за чем пришли: у гамма-линий распада и у
            // всех линий рентгена, когда искали именно рентген элемента.
            bool isGamma = decrad.DecayLine == "G"
                           || decrad.DecayLine == NucBaseFramework.FluorescenceLine;
            // ⛔ Kβ лежит в базе ДВАЖДЫ — итогом `KB` и разложением `KpB1`+`KpB2`
            // (`D33`). У лишней при сложении половины в колонке серии стоит знак
            // суммы: складывать её с соседями значит считать Kβ дважды, на
            // Lu-176 это 40.53 % вместо 33.49 %.
            //
            // ⚠ Галочку здесь снимать не надо и НЕ НАДО ПИСАТЬ КОД, который её
            // снимает: у линий распада типа `X` она и так не ставится — стоит
            // она у гамм и у рентгена ЭЛЕМЕНТА, когда искали именно его. Ветка
            // «если лишняя, снять» была бы кодом, который никогда не работает.
            string series = decrad.XrayType + (decrad.Redundant ? DecayRad.RedundantMark : "");
            // ⛔ ИНВАРИАНТНАЯ КУЛЬТУРА, И ВТОРАЯ ПОЛОВИНА ЭТОЙ ПРАВКИ —
            // `HalfLifeYearsFromCell` (`A242`). Ячейка пишется ЗДЕСЬ, а
            // разбирается ТАМ, при ввозе в набор нуклидов, и до 05.09.2026 обе
            // стороны брали культуру потока: печать и разбор врали согласованно
            // и потому были незаметны. Починить одну — значит записать «5.75» и
            // прочитать ноль (`ru-RU`) или 575 (`de-DE`). Обе стороны сведены к
            // точке разом.
            string hl = decrad.HalfLife.ToString(CultureInfo.InvariantCulture)
                        + "(" + decrad.HalfLifeUnit + ")";
            int index = this.ResultDataGridView.Rows.Add(isGamma, decrad.Name, decrad.DecayLine, decrad.Energy, decrad.Intensity, series, decrad.DecayTypeString, hl);
            if (decrad.Redundant)
            {
                DataGridViewRow added = this.ResultDataGridView.Rows[index];
                added.Cells[SeriesColumnIdx].ToolTipText = Resources.NucBase_KSeriesRedundantHint;
                added.DefaultCellStyle.ForeColor = System.Drawing.SystemColors.GrayText;
            }
        }

        public void CallSearch(decimal energy)
        {
            double delta = 10;
            double lowenergy = (double)energy - delta;
            double highenergy = (double)energy + delta;
            if (lowenergy < 0)
            {
                lowenergy = 0;
            }

            this.LowEnrgTextBox.Text = lowenergy.ToString(CultureInfo.InvariantCulture);
            this.HighEnrgTextBox.Text = highenergy.ToString(CultureInfo.InvariantCulture);

            DoSearch();
        }

        private void ResultDataGridView_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex == -1)
            {
                if (e.ColumnIndex == CheckedColumnIdx)
                {
                    ToggleSelection();
                }

                return;
            }
            string isotope = this.ResultDataGridView.Rows[e.RowIndex].Cells[NameColumnIdx].Value.ToString();
            // ⛔ Карточка чистится в любом случае — и прежде всего когда
            // нуклида не нашлось (`T92`). Здесь стоял выход с примечанием
            // «Stable isotope (no half-life row) — nothing to display», и оно
            // было неверно дважды: у стабильных период есть строкой `STABLE`,
            // отбор их пропускает и карточка у них показывается (`D42`), а
            // «нечего показывать» на деле означало «остаётся карточка
            // предыдущего нуклида».
            ShowCardNote(ShowCardFor(isotope));
        }

        /// <summary>
        /// Сказать о карточке то, что вернул <see cref="ShowCardFor"/>, — во
        /// ВТОРОЙ слот строки состояния (`T96`).
        ///
        /// ⛔ <c>null</c> здесь — не «промолчать», а «снять прежнюю заметку»:
        /// карточка показалась, и старые слова про то, что её нет, ложь. Прежде
        /// метод на <c>null</c> не делал ничего, и заметка о нечитаемой строке
        /// оставалась висеть, пока человек листал СЛЕДУЮЩИЕ, читаемые. Молчание
        /// же было единственным, что берегло итог поиска, — а берёг он его лишь
        /// пока сказать было нечего.
        /// </summary>
        private void ShowCardNote(StatusNote note)
        {
            SetCardStatus(note);
        }

        /// <summary>
        /// ПРИЧИНА ОТКАЗА БАЗЫ ВЕЩЕСТВ у последней показанной карточки, или
        /// <c>null</c> (`A25`). Пишет <see cref="ShowIsotopeCard"/> — она одна
        /// эту базу и трогает, — читает <see cref="ShowCardFor"/> сразу после
        /// вызова, до следующего; так же читаются
        /// <c>NucBaseFramework.LastError</c> и <c>LastNoCriteria</c>.
        ///
        /// Признак нужен затем, что <see cref="ShowIsotopeCard"/> ничего не
        /// возвращает, а сказать человеку надо: пустая графа «Z» при отказе
        /// базы неотличима от пустой графы у нуклида, которого в таблице
        /// элементов нет.
        /// </summary>
        private string lastElementError;

        private void ToggleSelection()
        {
            this.ResultDataGridView.SuspendLayout();
            DataGridViewColumn checkCol = this.ResultDataGridView.Columns[CheckedColumnIdx];
            checkCol.HeaderText = checkCol.HeaderText == "X"
                ? ""
                : "X";

            foreach (DataGridViewRow row in this.ResultDataGridView.Rows)
            {
                row.Cells[CheckedColumnIdx].Value = checkCol.HeaderText == "X";
            }
            this.ResultDataGridView.RefreshEdit();
            this.ResultDataGridView.ResumeLayout();
        }

        private void IsotopeTextBox_TextChanged(object sender, EventArgs e)
        {
            if (this.IsotopeTextBox.Text.Length == 0)
            {
                this.IncludeDecayChainCheckBox.Enabled = false;
                this.IncludeDecayChainCheckBox.Checked = false;
            } else
            {
                this.IncludeDecayChainCheckBox.Enabled = true;
            }
        }

        private void RestoreSorting()
        {
            ListSortDirection direction;
            if (this.ResultDataGridView.SortOrder == SortOrder.Ascending) direction = ListSortDirection.Ascending;
            else direction = ListSortDirection.Descending;
            if (this.ResultDataGridView.SortedColumn != null)
            {
                this.ResultDataGridView.Sort(this.ResultDataGridView.SortedColumn, direction);
            }
        }

        Form mainForm;

        private void IsotopeTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                DoSearch();
            }
        }

        private void IntencityTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                DoSearch();
            }
        }

        private void HalfLifeTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                DoSearch();
            }
        }

        private void LowEnrgTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                DoSearch();
            }
        }

        private void HighEnrgTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                DoSearch();
            }
        }

        private void ResultDataGridView_CellEnter(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex == -1)
            {
                return;
            }
            string isotope = this.ResultDataGridView.Rows[e.RowIndex].Cells[NameColumnIdx].Value.ToString();
            // Та же карточка и то же правило, что при щелчке по строке: показать
            // или очистить, а об отказе сказать (`T92`).
            ShowCardNote(ShowCardFor(isotope));
        }

        private void IsotopeTextBox_Enter(object sender, EventArgs e)
        {
            int DisplayTime = 10000;
            this.toolTip1.Show(Resources.NucBase_IsotopeTextBoxTooltip1, this.IsotopeTextBox, 0, -23, DisplayTime);
        }

        private void buttonImportDef_Click(object sender, EventArgs e)
        {
            try
            {
                int updatedCount = 0;
                int createdCount = 0;
                int redundantSkipped = 0;
                NuclideDefinitionManager defManager = NuclideDefinitionManager.GetInstance();
                // Ряд у всех ввозимых линий один — тот, по которому шёл поиск.
                // Пишется НЕЗАВИСИМО от «дописать имя родителя»: та галочка
                // решает, как линия подписана на графике, а поле — на чей
                // распад дан выход. Раньше это было одно и то же, и выключенная
                // галочка молча теряла принадлежность к ряду.
                string chain = this.IncludeDecayChainCheckBox.Checked
                               && !string.IsNullOrEmpty(this.SearchedIsotope)
                    ? FormatIsotopeName(this.SearchedIsotope)
                    : "";
                foreach (DataGridViewRow row in this.ResultDataGridView.Rows)
                {
                    if ((bool)row.Cells[CheckedColumnIdx].Value == true)
                    {
                        // ⛔ Обе половины Kβ вместе НЕ ВЫГРУЖАЮТСЯ (`D33`): это
                        // не запрет взять помеченную строку, а запрет взять её
                        // ВМЕСТЕ с теми, чью сумму она и есть. Молча пропустить
                        // нельзя — считается и говорится вслух.
                        if (IsRedundantSeries(row) && HasCheckedCounterpart(row))
                        {
                            redundantSkipped++;
                            continue;
                        }

                        string name = (string)row.Cells[NameColumnIdx].Value;
                        bool fluorescence = NucBaseFramework.FluorescenceLine.Equals(
                            row.Cells[LineColumnIdx].Value as string, StringComparison.Ordinal);
                        // Формат имени — про нуклиды («137CS» -> «Cs-137»), у
                        // символа элемента ему не за что зацепиться. Подпись
                        // складывается своя: «W x-ray». Слово в ней не украшение
                        // — по отсутствию массового числа в имени рентген и
                        // отличается потом от нуклида (NuclideDefinition).
                        string formattedName = fluorescence
                            ? XrayDefinitionName(name)
                            : FormatIsotopeName(name);
                        double energy = (double)row.Cells[EnergyColumnIdx].Value;
                        double intencity = (double)row.Cells[IntencityColumnIdx].Value;
                        double halfLifeYears = HalfLifeYearsFromCell(
                            (string)row.Cells[HalfLifeColumnIdx].Value);

                        if (!fluorescence && IncludeDecayChainCheckBox.Checked
                            && checkBoxAppendRootName.Checked && this.SearchedIsotope != name)
                        {
                            formattedName += " (" + FormatIsotopeName(this.SearchedIsotope) + ")";
                        }

                        // Ряда у рентгена нет: выход дан не на распад родителя, а
                        // долей внутри K-серии, и вписанный сюда родитель означал
                        // бы, что линию можно ставить на вековое равновесие.
                        string rowChain = fluorescence ? "" : chain;

                        NuclideDefinition existingDef = defManager.NuclideDefinitions.FirstOrDefault(def => def.Energy == energy);
                        if (existingDef != null && checkBoxOverwriteDef.Checked)
                        {
                            existingDef.Name = formattedName;
                            existingDef.Intencity = intencity;
                            existingDef.HalfLife = halfLifeYears;
                            existingDef.Chain = rowChain;
                            updatedCount++;
                        }

                        if (existingDef == null)
                        {
                            defManager.NuclideDefinitions.Add(new NuclideDefinition()
                            {
                                Name = formattedName,
                                Chain = rowChain,
                                Energy = energy,
                                Intencity = intencity,
                                HalfLife = halfLifeYears,
                                Visible = true,
                                NuclideColor = new SerializableColor(System.Drawing.Color.Green)
                            });
                            createdCount++;
                        }
                    }
                }

                if (updatedCount > 0 || createdCount > 0)
                {
                    defManager.SaveDefinitionFile();
                    string text = string.Format(CultureInfo.InvariantCulture, Resources.NuclideDefImportSuccess, createdCount, updatedCount);
                    if (redundantSkipped > 0)
                    {
                        text += Environment.NewLine + Environment.NewLine
                                + string.Format(CultureInfo.InvariantCulture, Resources.NucBase_KSeriesRedundantSkipped, redundantSkipped);
                    }

                    MessageBox.Show(text);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Resources.NuclideDefImportError, ex.Message + ex.StackTrace));
            }
        }

        /// <summary>Строка помечена как лишняя при сложении (`D33`).</summary>
        static bool IsRedundantSeries(DataGridViewRow row)
        {
            string series = row.Cells[SeriesColumnIdx].Value as string;
            return series != null && series.EndsWith(DecayRad.RedundantMark, StringComparison.Ordinal);
        }

        /// <summary>
        /// Отмечена ли у того же родителя и того же типа распада ХОТЬ ОДНА
        /// строка той же Kβ с другой стороны — то есть та, чью сумму помеченная
        /// строка и представляет. Без этой проверки запрет был бы шире, чем
        /// нужно: взять помеченную строку ОДНУ никто не мешает.
        /// </summary>
        bool HasCheckedCounterpart(DataGridViewRow marked)
        {
            string name = marked.Cells[NameColumnIdx].Value as string;
            string decay = marked.Cells[DecayTypeColumnIdx].Value as string;
            string series = (marked.Cells[SeriesColumnIdx].Value as string) ?? "";
            bool markedIsTotal = series.StartsWith(FullSpectrumAnalysis.KSeriesRule.BetaTotal,
                                                   StringComparison.Ordinal);
            foreach (DataGridViewRow row in this.ResultDataGridView.Rows)
            {
                if (ReferenceEquals(row, marked) || !(row.Cells[CheckedColumnIdx].Value is bool)
                    || !(bool)row.Cells[CheckedColumnIdx].Value)
                {
                    continue;
                }

                if (!string.Equals(row.Cells[NameColumnIdx].Value as string, name, StringComparison.Ordinal)
                    || !string.Equals(row.Cells[DecayTypeColumnIdx].Value as string, decay, StringComparison.Ordinal))
                {
                    continue;
                }

                string other = (row.Cells[SeriesColumnIdx].Value as string) ?? "";
                bool otherIsTotal = other.StartsWith(FullSpectrumAnalysis.KSeriesRule.BetaTotal,
                                                    StringComparison.Ordinal);
                bool otherIsSplit = other.StartsWith("Kp", StringComparison.Ordinal);
                if (markedIsTotal ? otherIsSplit : otherIsTotal)
                {
                    return true;
                }
            }

            return false;
        }

        private string FormatIsotopeName(string nameFromDb)
        {
            Regex nameFormat = new Regex("^([0-9]+){1}([A-Z]+){1}(m[0-9]+)?$");
            Match match = nameFormat.Match(nameFromDb);
            if (!match.Success)
            {
                return nameFromDb;
            }

            string mass = match.Groups[1].Value;
            string isotope = match.Groups[2].Value;
            string isotopeLower = $"{isotope.Substring(0, 1)}{isotope.Substring(1).ToLower()}";
            string isomer = match.Groups.Count > 3
                ? match.Groups[3].Value
                : string.Empty;

            switch (comboBoxNameFormat.SelectedIndex)
            {
                case 0: // 137CS, 234PAm1
                    return $"{mass}{isotope}{isomer}";
                case 1: // Cs137, Pa234m1
                    return $"{isotopeLower}{mass}{isomer}";
                case 2: // Cs-137, Pa-234m1
                    return $"{isotopeLower}-{mass}{isomer}";
                default: // Cs137, Pa234m1
                    return $"{isotopeLower}{mass}{isomer}";
            }
        }

        private void IncludeDecayChainCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            UpdateNuclideDefinitionControlsState();
        }
    }
}
