using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

/// <summary>
/// S47: лежат ли пики вылета УЖЕ ВНУТРИ матрицы отклика — и, значит, не считает
/// ли разбор их дважды, когда рядом стоит свободный столбец `SE-2614`/`DE-2614`.
///
/// Зачем проба. Вопрос строки S47 — «не привязать ли амплитуды `SE`/`DE` к
/// амплитуде родителя вместо свободной колонки». Ответ на него нельзя дать
/// рассуждением: он зависит от того, что именно кладёт в образ матрица, а это
/// свойство МОДЕЛИ, а не намерения. Поэтому здесь измеряется прямо: отклик
/// линии 2614.5 кэВ раскладывается по каналам исхода, и в нём ищутся пики на
/// 2103.5 (одиночный вылет, −511) и 1592.5 (двойной, −1022).
///
/// Что означает исход:
///   * пики есть  -> образ Tl-208, построенный через матрицу, УЖЕ несёт свои
///                   вылеты в физически верной доле; свободный столбец рядом —
///                   второй счёт того же, и взять он может только чужое;
///   * пиков нет  -> без матрицы образ строится из одних пиков, вылетов в нём
///                   нет вовсе, и свободный столбец — единственный способ их
///                   выразить; там его трогать нельзя.
///
/// Печатает отношение площадей вылет/пик — ту самую величину, которой связка
/// амплитуд и потребует, если её делать.
///
///   escapegateprobe [--matrix=<файл.rmx>] [--all]
///
/// Без ключей берёт матрицы из `config\device\response` текущего каталога —
/// запускать из рабочего каталога корпуса (`tools\CORPUS\scripts\wd_app`).
/// </summary>
static class EscapeGateProbe
{
    const double Parent = 2614.511;      // Tl-208, полное поглощение
    const double Single = 2103.5;        // −511
    const double Double = 1592.5;        // −1022

    static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

        string only = null;
        bool all = false;
        foreach (string a in args)
        {
            if (a.StartsWith("--matrix=", StringComparison.Ordinal)) only = a.Substring(9);
            else if (a == "--all") all = true;
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }

        List<string> files = new List<string>();
        if (only != null)
        {
            files.Add(only);
        }
        else
        {
            string dir = Path.Combine("config", "device", "response");
            if (!Directory.Exists(dir))
            {
                Console.WriteLine("ОТКАЗ: нет " + Path.GetFullPath(dir) + ".");
                Console.WriteLine("       Пробу запускают из рабочего каталога корпуса,");
                Console.WriteLine("       например tools\\CORPUS\\scripts\\wd_app.");
                Console.WriteLine("РАЗОШЛОСЬ");
                return 2;
            }

            files.AddRange(Directory.GetFiles(dir, "*.rmx"));
            if (!all && files.Count > 3)
            {
                files = files.GetRange(0, 3);
            }
        }

        if (files.Count == 0)
        {
            Console.WriteLine("матриц не нашлось");
            Console.WriteLine("РАЗОШЛОСЬ");
            return 1;
        }

        Console.WriteLine("Вылеты 2614.5 кэВ внутри матрицы отклика (S47)");
        Console.WriteLine("матриц: {0}{1}", files.Count, all || only != null ? "" : " (первые три; --all — все)");
        Console.WriteLine();

        bool ok = true;
        int withEscape = 0;
        foreach (string path in files)
        {
            ok &= Report(path, ref withEscape);
        }

        Console.WriteLine();
        Console.WriteLine("== вывод ==");
        Console.WriteLine("матриц с пиками вылета внутри: {0} из {1}", withEscape, files.Count);
        if (withEscape == files.Count && files.Count > 0)
        {
            Console.WriteLine("Образ, построенный через матрицу, НЕСЁТ свои вылеты сам.");
            Console.WriteLine("Свободный столбец `SE-2614`/`DE-2614` рядом с ним — ВТОРОЙ СЧЁТ");
            Console.WriteLine("того же, и взять он может только чужое.");
        }
        else if (withEscape == 0)
        {
            Console.WriteLine("Вылетов внутри матрицы НЕТ — свободный столбец необходим.");
        }
        else
        {
            Console.WriteLine("Матрицы РАЗНЫЕ: часть несёт вылеты, часть нет. Правило по одному");
            Console.WriteLine("признаку «есть матрица» тут не годится — разбираться поштучно.");
            ok = false;
        }

        Console.WriteLine();
        Console.WriteLine(ok ? "СОШЛОСЬ" : "РАЗОШЛОСЬ");
        return ok ? 0 : 1;
    }

    static bool Report(string path, ref int withEscape)
    {
        ResponseMatrix matrix;
        try
        {
            matrix = ResponseMatrix.Load(path);
        }
        catch (Exception e)
        {
            Console.WriteLine("{0}: НЕ ПРОЧИТАЛАСЬ — {1}", Path.GetFileName(path), e.Message);
            return false;
        }

        Console.WriteLine("== {0} ==", Path.GetFileName(path));
        Console.WriteLine("   узлов {0}, бин {1:F1} кэВ, историй {2}, каналы исхода: {3}",
                          matrix.NodeCount, matrix.BinKev, matrix.Histories,
                          matrix.HasChannels ? "есть" : "НЕТ");

        if (!(matrix.BinKev > 0.0))
        {
            Console.WriteLine("   бин не задан — считать нечего");
            return false;
        }

        int bins = (int)(Parent / matrix.BinKev) + 3;
        double[] full = matrix.Evaluate(Parent, bins);

        // Канал вылета отдельно — он и есть прямой ответ на вопрос строки:
        // «пики вылета внутри матрицы» и «строка отклика вообще что-то
        // содержит» — разные утверждения, и путать их нельзя.
        double[] escapeOnly = null;
        if (matrix.HasChannels)
        {
            escapeOnly = new double[bins];
            matrix.AccumulateChannel(escapeOnly, Parent, 1.0,
                                     (int)EfficiencySimulator.ResponseChannel.Escape511);
        }

        double peak = Area(full, matrix.BinKev, Parent, 2);
        double single = Area(full, matrix.BinKev, Single, 2);
        double dbl = Area(full, matrix.BinKev, Double, 2);
        double escapeTotal = escapeOnly != null ? Sum(escapeOnly) : double.NaN;

        Console.WriteLine("   пик 2614.5      : {0:E4}", peak);
        Console.WriteLine("   вылет 2103.5    : {0:E4}   к пику {1:F4}", single,
                          peak > 0.0 ? single / peak : 0.0);
        Console.WriteLine("   вылет 1592.5    : {0:E4}   к пику {1:F4}", dbl,
                          peak > 0.0 ? dbl / peak : 0.0);
        if (escapeOnly != null)
        {
            // Те же два окна, но в КАНАЛЕ ВЫЛЕТА. Так число не мешается с
            // комптоновским континуумом полного отклика: в канал «ушёл 511»
            // событие попадает по своему исходу, а не по тому, куда оно легло.
            // Разница между этими двумя строками — и есть чужой счёт в окне.
            // Площадь считается НАД ПОДЛОЖКОЙ. В канале вылета есть и свой
            // континуум: событие, у которого один квант ушёл, а остальное
            // недобрало, ложится ниже 2103.5 — в том числе ровно на 1592.5.
            // Голая сумма в окне мерила бы пик вместе с ним и завышала бы
            // двойной вылет в разы.
            double singleEsc = Excess(escapeOnly, matrix.BinKev, Single, 2, 10);
            double doubleEsc = Excess(escapeOnly, matrix.BinKev, Double, 2, 10);
            Console.WriteLine("   над подложкой   : 2103.5 — {0:E4} ({1:F4} к пику), "
                              + "1592.5 — {2:E4} ({3:F4} к пику)",
                              singleEsc, peak > 0.0 ? singleEsc / peak : 0.0,
                              doubleEsc, peak > 0.0 ? doubleEsc / peak : 0.0);
            Console.WriteLine("   канал «ушёл 511»: {0:E4} всего, из них в двух пиках {1:F1} %",
                              escapeTotal,
                              escapeTotal > 0.0
                                  ? 100.0 * (singleEsc + doubleEsc) / escapeTotal
                                  : 0.0);

            // Встречная проверка: НИЖЕ ПОРОГА РОЖДЕНИЯ ПАР (1022 кэВ) канала
            // вылета быть не может вовсе. Без неё «площадь в окне» ничего не
            // доказывает: она насчиталась бы и у величины, которая на самом
            // деле означает что-то другое.
            //
            // Отношения SE/пик и DE/пик тождеством 2(1−p)/p и ((1−p)/p)² между
            // собой НЕ связаны, и проверять их так нельзя: пик полного
            // поглощения на 2614 кэВ кормится в основном НЕ парами, а
            // комптоновским каскадом, так что общего p у трёх чисел нет.
            double[] below = new double[(int)(900.0 / matrix.BinKev) + 3];
            matrix.AccumulateChannel(below, 900.0, 1.0,
                                     (int)EfficiencySimulator.ResponseChannel.Escape511);
            double belowSum = Sum(below);
            Console.WriteLine("   ниже порога пар (900 кэВ) канал вылета: {0:E3} — {1}",
                              belowSum, belowSum <= 0.0 ? "ПУСТ, как и должен" : "⚠ НЕ ПУСТ");
            if (belowSum > 0.0)
            {
                return false;
            }
        }

        // Встречная проверка: пики вылета обязаны быть ЛОКАЛЬНЫМ превышением, а
        // не куском гладкого континуума. Иначе «площадь в окне» насчитается и
        // там, где никакого пика нет, и вывод пробы будет ложным.
        bool bumpSingle = IsBump(full, matrix.BinKev, Single);
        bool bumpDouble = IsBump(full, matrix.BinKev, Double);
        Console.WriteLine("   это пики, а не континуум: 2103.5 — {0}, 1592.5 — {1}",
                          bumpSingle ? "ДА" : "нет", bumpDouble ? "ДА" : "нет");

        bool has = bumpSingle && bumpDouble && single > 0.0 && dbl > 0.0;
        if (has)
        {
            withEscape++;
        }

        Console.WriteLine("   вылеты внутри матрицы: {0}", has ? "ЕСТЬ" : "нет");
        Console.WriteLine();
        return true;
    }

    /// <summary>Площадь в окне ±<paramref name="half"/> бинов вокруг энергии.</summary>
    static double Area(double[] histogram, double binKev, double energyKev, int half)
    {
        int at = (int)(energyKev / binKev + 0.5);
        double sum = 0.0;
        for (int b = at - half; b <= at + half; b++)
        {
            if (b >= 0 && b < histogram.Length)
            {
                sum += histogram[b];
            }
        }

        return sum;
    }

    /// <summary>
    /// Площадь НАД подложкой: сумма в окне ±<paramref name="half"/> бинов минус
    /// уровень, снятый в стороне на <paramref name="gap"/> бинов с обеих
    /// сторон, умноженный на ширину окна. Отрицательное зажимается нулём.
    /// </summary>
    static double Excess(double[] histogram, double binKev, double energyKev, int half, int gap)
    {
        int at = (int)(energyKev / binKev + 0.5);
        double inside = Area(histogram, binKev, energyKev, half);
        double left = Area(histogram, binKev, (at - gap) * binKev, half);
        double right = Area(histogram, binKev, (at + gap) * binKev, half);
        double side = 0.5 * (left + right);
        double excess = inside - side;
        return excess > 0.0 ? excess : 0.0;
    }

    /// <summary>
    /// Локальное превышение: середина окна выше обеих подложек, снятых в
    /// стороне. Без этой проверки «площадь в окне» ничего не доказывает — на
    /// гладком комптоновском плато она тоже не нулевая.
    /// </summary>
    static bool IsBump(double[] histogram, double binKev, double energyKev)
    {
        int at = (int)(energyKev / binKev + 0.5);
        double here = Area(histogram, binKev, energyKev, 1);
        double left = Area(histogram, binKev, (at - 8) * binKev, 1);
        double right = Area(histogram, binKev, (at + 8) * binKev, 1);
        double side = 0.5 * (left + right);
        return here > 1.5 * side && here > 0.0;
    }

    static double Sum(double[] values)
    {
        double sum = 0.0;
        foreach (double value in values)
        {
            sum += value;
        }

        return sum;
    }
}
