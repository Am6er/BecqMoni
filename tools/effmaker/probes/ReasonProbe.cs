using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

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
    ///   * <c>ветвление</c> — <c>AggregateException</c>. Плечо СПРАВОЧНОЕ:
    ///     обход идёт по <c>InnerException</c>, а он отдаёт лишь первую ветвь.
    ///
    /// ⚠ Ожидание РАЗНОЕ у двух сборок, и это не изъян пробы, а её смысл: на
    /// старой сборке плечо <c>цитата</c> обязано дать «ДВАЖДЫ» и код 1, на
    /// новой — «ОДИН РАЗ» и код 0; после `A144` на старой сборке отказывают ещё
    /// и плечи цепочки. Прогон без старой сборки мерит пустоту.
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
        /// СПРАВОЧНОЕ ПЛЕЧО, вердикта не выносит по соседним ветвям.
        /// <c>AggregateException</c> держит НЕСКОЛЬКО внутренних, а
        /// <c>InnerException</c> отдаёт только первую: обход идёт по ней, соседние
        /// ветви не называются. Для полосы `A144` это находка, а не правка —
        /// раскрытие ветвления было бы вторым правилом обхода.
        /// </summary>
        static void Branched()
        {
            Exception one = new InvalidOperationException("ветвь ПЕРВАЯ: нет калибровки");
            Exception two = new FileNotFoundException("ветвь ВТОРАЯ: нет файла спектра");
            Exception outer = new AggregateException("две беды разом", one, two);

            string said = AppUi.Reason(outer);
            Print("ветвление", said);
            Say("ветвление", "первая ветвь названа", Count(said, "ветвь ПЕРВАЯ") >= 1);
            bool second = Count(said, "ветвь ВТОРАЯ") >= 1;
            Console.WriteLine("  {0,-34} {1}", "вторая ветвь названа",
                              second ? "ДА" : "НЕТ — находка, вердикта нет");
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
