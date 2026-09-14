using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace LightAnchorProbe
{
    /// <summary>
    /// ЦЕНА РАСХОЖДЕНИЯ ДВУХ ПРАВИЛ БИНА (`A267`) — числом, из ОДНОГО прогона.
    ///
    /// ЗАЧЕМ. `EfficiencySimulator.Deposit` кладёт вес истории в бин пика
    /// только при `InPeak`, иначе двигает в `peak−1`; `ScoreLight` до
    /// 10.09.2026 такой оговорки не имел и свет той же истории оставлял в
    /// `lightSum[peak]`. А `lightSum[peak]` — ЯКОРЬ всей световой шкалы
    /// (`RemapLightScale`): лишний свет в якоре двигает по бинам ВСЕ строки
    /// матрицы. Величина сдвига до сих пор не была названа.
    ///
    /// ⛔ ПОЧЕМУ НЕ ДВУМЯ СБОРКАМИ. Дерево правят соседние полосы, и разность
    /// «сборка до» против «сборки после» вобрала бы их правки. Симулятор
    /// поэтому считает ОБА якоря сразу — <see
    /// cref="EfficiencySimulator.LastPhotonLightScale"/> (действующее правило) и
    /// <see cref="EfficiencySimulator.LastPhotonLightScaleSplit"/> (единое), —
    /// а проба печатает их отношение. Розыгрыш у обеих оценок один и тот же по
    /// построению.
    ///
    ///     lightanchorprobe --geometry=X.in [--energies=59.5,662,...]
    ///                      [--n=400000] [--bin=1] [--peakw] [--peakb] [--store]
    ///                      [--nodes=lo-hi] [--seed=N] [--binof]
    ///
    /// `--binof` (П23 12.09.2026, приёмка `A267` — решение Amber «BinOf — в
    /// СЛЕДУЮЩИЙ единый счёт склада»): включить ключ
    /// `EfficiencySimulator.LightBinUnified` (клеймо `lbin=1`) — свет истории
    /// идёт в бин её веса. С ключом обе оценки якоря совпадают ПО ПОСТРОЕНИЮ,
    /// поэтому мерка плеча — не «сдвиг», а РАВЕНСТВО его действующего якоря
    /// («свет/E») единому якорю плеча БЕЗ ключа на том же зерне: единая
    /// колонка П1/П20 гладкая и монотонная, и ключ обязан дать ровно её.
    /// Проба с `--binof` печатает столбец «свет/E» и отказывает (код 1), если
    /// хоть на одном узле её оценки якоря разошлись.
    ///
    /// `--store` (П20 12.09.2026, замер `A267` «на умолчаниях склада»): ключи
    /// физики, которые симулятор САМ по умолчанию держит иначе, чем строитель
    /// склада (`ResponseMatrixBuilder.MakeSimulator` по умолчаниям
    /// `ResponseMatrixOptions`): разведение K/L-вылета (`SplitXrayShells`) и
    /// обе половины K-провала кривой света (`KDipLight` → `LightSubKevCurve`,
    /// `LightCascadeSplit`). Берутся у самого класса настроек, а не литералом,
    /// чтобы смена умолчания склада доехала сюда сама. Без ключа проба
    /// считает умолчаниями симулятора — как П1 10.09.2026, и это плечо
    /// воспроизводимости прежних чисел.
    ///
    /// `--nodes=lo-hi` — вместо списка энергий взять УЗЛЫ складской сетки
    /// (`ResponseMatrixOptions.BuildGrid`, 140 узлов 5…3000 кэВ, без K-краёв)
    /// в полосе `[lo, hi]` кэВ. Расходящийся класс при допуске ПОЛУБИНОМ жив
    /// только у узла, не лежащего на середине бина, — то есть мерить его надо
    /// на тех энергиях, где склад считает, а не на круглых.
    ///
    /// ⛔ `--peakw` — ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ, а не удобство. Он ставит допуск
    /// пика из геометрии (`E34`, ключ склада `--peakw=1`). Полуширина ПШПВ
    /// обгоняет окно округления уже с ~6 кэВ, поэтому расходящийся класс на
    /// этом плече обязан быть ПУСТ, а отношение якорей — ровно 1. Плечо, где
    /// проба показывает то же самое, что и без ключа, не меряет ничего.
    ///
    /// Столбцы: `свет/E` — действующий якорь; `единое` — он же по единому
    /// правилу; `сдвиг %` — на столько сдвинется шкала бинов после сведения
    /// правила (индекс бина обратно пропорционален якорю); `класс` — доля веса
    /// историй, у которых бин веса и бин света разошлись, от веса бина пика.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string geometryPath = null;
            int histories = 400000;
            int seed = 0;
            double binKev = 1.0;
            bool peakw = false;
            bool peakb = false;     // `AMBER16` п.2/3: допуск по ПОЛУБИНУ — решение Amber 11.09.2026
            bool store = false;     // П20: ключи физики как у строителя склада
            bool binof = false;     // П23: ключ `LightBinUnified` (`A267`)
            double nodesLo = -1.0, nodesHi = -1.0;
            var energies = new List<double>
            {
                30, 59.5, 122, 356, 661.657, 1173.2, 1332.5, 2614.5,
            };

            foreach (string a in args)
            {
                if (a == "--peakw") { peakw = true; continue; }
                if (a == "--peakb") { peakb = true; continue; }
                if (a == "--store") { store = true; continue; }
                if (a == "--binof") { binof = true; continue; }
                if (a.StartsWith("--nodes=", StringComparison.Ordinal))
                {
                    string[] pair = a.Substring(8).Split('-');
                    if (pair.Length != 2
                        || !double.TryParse(pair[0], NumberStyles.Float, CultureInfo.InvariantCulture, out nodesLo)
                        || !double.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out nodesHi)
                        || !(nodesHi > nodesLo) || !(nodesLo > 0.0))
                    {
                        Console.Error.WriteLine("--nodes= ждёт полосу «lo-hi» в кэВ, дано: " + a.Substring(8));
                        return 2;
                    }

                    continue;
                }

                if (a.StartsWith("--geometry=", StringComparison.Ordinal)) geometryPath = a.Substring(11);
                else if (a.StartsWith("--n=", StringComparison.Ordinal)) histories = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--seed=", StringComparison.Ordinal)) seed = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--bin=", StringComparison.Ordinal)) binKev = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--energies=", StringComparison.Ordinal))
                {
                    energies.Clear();
                    foreach (string part in a.Substring(11).Split(','))
                    {
                        energies.Add(double.Parse(part.Trim(), CultureInfo.InvariantCulture));
                    }
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            if (geometryPath == null || !File.Exists(geometryPath))
            {
                Console.Error.WriteLine("нужен --geometry=<файл .in>");
                return 2;
            }

            GeometryModel geometry = GeometryModel.Load(geometryPath);
            var probe = new EfficiencySimulator(geometry.Clone());
            // Умолчания строителя склада — ОДИН экземпляр класса настроек
            // (`T255`): ключи ниже читаются у него, а не переписываются числом.
            var storeOptions = new ResponseMatrixOptions();
            if (nodesLo > 0.0)
            {
                // Узлы складской сетки без K-краёв: `ResolveEdges` добавляет
                // их по веществам геометрии, а мерить нужно ЛОГАРИФМИЧЕСКУЮ
                // сетку — ту, где положение узла внутри бина гуляет.
                energies.Clear();
                foreach (double node in storeOptions.BuildGrid())
                {
                    if (node >= nodesLo && node <= nodesHi)
                    {
                        energies.Add(node);
                    }
                }

                if (energies.Count == 0)
                {
                    Console.Error.WriteLine("в полосе --nodes= нет ни одного узла сетки склада");
                    return 2;
                }
            }

            Console.WriteLine("геометрия: {0}", geometry.Describe());
            Console.WriteLine("ключи физики: {0}", store
                ? string.Format(CultureInfo.InvariantCulture,
                    "КАК У СТРОИТЕЛЯ СКЛАДА (--store): SplitXrayShells={0}, KDipLight={1} → LightSubKevCurve={2}, LightCascadeSplit={3}",
                    storeOptions.SplitXrayShells, storeOptions.KDipLight,
                    ResponseMatrixOptions.KDipCurveHalf(storeOptions.KDipLight),
                    ResponseMatrixOptions.KDipCascadeHalf(storeOptions.KDipLight))
                : "умолчания симулятора (как П1 10.09.2026)");
            if (nodesLo > 0.0)
            {
                Console.WriteLine("энергии: {0} узлов сетки склада в полосе {1}…{2} кэВ (бин склада {3} кэВ)",
                    energies.Count, nodesLo.ToString("F1", CultureInfo.InvariantCulture),
                    nodesHi.ToString("F1", CultureInfo.InvariantCulture),
                    storeOptions.BinKev.ToString("F1", CultureInfo.InvariantCulture));
            }
            Console.WriteLine("кривая света: {0}",
                probe.LightYieldName == "" ? "НЕТ (шкала пропорциональна, замерять нечего)" : probe.LightYieldName);
            Console.WriteLine("историй {0}, бин {1} кэВ, ПШПВ(662) геометрии {2} %",
                histories.ToString(CultureInfo.InvariantCulture),
                binKev.ToString("F2", CultureInfo.InvariantCulture),
                geometry.FwhmAt662Percent.ToString("F2", CultureInfo.InvariantCulture));
            Console.WriteLine("допуск пика: {0}",
                peakw ? "ИЗ ГЕОМЕТРИИ (--peakw, `E34`) — ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ, класс обязан быть пуст"
                : peakb ? "ПОЛУБИН (--peakb, `AMBER16` — решение Amber 11.09.2026 «Допуск по БИНУ»): класс обязан быть пуст"
                      : "НОЛЬ (как у поставочного склада)");
            Console.WriteLine("бин света: {0}",
                binof ? "ЕДИНЫЙ (--binof, ключ LightBinUnified, `A267`) — свет в бин веса; «свет/E» обязан совпасть с «единое» плеча без ключа"
                      : "СВОИМ округлением (как склад до единого счёта); «единое» — то, что даст ключ");
            Console.WriteLine();
            Console.WriteLine("     E, кэВ   допуск   свет/E     единое     сдвиг %   класс: историй    вес/пик %");

            int nonEmpty = 0;
            double worstShift = 0.0, worstAt = 0.0;
            foreach (double e in energies)
            {
                var sim = new EfficiencySimulator(geometry.Clone())
                {
                    Histories = histories,
                    // Тот же порядок старшинства, что у `ResponseMatrixBuilder.PeakTolerance`:
                    // геометрия, потом полубин, потом ноль.
                    PeakHalfWidthKev = peakw ? geometry.PeakHalfWidthKev(e) : peakb ? 0.5 * binKev : 0.0,
                };
                if (store)
                {
                    // Те же три присвоения, что у `ResponseMatrixBuilder.MakeSimulator`;
                    // остальные ключи у симулятора и настроек склада совпадают.
                    sim.SplitXrayShells = storeOptions.SplitXrayShells;
                    sim.LightSubKevCurve = ResponseMatrixOptions.KDipCurveHalf(storeOptions.KDipLight);
                    sim.LightCascadeSplit = ResponseMatrixOptions.KDipCascadeHalf(storeOptions.KDipLight);
                    sim.LightEtaEh = storeOptions.LightEtaEh;
                }

                // (П23, `A267`) Ключ не тянет случайных чисел, поэтому плечо с
                // ним и без него — ОДИН поток: «свет/E» здесь обязан равняться
                // «единое» там до последнего знака.
                sim.LightBinUnified = binof;

                if (seed != 0)
                {
                    sim.Seed = seed;
                }

                sim.ResetStream((ulong)sim.Seed ^ (ulong)Math.Round(e * 64.0) * 0x9E3779B97F4A7C15UL);
                double err;
                double[] response = sim.Response(e, binKev, out err);
                double now = sim.LastPhotonLightScale;
                double one = sim.LastPhotonLightScaleSplit;
                double shift = now > 0.0 && one > 0.0 ? (now / one - 1.0) * 100.0 : 0.0;
                double peakWeight = response != null && response.Length > 0
                    ? response[response.Length - 1] * Math.Max(1000, histories)
                    : 0.0;
                if (sim.CountLightBinSplit > 0)
                {
                    nonEmpty++;
                }

                if (Math.Abs(shift) > Math.Abs(worstShift))
                {
                    worstShift = shift;
                    worstAt = e;
                }

                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,9:F3}   {1,6:F2}   {2}   {3}   {4,7:F3}   {5,14}   {6,10:F3}",
                    e, sim.PeakHalfWidthKev,
                    now > 0.0 ? now.ToString("F6", CultureInfo.InvariantCulture) : "   —    ",
                    one > 0.0 ? one.ToString("F6", CultureInfo.InvariantCulture) : "   —    ",
                    shift, sim.CountLightBinSplit,
                    peakWeight > 0.0 ? 100.0 * sim.WeightLightBinSplit / peakWeight : 0.0));
            }

            Console.WriteLine();
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "наибольший сдвиг якоря: {0:F3} % на {1:F3} кэВ; класс непуст на {2} узлах из {3}.",
                worstShift, worstAt, nonEmpty, energies.Count));
            if (binof)
            {
                // ⛔ (П23) С ключом обе оценки якоря обязаны совпасть ДО БИТА —
                // это свойство самого ключа (`lightBinSplit` при нём не
                // копится), и его нарушение значило бы, что ключ не доехал
                // до `ScoreLight`. Класс при этом по-прежнему считается — он
                // мерит, сколько историй ключ ПЕРЕЛОЖИЛ, — и пустым быть не
                // обязан.
                if (worstShift != 0.0)
                {
                    Console.Error.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "⛔ С --binof оценки якоря разошлись на {0:F6} % — ключ LightBinUnified не доехал до ScoreLight.",
                        worstShift));
                    return 1;
                }

                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "КЛЮЧ ДЕЙСТВУЕТ: с --binof обе оценки якоря совпали на всех {0} узлах; переложено историй на {1} узлах. "
                    + "Приёмка — сверить столбец «свет/E» с «единое» плеча без ключа на том же зерне.",
                    energies.Count, nonEmpty));
                return 0;
            }

            if (peakw)
            {
                // ⛔ Контроль СУДИТ, а не украшает: пустой класс на этом плече —
                // условие, при котором числа второго плеча вообще что-то значат.
                if (nonEmpty == 0)
                {
                    Console.WriteLine("КОНТРОЛЬ ПРОШЁЛ: с допуском из геометрии расходящийся класс ПУСТ на всех узлах.");
                    return 0;
                }

                Console.Error.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "⛔ КОНТРОЛЬ ОТКАЗАЛ: класс непуст на {0} узлах из {1} — допуск не перекрывает окно округления,",
                    nonEmpty, energies.Count));
                Console.Error.WriteLine("   и «пусто/непусто» перестало отличать правило от правила.");
                return 1;
            }

            if (peakb)
            {
                // ⚠ Для `--peakb` это ПРИЁМКА пункта (3) `AMBER16`, и она
                // УСЛОВНА: «недобрал не больше полубина» и «округлился в бин
                // пика» совпадают, только когда энергия линии лежит на СЕРЕДИНЕ
                // бина (E = 2·peak при бине 2). Узлы склада разложены
                // логарифмически и на середину не попадают — у узла с
                // E − 2·peak ∈ (0, 1] история с депозитом в [2·peak − 1, E − 1)
                // округляется в бин пика, а `InPeak` её не берёт: класс НЕПУСТ
                // по построению (П21б 12.09.2026, замер П20). Поэтому на этом
                // плече «непусто» — не отказ, а измерение: судит сдвиг якоря,
                // и он напечатан выше. Отказом остаётся только пустой класс на
                // ВСЕХ узлах при энергиях, заведомо не лежащих на середине бина
                // (--nodes=): тогда проба не меряет то, что думает.
                if (nonEmpty == 0 && nodesLo > 0.0)
                {
                    Console.Error.WriteLine("⛔ КЛАСС ПУСТ НА ВСЕХ УЗЛАХ СЕТКИ ПРИ ПОЛУБИНЕ — так не бывает у узлов вне середины бина; проба не меряет.");
                    return 1;
                }

                Console.WriteLine(nonEmpty == 0
                    ? "ПРИЁМКА ПРОШЛА: с допуском по ПОЛУБИНУ расходящийся класс ПУСТ на всех узлах (энергии на середине бина — правило бина и правило пика совпали, AMBER16 п.3)."
                    : "ПОЛУБИН, узлы вне середины бина: класс непуст по построению — читать сдвиг якоря, не «пусто/непусто».");
                return 0;
            }

            if (nonEmpty == 0)
            {
                Console.Error.WriteLine("⛔ КЛАСС ПУСТ НА ВСЕХ УЗЛАХ БЕЗ КОНТРОЛЬНОГО КЛЮЧА — мерить нечего:");
                Console.Error.WriteLine("   либо кривой света нет, либо допуск пика ненулевой. Числа ниже пусты не по делу.");
                return 1;
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "класс непуст на {0} узлах из {1}.", nonEmpty, energies.Count));
            return 0;
        }
    }
}
