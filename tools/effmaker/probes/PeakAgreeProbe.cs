using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.IO;
using System.Threading;

// F21: сходятся ли ПИК СТРОКИ ОТКЛИКА и ПИК КРИВОЙ эффективности.
//
// Считаются они разными путями: кривая — пиковой ветвью (история идёт в пик,
// только если недобрала не больше PeakHalfWidthKev), отклик — раскладкой той
// же истории по бинам поглощённой энергии. Пока бин выбирался одним
// округлением, вклад с потерей меньше ПОЛУБИНА попадал в бин пика, хотя пиком
// не был, и расхождение зависело от шага бина — поэтому проба гоняет ОДНУ
// геометрию на нескольких шагах: у правильного правила расхождение не должно
// зависеть от шага вовсе.
//
// Прежняя оговорка «сравнение статистическое, потоки расходятся, потому что
// рассеянная ветвь разная» СНЯТА 08.08.2026 (F28): ветвь у обоих путей теперь
// одна (`ScatteredRun`), случайных чисел она тянет столько же, и расхождение
// потоков было бы дефектом, а не свойством пробы. Смотреть всё равно надо и на
// ХОД отношения по шагу бина — ради него проба и гоняет несколько шагов.
//
// ВТОРАЯ КОЛОНКА — погрешность. Она и есть читатель F28: `relativeError`
// считается по тому же счёту, что и возвращаемая эффективность, и пока
// рассеянный вклад попадал в счёт только на пути без гистограммы, погрешности
// двух путей расходились. Смотреть надо при НЕНУЛЕВОМ допуске пика: при
// нулевом рассеянная поправка не даёт ничего вовсе и сравнивать нечего —
// поэтому есть `--fwhm662=`, когда в самой геометрии ключа `DS_Fwhm662` нет.
//
//   peakagreeprobe --geometry=X.in [--energies=662,1461,2614] [--bins=1,2,5,10]
//                  [--n=200000] [--fwhm662=6.66]
class PeakAgreeProbe
{
    static int Main(string[] args)
    {
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        string geometryPath = null;
        double[] energies = { 662.0, 1461.0, 2614.0 };
        double[] bins = { 1.0, 2.0, 5.0, 10.0 };
        int histories = 200000;
        double fwhm662 = 0.0;
        foreach (string a in args)
        {
            if (a.StartsWith("--geometry=", StringComparison.Ordinal)) geometryPath = a.Substring(11);
            else if (a.StartsWith("--energies=", StringComparison.Ordinal))
                energies = Parse(a.Substring(11));
            else if (a.StartsWith("--bins=", StringComparison.Ordinal))
                bins = Parse(a.Substring(7));
            else if (a.StartsWith("--n=", StringComparison.Ordinal))
                histories = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--fwhm662=", StringComparison.Ordinal))
                fwhm662 = double.Parse(a.Substring(10), CultureInfo.InvariantCulture);
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }

        if (geometryPath == null) { Console.Error.WriteLine("нужен --geometry=<файл .in>"); return 2; }

        GlobalConfigManager.GetInstance();
        GeometryModel geometry = GeometryModel.Load(geometryPath);
        if (fwhm662 > 0.0)
        {
            geometry.FwhmAt662Percent = fwhm662;
        }

        Console.WriteLine("геометрия {0}", Path.GetFileName(geometryPath));
        Console.WriteLine("историй {0}, разрешение {1:F2} %, допуск пика {2:F2} кэВ на 662",
                          histories, geometry.FwhmAt662Percent, geometry.PeakHalfWidthKev(662.0));
        Console.WriteLine();
        Console.Write("{0,10}", "шаг, кэВ");
        foreach (double e in energies) Console.Write("{0,22}", e.ToString("F0") + " кэВ");
        Console.WriteLine();

        // Кривая от шага бина не зависит — считается один раз на энергию.
        double[] curve = new double[energies.Length];
        double[] curveError = new double[energies.Length];
        for (int i = 0; i < energies.Length; i++)
        {
            curve[i] = Make(geometry, histories, energies[i])
                .Efficiency(energies[i], out curveError[i]);
        }

        foreach (double bin in bins)
        {
            Console.Write("{0,10:F1}", bin);
            double[] rowError = new double[energies.Length];
            for (int i = 0; i < energies.Length; i++)
            {
                double e = energies[i];
                double[] histogram = Make(geometry, histories, e).Response(e, bin, out rowError[i]);
                double curvePeak = curve[i];
                double rowPeak = histogram[histogram.Length - 1];
                Console.Write("{0,22}", curvePeak > 0.0
                    ? string.Format(CultureInfo.InvariantCulture, "{0:F3} ({1:+0.00;-0.00} %)",
                                    rowPeak / curvePeak, 100.0 * (rowPeak / curvePeak - 1.0))
                    : "—");
            }

            Console.WriteLine();

            // Погрешность у обоих путей считается по ОДНОМУ и тому же счёту, а
            // значит обязана совпадать до последнего знака — расхождение и есть
            // F28. От шага бина она не зависит вовсе: счёт бинов не касается.
            Console.Write("{0,10}", "  δ, %");
            for (int i = 0; i < energies.Length; i++)
            {
                Console.Write("{0,22}", string.Format(CultureInfo.InvariantCulture,
                    "{0:F4} / {1:F4}", curveError[i], rowError[i]));
            }

            Console.WriteLine();

            // ОКНО ПИКА (F29). Одиночный бин пика строки заведомо меньше пика
            // кривой при ненулевом допуске: кривая считает пиком всё, что
            // недобрало не больше допуска, а строка кладёт такую историю в бин
            // её поглощённой энергии — то есть на несколько бинов ниже. Но
            // никуда эти события не делись: они лежат в окне [E − допуск, E], и
            // уширение настоящей ПШПВ прибора, которое накладывается на образ
            // позже, сводит их в тот же фотопик. Сумма окна и есть та величина,
            // которую надо сравнивать с пиком кривой.
            Console.Write("{0,10}", " окно");
            for (int i = 0; i < energies.Length; i++)
            {
                double e = energies[i];
                double w = geometry.PeakHalfWidthKev(e);
                double[] histogram = Make(geometry, histories, e).Response(e, bin, out rowError[i]);
                int peakBin = histogram.Length - 1;
                int from = (int)Math.Floor((e - w) / bin + 0.5);
                if (from < 0) from = 0;
                double window = 0.0;
                for (int b = from; b <= peakBin; b++) window += histogram[b];
                Console.Write("{0,22}", curve[i] > 0.0
                    ? string.Format(CultureInfo.InvariantCulture, "{0:F3} ({1:+0.00;-0.00} %)",
                                    window / curve[i], 100.0 * (window / curve[i] - 1.0))
                    : "—");
            }

            Console.WriteLine();
        }

        Console.WriteLine();
        Console.WriteLine("Отношение «пик строки / пик кривой». При допуске 0 это единица во");
        Console.WriteLine("всех клетках и независимость от шага бина — то, ради чего заведена");
        Console.WriteLine("проба (F21). При НЕНУЛЕВОМ допуске единицы не будет и быть не может:");
        Console.WriteLine("кривая считает пиком всё, что недобрало не больше допуска, а отклик");
        Console.WriteLine("кладёт историю в бин ПОГЛОЩЁННОЙ энергии — недобравшая 17 кэВ при");
        Console.WriteLine("допуске 22 идёт в свой бин, а не в пиковый. Величина расхождения");
        Console.WriteLine("должна оставаться независимой от шага бина (F29).");
        Console.WriteLine();
        Console.WriteLine("Строка «окно» — сумма строки по [E − допуск, E], то есть по тем же");
        Console.WriteLine("историям, которые кривая считает пиком. Она и есть ответ на F29:");
        Console.WriteLine("события никуда не делись, они разложены по поглощённой энергии, и");
        Console.WriteLine("уширение настоящей ПШПВ прибора сводит их в тот же фотопик.");
        Console.WriteLine("Тождества тут не будет: бины ниже пика ПЕРЕЗАПИСЫВАЕТ аналоговый");
        Console.WriteLine("континуум своим прогоном, так что окно проверяет то, что реально");
        Console.WriteLine("лежит в матрице, а не равенство двух формул.");
        Console.WriteLine("Строка «δ» — погрешность счёта «кривая / отклик»: два пути считают");
        Console.WriteLine("ОДНУ величину, и числа обязаны сойтись (F28).");
        return 0;
    }

    static EfficiencySimulator Make(GeometryModel geometry, int histories, double energyKev)
    {
        // Свежий симулятор на каждый замер: зерно у него одно, и оба пути
        // стартуют с одного места потока.
        return new EfficiencySimulator(geometry)
        {
            Histories = histories,
            PeakHalfWidthKev = geometry.PeakHalfWidthKev(energyKev),
        };
    }

    static double[] Parse(string list)
    {
        string[] parts = list.Split(',');
        double[] result = new double[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            result[i] = double.Parse(parts[i], CultureInfo.InvariantCulture);
        }

        return result;
    }
}
