using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

// ⛔ ОТЧЁТ ПРОБЫ О СВОИХ НАСТРОЙКАХ РАЗБОРА — ОДНИМ МЕСТОМ НА ВСЕХ (`T243`).
//
// Файл без `Main`: `build_all.ps1` кладёт такие довеском к КАЖДОЙ пробе
// (правило выводится из отсутствия `Main`, таблицы «кому какой довесок» нет
// нарочно). Тот же приём, что у `ResidualScan.cs` и `GadrasDetector.cs`.
//
// Зачем он вообще. Проба, собирающая анализатор руками, обязана СКАЗАТЬ, чем
// её разбор отличается от поставочного, — иначе прогон с ключами выглядит в
// выводе в точности как умолчательный. Это механизм ~~`S82`~~ («стенд настроен
// иначе, чем экран, и молчит»), и им болели семеро из девяти проб, считающих
// FSA: `--sum-layer-continuum` у `FsaCascadeProbe`, плечи A/B у
// `FsaDoubleCountProbe` меняли разбор, не оставляя в выводе ни строки.
//
// Зачем ОДНИМ местом. До 06.09.2026 правило жило ДВУМЯ копиями — в
// `CorpusFsaProbe.cs` и `FsaStackShot.cs`, — и копии уже разошлись на глазах:
// доля пола печаталась `F5` в одной и `P3` в другой. Две копии одного правила
// расходятся молча (`S37`), а третья копия на семь проб сделала бы расхождение
// неизбежным. Поэтому: правило здесь, у проб — два вызова.
//
// Как этим пользоваться (ровно два движения, внутри точки входа пробы):
//
//   FsaTuningReport.Snapshot();          // ПЕРВЫМ делом, ДО разбора ключей
//   ... разбор ключей, сборка анализатора ...
//   FsaTuningReport.Print(analyzer);     // ПЕРЕД `Analyze`
//   FsaResult r = analyzer.Analyze(...);
//
// ⛔ Объявления точки входа в этом файле быть не должно ДАЖЕ В КОММЕНТАРИИ:
// файл без точки входа — довесок, и `build_all.ps1` отличает довесок от пробы
// поиском её образца ПО ТЕКСТУ, не разбирая код. Пример в комментарии здесь
// однажды уже сделал довесок «пробой», и одиннадцать проб перестали
// собираться разом (`error CS5001` у довеска, `CS0103` у всех, кто его звал).
//
// ⛔ ПОРЯДОК ОБОИХ ВЫЗОВОВ — НЕ УКРАШЕНИЕ, и оба порядка стоили по замеру:
//
//   * `Snapshot()` ПОСЛЕ разбора ключей: полоса — это СТАТИКА, которую оба
//     конца читают в момент обращения, и отражение её не видит вовсе. Эталон,
//     снятый после ключей, родится с уже уведённой полосой, и `--band=` в
//     выводе не будет виден (поймано прогоном `--band=whole` 27.08.2026:
//     состав разошёлся у 5 групп из 16, а шапка печатала «НИЧЕГО»).
//   * `Print()` ПОСЛЕ `Analyze`: у анализатора к этому мигу есть СОСТОЯНИЕ
//     прогона (`RefitZState`, `T240`), и сличение с чистым эталоном начнёт
//     называть исходом то, что обязано называть настройками.
//
// ⚠ Печатается НЕ «что заказано ключами», а всякое расхождение настроенного
// анализатора с чистым `new FsaAnalyzer()` — включая то, что проба взяла у
// КОНФИГУРАЦИИ ПРИБОРА (полоса поиска пиков, окно совпадения, вещество
// кристалла). Вопрос строки — «с какими настройками считали», а не «какие
// ключи набраны»; источник расхождения читается по имени поля.
static class FsaTuningReport
{
    /// <summary>
    /// Снят ли эталон. Проба, забывшая <see cref="Snapshot"/>, обязана быть
    /// НАЗВАНА в выводе, а не тихо сличаться с нулями структуры: `default`
    /// у <c>FsaBandMode</c> — законное значение перечисления, и молчащий
    /// отчёт врал бы в обе стороны.
    /// </summary>
    static bool taken;

    static FsaBandMode stockMode;
    static double stockFloor;
    static double stockFraction;
    static double stockShareThreshold;
    static FsaNoCurveFloor stockNoCurveFloor;
    static double stockNoCurveFloorKev;

    // (`A302`) Пол полосы ФИТА — рычаг замера, поставляется выключенным.
    static FsaFitFloor stockFitFloor;
    static double stockFitFloorKev;

    /// <summary>
    /// ПОСТАВОЧНАЯ ПОЛОСА — снимается ДО разбора ключей и до чтения любой
    /// конфигурации. Первый вызов побеждает: помощник, позвавший снимок
    /// позже, не имеет права заменить эталон уже уведённым состоянием.
    /// </summary>
    public static void Snapshot()
    {
        if (taken)
        {
            return;
        }

        stockMode = FsaBand.DefaultMode;
        stockFloor = FsaBand.DefaultFloor;
        stockFraction = FsaBand.DefaultFloorFraction;
        stockShareThreshold = FsaBand.DefaultShareThreshold;
        stockNoCurveFloor = FsaBand.DefaultNoCurveFloor;
        stockNoCurveFloorKev = FsaBand.DefaultNoCurveFloorKev;
        stockFitFloor = FsaBand.DefaultFitFloor;
        stockFitFloorKev = FsaBand.DefaultFitFloorKev;
        taken = true;
    }

    /// <summary>Отчёт об одном анализаторе, без пометки плеча.</summary>
    public static void Print(FsaAnalyzer tuned)
    {
        Print(tuned, null);
    }

    /// <summary>
    /// Отчёт об одном анализаторе. <paramref name="arm"/> — пометка плеча для
    /// проб, которые считают НЕСКОЛЬКИМИ разборами за прогон
    /// (<c>FsaDoubleCountProbe</c>, <c>FsaPaletteProbe</c>): без неё две
    /// строки отчёта в одном выводе неразличимы, и та самая беда, от которой
    /// заведён отчёт, возвращается внутри одного прогона.
    /// </summary>
    public static void Print(FsaAnalyzer tuned, string arm)
    {
        string mark = string.IsNullOrEmpty(arm) ? "" : " [" + arm + "]";
        if (tuned == null)
        {
            Console.WriteLine("SETUP\tпротив поставочного разбора изменено{0}:"
                              + " СУДИТЬ НЕЧЕГО — анализатора не подали", mark);
            return;
        }

        if (!taken)
        {
            // Сравнивать со статикой, снятой в этот же миг, бессмысленно: она
            // уже уведена. Поэтому полоса здесь НЕ судится вовсе, и об этом
            // сказано вслух — умолчание, которого не видно, ничем не
            // отличается от случайного.
            Console.WriteLine("SETUP\t⚠ ЭТАЛОН ПОЛОСЫ НЕ СНЯТ: FsaTuningReport.Snapshot()"
                              + " не позван до разбора ключей — полоса, пол и доля"
                              + " НЕ СУДЯТСЯ, ключ --band= в отчёте не виден (`T243`)");
        }

        FsaAnalyzer stock = MakeStock();

        var changed = new List<string>();
        Type t = typeof(FsaAnalyzer);
        foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead || p.GetIndexParameters().Length > 0)
            {
                continue;
            }

            Differs(changed, p.Name, Value(p.GetValue, tuned), Value(p.GetValue, stock));
        }

        foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            Differs(changed, f.Name, Value(f.GetValue, tuned), Value(f.GetValue, stock));
        }

        if (taken)
        {
            AddBand(changed);
        }

        changed.Sort(StringComparer.Ordinal);
        Console.WriteLine("SETUP\tпротив поставочного разбора изменено{0}: {1}",
                          mark,
                          changed.Count == 0
                              ? "НИЧЕГО (все настройки анализатора — умолчания приложения)"
                              : string.Join("; ", changed.ToArray()));
    }

    /// <summary>
    /// ЭТАЛОН — только <c>new FsaAnalyzer()</c>, и никогда список поставочных
    /// значений, выписанный рядом руками: такой список есть ВТОРАЯ КОПИЯ
    /// умолчаний, он однажды разойдётся с конструктором молча, и отчёт станет
    /// врать в обе стороны (`T65`, `T70`).
    ///
    /// Эталон рождается с ПОСТАВОЧНОЙ полосой: статика подменяется на снятую
    /// в <see cref="Snapshot"/> и возвращается на место в <c>finally</c>.
    /// </summary>
    static FsaAnalyzer MakeStock()
    {
        if (!taken)
        {
            return new FsaAnalyzer();
        }

        FsaBandMode liveMode = FsaBand.DefaultMode;
        double liveFloor = FsaBand.DefaultFloor;
        double liveFraction = FsaBand.DefaultFloorFraction;
        double liveThreshold = FsaBand.DefaultShareThreshold;
        FsaNoCurveFloor liveNoCurve = FsaBand.DefaultNoCurveFloor;
        double liveNoCurveKev = FsaBand.DefaultNoCurveFloorKev;
        FsaFitFloor liveFitFloor = FsaBand.DefaultFitFloor;
        double liveFitFloorKev = FsaBand.DefaultFitFloorKev;

        FsaBand.DefaultMode = stockMode;
        FsaBand.DefaultFloor = stockFloor;
        FsaBand.DefaultFloorFraction = stockFraction;
        FsaBand.DefaultShareThreshold = stockShareThreshold;
        FsaBand.DefaultNoCurveFloor = stockNoCurveFloor;
        FsaBand.DefaultNoCurveFloorKev = stockNoCurveFloorKev;
        FsaBand.DefaultFitFloor = stockFitFloor;
        FsaBand.DefaultFitFloorKev = stockFitFloorKev;
        try
        {
            FsaAnalyzer stock = new FsaAnalyzer();
            return stock;
        }
        finally
        {
            FsaBand.DefaultMode = liveMode;
            FsaBand.DefaultFloor = liveFloor;
            FsaBand.DefaultFloorFraction = liveFraction;
            FsaBand.DefaultShareThreshold = liveThreshold;
            FsaBand.DefaultNoCurveFloor = liveNoCurve;
            FsaBand.DefaultNoCurveFloorKev = liveNoCurveKev;
            FsaBand.DefaultFitFloor = liveFitFloor;
            FsaBand.DefaultFitFloorKev = liveFitFloorKev;
        }
    }

    /// <summary>
    /// ⛔ ПОЛОСУ ОТРАЖЕНИЕ НЕ ВИДИТ, И ЭТО НЕ МЕЛОЧЬ (`S101` + `T65`). Оба конца
    /// читают статику В МОМЕНТ ОБРАЩЕНИЯ, а не хранят копию, — значит настроенный
    /// анализатор и эталон отдают ОДНО И ТО ЖЕ, что бы ключ ни сделал, и цикл по
    /// свойствам честно находит «НИЧЕГО». Поэтому полоса сличается здесь,
    /// поимённо и по СТАТИКЕ, с эталоном, снятым до разбора ключей.
    /// </summary>
    static void AddBand(List<string> changed)
    {
        if (FsaBand.DefaultMode != stockMode)
        {
            changed.Add(string.Format(CultureInfo.InvariantCulture,
                "Band: {0} (поставка {1})", FsaBand.DefaultMode, stockMode));
        }

        if (Math.Abs(FsaBand.DefaultFloor - stockFloor) > 1e-9)
        {
            changed.Add(string.Format(CultureInfo.InvariantCulture,
                "LibraryFloorKev: {0:F2} (поставка {1:F2})", FsaBand.DefaultFloor, stockFloor));
        }

        if (Math.Abs(FsaBand.DefaultFloorFraction - stockFraction) > 1e-12)
        {
            changed.Add(string.Format(CultureInfo.InvariantCulture,
                "FloorFraction: {0:F5} (поставка {1:F5})",
                FsaBand.DefaultFloorFraction, stockFraction));
        }

        if (Math.Abs(FsaBand.DefaultShareThreshold - stockShareThreshold) > 1e-12)
        {
            changed.Add(string.Format(CultureInfo.InvariantCulture,
                "ShareThreshold: {0:F3} (поставка {1:F3})",
                FsaBand.DefaultShareThreshold, stockShareThreshold));
        }

        if (FsaBand.DefaultNoCurveFloor != stockNoCurveFloor
            || (FsaBand.DefaultNoCurveFloor == FsaNoCurveFloor.Fixed
                && Math.Abs(FsaBand.DefaultNoCurveFloorKev - stockNoCurveFloorKev) > 1e-9))
        {
            changed.Add(string.Format(CultureInfo.InvariantCulture,
                "NoCurveFloor: {0}{1} (поставка {2})",
                FsaBand.DefaultNoCurveFloor,
                FsaBand.DefaultNoCurveFloor == FsaNoCurveFloor.Fixed
                    ? string.Format(CultureInfo.InvariantCulture, " {0:F2} кэВ",
                                    FsaBand.DefaultNoCurveFloorKev)
                    : "",
                stockNoCurveFloor));
        }

        // (`A302`) ПОЛ ПОЛОСЫ ФИТА. Сличается по той же причине, что и всё
        // выше: он двигает САМУ ПОЛОСУ СЧЁТА, то есть все числа прогона, а
        // отражение его не видит — анализатор читает статику в момент
        // обращения.
        if (FsaBand.DefaultFitFloor != stockFitFloor
            || (FsaBand.DefaultFitFloor == FsaFitFloor.Fixed
                && Math.Abs(FsaBand.DefaultFitFloorKev - stockFitFloorKev) > 1e-9))
        {
            changed.Add(string.Format(CultureInfo.InvariantCulture,
                "FitFloor: {0}{1} (поставка {2})",
                FsaBand.DefaultFitFloor,
                FsaBand.DefaultFitFloor == FsaFitFloor.Fixed
                    ? string.Format(CultureInfo.InvariantCulture, " {0:F2} кэВ",
                                    FsaBand.DefaultFitFloorKev)
                    : "",
                stockFitFloor));
        }
    }

    /// <summary>
    /// Значение поля словами. Отказ геттера — тоже ЗНАЧЕНИЕ, а не повод
    /// пропустить поле: свойство, кидающее у настроенного и молчащее у
    /// нетронутого (или наоборот), попадёт в строку различий и будет видно.
    /// ⚠ Отказ ОДИНАКОВЫЙ у обоих различием не является и не печатается —
    /// ключами такое поле и правда не тронуто.
    /// </summary>
    static string Value(Func<object, object> getter, FsaAnalyzer a)
    {
        try
        {
            object v = getter(a);
            return v == null ? "нет" : Convert.ToString(v, CultureInfo.InvariantCulture);
        }
        catch (Exception e)
        {
            return "ЧИТАТЕЛЬ ОТКАЗАЛ: " + e.GetType().Name;
        }
    }

    static void Differs(List<string> to, string name, string tuned, string stock)
    {
        if (!string.Equals(tuned, stock, StringComparison.Ordinal))
        {
            to.Add(name + " " + stock + " → " + tuned);
        }
    }
}
