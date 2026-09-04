using BecquerelMonitor.Properties;
using System;
using System.Deployment.Application;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    /// <summary>
    /// ЕДИНСТВЕННЫЙ ответ на вопрос «есть ли кому закрыть модальное окно», и
    /// единственная дверь, через которую менеджеры-одиночки сообщают о беде.
    /// Второго способа заводить нельзя: «молчащая проба = <c>MessageBox</c>» —
    /// это известная грабля, и стоила она уже нескольких прогонов подряд.
    ///
    /// ⛔ <c>Environment.UserInteractive</c> САМ ПО СЕБЕ НЕ ГОДИТСЯ, и это
    /// измерено, а не выведено: 27.08.2026 безоконная проба
    /// (<c>Start-Process -NoNewWindow</c>, вывод в файл) напечатала
    /// <c>UserInteractive = True</c>. Он отличает службу от сеанса пользователя,
    /// а не «приложение с окнами» от консольной пробы. Признак, по которому
    /// действительно можно судить, — ВХОДНАЯ СБОРКА: у приложения это
    /// <c>BecquerelMonitor.exe</c>, у всякой пробы и у харнесса — их
    /// собственный <c>*.exe</c>, приложение им лишь библиотека. Проверка точная,
    /// без догадок по консоли, и не зависит ни от перенаправления вывода, ни от
    /// того, из какого каталога пробу запустили.
    ///
    /// <c>UserInteractive</c> оставлен вторым условием ради случая «служба»:
    /// он не заменяет первое, но и не мешает.
    ///
    /// Правило пользования: менеджер НЕ зовёт <c>MessageBox.Show</c> напрямую.
    /// Беда, после которой можно продолжать, идёт в <see cref="Report"/>;
    /// беда, после которой продолжать НЕЛЬЗЯ (нет поставочного конфига —
    /// значит, числа будут ЧУЖИЕ), в безоконном запуске бросает исключение:
    /// у отказа обязан быть читатель, и читатель здесь — код возврата пробы.
    /// </summary>
    public static class AppUi
    {
        /// <summary>
        /// true — приложение с окнами: окно поднимать можно, есть кому нажать
        /// «ОК». false — проба, харнесс, служба: окно повесит запуск намертво.
        /// </summary>
        public static bool HasWindows
        {
            get
            {
                return AppUi.hasWindows;
            }
        }

        /// <summary>
        /// Сообщить о беде, после которой работа продолжается. В окнах —
        /// модальное окно, как было; без окон — одна строка в поток ошибок
        /// (её видно в перехваченном выводе пробы), и запуск идёт дальше.
        ///
        /// ⚠ Заголовок у сообщения бывает ПУСТЫМ, и это не редкость, а
        /// обычай: окно без заголовка — штатный вид <c>MessageBox</c>, и так
        /// её зовут все места ввоза N42 и большая часть <c>DocumentManager</c>.
        /// Без окон пустой заголовок печатался как есть, и строка выходила
        /// «BecqMoni: : текст» (`A174`, 05.09.2026). Разделитель ставится
        /// только за непустым заголовком; строка с заголовком — прежняя,
        /// байт в байт. Мерит <c>ReasonProbe</c>, плечо <c>заголовок</c>.
        /// </summary>
        public static void Report(string text, string caption, MessageBoxIcon icon)
        {
            if (AppUi.hasWindows)
            {
                MessageBox.Show(text, caption, MessageBoxButtons.OK, icon);
                return;
            }
            try
            {
                string head = string.IsNullOrEmpty(caption) ? "" : caption + ": ";
                Console.Error.WriteLine("BecqMoni: " + head + AppUi.OneLine(text));
                Console.Error.Flush();
            }
            catch (IOException)
            {
                // Потока ошибок может не быть вовсе (WinExe без консоли) —
                // это не повод падать: сообщение здесь не единственный итог.
            }
        }

        /// <summary>
        /// Спросить «да/нет» — и НИКОГДА не отвечать за человека.
        ///
        /// Заведено 27.08.2026 вместе с починкой документной прослойки
        /// (<c>S100</c>): пять мест <c>DocumentManager</c> спрашивали
        /// «проверка спектра не прошла — сбросить калибровку?» голым
        /// <c>MessageBox</c> с кнопками Да/Нет, и безоконный прогон вставал на
        /// каждом. Измерено: проба на битой калибровке стояла до убийства,
        /// окно класса <c>#32770</c> с заголовком «Вопрос по сбросу
        /// калибровки» видно в перечне окон процесса.
        ///
        /// ⛔ Умолчания у вопроса быть не может, и это не осторожность, а
        /// арифметика: «Да» ставит <c>new PolynomialEnergyCalibration()</c>,
        /// то есть y = x — номер канала объявляется энергией; «Нет» возвращает
        /// <c>null</c>, и вызывающий считает по несуществующему документу. Оба
        /// ответа меняют числа, поэтому без окон — бросок, а решает его САМА
        /// дверь: вызывающий не может забыть проверку.
        /// </summary>
        public static bool AskYesNo(string text, string caption)
        {
            if (!AppUi.hasWindows)
            {
                throw new InvalidOperationException(
                    "BecqMoni: " + AppUi.OneLine(caption) + ": " + AppUi.OneLine(text)
                    + " — ответить на этот вопрос без окон некому, а оба ответа меняют числа.");
            }
            return MessageBox.Show(text, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                   == DialogResult.Yes;
        }

        /// <summary>
        /// Заверение о том, ЧТО ИМЕННО открылось: не беда, а факт. Печатается
        /// ТОЛЬКО без окон и ТОЛЬКО по факту открытия файла.
        ///
        /// Нужно оно затем, что заверение приходилось писать оснастке, и она
        /// писала его ПРО ФАЙЛ, КОТОРЫЙ ПОЛОЖИЛА, а не про тот, который
        /// приложение открыло (<c>S102</c>): «библиотека нуклидов: 152 записей»
        /// печаталось до запуска, а проба с другим рабочим каталогом открывала
        /// корневой файл на 143 записи, и никто об этом не узнавал. Обещание
        /// обязано называть случившееся, поэтому строку пишет тот, кто файл
        /// открыл.
        ///
        /// В окнах молчит: там путь виден в диалогах, а лишний поток ошибок у
        /// <c>WinExe</c> всё равно некому читать.
        /// </summary>
        public static void Note(string text)
        {
            if (AppUi.hasWindows)
            {
                return;
            }
            try
            {
                Console.Error.WriteLine("BecqMoni: " + AppUi.OneLine(text));
                Console.Error.Flush();
            }
            catch (IOException)
            {
                // Потока ошибок может не быть вовсе — см. Report.
            }
        }

        /// <summary>
        /// Путь для сообщения об отказе — ПОЛНЫЙ. Пути менеджеров считаются от
        /// каталога сборки (<c>Package</c>), но собираются они конкатенацией и
        /// приходят сюда как угодно; «config\… не найден» без корня не говорит
        /// ничего о том, где именно искали.
        /// </summary>
        public static string Where(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "<no path>";
            }
            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception)
            {
                return path;
            }
        }

        /// <summary>
        /// ПРИЧИНА ОТКАЗА СЛОВАМИ — И ОБЯЗАТЕЛЬНО С ВЛОЖЕННОЙ (`A22`, `A25`).
        ///
        /// Стоит здесь, а не у каждого потребителя, ровно потому же, почему
        /// здесь стоит сама дверь: сообщений об отказе в дереве много, и второго
        /// соглашения о том, как называется причина, быть не должно. Двух копий
        /// этого метода — в <c>ROIConfigManager</c> и в
        /// <c>NucBase.NucBaseFramework</c> — хватило, чтобы это стало правдой в
        /// один вечер.
        ///
        /// ⚠ Внешнего исключения МАЛО, и это измерено, а не выведено. Каталог
        /// без <c>&lt;приложение&gt;.exe.config</c>, 03.09.2026: внешнее говорит
        /// «Инициализатор типа "Microsoft.Data.Sqlite.SqliteConnection" выдал
        /// исключение» — по этим словам нельзя сделать ничего; вложенное
        /// называет вещь — «не удалось загрузить сборку SQLitePCLRaw.core,
        /// Version=2.0.6.1341 … определение манифеста не соответствует ссылке».
        /// Разбор XML устроен так же: внешнее сообщает «в документе ошибка»,
        /// вложенное — строку и позицию.
        ///
        /// Сам текст исключения НЕ переводится и переводу не подлежит: он
        /// приходит от платформы на языке системы. Переведена подпись к нему
        /// (<c>ERRFailureReason</c>), и она заведена в обе культуры.
        ///
        /// ⛔ И РОВНО ОДИН РАЗ (`A129`). Тот, кто отказ ОБОРАЧИВАЕТ, обязан
        /// назвать причину в СВОЁМ сообщении — иначе её не увидит потребитель,
        /// печатающий один лишь <c>Message</c>, — и обязан отдать исходное
        /// исключение внутренним, иначе теряется стек. Так устроен
        /// <c>MaterialDatabase.Refuse</c> (`A89`), и поверх такой обёртки
        /// приписка хвоста ставила самое внутреннее ВТОРОЙ раз. Измерено
        /// 04.09.2026 на сцене `F_a25_noconf`: у настоящего отказа базы веществ
        /// сказанное дверью было 862 знака и стало 573 — 289 знаков
        /// дословного повтора. Строка состояния редактора нуклидов на той же
        /// сцене похудела ровно на те же 289 знаков во всех трёх запросах
        /// (1801 → 1512, 1786 → 1497, 1015 → 726).
        ///
        /// ⛔ ПРИЗНАК «УЖЕ НАЗВАНО» — ТОЧНЫЙ, И ЭТО НЕ ПРИДИРКА. Сверяется
        /// ровно та строка, которую собираются приписать («Тип: сообщение»
        /// очередного звена), а не похожая на неё. Цена ошибки в другую
        /// сторону выше: правка, срезающая хвост по частичному совпадению,
        /// ОТНЯЛА БЫ ПРИЧИНУ — а внешнее цитирует внутреннее как угодно
        /// (половиной сообщения, без имени класса, своими словами). Не совпало
        /// дословно — хвост приписывается, и причина остаётся названной
        /// целиком; лишний частичный повтор дешевле потерянной причины.
        ///
        /// ⛔ НАЗЫВАЕТСЯ ВСЯ ЦЕПОЧКА, А НЕ ОДНО ЗВЕНО (`A144`, решение Amber
        /// 05.09.2026). До 05.09.2026 этот метод брал ТОЛЬКО самое внутреннее
        /// исключение, и средние звенья пропадали МОЛЧА. На сцене
        /// `F_a25_noconf` <c>TypeInitializationException</c> был виден лишь
        /// потому, что <c>MaterialDatabase.Refuse</c> положил его в текст своей
        /// рукой; у обёртки БЕЗ собственных слов (например у
        /// <c>TargetInvocationException</c>, чьё сообщение не говорит о причине
        /// ничего) среднее звено терялось целиком. Довод «цепочка не влезет в
        /// строку качества» снят решением Amber: «ширину строки не трогай; этот
        /// функционал <c>FsaOverlay</c> уедет в отдельное окно, там места
        /// хватит всем».
        ///
        /// ПРАВИЛО ОДНО НА ВСЕ ЗВЕНЬЯ, и это то же самое правило `A129`: звено
        /// приписывается, если его «Тип: сообщение» ДОСЛОВНО ещё не стоит в уже
        /// собранном тексте. Второго, более мягкого признака (например «сообщение
        /// звена уже где-то есть») здесь нарочно НЕТ: он отнял бы имя класса у
        /// того, кто цитирует сообщение без класса, — а это ровно тот случай,
        /// ради которого `A129` выбрала точный признак.
        ///
        /// РАЗДЕЛИТЕЛЬ — прежний «&lt;-»: он уже значит в этом дереве «причина
        /// была в», его читают пять проб и все журналы. Цепочка идёт слева
        /// направо, от внешнего к внутреннему.
        ///
        /// ГДЕ ОСТАНАВЛИВАЕТСЯ ОБХОД. Он идёт до конца цепочки, и у него два
        /// сторожа, оба — от ИСПОРЧЕННОЙ цепочки, а не от длины текста:
        /// <list type="bullet">
        /// <item>ПЕТЛЯ. Звено, уже встречавшееся ПО ССЫЛКЕ, обход прекращает
        /// (с `A165` — обход ЭТОЙ ветви; соседним ветвям ход остаётся).
        /// Штатным путём петлю не собрать (<c>InnerException</c> задаётся
        /// конструктором и не переопределяется), но полем <c>_innerException</c>
        /// её кладут и отражение, и десериализация, а ценой была бы вечная
        /// петля в двери, через которую сообщают о беде.</item>
        /// <item>ПРЕДЕЛ ЗВЕНЬЕВ (<see cref="ReasonChainLimit"/>). Глубочайшая
        /// цепочка, которая в этом дереве измерена, — ТРИ звена (обёртка
        /// <c>Refuse</c> → <c>TypeInitializationException</c> →
        /// <c>FileLoadException</c>); предел взят на порядок выше, чтобы быть
        /// сторожем, а не обрезкой по длине.</item>
        /// </list>
        ///
        /// ⚠ Сработавший сторож ГОВОРИТ О СЕБЕ — хвостовым «&lt;- …». Молчаливая
        /// потеря звена и есть тот дефект, который здесь чинится, и заводить её
        /// заново своими руками нельзя. Знак выбран безъязыкий: текст платформы
        /// не переводится (см. выше), и своих переводимых слов тут быть не должно.
        ///
        /// ⛔ ВЕТВЛЕНИЕ РАСКРЫВАЕТСЯ (`A165`, 05.09.2026). У
        /// <c>AggregateException</c> вложенных НЕСКОЛЬКО, а <c>InnerException</c>
        /// отдаёт лишь ПЕРВУЮ из <c>InnerExceptions</c>; до `A165` обход шёл по
        /// ней, и соседние ветви терялись МОЛЧА. Случай живой: <c>Parallel.For</c>
        /// и <c>Task.Run</c> стоят в <c>ResponseMatrixBuilder</c>,
        /// <c>EfficiencyCalculation</c>, <c>FsaOverlay</c>, <c>PeakFilter</c>, и у
        /// падения фонового счёта причина называлась бы неполной. Теперь у
        /// такого звена обходятся ВСЕ <c>InnerExceptions</c>, каждая ветвь — своей
        /// припиской с безъязыкой пометкой «<c>(i/n)</c>»: «<c>Agg: m &lt;- (1/2)
        /// A: m &lt;- B: m &lt;- (2/2) C: m</c>». Цепочка ПОД ветвью идёт тем же
        /// «&lt;-», пока не начнётся следующая ветвь. Ветвь ЕДИНСТВЕННАЯ пометки не
        /// получает, и текст для неё БАЙТ В БАЙТ прежний: приписки `A129` и метки
        /// редактора нуклидов на этом держатся.
        ///
        /// ⚠ <c>Flatten()</c> здесь НЕ зовётся, и это не забывчивость. Он
        /// (1) строит НОВЫЙ объект, и сторож петли по ссылке его не узнал бы;
        /// (2) сам ходит по вложенным <c>AggregateException</c> БЕЗ сторожа, и на
        /// испорченном дереве повис бы раньше нашего обхода; (3) теряет сообщения
        /// вложенных <c>AggregateException</c>. Вложенное ветвление обходится
        /// тем же правилом: вложенный <c>AggregateException</c> — обычное звено,
        /// у которого несколько вложенных.
        ///
        /// СТОРОЖА ОБЩИЕ НА ВСЁ ДЕРЕВО, а не на ветвь: звенья, названные в ЛЮБОЙ
        /// ветви, лежат в одном списке «встречено», и предел
        /// <see cref="ReasonChainLimit"/> считает их ВСЕ. Сработавший предел
        /// останавливает ОБХОД ЦЕЛИКОМ (называть больше нечего) и говорит о себе
        /// хвостовым «&lt;- …». Петля через ветвь — ветвь ссылается на предка или
        /// на соседа — останавливает ТУ ветвь, ставит «&lt;- …» и отдаёт ход
        /// следующей ветви: соседняя ветвь не виновата в чужой петле, и терять
        /// её молча нельзя. Мерится плечами <c>ветвление</c>, <c>одна ветвь</c>,
        /// <c>петля через ветвь</c> (со сроком) и <c>предел по дереву</c> пробы
        /// <c>ReasonProbe</c>.
        ///
        /// ⚠ Приём `A129` работал потому, что прежний метод ВСЕГДА кончался тем
        /// самым хвостом. Теперь он не зависит и от этого: каждое звено ищется
        /// во всём собранном тексте, а не только в его конце.
        /// </summary>
        public static string Reason(Exception ex)
        {
            if (ex == null)
            {
                return "";
            }

            ReasonWalk walk = new ReasonWalk(ex);
            walk.Descend(ex);
            return walk.Text;
        }

        /// <summary>
        /// Состояние ОДНОГО обхода <see cref="Reason"/>: собранный текст, список
        /// встреченных по ссылке звеньев и число названных. Один на всё дерево —
        /// так сторожа петли и предела (`A144`) остаются общими и после того, как
        /// у звена стало несколько вложенных (`A165`).
        /// </summary>
        sealed class ReasonWalk
        {
            public string Text;
            readonly Exception[] seen = new Exception[ReasonChainLimit];
            int depth;
            bool stopped;

            public ReasonWalk(Exception root)
            {
                this.Text = root.GetType().Name + ": " + root.Message;
                this.seen[0] = root;
                this.depth = 1;
            }

            /// <summary>
            /// От НАЗВАННОГО звена — к его вложенным. У обычного звена вложенное
            /// одно (<c>InnerException</c>); у <c>AggregateException</c> — все
            /// <c>InnerExceptions</c>, и <c>InnerException</c> для него НЕ берётся
            /// отдельно: это та же первая ветвь, второй раз она попала бы под
            /// сторож петли и ложно взвела бы его.
            /// </summary>
            public void Descend(Exception from)
            {
                AggregateException fork = from as AggregateException;
                if (fork == null)
                {
                    this.Visit(from.InnerException, "");
                    return;
                }

                System.Collections.ObjectModel.ReadOnlyCollection<Exception> branches =
                    fork.InnerExceptions;
                int n = branches == null ? 0 : branches.Count;
                for (int i = 0; i < n && !this.stopped; i++)
                {
                    // Единственная ветвь пометки не получает — текст обязан
                    // остаться байт в байт прежним (`A129`, метки редактора).
                    string mark = n > 1 ? "(" + (i + 1) + "/" + n + ") " : "";
                    this.Visit(branches[i], mark);
                }
            }

            void Visit(Exception link, string mark)
            {
                if (link == null || this.stopped)
                {
                    return;
                }

                for (int i = 0; i < this.depth; i++)
                {
                    if (ReferenceEquals(this.seen[i], link))
                    {
                        // Петля. Сторож сработал — и говорит об этом. Молчать
                        // нельзя: это ровно тот дефект, который здесь чинится
                        // (`A144`). Останавливается ЭТА ветвь; соседним ход
                        // остаётся (`A165`).
                        this.Text += " <- …";
                        return;
                    }
                }

                if (this.depth >= ReasonChainLimit)
                {
                    // Предел звеньев — общий на всё дерево. Больше назвать
                    // нечего, обход останавливается целиком.
                    this.Text += " <- …";
                    this.stopped = true;
                    return;
                }

                this.seen[this.depth] = link;
                this.depth++;

                string part = link.GetType().Name + ": " + link.Message;
                if (this.Text.IndexOf(part, StringComparison.Ordinal) < 0)
                {
                    this.Text += " <- " + mark + part;
                }

                this.Descend(link);
            }
        }

        /// <summary>
        /// Сколько звеньев цепочки исключений называет <see cref="Reason"/>
        /// (`A144`) — по ВСЕМУ дереву, ветви <c>AggregateException</c> считаются
        /// в тот же предел (`A165`). Это СТОРОЖ ОТ ИСПОРЧЕННОЙ ЦЕПОЧКИ, а не обрезка по длине:
        /// глубочайшая цепочка, измеренная в этом дереве, — три звена, предел
        /// взят на порядок выше. Резать текст по ширине строки качества
        /// запрещено решением Amber 05.09.2026.
        /// </summary>
        public const int ReasonChainLimit = 16;

        static string OneLine(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            return text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
        }

        static bool DetectWindows()
        {
            try
            {
                if (!Environment.UserInteractive)
                {
                    return false;
                }
                Assembly entry = Assembly.GetEntryAssembly();
                if (entry == null)
                {
                    // Неуправляемый хозяин процесса: окон у него может и не
                    // быть, а вешать его нам нечем — считаем безоконным.
                    return false;
                }
                return entry.FullName == typeof(AppUi).Assembly.FullName;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static readonly bool hasWindows = AppUi.DetectWindows();
    }

    // Token: 0x02000078 RID: 120
    public class GlobalConfigManager
    {
        // Token: 0x170001D2 RID: 466
        // (get) Token: 0x0600060B RID: 1547 RVA: 0x00026490 File Offset: 0x00024690
        // (set) Token: 0x0600060C RID: 1548 RVA: 0x00026498 File Offset: 0x00024698
        public GlobalConfigInfo GlobalConfig
        {
            get
            {
                return this.globalConfig;
            }
            set
            {
                this.globalConfig = value;
            }
        }

        // Token: 0x0600060D RID: 1549 RVA: 0x000264A4 File Offset: 0x000246A4
        public static GlobalConfigManager GetInstance()
        {
            GlobalConfigManager.instance.LoadConfigFile();
            return GlobalConfigManager.instance;
        }

        // Token: 0x0600060F RID: 1551 RVA: 0x000264E8 File Offset: 0x000246E8
        public void LoadConfigFile()
        {
            if (this.isLoaded)
            {
                return;
            }
            XmlSerializer xmlSerializer = new XmlSerializer(typeof(GlobalConfigInfo));
            try
            {
                using (FileStream fileStream = new FileStream(becqMoniMainConfig, FileMode.Open))
                {
                    this.globalConfig = (GlobalConfigInfo)xmlSerializer.Deserialize(fileStream);
                }
                if (this.globalConfig.ColorConfig.SpectrumColorList == null || this.globalConfig.ColorConfig.SpectrumColorList.Count < this.MaximumSpectrumPerFile)
                {
                    this.globalConfig.ColorConfig.InitializeSpectrumColor();
                }
                // `S102`: назвать ОТКРЫТЫЙ файл, а не тот, который положили.
                AppUi.Note("main config: " + AppUi.Where(this.becqMoniMainConfig));
            }
            catch (Exception ex)
            {
                // ⛔ Встроенные умолчания — НЕ замена поставочному конфигу, и это
                //    измерено 27.08.2026, а не выведено: в поставочном
                //    `config\BecquerelMonitor.xml` окно сглаживания SMA/WMA
                //    равно 6, а в самих классах (`ChartViewConfig`) — 11.
                //    Значит безоконный прогон «на умолчаниях» считает ДРУГИМ
                //    сглаживанием и выдаёт правдоподобные, но чужие числа —
                //    ровно тот же класс беды, что четырёхзаписная библиотека.
                //    Поэтому без окон — ОТКАЗ с кодом возврата, а не тишина.
                if (!AppUi.HasWindows)
                {
                    throw new InvalidOperationException(
                        "BecqMoni: global configuration could not be loaded and there is no UI to report it to: "
                        + AppUi.Where(this.becqMoniMainConfig)
                        + ". Built-in defaults are NOT the shipped configuration - they differ, so a headless run "
                        + "on defaults would be quietly wrong. Run from a directory that has config\\BecquerelMonitor.xml.",
                        ex);
                }
                AppUi.Report(Resources.ERRLoadingGlobalConfigFailed, Resources.ErrorDialogTitle, MessageBoxIcon.Hand);
                this.globalConfig = new GlobalConfigInfo();
                this.globalConfig.ColorConfig.InitializeSpectrumColor();
            }

            this.VersionString = BecquerelMonitor.Package.GetInstance().PackageVersion;

            this.isLoaded = true;
        }

        // Token: 0x06000610 RID: 1552 RVA: 0x000265E4 File Offset: 0x000247E4
        public void PrepareConfigFile()
        {
            DeviceConfigManager.GetInstance();
            ROIConfigManager.GetInstance();
        }

        // Token: 0x06000611 RID: 1553 RVA: 0x000265F4 File Offset: 0x000247F4
        public void SaveConfigFile()
        {
            XmlSerializer xmlSerializer = new XmlSerializer(typeof(GlobalConfigInfo));
            try
            {
                Utils.AtomicFileWriter.Write(becqMoniMainConfig, fileStream =>
                {
                    xmlSerializer.Serialize(fileStream, this.globalConfig);
                });
            }
            catch (Exception)
            {
                AppUi.Report(Resources.ERRSavingGlobalConfigFailed, Resources.ErrorDialogTitle, MessageBoxIcon.Hand);
            }
        }

        string becqMoniMainConfig = BecquerelMonitor.Package.GetInstance().MainConfig;

        // Token: 0x04000330 RID: 816
        public string VersionString = "1.0";

        // Token: 0x04000331 RID: 817
        public DateTime LimitDate = new DateTime(2111, 3, 11);

        // Token: 0x04000332 RID: 818
        public int MaximumSpectrumPerFile = 16;

        // Token: 0x04000333 RID: 819
        static GlobalConfigManager instance = new GlobalConfigManager();

        // Token: 0x04000334 RID: 820
        GlobalConfigInfo globalConfig;

        // Token: 0x04000335 RID: 821
        bool isLoaded;
    }
}
