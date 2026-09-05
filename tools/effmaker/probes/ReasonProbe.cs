using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ReasonProbe
{
    /// <summary>
    /// МЕРКА ЕДИНСТВЕННОЙ ДВЕРИ, КОТОРАЯ НАЗЫВАЕТ ПРИЧИНУ (`A129`, `A144`).
    ///
    /// ЗАЧЕМ ПРОБА. <c>AppUi.Reason</c> приписывает к внешнему исключению самое
    /// внутреннее («<c> &lt;- Тип: сообщение</c>»), и делала она это ВСЕГДА —
    /// в том числе когда внешнее сообщение уже содержит ровно этот же хвост.
    /// Так устроен <c>MaterialDatabase.Refuse</c> (`A89`): он кладёт
    /// <c>AppUi.Reason(ex)</c> В СВОЁ СООБЩЕНИЕ (чтобы отказ назвал себя тому,
    /// кто печатает один лишь <c>Message</c>) И передаёт <c>ex</c> внутренним
    /// (чтобы не терялся стек). Причина после этого стояла дважды.
    ///
    /// ⛔ ПРАВКА, СРЕЗАЮЩАЯ ХВОСТ ВСЕГДА, ХУЖЕ ДЕФЕКТА: она отняла бы причину.
    /// Поэтому плечи здесь мерят ОБЕ стороны:
    ///   * <c>цитата</c> — хвост уже стоит в тексте внешнего: обязан быть ОДИН раз;
    ///   * <c>без цитаты</c> — ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: внешнее о внутреннем
    ///     молчит, хвост обязан ОСТАТЬСЯ;
    ///   * <c>половина</c> и <c>без класса</c> — внешнее цитирует внутреннее
    ///     НЕПОЛНО: причина обязана остаться названной ЦЕЛИКОМ, то есть хвост
    ///     приписывается. Признак «уже названо» — точный: ровно та строка,
    ///     которую собираются приписать, а не «похожая».
    ///
    /// ⛔ ЦЕПОЧКА ЦЕЛИКОМ (`A144`, решение Amber 05.09.2026). Прежде дверь
    /// называла ТОЛЬКО самое внутреннее звено, и средние пропадали молча.
    /// Плечи, мерящие это:
    ///   * <c>без своих слов</c> — обёртка, чьё сообщение о причине не говорит
    ///     НИЧЕГО (<c>TargetInvocationException</c>): среднее звено сегодня
    ///     теряется целиком, после правки обязано называться;
    ///   * <c>петля</c> и <c>предел</c> — сторожа обхода. Оба обязаны
    ///     ГОВОРИТЬ О СЕБЕ хвостовым «&lt;- …»: молчаливая потеря звена и есть
    ///     чинимый дефект;
    ///   * <c>ветвление</c> — <c>AggregateException</c>. До `A165` плечо было
    ///     СПРАВОЧНЫМ: обход шёл по <c>InnerException</c>, а он отдаёт лишь
    ///     первую ветвь.
    ///
    /// ⛔ ВЕТВЛЕНИЕ РАСКРЫВАЕТСЯ (`A165`, 05.09.2026). Плечи:
    ///   * <c>ветвление</c> — три разных ветви, все три обязаны быть названы,
    ///     с пометками «(i/3)», цепочка под ветвью — тем же «&lt;-»;
    ///   * <c>одна ветвь</c> — ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ФОРМАТА: у единственной
    ///     ветви текст обязан быть БАЙТ В БАЙТ прежним, без пометки;
    ///   * <c>вложенное ветвление</c> — узел в узле, все листья названы;
    ///   * <c>петля через ветвь</c> — ветвь на предка и ветвь на соседа, СО
    ///     СРОКОМ в секунду; сторож говорит о себе, чистая ветвь не теряется;
    ///     после `A185` — знак сторожа ОДИН, текст байт в байт; после `A219`
    ///     — все три пометки «(i/3)» на месте, у уже названной второй ветви
    ///     форма «&lt;- (2/3) = выше»;
    ///   * <c>предел по дереву</c> — предел один на всё дерево, а не на ветвь;
    ///     после `A230` — два дерева: предел, упёршийся в ветвь узла, даёт
    ///     «&lt;- (3/3) …», предел внутри ветви — прежний голый знак.
    ///
    /// ⛔ СОСЕДНЯЯ ДВЕРЬ ТОЙ ЖЕ ОБЁРТКИ — <c>AppUi.Report</c> (`A174`, 05.09.2026).
    /// Без окон она печатает одну строку в поток ошибок, и при ПУСТОМ
    /// заголовке (так её зовут все места ввоза N42) строка выходила
    /// «BecqMoni: : текст». Плечо <c>заголовок</c> перехватывает поток ошибок
    /// и мерит обе стороны: без заголовка — «BecqMoni: текст», с заголовком
    /// — прежняя строка БАЙТ В БАЙТ, «BecqMoni: заголовок: текст».
    ///
    /// ⚠ Ожидание РАЗНОЕ у двух сборок, и это не изъян пробы, а её смысл: на
    /// старой сборке плечо <c>цитата</c> обязано дать «ДВАЖДЫ» и код 1, на
    /// новой — «ОДИН РАЗ» и код 0; после `A144` на старой сборке отказывают ещё
    /// и плечи цепочки, после `A165` — плечи ветвления, после `A174` — плечо
    /// <c>заголовок</c>. Прогон без старой сборки мерит пустоту.
    ///
    ///     reasonprobe [--real]
    ///
    /// Ключ <c>--real</c> добавляет плечо на НАСТОЯЩЕМ отказе базы веществ: оно
    /// имеет смысл только в каталоге, где база не поднимается (сцена
    /// `F_a25_noconf` — каталог без <c>&lt;проба&gt;.exe.config</c>), и печатает
    /// длину сказанного и число повторов причины.
    /// </summary>
    static class Program
    {
        static int bad;

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            bool real = false;
            foreach (string a in args)
            {
                if (a == "--real")
                {
                    real = true;
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            Console.WriteLine("=== как называется причина (A129) ===");
            Console.WriteLine("каталог сборки: {0}", AppDomain.CurrentDomain.BaseDirectory);
            Console.WriteLine();

            Bare();
            Quoted();
            NotQuoted();
            HalfQuoted();
            WithoutClass();
            EmptyInner();
            Speechless();
            Looped();
            TooDeep();
            Branched();
            SingleBranch();
            NestedBranch();
            BranchedLoop();
            TooDeepTree();
            Reported();
            if (real)
            {
                Real();
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------
        // Заготовки: та самая тройка, которая и дала дефект. Внутреннее —
        // отказ загрузки сборки поставщика SQLite, среднее — отказ его
        // инициализатора типа, внешнее собирает `MaterialDatabase.Refuse`.
        // ------------------------------------------------------------------

        const string InnerText =
            "Не удалось загрузить файл или сборку \"SQLitePCLRaw.core, Version=2.0.6.1341, "
            + "Culture=neutral, PublicKeyToken=1488e028ca7ab535\" или один из зависимых от них "
            + "компонентов. Определение манифеста не соответствует ссылке на сборку.";

        static Exception Innermost()
        {
            return new FileLoadException(InnerText);
        }

        static Exception Middle()
        {
            return new TypeInitializationException("Microsoft.Data.Sqlite.SqliteConnection", Innermost());
        }

        /// <summary>Хвост, который дверь собирается приписать: «Тип: сообщение» самого внутреннего.</summary>
        static string Tail(Exception ex)
        {
            Exception inner = ex;
            while (inner.InnerException != null)
            {
                inner = inner.InnerException;
            }

            return inner.GetType().Name + ": " + inner.Message;
        }

        /// <summary>Звено так, как его называет дверь: «Тип: сообщение».</summary>
        static string Link(Exception ex)
        {
            return ex.GetType().Name + ": " + ex.Message;
        }

        /// <summary>
        /// Предел звеньев обхода — то же число, что <c>AppUi.ReasonChainLimit</c>
        /// (`A144`). Держится здесь СВОИМ, а не берётся из приложения нарочно:
        /// проба обязана собираться и против СТАРОЙ сборки, где такого имени ещё
        /// нет. Разошлись — плечо <c>предел</c> отказывает и называет расхождение.
        /// </summary>
        const int ChainLimit = 16;

        // ------------------------------------------------------------------

        /// <summary>Голое исключение: вкладывать нечего, хвоста нет и быть не должно.</summary>
        static void Bare()
        {
            Exception ex = new InvalidOperationException("нет калибровки у спектра");
            string said = AppUi.Reason(ex);
            Print("голое", said);
            Say("голое", "хвоста нет", Count(said, " <- ") == 0);
            Say("голое", "тип и сообщение названы",
                said == "InvalidOperationException: нет калибровки у спектра");
        }

        /// <summary>
        /// ДЕФЕКТ `A129`: внешнее уже содержит ровно тот хвост, который дверь
        /// собирается приписать. Текст внешнего собран ТАК ЖЕ, как его собирает
        /// <c>MaterialDatabase.Refuse</c> — своими словами плюс
        /// <c>AppUi.Reason(ex)</c>, и то же <c>ex</c> уходит внутренним.
        /// </summary>
        static void Quoted()
        {
            Exception middle = Middle();
            Exception outer = new InvalidOperationException(
                "matdb.sqlite: подъём таблиц вещества — отказ. Файл: C:\\probes\\matdb.sqlite. "
                + AppUi.Reason(middle), middle);

            string said = AppUi.Reason(outer);
            string tail = Tail(outer);
            int n = Count(said, tail);
            Print("цитата", said);
            Console.WriteLine("    длина {0} знаков, причина стоит {1} раз(а)", said.Length, n);
            Say("цитата", "причина названа", n >= 1);
            Say("цитата", "и названа ОДИН раз", n == 1);
        }

        /// <summary>
        /// ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ. Внешнее о внутреннем НЕ говорит ничего —
        /// хвост обязан остаться. Правка, срезающая его всегда, отняла бы
        /// причину, и мерит это плечо именно её.
        /// </summary>
        static void NotQuoted()
        {
            Exception middle = Middle();
            Exception outer = new InvalidOperationException(
                "matdb.sqlite: подъём таблиц вещества — отказ. Файл: C:\\probes\\matdb.sqlite.", middle);

            string said = AppUi.Reason(outer);
            string tail = Tail(outer);
            int n = Count(said, tail);
            Print("без цитаты", said);
            Console.WriteLine("    длина {0} знаков, причина стоит {1} раз(а)", said.Length, n);
            Say("без цитаты", "ХВОСТ ОСТАЛСЯ", Count(said, " <- ") >= 1);
            Say("без цитаты", "причина названа ровно раз", n == 1);
            Say("без цитаты", "внешнее на месте",
                said.IndexOf("matdb.sqlite: подъём таблиц вещества", StringComparison.Ordinal) >= 0);
            // `A144`: среднее звено здесь никем не названо — обязано появиться.
            Say("без цитаты", "СРЕДНЕЕ звено названо",
                Count(said, Link(middle)) == 1);
            Say("без цитаты", "звеньев ровно два", Count(said, " <- ") == 2);
        }

        /// <summary>
        /// Внешнее цитирует внутреннее НЕПОЛНО — половиной сообщения. Признак
        /// «уже названо» обязан быть точным, и здесь он не срабатывает: причина
        /// остаётся названной ЦЕЛИКОМ, пусть и ценой частичного повтора.
        /// </summary>
        static void HalfQuoted()
        {
            Exception middle = Middle();
            string half = InnerText.Substring(0, InnerText.Length / 2);
            Exception outer = new InvalidOperationException(
                "matdb.sqlite: подъём таблиц вещества — отказ. " + half, middle);

            string said = AppUi.Reason(outer);
            Print("половина", said);
            Console.WriteLine("    длина {0} знаков", said.Length);
            Say("половина", "ПОЛНОЕ сообщение причины на месте",
                said.IndexOf(InnerText, StringComparison.Ordinal) >= 0);
            Say("половина", "хвост приписан", Count(said, " <- ") >= 1);
            Say("половина", "СРЕДНЕЕ звено названо", Count(said, Link(middle)) == 1);
            Say("половина", "звеньев ровно два", Count(said, " <- ") == 2);
        }

        /// <summary>
        /// Внешнее приводит сообщение внутреннего ЦЕЛИКОМ, но без имени класса.
        /// Класс причины — то, по чему её узнаёт `RefusalWordsProbe`, и терять
        /// его нельзя: хвост приписывается.
        /// </summary>
        static void WithoutClass()
        {
            Exception middle = Middle();
            Exception outer = new InvalidOperationException(
                "matdb.sqlite: подъём таблиц вещества — отказ. " + InnerText, middle);

            string said = AppUi.Reason(outer);
            Print("без класса", said);
            Console.WriteLine("    длина {0} знаков", said.Length);
            Say("без класса", "класс причины назван",
                said.IndexOf("FileLoadException", StringComparison.Ordinal) >= 0);
            Say("без класса", "хвост приписан", Count(said, " <- ") >= 1);
            Say("без класса", "СРЕДНЕЕ звено названо", Count(said, Link(middle)) == 1);
            Say("без класса", "звеньев ровно два", Count(said, " <- ") == 2);
        }

        /// <summary>
        /// У внутреннего пустое сообщение: «уже названо» не должно стать
        /// истиной само собой (пустая строка входит в любой текст), иначе
        /// пропал бы класс причины — единственное, что у такого отказа есть.
        /// </summary>
        static void EmptyInner()
        {
            Exception inner = new ObjectDisposedException("", "");
            Exception outer = new InvalidOperationException("соединение с базой уже закрыто", inner);

            string said = AppUi.Reason(outer);
            Print("пустое внутри", said);
            Say("пустое внутри", "класс причины назван",
                said.IndexOf("ObjectDisposedException", StringComparison.Ordinal) >= 0);
        }

        /// <summary>
        /// ⛔ ГЛАВНОЕ ПЛЕЧО `A144`: ОБЁРТКА БЕЗ СВОИХ СЛОВ. Сообщение
        /// <c>TargetInvocationException</c> — платформенное, о причине оно не
        /// говорит НИЧЕГО, и позаботиться о среднем звене здесь некому. Прежняя
        /// дверь брала только самое внутреннее, и <c>TypeInitializationException</c>
        /// пропадал целиком: у читателя оставалось «вызов бросил» плюс «сборка не
        /// загрузилась», а звена «инициализатор типа поставщика SQLite» — не
        /// было. На сцене `F_a25_noconf` его видно лишь потому, что
        /// <c>MaterialDatabase.Refuse</c> положил его в текст своей рукой.
        /// </summary>
        static void Speechless()
        {
            Exception middle = Middle();
            Exception outer = new TargetInvocationException(middle);

            string said = AppUi.Reason(outer);
            Print("без своих слов", said);
            Console.WriteLine("    длина {0} знаков, звеньев названо {1}",
                              said.Length, Count(said, " <- ") + 1);
            Say("без своих слов", "внешнее названо",
                said.IndexOf("TargetInvocationException", StringComparison.Ordinal) == 0);
            Say("без своих слов", "СРЕДНЕЕ звено названо", Count(said, Link(middle)) == 1);
            Say("без своих слов", "причина названа", Count(said, Tail(outer)) == 1);
            Say("без своих слов", "звеньев ровно три", Count(said, " <- ") == 2);
            Say("без своих слов", "сторож обхода не сработал",
                said.IndexOf(" <- …", StringComparison.Ordinal) < 0);
        }

        /// <summary>
        /// СТОРОЖ ПЕТЛИ. Штатным путём петлю во <c>InnerException</c> не собрать
        /// (задаётся конструктором), но полем <c>_innerException</c> её кладут и
        /// отражение, и десериализация. ⚠ Сторож обязан ГОВОРИТЬ О СЕБЕ:
        /// молчаливая потеря звена и есть чинимый дефект.
        ///
        /// ⛔ ЗОВЁТСЯ СО СРОКОМ, И ЭТО НЕ ПЕРЕСТРАХОВКА, А ИЗМЕРЕНИЕ. Дверь ДО
        /// правки `A144` искала самое внутреннее циклом <c>while
        /// (inner.InnerException != null)</c> — на петле он вечный. Измерено
        /// 05.09.2026 на старой сборке: <c>ReasonProbe.exe</c> сжёг 616 секунд
        /// процессора и был убит по сроку. Прямой вызов повесил бы саму пробу, и
        /// «до» замерить стало бы нечем; поток фоновый, поэтому невернувшийся
        /// обход выходу процесса не мешает.
        /// </summary>
        static void Looped()
        {
            FieldInfo slot = typeof(Exception).GetField("_innerException",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (slot == null)
            {
                Console.WriteLine("ПЛЕЧО петля");
                Console.WriteLine("  ⚠ поля _innerException нет — петлю не собрать, плечо НЕ МЕРИТ");
                bad++;
                return;
            }

            Exception first = new InvalidOperationException("первое звено петли");
            Exception second = new InvalidOperationException("второе звено петли", first);
            slot.SetValue(first, second);   // first -> second -> first

            string said = null;
            Thread walk = new Thread(delegate() { said = AppUi.Reason(second); });
            walk.IsBackground = true;
            walk.Start();
            bool done = walk.Join(TimeSpan.FromSeconds(10.0));

            Console.WriteLine("ПЛЕЧО петля");
            if (!done)
            {
                Console.WriteLine("  ⛔ ОБХОД НЕ ВЕРНУЛСЯ за 10 с — сторожа петли НЕТ");
                bad++;
                return;
            }

            Console.WriteLine("  сказано: {0}", OneLine(said));
            Say("петля", "оба звена названы",
                Count(said, "второе звено петли") == 1 && Count(said, "первое звено петли") == 1);
            Say("петля", "сторож СКАЗАЛ О СЕБЕ",
                said.EndsWith(" <- …", StringComparison.Ordinal));
        }

        /// <summary>
        /// СТОРОЖ ПРЕДЕЛА. Цепочка заведомо длиннее предела; у каждого звена своё
        /// сообщение, иначе их сняло бы правило «уже названо». Предел — сторож от
        /// испорченной цепочки, а не обрезка по длине: резать текст под ширину
        /// строки качества запрещено решением Amber 05.09.2026.
        /// </summary>
        static void TooDeep()
        {
            Exception link = new InvalidOperationException("звено 0");
            for (int i = 1; i <= ChainLimit + 4; i++)
            {
                link = new InvalidOperationException("звено " + i, link);
            }

            string said = AppUi.Reason(link);
            Console.WriteLine("ПЛЕЧО предел");
            Console.WriteLine("  цепочка {0} звеньев, названо {1}, предел пробы {2}",
                              ChainLimit + 5, Count(said, " <- "), ChainLimit);
            Console.WriteLine("  сказано: {0}", OneLine(said));
            Say("предел", "сторож СКАЗАЛ О СЕБЕ",
                said.EndsWith(" <- …", StringComparison.Ordinal));
            Say("предел", "названо ровно предел звеньев",
                Count(said, " <- ") == ChainLimit);
            Say("предел", "самое внешнее названо первым",
                said.IndexOf("звено " + (ChainLimit + 4), StringComparison.Ordinal) >= 0);
        }

        /// <summary>
        /// ⛔ ГЛАВНОЕ ПЛЕЧО `A165`: ВЕТВЛЕНИЕ. <c>AggregateException</c> держит
        /// НЕСКОЛЬКО внутренних, а <c>InnerException</c> отдаёт только первую.
        /// До `A165` обход шёл по ней, и соседние ветви терялись молча — это
        /// плечо было справочным и вердикта не выносило. Теперь три РАЗНЫХ ветви
        /// обязаны быть названы все три; у каждой своя пометка «(i/3)», и
        /// цепочка ПОД ветвью (вторая ветвь несёт своё вложенное) обязана
        /// назваться тем же «&lt;-». На старой сборке плечо ОТКАЗЫВАЕТ — это
        /// его положительный контроль.
        /// </summary>
        static void Branched()
        {
            Exception one = new InvalidOperationException("ветвь ПЕРВАЯ: нет калибровки");
            Exception twoInner = new FileLoadException("под второй ветвью: сборка не загрузилась");
            Exception two = new FileNotFoundException("ветвь ВТОРАЯ: нет файла спектра", twoInner);
            Exception three = new ArgumentException("ветвь ТРЕТЬЯ: пустой спектр");
            Exception outer = new AggregateException("три беды разом", one, two, three);

            string said = AppUi.Reason(outer);
            Print("ветвление", said);
            Console.WriteLine("    длина {0} знаков, приписок {1}", said.Length, Count(said, " <- "));
            Say("ветвление", "первая ветвь названа", Count(said, "ветвь ПЕРВАЯ") >= 1);
            Say("ветвление", "ВТОРАЯ ветвь названа", Count(said, "ветвь ВТОРАЯ") >= 1);
            Say("ветвление", "ТРЕТЬЯ ветвь названа", Count(said, "ветвь ТРЕТЬЯ") >= 1);
            Say("ветвление", "звено ПОД второй ветвью названо",
                Count(said, Link(twoInner)) == 1);
            Say("ветвление", "ветви помечены (1/3)…(3/3)",
                Count(said, " <- (1/3) ") == 1 && Count(said, " <- (2/3) ") == 1
                && Count(said, " <- (3/3) ") == 1);
            Say("ветвление", "порядок ветвей сохранён",
                said.IndexOf("ветвь ПЕРВАЯ", StringComparison.Ordinal)
                    < said.IndexOf("ветвь ВТОРАЯ", StringComparison.Ordinal)
                && said.IndexOf(Link(twoInner), StringComparison.Ordinal)
                    < said.IndexOf("ветвь ТРЕТЬЯ", StringComparison.Ordinal));
            Say("ветвление", "приписок ровно четыре", Count(said, " <- ") == 4);
            Say("ветвление", "сторож обхода не сработал",
                said.IndexOf(" <- …", StringComparison.Ordinal) < 0);
        }

        /// <summary>
        /// ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ФОРМАТА (`A165`). <c>AggregateException</c>
        /// с ОДНОЙ ветвью — то, что даёт <c>Task.Run</c> с одним упавшим
        /// делегатом, самый частый живой случай. Текст обязан быть БАЙТ В БАЙТ
        /// прежним: без пометки ветви, ровно «Тип: сообщение &lt;- Тип:
        /// сообщение &lt;- …». На этом держатся приписки `A129` и длины меток
        /// редактора нуклидов: ветвление не должно раздувать текст там, где
        /// веток нет.
        /// </summary>
        static void SingleBranch()
        {
            Exception middle = Middle();
            Exception outer = new AggregateException("одна беда", middle);
            string want = Link(outer) + " <- " + Link(middle) + " <- " + Tail(outer);

            string said = AppUi.Reason(outer);
            Print("одна ветвь", said);
            Console.WriteLine("    длина {0} знаков, ожидалось {1}", said.Length, want.Length);
            Say("одна ветвь", "текст БАЙТ В БАЙТ прежний", said == want);
            Say("одна ветвь", "пометки ветви нет",
                said.IndexOf("(1/1)", StringComparison.Ordinal) < 0);
        }

        /// <summary>
        /// ВЛОЖЕННОЕ ВЕТВЛЕНИЕ — то, что даёт <c>Task.WhenAll</c> поверх упавших
        /// задач: <c>AggregateException</c> внутри <c>AggregateException</c>.
        /// Дверь <c>Flatten()</c> не зовёт нарочно (сторож петли по ссылке,
        /// сообщения вложенных), а обходит вложенное как обычное звено с
        /// несколькими вложенными: все листья обязаны быть названы.
        /// </summary>
        static void NestedBranch()
        {
            Exception a = new InvalidOperationException("лист А");
            Exception b = new InvalidOperationException("лист Б");
            Exception c = new InvalidOperationException("лист В");
            Exception inner = new AggregateException("вложенный узел", a, b);
            Exception outer = new AggregateException("внешний узел", inner, c);

            string said = AppUi.Reason(outer);
            Print("вложенное ветвление", said);
            Say("вложенное ветвление", "все три листа названы",
                Count(said, "лист А") == 1 && Count(said, "лист Б") == 1 && Count(said, "лист В") == 1);
            Say("вложенное ветвление", "вложенный узел назван",
                Count(said, Link(inner)) == 1);
            Say("вложенное ветвление", "сторож обхода не сработал",
                said.IndexOf(" <- …", StringComparison.Ordinal) < 0);
        }

        /// <summary>
        /// ⛔ ПЕТЛЯ ЧЕРЕЗ ВЕТВЛЕНИЕ, СО СРОКОМ (`A165`). Ветвление — новый способ
        /// собрать петлю: ветвь ссылается на предка (вторая — на сам
        /// <c>AggregateException</c>) или на соседа (первая — на вторую).
        /// Собирается тем же полем <c>_innerException</c>, что и плечо
        /// <c>петля</c>. Сторож обязан пережить раскрытие ветвей: обход
        /// возвращается за секунду, говорит о себе «&lt;- …», а ветвь, НЕ
        /// виноватая в чужой петле, остаётся названной. Срок — измерение, а не
        /// перестраховка: до `A144` дверь на петле висела насмерть (616 с ЦП).
        ///
        /// ⛔ ЗНАК СТОРОЖА ОДИН (`A185`, решение Amber 05.09.2026). Здесь две
        /// ветви упираются в одно уже названное звено: первая — через свою
        /// цепочку (сосед → предок), вторая — сама (она уже названа под
        /// первой). Каждый под-обход говорил о себе, и дверь печатала
        /// «&lt;- … &lt;- …» — честно, но читается как сбой печати. Подряд
        /// идущие знаки схлопываются в один; текст сверяется БАЙТ В БАЙТ с
        /// эталоном, расхождение печатается обеими строками. На сборке до
        /// `A185` плечо ОТКАЗЫВАЕТ — это его положительный контроль (измерено
        /// 05.09.2026: старая печать против нового эталона — код 1).
        ///
        /// ⛔ ВСЕ ТРИ ПОМЕТКИ ВИДНЫ (`A219`, решение Amber 05.09.2026). Вторая
        /// ветвь узла уже названа под первой (без пометки — там она вложенное
        /// соседа), и второй раз дверь её не называет (`A129`); до `A219`
        /// текст нёс «(1/3)» и «(3/3)», и читатель вправе был решить, что
        /// вторую потеряли. Теперь у уже названного звена стоит своя пометка
        /// с отсылкой выше — «&lt;- (2/3) = выше». Проверки: каждая из трёх
        /// пометок ровно по разу, у второй — форма «= выше», текст байт в
        /// байт. На сборке до `A219` плечо ОТКАЗЫВАЕТ: пометки «(2/3)» нет
        /// (измерено 05.09.2026: старая печать против нового эталона — код 1).
        /// </summary>
        static void BranchedLoop()
        {
            FieldInfo slot = typeof(Exception).GetField("_innerException",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (slot == null)
            {
                Console.WriteLine("ПЛЕЧО петля через ветвь");
                Console.WriteLine("  ⚠ поля _innerException нет — петлю не собрать, плечо НЕ МЕРИТ");
                bad++;
                return;
            }

            Exception one = new InvalidOperationException("ветвь-сосед");
            Exception two = new InvalidOperationException("ветвь-к-предку");
            Exception three = new InvalidOperationException("ветвь чистая");
            Exception outer = new AggregateException("узел с петлёй", one, two, three);
            slot.SetValue(one, two);     // первая ветвь -> вторая (сосед)
            slot.SetValue(two, outer);   // вторая ветвь -> узел (предок)

            string said = null;
            Thread walk = new Thread(delegate() { said = AppUi.Reason(outer); });
            walk.IsBackground = true;
            Stopwatch clock = Stopwatch.StartNew();
            walk.Start();
            bool done = walk.Join(TimeSpan.FromSeconds(1.0));
            clock.Stop();

            Console.WriteLine("ПЛЕЧО петля через ветвь");
            if (!done)
            {
                Console.WriteLine("  ⛔ ОБХОД НЕ ВЕРНУЛСЯ за 1 с — сторож петли через ветвь НЕ РАБОТАЕТ");
                bad++;
                return;
            }

            // Эталон `A185` + `A219`: первая ветвь с пометкой, под ней сосед
            // БЕЗ пометки (он вложенное первой, а не ветвь узла), знак сторожа
            // за петлю сосед → предок, затем вторая ветвь узла — уже названа,
            // потому не повторяется, а помечается «(2/3) = выше», — и третья
            // ветвь с пометкой.
            string want = Link(outer)
                + " <- (1/3) " + Link(one)
                + " <- " + Link(two)
                + " <- …"
                + " <- (2/3) = выше"
                + " <- (3/3) " + Link(three);

            Console.WriteLine("  вернулся за {0} мс", clock.ElapsedMilliseconds);
            Console.WriteLine("  сказано: {0}", OneLine(said));
            Say("петля через ветвь", "все три ветви названы по разу",
                Count(said, "ветвь-сосед") == 1 && Count(said, "ветвь-к-предку") == 1
                && Count(said, "ветвь чистая") == 1);
            Say("петля через ветвь", "сторож СКАЗАЛ О СЕБЕ",
                Count(said, " <- …") >= 1);
            Say("петля через ветвь", "чистая ветвь названа ПОСЛЕ сторожа",
                said.IndexOf(" <- …", StringComparison.Ordinal)
                    < said.IndexOf("ветвь чистая", StringComparison.Ordinal));
            Say("петля через ветвь", "знак сторожа ОДИН, не «<- … <- …»",
                Count(said, " <- … <- …") == 0);
            Say("петля через ветвь", "пометки (1/3), (2/3), (3/3) по разу",
                Count(said, "(1/3)") == 1 && Count(said, "(2/3)") == 1
                && Count(said, "(3/3)") == 1);
            Say("петля через ветвь", "у названной ветви «(2/3) = выше»",
                Count(said, " <- (2/3) = выше") == 1);
            Say("петля через ветвь", "текст БАЙТ В БАЙТ эталонный", said == want);
            if (said != want)
            {
                Console.WriteLine("    ожидалось: {0}", OneLine(want));
                Console.WriteLine("    длина {0} знаков, ожидалось {1}", said.Length, want.Length);
            }
        }

        /// <summary>
        /// ПРЕДЕЛ ПО ДЕРЕВУ (`A165`). Предел <c>ReasonChainLimit</c> — ОДИН на
        /// всё дерево, а не на ветвь. Два дерева, оба байт в байт:
        ///   * ПРЕДЕЛ НА ВЕТВИ (`A230`): плечи 8, 7 и 8 звеньев — корень и две
        ///     ветви дают ровно предел, третья ветвь и есть звено за пределом.
        ///     Сторож обязан назвать её номером: «&lt;- (3/3) …» (решение Amber
        ///     05.09.2026), а не голым знаком — иначе читатель видел «(1/3)»,
        ///     «(2/3)» и знак и не мог сказать, дошёл ли обход до третьей ветви;
        ///   * ПРЕДЕЛ ВНУТРИ ВЕТВИ (`A165`, положительный контроль формата):
        ///     три ветви по восемь звеньев — предел встаёт на последнем звене
        ///     ВТОРОЙ ветви (корень + 8 + 7 = 16), знак голый «&lt;- …», как и
        ///     был, третья ветвь не называется вовсе.
        /// ⚠ До `A230` строка реестра считала, что на дереве 8+8+8 предел
        /// упирается в третью ветвь; замер показал звено внутри второй, поэтому
        /// дерево под решение построено отдельно.
        /// </summary>
        static void TooDeepTree()
        {
            string wantOnBranch =
                "AggregateException: широкое дерево"
                + " <- (1/3) InvalidOperationException: ветвь 0 звено 7"
                + " <- InvalidOperationException: ветвь 0 звено 6"
                + " <- InvalidOperationException: ветвь 0 звено 5"
                + " <- InvalidOperationException: ветвь 0 звено 4"
                + " <- InvalidOperationException: ветвь 0 звено 3"
                + " <- InvalidOperationException: ветвь 0 звено 2"
                + " <- InvalidOperationException: ветвь 0 звено 1"
                + " <- InvalidOperationException: ветвь 0 звено 0"
                + " <- (2/3) InvalidOperationException: ветвь 1 звено 6"
                + " <- InvalidOperationException: ветвь 1 звено 5"
                + " <- InvalidOperationException: ветвь 1 звено 4"
                + " <- InvalidOperationException: ветвь 1 звено 3"
                + " <- InvalidOperationException: ветвь 1 звено 2"
                + " <- InvalidOperationException: ветвь 1 звено 1"
                + " <- InvalidOperationException: ветвь 1 звено 0"
                + " <- (3/3) …";

            string wantInside =
                "AggregateException: широкое дерево"
                + " <- (1/3) InvalidOperationException: ветвь 0 звено 7"
                + " <- InvalidOperationException: ветвь 0 звено 6"
                + " <- InvalidOperationException: ветвь 0 звено 5"
                + " <- InvalidOperationException: ветвь 0 звено 4"
                + " <- InvalidOperationException: ветвь 0 звено 3"
                + " <- InvalidOperationException: ветвь 0 звено 2"
                + " <- InvalidOperationException: ветвь 0 звено 1"
                + " <- InvalidOperationException: ветвь 0 звено 0"
                + " <- (2/3) InvalidOperationException: ветвь 1 звено 7"
                + " <- InvalidOperationException: ветвь 1 звено 6"
                + " <- InvalidOperationException: ветвь 1 звено 5"
                + " <- InvalidOperationException: ветвь 1 звено 4"
                + " <- InvalidOperationException: ветвь 1 звено 3"
                + " <- InvalidOperationException: ветвь 1 звено 2"
                + " <- InvalidOperationException: ветвь 1 звено 1"
                + " <- …";

            string onBranch = AppUi.Reason(WideTree(8, 7, 8));
            string inside = AppUi.Reason(WideTree(8, 8, 8));
            Console.WriteLine("ПЛЕЧО предел по дереву");
            Console.WriteLine("  предел на ветви: дерево {0} звеньев, приписок {1}, предел пробы {2}",
                              1 + 8 + 7 + 8, Count(onBranch, " <- "), ChainLimit);
            Console.WriteLine("  сказано: {0}", OneLine(onBranch));
            Say("предел по дереву", "предел на ветви: знак с номером «(3/3) …»",
                onBranch.EndsWith(" <- (3/3) …", StringComparison.Ordinal));
            Say("предел по дереву", "предел на ветви: пометки (1/3), (2/3), (3/3) по разу",
                Count(onBranch, "(1/3) ") == 1 && Count(onBranch, "(2/3) ") == 1
                && Count(onBranch, "(3/3) ") == 1);
            Say("предел по дереву", "предел на ветви: названо ровно предел звеньев",
                Count(onBranch, " <- ") == ChainLimit);
            Say("предел по дереву", "предел на ветви: третья ветвь за пределом не названа",
                Count(onBranch, "ветвь 2 ") == 0);
            Say("предел по дереву", "предел на ветви: текст БАЙТ В БАЙТ эталонный",
                string.Equals(onBranch, wantOnBranch, StringComparison.Ordinal));
            if (!string.Equals(onBranch, wantOnBranch, StringComparison.Ordinal))
            {
                Console.WriteLine("    ожидалось: {0}", OneLine(wantOnBranch));
                Console.WriteLine("    длина {0} знаков, ожидалось {1}", onBranch.Length, wantOnBranch.Length);
            }

            Console.WriteLine("  предел внутри ветви: дерево {0} звеньев, приписок {1}, предел пробы {2}",
                              1 + 3 * 8, Count(inside, " <- "), ChainLimit);
            Console.WriteLine("  сказано: {0}", OneLine(inside));
            Say("предел по дереву", "предел внутри ветви: сторож СКАЗАЛ О СЕБЕ",
                inside.EndsWith(" <- …", StringComparison.Ordinal));
            Say("предел по дереву", "предел внутри ветви: знак голый, без номера",
                Count(inside, "(3/3)") == 0);
            Say("предел по дереву", "предел внутри ветви: названо ровно предел звеньев",
                Count(inside, " <- ") == ChainLimit);
            Say("предел по дереву", "предел внутри ветви: первая ветвь названа целиком",
                Count(inside, "ветвь 0 звено 0") == 1);
            Say("предел по дереву", "предел внутри ветви: третья ветвь за пределом не названа",
                Count(inside, "ветвь 2 ") == 0);
            Say("предел по дереву", "предел внутри ветви: текст БАЙТ В БАЙТ эталонный",
                string.Equals(inside, wantInside, StringComparison.Ordinal));
            if (!string.Equals(inside, wantInside, StringComparison.Ordinal))
            {
                Console.WriteLine("    ожидалось: {0}", OneLine(wantInside));
                Console.WriteLine("    длина {0} знаков, ожидалось {1}", inside.Length, wantInside.Length);
            }
        }

        /// <summary>
        /// Узел <c>AggregateException</c> с ветвями заданной длины; звенья
        /// ветви b считаются от вершины: «ветвь b звено (len−1)» … «звено 0».
        /// </summary>
        static AggregateException WideTree(params int[] arms)
        {
            Exception[] tips = new Exception[arms.Length];
            for (int b = 0; b < arms.Length; b++)
            {
                Exception link = new InvalidOperationException("ветвь " + b + " звено 0");
                for (int i = 1; i < arms[b]; i++)
                {
                    link = new InvalidOperationException("ветвь " + b + " звено " + i, link);
                }
                tips[b] = link;
            }
            return new AggregateException("широкое дерево", tips);
        }

        /// <summary>
        /// ДВЕРЬ <c>AppUi.Report</c> БЕЗ ЗАГОЛОВКА (`A174`). Проба безоконна
        /// (входная сборка — не приложение), поэтому дверь пишет в поток
        /// ошибок; поток на время плеча подменяется и читается обратно.
        /// Три стороны: пустой заголовок — разделителя нет; непустой —
        /// строка прежняя, байт в байт (положительный контроль формата: правка
        /// не смеет тронуть то, что уже читают перехваты других проб);
        /// многострочный текст — в одну строку, как и было.
        /// </summary>
        static void Reported()
        {
            const string Text = "в файле N42 не прочитано время начала набора";
            const string Caption = "Ввоз N42";
            string nl = Environment.NewLine;

            string bare = CaptureError(() => AppUi.Report(Text, "", MessageBoxIcon.None));
            string headed = CaptureError(() => AppUi.Report(Text, Caption, MessageBoxIcon.Warning));
            string folded = CaptureError(() => AppUi.Report("первая\r\nвторая", "", MessageBoxIcon.None));

            Console.WriteLine("ПЛЕЧО заголовок");
            Console.WriteLine("  без заголовка: {0}", OneLine(bare));
            Console.WriteLine("  с заголовком : {0}", OneLine(headed));
            Console.WriteLine("  две строки   : {0}", OneLine(folded));
            Say("заголовок", "без заголовка — «BecqMoni: текст»",
                bare == "BecqMoni: " + Text + nl);
            Say("заголовок", "с заголовком — прежняя строка",
                headed == "BecqMoni: " + Caption + ": " + Text + nl);
            Say("заголовок", "две строки сложены в одну",
                folded == "BecqMoni: первая вторая" + nl);
        }

        /// <summary>Поток ошибок на время одного вызова — строкой.</summary>
        static string CaptureError(Action act)
        {
            TextWriter saved = Console.Error;
            StringWriter caught = new StringWriter();
            try
            {
                Console.SetError(caught);
                act();
            }
            finally
            {
                Console.SetError(saved);
            }
            return caught.ToString();
        }

        /// <summary>
        /// НАСТОЯЩИЙ отказ базы веществ — то, на чём дефект и был измерен.
        /// Имеет смысл только там, где база не поднимается.
        /// </summary>
        static void Real()
        {
            Console.WriteLine("ПЛЕЧО real — настоящий отказ MaterialDatabase");
            Exception caught = null;
            try
            {
                MaterialDatabase.FluorescenceOf(55);
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            if (caught == null)
            {
                Console.WriteLine("  ⚠ ОТКАЗА НЕ БЫЛО — база поднялась, плечо ничего не мерит");
                Console.WriteLine();
                bad++;
                return;
            }

            string message = OneLine(caught.Message);
            string said = AppUi.Reason(caught);
            string tail = Tail(caught);
            Console.WriteLine("  брошено : {0}", caught.GetType().Name);
            Console.WriteLine("  причина : {0}", Tail(caught).Split(':')[0]);
            Console.WriteLine("  сообщение внешнего: {0} знаков, причина в нём {1} раз(а)",
                              message.Length, Count(message, OneLine(tail)));
            Console.WriteLine("  сказано дверью    : {0} знаков, причина в нём {1} раз(а)",
                              OneLine(said).Length, Count(OneLine(said), OneLine(tail)));
            Console.WriteLine("  сказано: {0}", OneLine(said));
            Say("real", "причина названа", Count(OneLine(said), OneLine(tail)) >= 1);
            Say("real", "и названа ОДИН раз", Count(OneLine(said), OneLine(tail)) == 1);
        }

        // ------------------------------------------------------------------

        static void Print(string arm, string said)
        {
            Console.WriteLine("ПЛЕЧО {0}", arm);
            Console.WriteLine("  сказано: {0}", OneLine(said));
        }

        static void Say(string arm, string what, bool ok)
        {
            Console.WriteLine("  {0,-34} {1}", what, ok ? "ДА" : "НЕТ");
            if (!ok)
            {
                bad++;
            }
        }

        static int Count(string text, string needle)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(needle))
            {
                return 0;
            }

            int n = 0;
            int at = text.IndexOf(needle, StringComparison.Ordinal);
            while (at >= 0)
            {
                n++;
                at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
            }

            return n;
        }

        static string OneLine(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            return text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Trim();
        }
    }
}
