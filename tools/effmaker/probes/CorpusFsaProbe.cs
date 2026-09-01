using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Serialization;

namespace CorpusFsaProbe
{
    /// <summary>
    /// ⛔⛔ ВОРОТА: поставочный `config\NuclideDefinition.xml` НА КОРПУСЕ НЕ
    /// ИСПОЛЬЗУЕТСЯ НИКОГДА — глобальное правило Amber 01.09.2026.
    ///
    /// Корпус гоняется только по УКАЗАННЫМ нуклидам, привязанным к конкретному
    /// спектру: состав берётся из `manifest.csv`, линии — из `nucdb`/`matdb`
    /// (<see cref="FsaSampleLibrary"/>), подписи пиков — из той же своей базы.
    /// Общий поставочный список — дело человека и графика; предъявив его
    /// спектру, отдаёшь необъяснённую структуру первому подходящему кандидату.
    ///
    /// ЦЕНА НАРУШЕНИЯ ИЗМЕРЕНА И НОСИТ ИМЯ (`N18`): одна запись поставочного
    /// списка — `Pu-238`, 152 кэВ, выход 0.0009 %, — дала на `ASN16_Lu176`
    /// состав с плутонием долей 1.7 % при z = 31.77, сев в полосу обратного
    /// рассеяния линии 306.78 самого лютеция. Плутония не объявлял ни один из
    /// 129 спектров манифеста.
    ///
    /// ⚠ ЧТО ЭТИМИ ВОРОТАМИ ПОТЕРЯНО, названо честно: ключи `--lib=peaks`
    /// (состав по подписям поиска) и `--lib=infer` (`S57`, состав выводится из
    /// пиков) предъявляли спектру ровно этот список, поэтому на корпусе они
    /// больше не работают. A/B «подписи против объявленной пробы» на корпусе
    /// заново не поставить, пока у вывода состава не появится свой источник
    /// кандидатов, не поставочный.
    ///
    /// Ворота двусторонние: <see cref="Allow"/> пускает только `sample`, а
    /// недостижимая ветвь разбора бросает <see cref="Rule"/> — правка, вернувшая
    /// другой режим, упадёт, а не посчитает корпус чужим списком. Механическая
    /// проверка тех же ворот — `tools/check_corpus_library.py`.
    /// </summary>
    static class SuppliedLibraryGuard
    {
        /// <summary>Само правило одной строкой — им же говорит отказ.</summary>
        public const string Rule =
            "поставочный config\\NuclideDefinition.xml на корпусе НЕ используется " +
            "(глобальное правило Amber 01.09.2026): корпус гоняется только по " +
            "указанным нуклидам, привязанным к спектру";

        /// <summary>Код возврата отказа — свой, чтобы не путать с разбором ключей.</summary>
        public const int ExitCode = 12;

        public static bool Allow(string library)
        {
            return library == "sample";
        }

        public static int Refuse(string library)
        {
            Console.Error.WriteLine("⛔ ОТКАЗ: " + Rule + ".");
            Console.Error.WriteLine(
                "   --lib=" + (library ?? "") + " предъявил бы спектру ОБЩИЙ поставочный список.");
            Console.Error.WriteLine(
                "   Разрешено единственное: --lib=sample (умолчание) — состав из manifest.csv, линии из nucdb.");
            Console.Error.WriteLine(
                "   Цена нарушения измерена: фантом Pu-238 на ASN16_Lu176 (N18).");
            return ExitCode;
        }
    }

    /// <summary>
    /// Полноспектральный разбор ВСЕГО корпуса кодом ПРИЛОЖЕНИЯ (TODO S1).
    ///
    /// Зачем ещё один обход, когда есть `tools/pie/run_corpus.ps1`: у того
    /// разложение своё, доматричное — `ResponseMatrix` в нём не упоминается ни
    /// разу, и «понятную часть с матрицей» им не измерить. Здесь считает тот же
    /// `FsaAnalyzer`, что работает в окне программы, матрица берётся тем же
    /// `ResponseMatrixStore.Load` по Guid кривой спектра, а библиотека — тем же
    /// `FsaLibrary.BuildFromPeaks` от найденных пиков. Числа этого обхода
    /// относятся к продукту, а не к его копии.
    ///
    /// ⚠ Части корпуса НЕ СМЕШИВАЮТСЯ. `corpus/parts.csv` делит его на
    /// «понятную» часть (геометрия восстановлена, матрица есть) и «непонятную»
    /// (ни того, ни другого); германий помечен `excluded` и не считается вовсе
    /// (приказ Amber 08.08.2026). Это две разные модели, и общее число по ним
    /// было бы средним двух разных вещей. Поэтому итог печатается ПО ЧАСТЯМ, и
    /// имя части идёт в каждую строку `runs.csv`.
    ///
    /// ⛔ **Состав библиотеки с 18.08.2026 задаёт ОБЪЯВЛЕННАЯ ПРОБА** (`S56`,
    /// первый постулат Amber): `--lib=sample` — умолчание, линии собираются
    /// `FsaSampleLibrary` из `nucdb`/`matdb` по `manifest.csv` и
    /// `materials.csv`.
    ///
    /// ⛔⛔ **С 01.09.2026 это ЕДИНСТВЕННЫЙ разрешённый режим** (глобальное
    /// правило Amber): поставочный `config\NuclideDefinition.xml` на корпусе не
    /// используется НИКОГДА, и прежние ключи `--lib=peaks` / `--lib=infer`
    /// ОТКАЗЫВАЮТ кодом 12 — см. <see cref="SuppliedLibraryGuard"/>.
    /// `NuclideDefinitionManager` этой пробой больше не поднимается вовсе.
    /// ⚠ Мерки при этом сменили смысл ещё в августе: recall и число фантомов
    /// считаются относительно ПРЕДЪЯВЛЕННОГО списка, и сужение списка улучшает
    /// их само по себе; числа прежних режимов корпусными не считать.
    ///
    ///   corpusfsaprobe --corpus=&lt;…\CORPUS\corpus&gt; [--out=out] [--part=all]
    ///                  [--lib=sample]   (peaks/infer ЗАПРЕЩЕНЫ, отказ кодом 12)
    ///                  [--infer-head] [--infer-head-only]
    ///                  [--no-infer-novel]
    ///                  [--no-atomic] [--no-equilibrium] [--audit] [--lib-dump]
    ///                  [--dump-curves=&lt;каталог&gt;] [--knot-fwhm=&lt;ПШПВ&gt;]
    ///                  [--band-audit=&lt;файл.csv&gt;]
    ///                  [--band=whole|fit|library|curve|share] [--band-floor=&lt;кэВ&gt;]
    ///                  [--floor-frac=&lt;доля&gt;] [--share-thr=&lt;0…1&gt;] [--band-selftest]
    ///                  [--roughness=&lt;вес&gt;]
    ///                  [--groups=G1S,ASN16] [--only=G1S24_Th232_Denta120_2]
    ///                  [--mode=spline|snip] [--no-matrix] [--no-cascade]
    ///                  [--no-pileup] [--no-background] [--limit=N] [--quiet]
    ///                  [--no-xray] [--no-ann] [--no-isomer] [--window=<секунды>]
    ///                  [--limits-mc=N [--mc-component=Имя]] [--huber=M] [--refit-z=Z]
    ///                  [--no-escape-gate]
    ///                  [--partial] [--no-pr-gate] [--gamma=G] [--beta=B]
    ///                  [--bg-rebin]
    ///
    /// ⛔ **«НАЙДЕНА» и «ПРИМЕНЕНА» — РАЗНЫЕ слова с 27.08.2026** (`T85`).
    /// `matrix_found` = матрица прочитана и отпечаток сошёлся с геометрией;
    /// `matrix_applied` (прежняя `matrix`, на своём месте) = хоть один образ
    /// ОТЧЁТНОГО фита построен ею. Второе слабее первого: образ строит матрицей
    /// только компонент со своими линиями, а производные (обратное рассеяние) и
    /// готовые (наложения) — нет; фит, из которого отсев по значимости и гейт
    /// ΔD выбросили всех нуклидных кандидатов, вернёт `matrix_applied = 0` при
    /// живой матрице. Измерено на снятых файлах: `out_v6` найдена 81 / применена
    /// 80 (`G1S16_Cd109_P25`), `out_fz_lib` 81 / 78 (все три кадмия), причём
    /// `out_fz_whole` с ТОЙ ЖЕ полосой и той же библиотекой даёт 81 / 81 —
    /// значит дело не в полосе, а в том, кто дожил до отчёта. Итог по частям
    /// печатает ОБА числа и называет разошедшиеся спектры поимённо.
    ///
    /// Файлы на выходе — того же вида, что у `tools/pie`, чтобы считал их тот же
    /// `tools/pie/score.py`: `&lt;группа&gt;_&lt;режим&gt;_components.csv` и
    /// `&lt;группа&gt;_&lt;режим&gt;_runs.csv`; плюс свой
    /// `&lt;группа&gt;_&lt;режим&gt;_limits.csv` — характеристические пределы S9
    /// по ВСЕМ кандидатам библиотеки, включая не вошедших в состав.
    ///
    /// `--limits-mc=N` — Монте-Карло-поверка пределов (S9): для каждого
    /// нуклида состава спектр пересобирается N раз пуассоновским розыгрышем
    /// модели БЕЗ этого нуклида (проверка ложных срабатываний против α) и N раз
    /// с ним на уровне МДА (проверка пропусков против β); библиотека и настройки
    /// не меняются, поиск пиков не перезапускается. Дорого — 2·N разборов на
    /// нуклид: запускать с `--only=` и, при нужде, `--mc-component=`.
    ///
    /// Запускать из каталога, где рядом лежат `config\NuclideDefinition.xml`
    /// (ПОСТАВОЧНЫЙ, а не сеты `mkconfig.py` с обманками), `config\device\*.xml`
    /// корпуса и `config\device\response\*.rmx`. Такой каталог собирает
    /// `tools/CORPUS/scripts/mk_appwd.ps1`. Конфиг Amber (`%AppData%\BecqMoni`)
    /// при этом не задействован: приложение считает себя standalone всегда,
    /// кроме ClickOnce, и пути идут от рабочего каталога.
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            // ⛔ ПОСТАВОЧНУЮ ПОЛОСУ ЗАПОМИНАЕМ ДО РАЗБОРА КЛЮЧЕЙ. Ключ `--band=`
            // двигает СТАТИКУ (иначе он двигает один конец из двух, `S101`), а
            // эталон для строки «ключами изменено» строится позже — и, читая уже
            // сдвинутую статику, показал бы «НИЧЕГО» на абляционном прогоне.
            // Это ровно дефект, который чинила `T65`: абляция, неотличимая в
            // журнале от умолчания. Поймано на себе 26.08.2026, первым же
            // прогоном `--band=whole`.
            StockBandMode = FsaBand.DefaultMode;
            StockBandFloor = FsaBand.DefaultFloor;
            StockBandFraction = FsaBand.DefaultFloorFraction;
            StockShareThreshold = FsaBand.DefaultShareThreshold;

            var o = new Options();
            foreach (string a in args)
            {
                if (a == "--no-matrix") { o.Matrix = false; continue; }
                if (a == "--no-cascade") { o.Cascade = false; continue; }
                if (a == "--no-pileup") { o.PileUp = false; continue; }
                // S27: атомные партнёры каскада. Ключи РАЗДЕЛЯЮЩИЕ — цена
                // правки снимается одним двоичным файлом, «было/стало» при
                // одной версии физики. Матрицу они не трогают вовсе (слой
                // стоит поверх неё), поэтому в клеймо не идут и идти не
                // должны — правило T42 сюда не относится.
                if (a == "--no-xray") { o.Xray = false; continue; }
                if (a == "--no-ann") { o.Annihilation = false; continue; }
                if (a == "--no-isomer") { o.Isomers = false; continue; }
                if (a == "--no-backscatter") { o.Backscatter = false; continue; }
                if (a == "--no-background") { o.Background = false; continue; }
                // S56: чем задаётся состав библиотеки. `sample` — объявленным
                // составом пробы (первый постулат), `peaks` — подписями поиска
                // пиков, как было до 18.08.2026. Ключ, а не пересборка: A/B
                // считается ОДНИМ двоичным файлом.
                if (a.StartsWith("--residual-peaks=")) { o.ResidualPeaks = a.Substring(17); continue; }
                if (a == "--lib=sample") { o.Library = "sample"; continue; }
                if (a == "--lib=peaks") { o.Library = "peaks"; continue; }
                if (a == "--lib=infer") { o.Library = "infer"; continue; }
                if (a == "--no-infer-anchor") { o.InferAnchors = false; continue; }
                if (a == "--no-infer-novel") { o.InferNovelty = false; continue; }
                // S65: ОБОРВАННЫЙ ряд. Два ключа, а не один, потому что это два
                // РАЗНЫХ утверждения: `--infer-head` меняет один знаменатель
                // доли (в состав, как велит правило Amber, идёт весь ряд),
                // `--infer-head-only` предъявляет фиту ОДНУ голову.
                if (a == "--infer-head") { o.InferCut = FsaChainCut.Criterion; continue; }
                if (a == "--infer-head-only") { o.InferCut = FsaChainCut.Only; continue; }
                if (a.StartsWith("--infer-theta=", StringComparison.Ordinal))
                {
                    o.InferTheta = double.Parse(a.Substring(14), CultureInfo.InvariantCulture);
                    continue;
                }

                // S56: атомные образы (рентген пробы, кристалла, защиты и пики
                // вылета кристалла) и вездесущие ряды комнаты — обе половины
                // разводятся ключами, потому что цена у них разная и на разных
                // частях корпуса.
                if (a == "--no-atomic") { o.Atomic = false; continue; }
                // S60: кросс-проверка по линиям, которые ОБЯЗАНЫ быть.
                // Ключ, а не умолчание: она стоит прохода по всем линиям
                // состава и пишет свой файл, а нужна не каждому прогону.
                if (a == "--audit") { o.Audit = true; continue; }
                if (a == "--no-equilibrium") { o.Equilibrium = false; continue; }
                if (a == "--lib-dump") { o.LibDump = true; continue; }
                if (a.StartsWith("--dump-curves=", StringComparison.Ordinal))
                {
                    o.DumpCurves = a.Substring(14);
                    continue;
                }
                if (a == "--quiet") { o.Quiet = true; continue; }
                if (a == "--peaks") { o.Peaks = true; continue; }
                if (a == "--partial") { o.Partial = true; continue; }
                if (a == "--pr-gate") { o.PartialGate = true; continue; }
                if (a == "--no-pr-gate") { o.PartialGate = false; continue; }
                if (a == "--bg-rebin") { o.RebinBackground = true; continue; }
                if (a == "--no-bg-rebin") { o.RebinBackground = false; continue; }
                if (a.StartsWith("--window=", StringComparison.Ordinal))
                {
                    // S27: окно совпадения, секунды. У корпусных конфигураций
                    // мёртвого времени нет (они заглушки нарочно), поэтому
                    // задать его можно только отсюда. Ноль — умолчание
                    // суммирователя.
                    o.WindowSec = double.Parse(a.Substring(9), CultureInfo.InvariantCulture);
                    continue;
                }

                if (a.StartsWith("--knots=", StringComparison.Ordinal))
                {
                    // `B17`: делитель диапазона, задающий самый редкий шаг узлов
                    // континуума. Больше — гуще узлы ВНИЗУ шкалы (наверху правит
                    // 4·ПШПВ и ничего не меняется). Заведён ключом, а не правкой
                    // умолчания, нарочно: A/B считается ОДНИМ двоичным файлом, и
                    // разница тогда принадлежит только узлам.
                    o.Knots = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                    continue;
                }

                if (a.StartsWith("--roughness=", StringComparison.Ordinal))
                {
                    // (`S85`) Вес штрафа на излом континуума; 0 — без штрафа.
                    o.Roughness = double.Parse(a.Substring(12), CultureInfo.InvariantCulture);
                    continue;
                }

                // (`S98`) Полоса разбора: `whole` — как было до 25.08.2026,
                // `fit-to-library` — сузить фит, `library-to-fit` — опустить пол
                // библиотеки (поставочное умолчание).
                // ⛔ ЗАЧЕМ КЛЮЧ, если умолчание «меняется здесь и только здесь»:
                // без него развести вклад `V13` и вклад `S98` НЕЧЕМ — обе правки
                // лежат в дереве разом, и прогон меряет их сумму. Решение Amber
                // 26.08.2026 по `B26` требует ровно обратного: одна причина на
                // один сдвиг базы. Ключ не заводит ВТОРУЮ копию умолчания — он
                // ничего не подставляет, когда не задан, и печатается шапкой.
                // ⛔ ДВИГАТЬ НАДО СТАТИКУ, А НЕ ПОЛЕ АНАЛИЗАТОРА. Измерено
                // 26.08.2026: `FsaSampleLibrary` держит СВОИ `Band` и
                // `LibraryFloorKev` и берёт их у `FsaBand.DefaultMode` /
                // `DefaultFloor` В МОМЕНТ СОЗДАНИЯ — то есть присваивание
                // `analyzer.Band` двигает ОДИН конец из двух, и корпусный
                // прогон (библиотека по объявленной пробе, `S56`) остаётся
                // на поставочной полосе. Первый заход так и вышел: плечи
                // `whole` и `library-to-fit` дали ПОБИТОВО одинаковые
                // `components` и `limits`, разошлись только `ms`/`cpu_ms`.
                if (a.StartsWith("--band=", StringComparison.Ordinal))
                {
                    o.BandName = a.Substring(7);
                    FsaBandMode band;
                    if (!FsaBand.TryParse(o.BandName, out band))
                    {
                        Console.Error.WriteLine(
                            "неизвестная полоса: {0}"
                            + " (whole | fit-to-library | library-to-fit | curve | share)",
                            o.BandName);
                        Environment.Exit(64);
                    }

                    FsaBand.DefaultMode = band;
                    continue;
                }

                if (a.StartsWith("--band-floor=", StringComparison.Ordinal))
                {
                    o.BandFloor = double.Parse(a.Substring(13), CultureInfo.InvariantCulture);
                    FsaBand.DefaultFloor = o.BandFloor;
                    continue;
                }

                // (`S98`, `S101`) РАЗВЁРТКА ПО ДОЛЕ ПОЛА ПО КРИВОЙ. Пол при
                // поставочном режиме назначает не число, а сама кривая: линии
                // впускаются не ниже той энергии, где эффективность набирает
                // эту долю от своего максимума. Доля взята из наблюдения
                // (`FsaBand.DefaultFloorFraction`), а не выведена, — развёртка
                // по ней и есть способ это исправить.
                //
                // ⛔ СТАВИТСЯ СТАТИКА, и только она: её читают ОБА конца — и
                // `FsaSampleSpec.CurveFloorKev`, который режет линии, и
                // заверение анализатора. Ставить долю одному концу значило бы
                // повторить `S101`.
                // ⛔ Ключ разбирается ПОСЛЕ снятия эталона (см. `StockBandFraction`
                // выше): эталон, снятый уже сдвинутым, показал бы «НИЧЕГО»
                // (`T65`).
                if (a.StartsWith("--floor-frac=", StringComparison.Ordinal))
                {
                    o.FloorFraction = double.Parse(a.Substring(13), CultureInfo.InvariantCulture);
                    if (!(o.FloorFraction > 0.0) || o.FloorFraction > 1.0)
                    {
                        Console.Error.WriteLine(
                            "доля пола по кривой обязана лежать в (0…1]: {0}", a.Substring(13));
                        Environment.Exit(64);
                    }

                    FsaBand.DefaultFloorFraction = o.FloorFraction;
                    continue;
                }

                // (`S103`) РАЗВЁРТКА ПО ОПОРЕ ПОЛОСЫ ПО СТОЛБЦУ. Порог доли
                // континуума: подпороговая линия выбрасывается, если сплайн и
                // так представляет её столбец лучше, чем на эту долю, — и
                // разбор идёт ВТОРЫМ проходом по сужённой библиотеке.
                //
                // ⛔ Оба крайних значения — контроли, и они даровые:
                //   * 1.0 не выбрасывает НИЧЕГО (условие строгое, доля ≤ 1) и
                //     обязано воспроизвести поставочный прогон ПОБИТОВО;
                //   * 0.0 выбрасывает всякую подпороговую линию, у которой
                //     континуум забирает хоть что-то, — это плечо `--band=whole`.
                // Плечо, которое не воспроизводит свой контроль, означает, что
                // режим сделан неверно, и числа развёртки недействительны.
                //
                // ⛔ СТАВИТСЯ СТАТИКА, как и у доли пола: её читают разбор и
                // заверение, а вторая копия — это `S101` заново.
                if (a.StartsWith("--share-thr=", StringComparison.Ordinal))
                {
                    o.ShareThreshold = double.Parse(a.Substring(12), CultureInfo.InvariantCulture);
                    if (o.ShareThreshold < 0.0 || o.ShareThreshold > 1.0)
                    {
                        Console.Error.WriteLine(
                            "порог доли континуума обязан лежать в [0…1]: {0}", a.Substring(12));
                        Environment.Exit(64);
                    }

                    FsaBand.DefaultShareThreshold = o.ShareThreshold;
                    continue;
                }

                // (`S101`) Положительный контроль сторожа обоих концов полосы.
                // Не корпусный прогон: считает три опыта над `FsaBand` и
                // выходит. Заведён потому, что сторож, у которого нет опыта с
                // ЗАВЕДОМЫМ рассинхроном, ничем не отличается от сторожа,
                // который всегда молчит.
                if (a == "--band-selftest") { o.BandSelfTest = true; continue; }

                if (a.StartsWith("--knot-fwhm=", StringComparison.Ordinal))
                {
                    // (`S88`) Густой край шага узлов в ПШПВ; умолчание 4.
                    // ⚠ АБЛЯЦИЯ: меньше 4 ломает состав, читать после этого
                    // можно форму невязки, а не разложение.
                    o.KnotFwhm = double.Parse(a.Substring(12), CultureInfo.InvariantCulture);
                    continue;
                }

                if (a.StartsWith("--residuals=", StringComparison.Ordinal))
                {
                    o.Residuals = int.Parse(a.Substring(12), CultureInfo.InvariantCulture);
                    continue;
                }
                if (a.StartsWith("--band-audit=", StringComparison.Ordinal))
                {
                    o.BandAudit = a.Substring(13);
                    continue;
                }
                if (a.StartsWith("--limits-mc=", StringComparison.Ordinal))
                {
                    o.LimitsMc = int.Parse(a.Substring(12), CultureInfo.InvariantCulture);
                    continue;
                }
                if (a.StartsWith("--mc-component=", StringComparison.Ordinal))
                {
                    o.McComponent = a.Substring(15);
                    continue;
                }
                if (a.StartsWith("--near=", StringComparison.Ordinal))
                {
                    string[] parts = a.Substring(7).Split(':');
                    if (parts.Length == 2)
                    {
                        o.NearFrom = double.Parse(parts[0], CultureInfo.InvariantCulture);
                        o.NearTo = double.Parse(parts[1], CultureInfo.InvariantCulture);
                    }
                    continue;
                }
                if (a.StartsWith("--corpus=", StringComparison.Ordinal)) o.Corpus = a.Substring(9);
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) o.Out = a.Substring(6);
                else if (a.StartsWith("--part=", StringComparison.Ordinal)) o.Part = a.Substring(7);
                else if (a.StartsWith("--mode=", StringComparison.Ordinal)) o.Mode = a.Substring(7);
                else if (a.StartsWith("--groups=", StringComparison.Ordinal))
                {
                    o.Groups = new List<string>(a.Substring(9).Split(','));
                }
                else if (a.StartsWith("--only=", StringComparison.Ordinal))
                {
                    o.Only = new List<string>(a.Substring(7).Split(','));
                }
                else if (a.StartsWith("--limit=", StringComparison.Ordinal))
                {
                    o.Limit = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--offset-range=", StringComparison.Ordinal))
                {
                    o.OffsetRangeKev = double.Parse(a.Substring(15), CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--offset-steps=", StringComparison.Ordinal))
                {
                    o.OffsetSteps = int.Parse(a.Substring(15), CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--gain-range=", StringComparison.Ordinal))
                {
                    o.GainRange = double.Parse(a.Substring(13), CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--gain-steps=", StringComparison.Ordinal))
                {
                    o.GainSteps = int.Parse(a.Substring(13), CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--huber=", StringComparison.Ordinal))
                {
                    o.HuberM = double.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                }
                else if (a == "--no-escape-gate")
                {
                    // S47: вернуть свободные `SE-2614`/`DE-2614` при матрице —
                    // A-сторона A/B. Гейт с 16.08.2026 включён умолчанием,
                    // поэтому мерится его ОТКЛЮЧЕНИЕ, как у Хубера (S41).
                    o.EscapeGate = false;
                }
                else if (a.StartsWith("--refit-z=", StringComparison.Ordinal))
                {
                    // S9 «б»: которая из двух ступеней занижает МДА слабого
                    // компонента — первый NNLS или отсев по значимости. Ноль
                    // снимает отсев целиком, и разница между прогонами и есть
                    // ответ. Ключ, а не пересборка: A/B на одном коде.
                    o.RefitZ = double.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--gamma=", StringComparison.Ordinal))
                {
                    o.NoiseGamma = double.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--beta=", StringComparison.Ordinal))
                {
                    o.NoiseBeta = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            // (`S101`) Положительный контроль сторожа полосы — ДО всякого
            // чтения корпуса: он не про спектры, а про то, один ли у полосы
            // рычаг. Корпуса и конфигов ему не нужно.
            if (o.BandSelfTest)
            {
                return BandSelfTest();
            }

            if (o.Mode != "spline" && o.Mode != "snip")
            {
                Console.Error.WriteLine("--mode= только spline или snip");
                return 2;
            }

            if (!SuppliedLibraryGuard.Allow(o.Library))
            {
                return SuppliedLibraryGuard.Refuse(o.Library);
            }

            if (o.Part != "all" && o.Part != "known" && o.Part != "unknown")
            {
                Console.Error.WriteLine("--part= только all, known или unknown");
                return 2;
            }

            string partsPath = Path.Combine(o.Corpus, "parts.csv");
            if (!File.Exists(partsPath))
            {
                Console.Error.WriteLine("нет " + partsPath + " — укажите --corpus=<…\\CORPUS\\corpus>");
                return 2;
            }

            List<Sample> samples = ReadParts(partsPath, o);
            if (samples.Count == 0)
            {
                Console.Error.WriteLine("под отбор не попал ни один спектр");
                return 2;
            }

            // S56: объявленный состав и вещества вокруг кванта. Читается ДО
            // прогона и падает, если чего-то нет: молча посчитать «свою базу»
            // без манифеста значит посчитать не то и не сказать об этом. Ровно
            // так прожили `E31` и `B14`.
            if (o.Library == "sample" && !ReadTruth(o, samples))
            {
                return 2;
            }

            if (o.Library == "infer" && !ReadMatter(o, samples))
            {
                return 2;
            }

            Directory.CreateDirectory(o.Out);

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();

            // ⛔ `NuclideDefinitionManager` здесь БОЛЬШЕ НЕ ПОДНИМАЕТСЯ (приказ
            // Amber 01.09.2026). Он читает поставочный `config\NuclideDefinition.xml`,
            // а корпус работает только по УКАЗАННЫМ нуклидам, привязанным к
            // спектру. Прежде менеджер создавался на каждом прогоне, даже когда
            // состав брался из `nucdb`, — то есть корпусный путь держал за руку
            // файл, которым ему пользоваться нельзя. Цена нарушения измерена и
            // носит имя: фантом `Pu-238` (`N18`).

            // (`T65`) Настройки прогона печатаются У АНАЛИЗАТОРА — у того
            // самого объекта, каким потом считается каждый спектр. Шапка,
            // собранная из второго списка констант, однажды уже разошлась
            // со счётом и врала молча.
            FsaAnalyzer head = NewAnalyzer(o);

            Console.WriteLine("корпус: {0}", Path.GetFullPath(o.Corpus));
            Console.WriteLine("спектров под отбор: {0} (часть: {1}, режим: {2})",
                              samples.Count, o.Part, o.Mode);
            // Заверение с читателем: у ворот `SuppliedLibraryGuard` должно быть
            // видно, что они стояли, — иначе правило живёт только в комментарии.
            Console.WriteLine("библиотека: --lib={0} — состав из manifest.csv, линии из nucdb/matdb; "
                              + "поставочный config\\NuclideDefinition.xml в РАЗБОРЕ НЕ УЧАСТВУЕТ: проба его "
                              + "не читает и спектру не предъявляет (правило Amber 01.09.2026)",
                              o.Library);
            // ⚠ `o.Matrix` и `o.Background` — НЕ поля анализатора: матрицу
            // подбирает и подаёт сама проба, фон она подаёт или не подаёт
            // отдельным доводом. Их и печатаем у себя; всё остальное —
            // у того объекта, который считает.
            Console.WriteLine("матрица {0}, суммирование {1}, наложения {2}, рассеяние {3}, фон {4}",
                              o.Matrix ? "по спектру" : "ВЫКЛЮЧЕНА",
                              head.CascadeSumming ? "вкл" : "выкл",
                              head.PileUp ? "вкл" : "выкл",
                              head.Backscatter ? "вкл" : "выкл",
                              o.Background ? "вычитается, если есть" : "НЕ вычитается");
            // S56: чем задан состав. Печатается ПЕРВЫМ среди настроек нарочно —
            // это единица измерения всего прогона: recall и число фантомов
            // считаются ОТНОСИТЕЛЬНО предъявленного списка, и сужение списка
            // улучшает обе мерки само по себе. Прогон, у которого эта строка не
            // записана, с прежней базой сравнивать нельзя.
            Console.WriteLine("библиотека: {0}{1}",
                              o.Library == "sample"
                                  ? "ПО ОБЪЯВЛЕННОЙ ПРОБЕ (S56, manifest.csv + materials.csv)"
                                  : o.Library == "infer"
                                      ? "ВЫВЕДЕНА ИЗ ПОИСКА ПИКОВ по цепочке родителя (S57), порог доли "
                                        + InferTheta(o).ToString("P0", CultureInfo.InvariantCulture)
                                        + ", якоря " + (o.InferAnchors ? "вкл" : "ВЫКЛ")
                                        + ", новизна " + (o.InferNovelty ? "вкл" : "ВЫКЛ")
                                        + ", оборванный ряд: "
                                        + (o.InferCut == FsaChainCut.Whole ? "не ищется"
                                           : o.InferCut == FsaChainCut.Criterion
                                               ? "ГОЛОВА СУДИТ, состав весь"
                                               : "ГОЛОВА СУДИТ И ИДЁТ В СОСТАВ")
                                      : "по подписям поиска пиков (как до 18.08.2026)",
                              o.Library != "peaks"
                                  ? "; атомные образы " + (o.Atomic ? "вкл" : "ВЫКЛ")
                                    // (`T65`) Равновесие ряда МЕНЯЕТ ЧИСЛО СВОБОДНЫХ
                                    // АМПЛИТУД, а не список компонентов, и до
                                    // 25.08.2026 не печаталось вовсе: прогон
                                    // `--no-equilibrium` выглядел в журнале в точности
                                    // как умолчательный. Печатается ТА ЖЕ переменная,
                                    // которая уходит в `FsaSampleSpec.Equilibrium`
                                    // (см. <c>SpecOf</c> и ветку `infer`).
                                    + ", равновесие ряда " + (o.Equilibrium ? "вкл" : "ВЫКЛ")
                                  : "");
            if (o.Library != "peaks")
            {
                Console.WriteLine("⚠ мерки сменили смысл: recall и фантомы считаются относительно"
                                  + " ПРЕДЪЯВЛЕННОГО списка — с прежней базой напрямую не сравнивать");
            }

            Console.WriteLine("изомеры по sandia_symbol: {0}",
                              head.CascadeIsomerPartners ? "вкл" : "ВЫКЛ");
            Console.WriteLine("атомные партнёры каскада: рентген {0}, аннигиляция {1};"
                              + " окно совпадения {2:E3} с{3}",
                              head.CascadeXrayPartners ? "вкл" : "ВЫКЛ",
                              head.CascadeAnnihilationPartners ? "вкл" : "ВЫКЛ",
                              head.CoincidenceWindowSec > 0.0
                                  ? head.CoincidenceWindowSec
                                  : FsaCascadeSummer.DefaultCoincidenceWindowSec,
                              head.CoincidenceWindowSec > 0.0 ? "" : " (умолчание)");
            // (`T65`) Сетка дрейфа — У АНАЛИЗАТОРА. Здесь стояла ВТОРАЯ
            // копия её умолчаний, и 24.08.2026 она разошлась со счётом
            // молча: печаталось «±3.00 кэВ, 9 узлов», считалось ±8.00 кэВ по
            // 17 (`S93`). Поймано на `G1S16_Cd109_P5`, который возвращал
            // усиление 0.980000 с пометкой «КРАЙ» — за объявленными шапкой
            // ±0.80 %. См. <c>NewAnalyzer</c>.
            Console.WriteLine("сетка дрейфа: ноль ±{0:F2} кэВ, узлов {1} ({2});"
                              + " усиление ±{3:P2}, узлов {4} ({5})",
                              head.OffsetRangeKev, head.OffsetSteps,
                              GridStep(head.OffsetRangeKev, head.OffsetSteps, "F3", " кэВ"),
                              head.GainRange, head.GainSteps,
                              GridStep(head.GainRange, head.GainSteps, "P3", ""));
            // (`S101`) ПОЛОСА — ОБОИМИ КОНЦАМИ И ВСЛУХ. Печатается не «что
            // заказано ключом», а что каждый конец отдаёт НА САМОМ ДЕЛЕ:
            // анализатор (полоса фита и заверение) и спецификация библиотеки
            // (то, что режет линии). Пока концов было два со своими копиями,
            // прогон `--band=whole` давал побитово поставочный результат и в
            // журнале был неотличим от него.
            PrintBand(head, o);
            PrintTuning(head);
            Console.WriteLine();

            var rows = new List<Row>();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            foreach (Sample sample in samples)
            {
                rows.Add(RunOne(sample, o));
            }

            Write(rows, o);
            DumpBandAudit(rows, o);
            Summary(rows, o, clock.Elapsed.TotalSeconds);
            return 0;
        }

        /// <summary>
        /// (`S103`) ДВА ФАЙЛА, и порознь они не отвечают.
        ///
        /// `<имя>` — по строке на линию ниже `Min_Range`: норма её столбца во
        /// взвешенной метрике фита, амплитуда компонента-владельца, площадь,
        /// которую линия кладёт в модель, и доля столбца, представимая ОДНИМ
        /// сплайном континуума. Отвечает на «чего стоит столбец».
        ///
        /// `<имя>_spectra.csv` — по строке на спектр: сколько отсчётов лежит
        /// ниже `Min_Range` и чем они в модели описаны (континуум против
        /// образов). Отвечает на «а кто тогда держит эти отсчёты». Без второго
        /// файла первый читается неверно: маленькая площадь линии значит либо
        /// «модель там ничего не предсказывает», либо «предсказывает, но всё
        /// забрал континуум», и различает их только сравнение с данными.
        /// </summary>
        static void DumpBandAudit(List<Row> rows, Options o)
        {
            if (string.IsNullOrEmpty(o.BandAudit))
            {
                return;
            }

            string dir = Path.GetDirectoryName(Path.GetFullPath(o.BandAudit));
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            int lines = 0;
            using (var w = new StreamWriter(o.BandAudit, false, new UTF8Encoding(false)))
            {
                w.WriteLine("key,det,part,component,kind,keV,intensity_pct,min_range_keV,"
                            + "curve_floor_keV,col_norm,col_sum,col_sum_below,amplitude,area,"
                            + "area_below,continuum_share,model_share,degenerate");
                foreach (Row r in rows)
                {
                    if (r.LineColumns == null)
                    {
                        continue;
                    }

                    foreach (FsaLineColumn c in r.LineColumns)
                    {
                        lines++;
                        w.WriteLine(string.Join(",",
                            r.Key, r.Det, r.Part, c.Component.Replace(',', ';'), c.Kind.ToString(),
                            F(c.EnergyKev, "F3"), F(c.IntensityPct, "G6"),
                            F(r.MinRangeKev, "F2"), F(r.CurveFloorKev, "F2"),
                            F(c.ColumnNorm, "G6"), F(c.ColumnSum, "G6"), F(c.ColumnSumBelow, "G6"),
                            F(c.Amplitude, "G6"), F(c.Area, "G6"), F(c.AreaBelow, "G6"),
                            F(c.ContinuumShare, "F6"), F(c.ModelShare, "F6"),
                            c.Degenerate ? "1" : "0"));
                    }
                }
            }

            string spectra = Path.Combine(dir ?? "",
                Path.GetFileNameWithoutExtension(o.BandAudit) + "_spectra.csv");
            int kept = 0;
            using (var w = new StreamWriter(spectra, false, new UTF8Encoding(false)))
            {
                w.WriteLine("key,det,part,min_range_keV,curve_floor_keV,lines_below,"
                            + "audited_lines,chi2ndf,model_residual_pct,data_total,data_below,"
                            + "model_below,continuum_below,images_below,area_lines_below");
                foreach (Row r in rows)
                {
                    if (double.IsNaN(r.MinRangeKev) || r.Result == null)
                    {
                        continue;
                    }

                    kept++;
                    double areaLines = 0.0;
                    if (r.LineColumns != null)
                    {
                        foreach (FsaLineColumn c in r.LineColumns)
                        {
                            areaLines += c.AreaBelow;
                        }
                    }

                    w.WriteLine(string.Join(",",
                        r.Key, r.Det, r.Part, F(r.MinRangeKev, "F2"), F(r.CurveFloorKev, "F2"),
                        r.LinesBelowMinRange < 0
                            ? ""
                            : r.LinesBelowMinRange.ToString(CultureInfo.InvariantCulture),
                        (r.LineColumns == null ? 0 : r.LineColumns.Count).ToString(CultureInfo.InvariantCulture),
                        F(r.Chi2Ndf, "F4"), F(r.ModelResidual * 100.0, "F2"),
                        F(r.DataTotal, "F1"), F(r.DataBelow, "F1"), F(r.ModelBelow, "F1"),
                        F(r.ContinuumBelow, "F1"), F(r.ModelBelow - r.ContinuumBelow, "F1"),
                        F(areaLines, "F1")));
                }
            }

            Console.WriteLine("поверка столбцов (`S103`): линий {0} -> {1}; спектров {2} -> {3}",
                              lines, o.BandAudit, kept, spectra);
        }

        /// <summary>
        /// (`S88`) Кривые ОДНОГО спектра по каналам в csv — тем же форматом, что
        /// у `FsaStackShot --dump=`, чтобы разбирал их один и тот же читатель
        /// (`tools/CORPUS/scripts/wave_shape.py`).
        ///
        /// Измерение берётся у РЕЗУЛЬТАТА (<c>FsaResult.NetSpectrum</c>), а не
        /// считается здесь заново: правило «спектр минус вычтенный фон» одно на
        /// вид и на пробы, и вторая его копия рядом разъехалась бы молча.
        /// </summary>
        static void DumpCurves(string dir, string key, EnergySpectrum spectrum, FsaResult result)
        {
            Directory.CreateDirectory(dir);
            List<FsaStackLayer> layers = result.BuildStackedLayers(FsaResult.DefaultMaxNamedLayers);
            double[] net = result.NetSpectrum(spectrum.Spectrum);
            EnergyCalibration calibration = spectrum.EnergyCalibration;

            var head = new StringBuilder("ch,keV,net,model,continuum");
            foreach (FsaStackLayer layer in layers)
            {
                head.Append(',').Append(layer.Name.Replace(',', ';'));
            }

            string path = Path.Combine(dir, key + "_curves.csv");
            using (var w = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                w.WriteLine(head.ToString());
                for (int i = 0; i < spectrum.NumberOfChannels; i++)
                {
                    var line = new StringBuilder();
                    line.Append(i.ToString(CultureInfo.InvariantCulture)).Append(',')
                        .Append(calibration.ChannelToEnergy(i).ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                        .Append(Cell(net, i)).Append(',')
                        .Append(Cell(result.Model, i)).Append(',')
                        .Append(Cell(result.Continuum, i));
                    foreach (FsaStackLayer layer in layers)
                    {
                        line.Append(',').Append(Cell(layer.Curve, i));
                    }

                    w.WriteLine(line.ToString());
                }
            }
        }

        static string Cell(double[] a, int i)
        {
            double v = a != null && i < a.Length ? a[i] : 0.0;
            return v.ToString("F3", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Анализатор, настроенный КЛЮЧАМИ ПРОГОНА и только ими; всё, что
        /// зависит от конкретного спектра (окно фита, матрица отклика),
        /// добавляет <c>RunOne</c>.
        ///
        /// (`T65`) Отдельный метод заведён ради ОДНОГО источника истины:
        /// шапка прогона печатает настройки У ЭТОГО ЖЕ объекта, а не по
        /// второму списку констант рядом. Прежде запасные умолчания сетки
        /// дрейфа лежали в печати СВОЕЙ копией (±3.00 кэВ / 9 узлов);
        /// 24.08.2026 анализатор сменил их на ±8.00 кэВ / 17 узлов (`S93`) —
        /// и шапка стала врать ВСЯКИЙ раз, когда ключ не задан руками, а по
        /// ней читали абляции дрейфа (`B17`).
        ///
        /// ⚠ Правило, чтобы это не вернулось: ключ, не заданный в командной
        /// строке, СЮДА НЕ ДОХОДИТ вовсе — поле остаётся с умолчанием
        /// <see cref="FsaAnalyzer"/>. Своих умолчаний у пробы нет, кроме тех,
        /// что названы в <c>Options</c> поимённо и с доводом, почему они
        /// держатся здесь (таков <c>RebinBackground</c>).
        ///
        /// ⚠ Двоичные ключи (каскад, наложения, рассеяние, рентген,
        /// аннигиляция, изомеры, гейт вылета, гейт ΔD) выставляются
        /// БЕЗУСЛОВНО — у них есть только сторона «A» и сторона «B», и
        /// умолчание поля <c>Options</c> повторяет умолчание анализатора.
        /// Сегодня все восемь совпадают (сверено 25.08.2026), но это та же
        /// вторая копия, только пока верная.
        /// </summary>
        static FsaAnalyzer NewAnalyzer(Options o)
        {
            var analyzer = new FsaAnalyzer();
            analyzer.Mode = o.Mode == "snip"
                ? FsaAnalyzer.ContinuumMode.Snip
                : FsaAnalyzer.ContinuumMode.Spline;
            analyzer.CascadeSumming = o.Cascade;
            analyzer.CascadeSumPeaks = o.Cascade;
            analyzer.CascadeXrayPartners = o.Xray;
            analyzer.CascadeAnnihilationPartners = o.Annihilation;
            analyzer.CascadeIsomerPartners = o.Isomers;
            analyzer.CoincidenceWindowSec = o.WindowSec;
            analyzer.PileUp = o.PileUp;
            analyzer.Backscatter = o.Backscatter;
            if (o.RefitZ >= 0.0)
            {
                analyzer.RefitZ = o.RefitZ;
            }

            analyzer.EscapeGate = o.EscapeGate;

            if (o.HuberM >= 0.0)
            {
                analyzer.HuberM = o.HuberM;
            }

            // (`S98`) Полосу здесь НЕ трогаем: она уже выставлена статикой при
            // разборе ключей, и конструктор `FsaAnalyzer` её оттуда взял — как
            // и `FsaSampleLibrary`. Двигать поле анализатора отдельно значило бы
            // развести два конца, а именно это и было дефектом.
            analyzer.NoiseGamma = o.NoiseGamma;
            analyzer.NoiseBeta = o.NoiseBeta;
            analyzer.PartialResiduals = o.Partial;
            if (o.Knots > 0)
            {
                analyzer.ContinuumKnotDivisor = o.Knots;
            }

            // (`S88`) A/B-ручка густоты узлов: сплайн со штатным порогом
            // 4·ПШПВ волну 50…130 кэВ повторить не может, и надо знать —
            // это потому, что волны там нет, или потому, что её нечем
            // взять. ⚠ Значение меньше 4 ломает состав, читать после него
            // можно форму невязки, а не разложение.
            if (o.KnotFwhm > 0.0)
            {
                analyzer.ContinuumKnotFwhm = o.KnotFwhm;
            }

            // (`S85`) Ноль — ЗНАЧАЩЕЕ значение («штрафа нет»), поэтому
            // ключ отличается от умолчания отрицательным, а не нулём.
            if (o.Roughness >= 0.0)
            {
                analyzer.ContinuumRoughness = o.Roughness;
            }
            analyzer.PartialResidualGate = o.PartialGate;
            analyzer.RebinBackgroundToSpectrum = o.RebinBackground;

            // Сетка дрейфа — ключами, а не пересборкой (S6): расширять её
            // вслепую нельзя, потому что при том же числе узлов вдвое более
            // широкая сетка вдвое грубее, и цену обеих половин надо мерить
            // вместе.
            if (o.OffsetRangeKev > 0.0)
            {
                analyzer.OffsetRangeKev = o.OffsetRangeKev;
            }

            if (o.OffsetSteps > 0)
            {
                analyzer.OffsetSteps = o.OffsetSteps;
            }

            if (o.GainRange > 0.0)
            {
                analyzer.GainRange = o.GainRange;
            }

            if (o.GainSteps > 0)
            {
                analyzer.GainSteps = o.GainSteps;
            }

            return analyzer;
        }

        /// <summary>
        /// Шаг сетки дрейфа словами. ОДИН УЗЕЛ — шага нет вовсе:
        /// <c>FsaAnalyzer</c> берёт <c>Math.Max(1, …)</c> и считает без
        /// дрейфа, а деление на <c>steps - 1</c> дало бы в шапке «∞», то
        /// есть опять не то, что происходит.
        /// </summary>
        static string GridStep(double range, int steps, string format, string unit)
        {
            return steps > 1
                ? "шаг " + (2.0 * range / (steps - 1)).ToString(format, CultureInfo.InvariantCulture) + unit
                : "один узел, дрейф не ищется";
        }

        /// <summary>
        /// (`T65`) Чем ЭТОТ прогон отличается от поставочного разбора —
        /// сличением настроенного анализатора с нетронутым
        /// <c>new FsaAnalyzer()</c>, поле за полем, отражением.
        ///
        /// ⛔ Затем, что первой половины `T65` мало. Печатать поля у
        /// анализатора вместо своих констант — это перестать ВРАТЬ; но
        /// шапка молчала и о том, что ключ вообще был задан: `--huber=0`,
        /// `--knots=`, `--no-escape-gate`, `--refit-z=`, `--roughness=`,
        /// `--knot-fwhm=`, `--gamma=`, `--beta=`, `--partial`, `--no-pr-gate`,
        /// `--no-bg-rebin` не печатались ВОВСЕ, и прогон-абляция выглядел в
        /// журнале в точности как прогон умолчанием. Это тот же отказ,
        /// другой стороной: по шапке нельзя было сказать, что считали.
        /// (Ключи, не доходящие до анализатора, — `--no-equilibrium`,
        /// `--no-atomic`, `--no-matrix`, `--no-background` —
        /// печатает шапка своими строками, каждая ТОЙ ЖЕ переменной, что
        /// уходит в дело.)
        ///
        /// Отражение здесь НЕ ради краткости, а ради того, чтобы список не
        /// пришлось вести руками: ровно ведение второго списка и есть
        /// болезнь, от которой лечится строка. Новое поле, выставленное в
        /// <c>NewAnalyzer</c>, попадает сюда само; ошибиться местом можно
        /// только удалив вызов.
        ///
        /// ⚠ Читается это как «ключами изменено», а не «отличается от
        /// приложения»: <see cref="FsaAnalyzer.MinEnergy"/> и
        /// <c>MaxEnergy</c> ставит <c>RunOne</c> по рабочей полосе ПРИБОРА,
        /// и здесь их ещё нет. Матрицы и материала сцинтиллятора тоже нет —
        /// они свои у каждого спектра.
        /// </summary>
        /// <summary>Поставочная полоса, снятая ДО разбора ключей (`S101`).</summary>
        static FsaBandMode StockBandMode;

        /// <summary>Поставочный пол полосы, снятый ДО разбора ключей (`S101`).</summary>
        static double StockBandFloor;

        /// <summary>
        /// Поставочная ДОЛЯ пола, снятая ДО разбора ключей (`S101`). Нужна той
        /// же цели, что <see cref="StockBandMode"/>: эталон для строки «ключами
        /// изменено» обязан родиться с поставочной долей, иначе `--floor-frac=`
        /// в журнале не виден и абляционный прогон неотличим от умолчательного
        /// (`T65`).
        /// </summary>
        static double StockBandFraction;

        /// <summary>
        /// (`S103`) Поставочный порог доли континуума, снятый ДО разбора
        /// ключей, — той же цели, что <see cref="StockBandFraction"/>.
        /// </summary>
        static double StockShareThreshold;

        /// <summary>
        /// ⛔ ПОЛОСА ОБОИМИ КОНЦАМИ И ВСЛУХ (`S101`). Печатается не «что заказано
        /// ключом», а что каждый конец отдаёт НА САМОМ ДЕЛЕ: анализатор (полоса
        /// фита и заверение) и спецификация библиотеки (то, что режет линии).
        /// Пока у концов были свои копии, `--band=whole` давал побитово
        /// поставочный результат и в журнале был от него неотличим.
        ///
        /// Доля печатается вместе с тем, что поставляется, — умолчание, которого
        /// не видно в выводе, ничем не отличается от случайного.
        /// </summary>
        static void PrintBand(FsaAnalyzer head, Options o)
        {
            Console.WriteLine(FsaBand.EndsLine(head, new FsaSampleSpec()));

            double frac = FsaBand.DefaultFloorFraction;
            Console.WriteLine("доля пола по кривой: {0:P3}{1}",
                              frac,
                              Math.Abs(frac - FsaBand.ShippedFloorFraction) <= 1e-12
                                  ? " (поставочная)"
                                  : string.Format(CultureInfo.InvariantCulture,
                                                  " — СДВИНУТА ключом --floor-frac=, поставляется {0:P3}",
                                                  FsaBand.ShippedFloorFraction));

            // (`S103`) Порог опоры по столбцу печатается ВСЕГДА, когда режим
            // включён, — в том числе нейтральный 1.0: контрольное плечо обязано
            // быть узнаваемым в журнале, иначе его не отличить от поставки.
            if (FsaBand.DefaultMode == FsaBandMode.LibraryToFitByShare)
            {
                double thr = FsaBand.DefaultShareThreshold;
                Console.WriteLine("порог доли континуума (опора по столбцу): {0:F3}{1}",
                                  thr,
                                  Math.Abs(thr - FsaBand.ShippedShareThreshold) <= 1e-12
                                      ? " — НЕЙТРАЛЬНЫЙ: не выбрасывает ничего,"
                                        + " плечо обязано совпасть с поставкой побитово"
                                      : "");
            }
        }

        /// <summary>
        /// ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ СТОРОЖА ПОЛОСЫ (`S101`), ключ
        /// `--band-selftest`. Корпус при нём не читается вовсе.
        ///
        /// Сторож <see cref="FsaBand.EndsNote(FsaAnalyzer, FsaSampleSpec)"/>
        /// сличает то, что концы отдают на самом деле. Сторож, который никогда
        /// не срабатывал, ничем не отличается от сторожа, который не работает,
        /// — поэтому здесь он проверяется В ОБЕ СТОРОНЫ:
        ///   * на живых объектах при сдвинутой статике концы обязаны СОЙТИСЬ
        ///     (один рычаг двигает оба);
        ///   * на подставленном рассинхроне сторож обязан ОТКАЗАТЬ.
        /// Код возврата 0 — сошлось, 4 — сторож слеп.
        /// </summary>
        static int BandSelfTest()
        {
            var bad = new List<string>();

            FsaBandMode liveMode = FsaBand.DefaultMode;
            double liveFloor = FsaBand.DefaultFloor;
            double liveFrac = FsaBand.DefaultFloorFraction;
            double liveThr = FsaBand.DefaultShareThreshold;
            try
            {
                // 1. ОДИН РЫЧАГ ДВИГАЕТ ОБА КОНЦА. Двигаем СТАТИКУ и спрашиваем
                //    у каждого конца, что он отдаёт читателю.
                foreach (FsaBandMode mode in new[] { FsaBandMode.Whole,
                                                     FsaBandMode.LibraryToFit,
                                                     FsaBandMode.LibraryToFitByCurve,
                                                     FsaBandMode.LibraryToFitByShare })
                {
                    FsaBand.DefaultMode = mode;
                    FsaBand.DefaultFloor = liveFloor + 7.0;

                    var analyzer = new FsaAnalyzer();
                    var spec = new FsaSampleSpec();
                    string note = FsaBand.EndsNote(analyzer, spec);
                    if (note.Length != 0)
                    {
                        bad.Add("рычаг НЕ ОДИН при " + mode + ": " + note);
                    }

                    if (analyzer.Band != mode || spec.Band != mode)
                    {
                        bad.Add(string.Format(CultureInfo.InvariantCulture,
                            "статика {0} не доехала до концов: анализатор {1}, библиотека {2}",
                            mode, analyzer.Band, spec.Band));
                    }
                }

                // 2. ОТРИЦАТЕЛЬНЫЙ КОНТРОЛЬ: подставленный рассинхрон обязан
                //    быть НАЗВАН. Без него «пусто» ничего не доказывает.
                string caught = FsaBand.EndsNote(FsaBandMode.Whole, 0.0,
                                                 FsaBandMode.LibraryToFit, 30.0);
                if (caught.Length == 0)
                {
                    bad.Add("сторож СЛЕП: подставленный рассинхрон (Whole/0 против LibraryToFit/30) не назван");
                }

                string same = FsaBand.EndsNote(FsaBandMode.LibraryToFitByCurve, 20.0,
                                               FsaBandMode.LibraryToFitByCurve, 20.0);
                if (same.Length != 0)
                {
                    bad.Add("сторож ложно тревожит на согласных концах: " + same);
                }
            }
            finally
            {
                FsaBand.DefaultMode = liveMode;
                FsaBand.DefaultFloor = liveFloor;
                FsaBand.DefaultFloorFraction = liveFrac;
                FsaBand.DefaultShareThreshold = liveThr;
            }

            if (bad.Count != 0)
            {
                Console.Error.WriteLine("⛔ СТОРОЖ ПОЛОСЫ НЕ ПРОШЁЛ САМОПРОВЕРКУ (`S101`):");
                foreach (string b in bad)
                {
                    Console.Error.WriteLine("   " + b);
                }

                return 4;
            }

            Console.WriteLine("сторож полосы (`S101`): один рычаг двигает ОБА конца"
                              + " на всех ЧЕТЫРЁХ режимах; подставленный рассинхрон НАЗВАН,"
                              + " согласные концы не тревожат. СОШЛОСЬ.");
            return 0;
        }

        static void PrintTuning(FsaAnalyzer tuned)
        {
            // Эталон обязан родиться с ПОСТАВОЧНОЙ полосой, а не с той, что уже
            // выставил ключ, — иначе `--band=` в журнале не виден вовсе.
            FsaBandMode liveMode = FsaBand.DefaultMode;
            double liveFloor = FsaBand.DefaultFloor;
            double liveFraction = FsaBand.DefaultFloorFraction;
            double liveThreshold = FsaBand.DefaultShareThreshold;
            FsaAnalyzer stock;
            FsaBand.DefaultMode = StockBandMode;
            FsaBand.DefaultFloor = StockBandFloor;
            FsaBand.DefaultFloorFraction = StockBandFraction;
            FsaBand.DefaultShareThreshold = StockShareThreshold;
            try
            {
                stock = new FsaAnalyzer();
            }
            finally
            {
                FsaBand.DefaultMode = liveMode;
                FsaBand.DefaultFloor = liveFloor;
                FsaBand.DefaultFloorFraction = liveFraction;
                FsaBand.DefaultShareThreshold = liveThreshold;
            }

            var changed = new List<string>();
            Type t = typeof(FsaAnalyzer);
            foreach (System.Reflection.PropertyInfo p in t.GetProperties(
                         System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (!p.CanRead || p.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                Differs(changed, p.Name, Read(p.GetValue, tuned), Read(p.GetValue, stock));
            }

            foreach (System.Reflection.FieldInfo f in t.GetFields(
                         System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                Differs(changed, f.Name, Read(f.GetValue, tuned), Read(f.GetValue, stock));
            }

            // ⛔ ПОЛОСУ ОТРАЖЕНИЕ НЕ ВИДИТ, И ЭТО НЕ МЕЛОЧЬ (`S101` + `T65`).
            // После связывания рычага оба конца читают статику В МОМЕНТ
            // ОБРАЩЕНИЯ, а не хранят копию, — значит `tuned` и `stock`
            // отдают ОДНО И ТО ЖЕ, что бы ключ ни сделал, и цикл выше
            // честно находит «НИЧЕГО». Поймано прогоном `--band=whole`
            // 27.08.2026: состав разошёлся у 5 групп из 16, а шапка
            // печатала «НИЧЕГО» — абляционный прогон был в журнале
            // неотличим от умолчательного. Поэтому полоса сличается ЗДЕСЬ,
            // поимённо и по СТАТИКЕ, с эталоном, снятым до разбора ключей.
            if (FsaBand.DefaultMode != StockBandMode)
            {
                changed.Add(string.Format(CultureInfo.InvariantCulture,
                    "Band: {0} (поставка {1})", FsaBand.DefaultMode, StockBandMode));
            }

            if (Math.Abs(FsaBand.DefaultFloor - StockBandFloor) > 1e-9)
            {
                changed.Add(string.Format(CultureInfo.InvariantCulture,
                    "LibraryFloorKev: {0:F2} (поставка {1:F2})", FsaBand.DefaultFloor, StockBandFloor));
            }

            if (Math.Abs(FsaBand.DefaultFloorFraction - StockBandFraction) > 1e-12)
            {
                changed.Add(string.Format(CultureInfo.InvariantCulture,
                    "FloorFraction: {0:P3} (поставка {1:P3})",
                    FsaBand.DefaultFloorFraction, StockBandFraction));
            }

            // (`S103`) Порог опоры по столбцу — та же статика и та же слепота
            // отражения, что у полосы и доли: сличается поимённо.
            if (Math.Abs(FsaBand.DefaultShareThreshold - StockShareThreshold) > 1e-12)
            {
                changed.Add(string.Format(CultureInfo.InvariantCulture,
                    "ShareThreshold: {0:F3} (поставка {1:F3})",
                    FsaBand.DefaultShareThreshold, StockShareThreshold));
            }

            changed.Sort(StringComparer.Ordinal);
            Console.WriteLine("ключами изменено против поставочного разбора: {0}",
                              changed.Count == 0
                                  ? "НИЧЕГО (все настройки анализатора — умолчания приложения)"
                                  : string.Join("; ", changed.ToArray()));
        }

        /// <summary>
        /// Значение поля словами. Отказ геттера — тоже ЗНАЧЕНИЕ, а не повод
        /// пропустить поле: свойство, кидающее у настроенного и молчащее у
        /// нетронутого (или наоборот), попадёт в строку различий и будет
        /// видно. ⚠ Отказ ОДИНАКОВЫЙ у обоих различием не является и не
        /// печатается — ключами такое поле и правда не тронуто.
        /// </summary>
        static string Read(Func<object, object> getter, FsaAnalyzer a)
        {
            try
            {
                object v = getter(a);
                return v == null
                    ? "нет"
                    : Convert.ToString(v, CultureInfo.InvariantCulture);
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

        /// <summary>
        /// (`T85`) Что стало с матрицей отклика у ОДНОГО спектра — до разбора.
        ///
        /// ⛔ Это ПОЛОВИНА ответа, и вторая половина —
        /// <see cref="Row.MatrixApplied"/>. «Матрица НАЙДЕНА» и «матрица
        /// ПРИМЕНЕНА» суть разные утверждения, и до 27.08.2026 отчёт называл их
        /// одним словом: колонка `matrix` печатала ПРИМЕНЕНИЕ, а колонка
        /// `matrix_note` рядом писала «есть» про НАХОДКУ, и человек читал одно,
        /// а итог по частям считал другое. Расхождение измерено на снятых
        /// файлах: `G1S16_Cd109_P25` в `out_v6`, три спектра кадмия в
        /// `out_fz_lib` — всюду «есть» при нуле.
        /// </summary>
        enum MatrixState
        {
            /// <summary>До выбора матрицы не дошло: спектр не прочитан.</summary>
            Unknown,

            /// <summary>`--no-matrix`.</summary>
            OffByKey,

            /// <summary>У спектра нет кривой эффективности вовсе.</summary>
            NoCurve,

            /// <summary>У кривой нет геометрии — норма непонятной части.</summary>
            NoGeometry,

            /// <summary>Геометрия есть, матрица выключена в самой кривой.</summary>
            OffInCurve,

            /// <summary>⛔ ОТКАЗ: узел кривой есть, файла матрицы под него нет.</summary>
            NoFile,

            /// <summary>⛔ ОТКАЗ: файл есть, отпечаток не сошёлся с геометрией (`B20`).</summary>
            StampMismatch,

            /// <summary>Найдена, проверена и подана анализатору.</summary>
            Found
        }

        /// <summary>Матрица НАЙДЕНА и подана анализатору.</summary>
        static bool MatrixFound(Row row)
        {
            return row.Matrix == MatrixState.Found;
        }

        /// <summary>
        /// ⛔ ОТКАЗ, а не норма: узел кривой у спектра есть, а матрицы под него
        /// нет. Судится ПРИЗНАКОМ, а не сличением печатной строки — прежде итог
        /// сравнивал <c>MatrixNote</c> с двумя строковыми литералами, то есть
        /// держал вторую копию словаря состояний и молча разошёлся бы с ним от
        /// любой правки текста.
        /// </summary>
        static bool MatrixFailed(Row row)
        {
            return row.Matrix == MatrixState.NoFile || row.Matrix == MatrixState.StampMismatch;
        }

        /// <summary>
        /// Состояние матрицы словами — ЕДИНСТВЕННОЕ место, где оно называется.
        /// Читают его и экран, и `runs.csv`, и итог по частям.
        ///
        /// ⚠ «НАЙДЕНА» и «применена» стоят в строке порознь нарочно: находка
        /// матрицы применения не обещает. Образ строится матрицей только у
        /// компонента, у которого есть свои линии и не выставлен
        /// `WeightsAreFinal`; если отсев по значимости и гейт ΔD оставили в
        /// отчётном фите одни производные образы (обратное рассеяние,
        /// наложения), <c>FsaAnalyzer</c> вернёт `ResponseMatrixUsed = false`
        /// при живой и совершенно исправной матрице.
        /// </summary>
        static string MatrixNote(Row row)
        {
            switch (row.Matrix)
            {
                case MatrixState.OffByKey: return "выключена ключом";
                case MatrixState.NoCurve: return "кривой нет";
                case MatrixState.NoGeometry: return "геометрии нет";
                case MatrixState.OffInCurve: return "выключена в кривой";
                case MatrixState.NoFile: return "файла нет";
                case MatrixState.StampMismatch: return "отпечаток НЕ сошёлся";
                case MatrixState.Found:
                    // ⚠ Упавший спектр про ПРИМЕНЕНИЕ не говорит ничего: до
                    // результата разбор не дошёл, и «не применена» тут было бы
                    // утверждением, которого никто не проверял.
                    return row.Error != null
                        ? "НАЙДЕНА (разбор не дошёл)"
                        : row.MatrixApplied
                            ? "НАЙДЕНА, применена"
                            : "НАЙДЕНА, НЕ ПРИМЕНЕНА (в отчёте одни производные образы)";
                default: return "";
            }
        }

        /// <summary>
        /// (`S111`) ВТОРОЙ ПРОХОД ПОИСКА ПИКОВ — ПО ОСТАТКУ. Гипотеза Amber
        /// 01.09.2026: если у спектра велика невязка формы, надо вычесть
        /// найденное и заново прогнать финдер по тому, что осталось.
        ///
        /// Остаток считается ровно так, как сказано: **(спектр − фон) − модель**.
        /// Фон здесь тот же, что вычла подгонка (`FsaResult.Background` — её
        /// пиковая часть; континуум фона забрал сплайн), а модель — сумма
        /// континуума и всех образов (`FsaResult.Model`). Иначе говоря, это та
        /// же величина, что стоит в невязке разбора, только поканально.
        ///
        /// ⛔ **ФИНДЕРУ ПРЕДЪЯВЛЯЕТСЯ ПУСТАЯ БИБЛИОТЕКА** (не `null`!): второй
        /// проход ищет ПИКИ, а не подписи, и поставочный список сюда не приходит
        /// ни при каких условиях — правило Amber 01.09.2026.
        ///
        /// ⚠ **ДВЕ ЗНАЧИМОСТИ, И ПУТАТЬ ИХ НЕЛЬЗЯ.** Финдер считает свою `z` по
        /// тому спектру, который ему дали, — а у остатка дисперсия НЕ его
        /// собственная: шум остался пуассоновским от ИЗМЕРЕНИЯ, модель его не
        /// уменьшила. Поэтому рядом печатается `z_data` = площадь остатка,
        /// делённая на √(отсчёты ИЗМЕРЕНИЯ в том же окне). Первая величина
        /// говорит «финдер это увидел», вторая — «это выше шума данных», и
        /// читать надо вторую.
        ///
        /// ⚠ Отрицательная часть остатка финдеру не подаётся (там модель ВЫШЕ
        /// измерения — это тоже находка, но другого рода: её видно по `Σ−`
        /// в шапке строки).
        /// </summary>
        static List<string> ResidualPeaks(ResultData rd, FsaResult result, Sample sample)
        {
            var rows = new List<string>();
            if (rd == null || rd.EnergySpectrum == null || result == null || result.Model == null)
            {
                return rows;
            }

            int[] raw = rd.EnergySpectrum.Spectrum;
            EnergyCalibration calibration = rd.EnergySpectrum.EnergyCalibration;
            if (raw == null || calibration == null)
            {
                return rows;
            }

            int lo = Math.Max(0, result.FirstChannel);
            int hi = Math.Min(raw.Length - 1, result.LastChannel);
            var residual = new int[raw.Length];
            double negative = 0.0;
            for (int i = lo; i <= hi; i++)
            {
                double background = result.Background != null && i < result.Background.Length
                    ? result.Background[i] : 0.0;
                double value = raw[i] - background - result.Model[i];
                if (value < 0.0)
                {
                    negative += value;
                    continue;
                }

                residual[i] = (int)Math.Round(value);
            }

            // Копия измерения, у которой ОТСЧЁТЫ подменены остатком: калибровки,
            // конфигурация поиска и живое время — те же, иначе финдер мерил бы
            // другой прибор.
            ResultData probe = rd.Clone();
            probe.EnergySpectrum.Spectrum = residual;

            List<Peak> peaks = new PeakDetector().DetectPeak(
                probe, BackgroundMode.Invisible, SmoothingMethod.None,
                null, new List<NuclideDefinition>());

            foreach (Peak peak in peaks)
            {
                // Окно ±1 ПШПВ вокруг вершины — по нему берётся шум ИЗМЕРЕНИЯ.
                double fwhm = peak.FWHM > 0.0 ? peak.FWHM : 1.0;
                int from = Math.Max(lo, (int)Math.Floor(peak.Channel - fwhm));
                int to = Math.Min(hi, (int)Math.Ceiling(peak.Channel + fwhm));
                double measured = 0.0;
                for (int i = from; i <= to; i++)
                {
                    measured += raw[i];
                }

                double zData = measured > 0.0 ? peak.Count / Math.Sqrt(measured) : double.NaN;
                rows.Add(string.Join(",", new[]
                {
                    sample.Key,
                    sample.Det,
                    sample.Part,
                    peak.Energy.ToString("0.###", CultureInfo.InvariantCulture),
                    peak.Channel.ToString("0.##", CultureInfo.InvariantCulture),
                    (fwhm * calibration.ChannelToEnergy(peak.Channel + 1.0)
                     - fwhm * calibration.ChannelToEnergy(peak.Channel)).ToString("0.##", CultureInfo.InvariantCulture),
                    peak.Count.ToString("0.#", CultureInfo.InvariantCulture),
                    peak.SNR.ToString("0.##", CultureInfo.InvariantCulture),
                    zData.ToString("0.##", CultureInfo.InvariantCulture),
                    negative.ToString("0", CultureInfo.InvariantCulture),
                }));
            }

            return rows;
        }

        /// <summary>Один спектр: пики, библиотека, матрица, разложение.</summary>
        static Row RunOne(Sample sample, Options o)
        {
            var row = new Row { Key = sample.Key, Det = sample.Det, Part = sample.Part };
            string path = Path.Combine(o.Corpus, "spectra", sample.Key + ".xml");
            if (!File.Exists(path))
            {
                row.Error = "нет файла спектра";
                Report(row, o);
                return row;
            }

            // Часы и ЦП-время порознь. Разбор однопоточный, поэтому в норме они
            // почти совпадают — и ровно поэтому расхождение говорит о том, что
            // машину делили, а не о том, что разбор подорожал. Сравнивать
            // прогоны между собой надо по ЦП: T28 трое суток числилась
            // «матрица подорожала вдвое», а подорожало ожидание, и той же
            // ошибкой здесь не заметили вчетверо подорожавший разбор (S39).
            var clock = System.Diagnostics.Stopwatch.StartNew();
            TimeSpan cpuBefore = System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime;
            try
            {
                ResultData rd = Load(path);
                EnergySpectrum background = o.Background ? rd.BackgroundEnergySpectrum : null;
                row.HasBackground = background != null;

                // ⛔ S56, первый постулат (Amber 17.08.2026). Состав библиотеки
                // задаёт ОБЪЯВЛЕННАЯ проба, а не подписи поиска пиков: корпус
                // знает, что снято (`manifest.csv`), и предъявлять спектру всю
                // поставочную библиотеку значит отдавать необъяснённую структуру
                // первому подходящему кандидату (`N18`: Pu-238 долей 1.7 % на
                // одной линии 152 кэВ в 0.0009 %).
                //
                // Порядок при этом ПЕРЕВЁРНУТ против прежнего: сперва
                // библиотека, потом поиск пиков — потому что подписывать пики
                // он обязан из той же своей базы. Счёт найденных пиков от этого
                // не меняется (финдер работает до всякой подписи, а вычёркивать
                // неподписанные некому: `nuclideSet` подаётся пустым), так что
                // колонка `peaks` остаётся неподвижным контролем.
                List<FsaComponent> library;
                List<Peak> peaks;
                if (o.Library == "sample")
                {
                    FsaSampleLibrary.Report built;
                    // (`S103`) Спецификация поднята в переменную: у неё же
                    // спрашиваются `Min_Range` и ФАКТИЧЕСКИЙ пол по кривой —
                    // второй копии этих двух чисел в пробе быть не должно.
                    FsaSampleSpec spec = SpecOf(rd, sample, o);
                    library = FsaSampleLibrary.Build(spec, out built);
                    row.LibraryNote = built.ToString();
                    row.MinRangeKev = spec.MinEnergyKev;
                    row.CurveFloorKev = spec.CurveFloorKev;
                    row.LinesBelowMinRange = built.LinesBelowMinRange;
                    peaks = new PeakDetector().DetectPeak(
                        rd, BackgroundMode.Invisible, SmoothingMethod.None,
                        null, FsaSampleLibrary.AsDefinitions(library));
                }
                else
                {
                    // ⛔ Сюда попасть нельзя: ворота у разбора ключей
                    // (`SuppliedLibraryGuard`) не пускают ничего, кроме
                    // `--lib=sample`. Ветка оставлена БРОСКОМ, а не удалена
                    // молча, чтобы правка, вернувшая другой режим, упала здесь,
                    // а не посчитала корпус чужим списком.
                    throw new InvalidOperationException(SuppliedLibraryGuard.Rule);
                }

                row.Peaks = peaks.Count;
                row.LibrarySize = library.Count;

                // (S70) Состав библиотеки построчно — мерка приёмки связки
                // равновесия. Печатается ДО разбора: сравнивать надо то, что
                // предъявлено фиту, а не то, что из фита вышло, — иначе
                // разница отсева по значимости выдаёт себя за разницу состава.
                if (o.LibDump)
                {
                    foreach (FsaComponent c in library)
                    {
                        Console.WriteLine("LIB	{0}	{1}	{2}	{3}",
                                          row.Key, c.Name, c.Kind, c.Lines.Count);
                    }
                }

                // (S78) И кто из построенного до отчёта не дожил, с той
                // значимостью, с которой его видели живым в последний раз.
                // Печатается ПОСЛЕ разбора, поэтому строка идёт ниже; здесь
                // только оговорка, чтобы её искали рядом.

                // Состав ДО фита: без него «компонента нет в разложении» значит
                // разом три разных случая — финдер не нашёл пика, финдер нашёл
                // и подписал ЧУЖИМ именем, гейт выбросил после фита. Числа
                // прогона различить их не позволяют, а разбор S36 упёрся ровно
                // в это.
                if (o.Peaks)
                {
                    Console.WriteLine("  {0}: пиков {1}, компонентов {2} ({3})",
                                      sample.Key, peaks.Count, library.Count, row.LibraryNote);
                    foreach (Peak peak in peaks)
                    {
                        Console.WriteLine("      пик {0,9:F2} кэВ  {1}", peak.Energy,
                                          peak.Nuclide != null ? peak.Nuclide.Name : "(без подписи)");
                    }

                    foreach (FsaComponent component in library)
                    {
                        Console.WriteLine("      образ {0,-14} {1,-9} линий {2}",
                                          component.Name, component.Kind, component.Lines.Count);
                    }
                }
                if (library.Count == 0)
                {
                    // Пустая библиотека — не «ошибка счёта», а результат: финдер
                    // не подписал ни одного пика. Молчаливый ноль уже принимали
                    // за «пиков нет» (см. hpge-peak-search-finds-nothing), потому
                    // причина пишется отдельным словом.
                    row.Error = o.Library == "sample"
                        ? "библиотека пуста (объявленный состав не дал линий в диапазоне)"
                        : o.Library == "infer"
                            ? "библиотека пуста (вывод состава не дал ни одного родителя)"
                            : "библиотека пуста (пиков подписано 0)";
                    row.Ms = clock.Elapsed.TotalMilliseconds;
                row.CpuMs = (System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime
                             - cpuBefore).TotalMilliseconds;
                    Report(row, o);
                    return row;
                }

                // (`T65`) Настройки прогона — ОДНИМ местом, тем же, из
                // которого их берёт на печать шапка.
                FsaAnalyzer analyzer = NewAnalyzer(o);
                if (rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig peakConfig)
                {
                    analyzer.MinEnergy = peakConfig.Min_Range;
                    analyzer.MaxEnergy = peakConfig.Max_Range;

                    // (`S103`) Порог поверки столбцов — РОВНО `Min_Range`: это
                    // та граница, ниже которой линии впускает пол полосы, и
                    // спор идёт про них. Без ключа поле остаётся нулём, и
                    // анализатор поверку не считает вовсе.
                    if (!string.IsNullOrEmpty(o.BandAudit))
                    {
                        analyzer.LineColumnAuditBelowKev = peakConfig.Min_Range;
                        if (double.IsNaN(row.MinRangeKev))
                        {
                            row.MinRangeKev = peakConfig.Min_Range;
                        }
                    }
                }

                // Матрица — ровно тем же путём, каким её берёт приложение
                // (`FsaOverlay.Launch`): по Guid кривой спектра и только если
                // отпечаток сошёлся с её геометрией. Разница ОДНА: приложение
                // молча работает без матрицы, а здесь причина запоминается —
                // «понятный» спектр, посчитанный без матрицы, обязан быть виден,
                // иначе он смешает две модели внутри одной части.
                //
                // ⛔ ЗДЕСЬ РЕШАЕТСЯ ТОЛЬКО «НАЙДЕНА» (`T85`). Применена она или
                // нет, скажет уже результат разбора (<c>MatrixApplied</c>): образ
                // строит матрицей лишь компонент со своими линиями, и отчётный
                // фит может не сохранить ни одного такого.
                if (o.Matrix && rd.Efficiency != null && rd.Efficiency.HasGeometry
                    && rd.Efficiency.UseResponseMatrix)
                {
                    ResponseMatrix matrix = ResponseMatrixStore.Load(rd.Efficiency.Guid);
                    if (matrix == null)
                    {
                        row.Matrix = MatrixState.NoFile;
                    }
                    else if (!matrix.IsValidFor(rd.Efficiency.Geometry))
                    {
                        row.Matrix = MatrixState.StampMismatch;
                    }
                    else
                    {
                        analyzer.ResponseMatrix = matrix;
                        analyzer.ScintillatorMaterial = EfficiencySimulator.ScintillatorNameOf(
                            rd.Efficiency.Geometry);
                        row.Matrix = MatrixState.Found;
                    }
                }
                else if (o.Matrix)
                {
                    row.Matrix = rd.Efficiency == null ? MatrixState.NoCurve
                        : (rd.Efficiency.HasGeometry ? MatrixState.OffInCurve : MatrixState.NoGeometry);
                }
                else
                {
                    row.Matrix = MatrixState.OffByKey;
                }

                FsaEfficiency efficiency = FsaEfficiency.FromConfig(rd.Efficiency);
                row.EfficiencyName = rd.Efficiency != null ? rd.Efficiency.Name : "";

                // (`A36`) Шкала модели держится на найденных пиках — тот же
                // список, из которого собран состав.
                analyzer.DriftPeaks = peaks;

                FsaResult result = analyzer.Analyze(rd.EnergySpectrum, background,
                                                    rd.FwhmCalibration, library, efficiency);
                row.Ms = clock.Elapsed.TotalMilliseconds;
                row.CpuMs = (System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime
                             - cpuBefore).TotalMilliseconds;
                // ⛔ `T105`. `FsaAnalyzer.BandNote` — заверение анализатора о
                // полосе, которую фит взял НА ДЕЛЕ (режим, каналы, кэВ), — до
                // сих пор не имело во всём дереве НИ ОДНОГО читателя. Шапка
                // прогона печатает `FsaBand.EndsLine`, то есть НАСТРОЕННЫЕ
                // концы, а это другое: настроенное и взятое расходятся, когда
                // пол считается по кривой спектра.
                //
                // Печатается ОДИН раз за прогон, с первого разобравшегося
                // спектра, и называет его: строка про полосу без имени
                // спектра непроверяема — у разных приборов она разная.
                if (result != null && !string.IsNullOrEmpty(analyzer.BandNote)
                    && Interlocked.CompareExchange(ref bandNoteShown, 1, 0) == 0)
                {
                    Console.WriteLine("полоса ПЕРВОГО разбора ({0}): {1}",
                                      row.Key, analyzer.BandNote);
                }

                if (result == null)
                {
                    row.Error = "разложение не получилось (нет калибровок или вырожденный диапазон)";
                    Report(row, o);
                    return row;
                }

                row.Result = result;

                // (`S103`) Что сдвинул рычаг опоры по столбцу — берётся у
                // результата, а не пересчитывается здесь: считает выброс
                // анализатор, и вторая копия правила разошлась бы с первой.
                row.ShareDropped = result.ShareDroppedLines;
                row.ShareOffered = result.ShareOfferedLines;

                // (`S88`) Кривые по каналам — до всех сводок: спор о форме
                // модели решается ими, а не числом в таблице.
                if (!string.IsNullOrEmpty(o.DumpCurves))
                {
                    DumpCurves(o.DumpCurves, row.Key, rd.EnergySpectrum, result);
                }

                // (`S103`) Чем описана полоса НИЖЕ `Min_Range`: сколько там
                // отсчётов и сколько из них взял континуум. Считается по тем же
                // кривым, что выгружает `--dump-curves=`, и той же
                // `NetSpectrum`: второй копии правила «спектр минус фон» здесь
                // не заводится.
                if (!string.IsNullOrEmpty(o.BandAudit))
                {
                    row.LineColumns = result.LineColumns;
                    double[] net = result.NetSpectrum(rd.EnergySpectrum.Spectrum);
                    EnergyCalibration cal = rd.EnergySpectrum.EnergyCalibration;
                    for (int ch = result.FirstChannel; ch <= result.LastChannel; ch++)
                    {
                        row.DataTotal += net[ch];
                        if (double.IsNaN(row.MinRangeKev) || cal.ChannelToEnergy(ch) >= row.MinRangeKev)
                        {
                            continue;
                        }

                        row.DataBelow += net[ch];
                        row.ContinuumBelow += result.Continuum != null ? result.Continuum[ch] : 0.0;
                        row.ModelBelow += result.Model != null ? result.Model[ch] : 0.0;
                    }
                }

                // (S78) Кто был построен и предъявлен фиту, но до отчёта не
                // дожил — с той значимостью, с которой его видели живым в
                // последний раз. Без этой строки «образ не строился» и
                // «образ признан незначимым» в сводке неразличимы.
                if (o.LibDump && result.SuppressedImages != null)
                {
                    foreach (FsaSuppressedImage s in result.SuppressedImages)
                    {
                        Console.WriteLine("CUT	{0}	{1}	{2}	{3}",
                                          row.Key, s.Name, s.Kind,
                                          s.Z.ToString("F2", CultureInfo.InvariantCulture));
                    }
                }
                row.Chi2Ndf = result.Chi2Ndf;
                row.Chi2NdfPoisson = result.Chi2NdfPoisson;
                row.ModelResidual = result.ModelResidual;

                // Фон подан и НЕ взят — печатаем причину и снимаем признак
                // «фон есть» (S44). Прежде колонка `background` мерила наличие
                // узла в файле, и одиннадцать спектров G1S годами числились с
                // фоном, который анализатор молча отбрасывал.
                if (result.BackgroundRejected != null)
                {
                    row.HasBackground = false;
                    row.BackgroundNote = result.BackgroundRejected;
                    if (!o.Quiet)
                    {
                        Console.WriteLine("  {0}: ФОН НЕ ВЗЯТ — {1}", row.Key, result.BackgroundRejected);
                    }
                }
                row.Gain = result.Gain;
                row.OffsetChannels = result.OffsetChannels;
                row.GainOnGridEdge = result.GainOnGridEdge;
                row.OffsetOnGridEdge = result.OffsetOnGridEdge;
                // (`T85`) ПРИМЕНЕНИЕ, а не находка: у `FsaAnalyzer` признак
                // поднимается тогда и только тогда, когда хоть один образ
                // ОТЧЁТНОГО фита построен матрицей (`FitOnce`,
                // `fromMatrix |= template != null`). Производные образы
                // (обратное рассеяние) и готовые (наложения) её не трогают по
                // построению, поэтому фит, где уцелели только они, вернёт здесь
                // ложь при живой матрице.
                row.MatrixApplied = result.ResponseMatrixUsed;
                row.CascadeUsed = result.CascadeSummingUsed;
                row.EfficiencyUsed = result.EfficiencyUsed;

                // Карта невязки: где измерение выше модели. Правило общее с
                // `FsaCascadeProbe` (`ResidualScan`), чтобы числа одного и того
                // же спектра в двух пробах совпадали.
                // S60: сверка по линиям, которые обязаны быть. Считается
                // ПОСЛЕ разбора и его не трогает — это поверка результата, а
                // не часть модели.
                if (o.Audit)
                {
                    row.Audit = FsaLineAudit.Run(rd.EnergySpectrum, result,
                                                 rd.FwhmCalibration, library);
                }

                if (o.Residuals > 0)
                {
                    Console.WriteLine("  {0}: крупнейшие невязки", sample.Key);
                    ResidualScan.Print(rd.EnergySpectrum, result, o.Residuals, "      ",
                                       rd.FwhmCalibration);
                }

                if (o.NearTo > o.NearFrom)
                {
                    ResidualScan.Excess near;
                    row.NearExcess = ResidualScan.Near(rd.EnergySpectrum, result,
                                                       o.NearFrom, o.NearTo,
                                                       rd.FwhmCalibration, out near)
                        ? near.Sigmas : double.NaN;
                    row.NearCounts = double.IsNaN(row.NearExcess) ? 0.0 : near.Counts;
                }

                if (o.LimitsMc > 0)
                {
                    ValidateLimits(rd, background, library, analyzer, result, efficiency, o, sample.Key);
                }

                if (!string.IsNullOrEmpty(o.ResidualPeaks))
                {
                    row.ResidualPeaks = ResidualPeaks(rd, result, sample);
                }
            }
            catch (Exception ex)
            {
                row.Ms = clock.Elapsed.TotalMilliseconds;
                row.CpuMs = (System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime
                             - cpuBefore).TotalMilliseconds;
                row.Error = ex.GetType().Name + ": " + ex.Message;
            }

            Report(row, o);
            return row;
        }

        // ------------------------------------------------------------------
        // S56: объявленный состав спектра -> вход сборщика библиотеки
        // ------------------------------------------------------------------

        /// <summary>
        /// Метка ряда в `manifest.csv` -> корень ряда в `nucdb`.
        ///
        /// `U-238u` стоит особняком нарочно: это урановое СТЕКЛО, где ряд
        /// оборван на радии — уран попал в стекло химически очищенным, и
        /// равновесия ниже Ra-226 нет. Список членов повторяет
        /// `build_corpus.sample_lines`, где то же самое сделано для калибровки;
        /// два разных ответа на вопрос «что излучает урановое стекло» в проекте
        /// держать нельзя.
        /// </summary>
        static FsaSampleChain ChainOf(string label)
        {
            switch (label)
            {
                case "Th-232": return new FsaSampleChain("232TH");
                case "Th-228": return new FsaSampleChain("228TH");
                case "Ra-226": return new FsaSampleChain("226RA");
                case "U-238": return new FsaSampleChain("238U");
                case "U-235": return new FsaSampleChain("235U");
                case "U-238u":
                    return new FsaSampleChain("238U", "238U", "234TH", "234PAm1", "234PA", "234U");
                default: return null;
            }
        }

        /// <summary>
        /// Объявленный состав спектра плюс вещества вокруг кванта.
        ///
        /// Кристалл и проба берутся ИЗ ГЕОМЕТРИИ, если она есть: там они
        /// записаны веществом, а не догадкой, и второй источник правды завёл бы
        /// расхождение, которое двигает линии (энергия пика вылета — разность с
        /// Kα кристалла). `materials.csv` добирает то, чего геометрия не знает
        /// вовсе: защиту — у всех, кристалл и пробу — у сорока семи спектров
        /// без геометрии.
        /// </summary>
        static FsaSampleSpec SpecOf(ResultData rd, Sample sample, Options o)
        {
            var spec = new FsaSampleSpec
            {
                AtomicXray = o.Atomic,
                Equilibrium = o.Equilibrium,

                // ⛔ Кривая — только ради пола полосы по ней самой (`S98`,
                // решение Amber 27.08.2026). Расчёт живёт в
                // `FsaEfficiency.FloorAtFraction`, здесь одно присваивание.
                Efficiency = FsaEfficiency.FromConfig(rd.Efficiency)
            };
            foreach (string label in sample.Chains)
            {
                FsaSampleChain chain = ChainOf(label);
                if (chain != null)
                {
                    spec.Chains.Add(chain);
                }
            }

            foreach (string nucid in sample.Nuclides)
            {
                spec.Nuclides.Add(nucid);
            }

            if (rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig peakConfig
                && peakConfig.Max_Range > peakConfig.Min_Range)
            {
                spec.MinEnergyKev = peakConfig.Min_Range;
                spec.MaxEnergyKev = peakConfig.Max_Range;
            }

            // Порог по массовой доле 1 %: ниже него элемент — примесь, а образ
            // примеси со свободной амплитудой ведёт себя как фантом. Окно по
            // Kα — рабочий диапазон самого спектра, потому что образ из линий
            // вне окна фита есть вырожденный столбец в NNLS.
            GeometryModel geometry = rd.Efficiency != null && rd.Efficiency.HasGeometry
                ? rd.Efficiency.Geometry : null;
            if (geometry != null)
            {
                // ⚠ У КРИСТАЛЛА окно по Kα не ставится, и это не оплошность.
                // Элемент кристалла делает ДВЕ разные вещи: светит сам (тогда
                // его Kα обязана попасть в окно — это проверяется построчно при
                // сборке образа) и уносит энергию ВЫЛЕТОМ, а пик вылета стоит на
                // E − Kα, то есть глубоко внутри окна даже когда сама Kα ниже
                // его низа. Поймано измерением 18.08.2026: у ASN16 нижняя
                // граница выше 28.6 кэВ, и иод CsI отсеивался целиком — вместе
                // со своими пиками вылета, которые видны прекрасно.
                // Кристалл целиком — с массовыми долями и именем вещества
                // (`S84`): образ вылета у него ОДИН, соотношение его членов
                // задаёт вещество.
                FsaSampleLibrary.DescribeCrystal(spec, geometry.Crystal, 0.01,
                    EfficiencySimulator.ScintillatorNameOf(geometry));

                // У ПРОБЫ окно ставится: она вне кристалла, вылета не даёт, и
                // элемент, чья K-серия ниже рабочего низа, не даёт ничего.
                spec.SampleElements.AddRange(FsaSampleLibrary.HeavyElementsOf(
                    geometry.Source, 0.01, spec.MinEnergyKev, spec.MaxEnergyKev));
            }

            AddElements(spec.CrystalElements, sample.Crystal);
            AddElements(spec.SampleElements, sample.SampleMatter);
            AddElements(spec.ShieldElements, sample.Shield);
            return spec;
        }

        /// <summary>Порог доли прогона: свой или умолчание вывода.</summary>
        static double InferTheta(Options o)
        {
            return o.InferTheta >= 0.0
                ? o.InferTheta
                : FsaCompositionInference.DefaultCoverage;
        }

        /// <summary>
        /// Вещества ПРИБОРА в выведенный состав — кристалл и защита, и только
        /// они.
        ///
        /// ⛔ Проба сюда НЕ ДОБИРАЕТСЯ, и это существо замера. В поле неизвестен
        /// СОСТАВ ОБРАЗЦА — прибор же свой, и из чего сделан его кристалл и его
        /// домик, знает всякий, кто его держит. `materials.csv` для этих двух
        /// колонок есть законный источник, `manifest.csv` не читается вовсе.
        ///
        /// ⚠ Без этого сравнение с `--lib=sample` было бы нечестным в другую
        /// сторону: у сорока семи спектров корпуса геометрии нет, кристалл и
        /// защиту им даёт только эта таблица, и выведенный состав проиграл бы
        /// им атомными образами, а не составом. Разводить надо то, что мерим.
        /// </summary>
        static void SpecMatter(FsaSampleSpec spec, ResultData rd, Sample sample)
        {
            AddElements(spec.CrystalElements, sample.Crystal);
            AddElements(spec.ShieldElements, sample.Shield);
        }

        static void AddElements(List<int> into, List<string> symbols)
        {
            foreach (string symbol in symbols)
            {
                int z = MaterialDatabase.ZOf(symbol);
                if (z > 0 && !into.Contains(z))
                {
                    into.Add(z);
                }
            }
        }

        /// <summary>
        /// `manifest.csv` (что снято) и `materials.csv` (чем снято и что вокруг)
        /// в отобранные спектры.
        ///
        /// ⛔ Отсутствие любой из таблиц — ОТКАЗ, а не «поработаем без неё».
        /// Спектр без объявленного состава получил бы пустую библиотеку и
        /// строку «библиотека пуста», а спектр без веществ — молча потерял бы
        /// атомные образы; и то и другое выглядит как результат.
        /// </summary>
        static bool ReadTruth(Options o, List<Sample> samples)
        {
            string manifest = Path.Combine(o.Corpus, "manifest.csv");
            string materials = Path.Combine(o.Corpus, "materials.csv");
            if (!File.Exists(manifest))
            {
                Console.Error.WriteLine("нет " + manifest + " — с --lib=sample он обязателен");
                return false;
            }

            if (!File.Exists(materials))
            {
                Console.Error.WriteLine("нет " + materials
                    + " — соберите его: python tools/CORPUS/scripts/mk_materials.py");
                return false;
            }

            var byKey = new Dictionary<string, Sample>(StringComparer.Ordinal);
            foreach (Sample s in samples)
            {
                byKey[s.Key] = s;
            }

            var declared = new HashSet<string>(StringComparer.Ordinal);
            foreach (Dictionary<string, string> row in ReadTable(manifest))
            {
                Sample s;
                string key = Value(row, "key");
                if (!byKey.TryGetValue(key, out s))
                {
                    continue;
                }

                declared.Add(key);
                foreach (string label in Split(Value(row, "chains")))
                {
                    if (ChainOf(label) == null)
                    {
                        Console.Error.WriteLine("манифест: неизвестный ряд '" + label
                                                + "' у " + key);
                        return false;
                    }

                    s.Chains.Add(label);
                }

                foreach (string nucid in Split(Value(row, "nuclides")))
                {
                    s.Nuclides.Add(nucid);
                }
            }

            foreach (Dictionary<string, string> row in ReadTable(materials))
            {
                Sample s;
                if (!byKey.TryGetValue(Value(row, "spectrum"), out s))
                {
                    continue;
                }

                s.Crystal.AddRange(Split(Value(row, "crystal")));
                s.SampleMatter.AddRange(Split(Value(row, "sample")));
                s.Shield.AddRange(Split(Value(row, "shield")));
            }

            var silent = new List<string>();
            foreach (Sample s in samples)
            {
                if (!declared.Contains(s.Key))
                {
                    silent.Add(s.Key);
                }
                else if (s.Chains.Count == 0 && s.Nuclides.Count == 0)
                {
                    silent.Add(s.Key + " (состав пуст)");
                }
            }

            if (silent.Count > 0)
            {
                Console.Error.WriteLine("в манифесте нет состава для: "
                                        + string.Join(", ", silent.ToArray()));
                return false;
            }

            return true;
        }

        /// <summary>
        /// Только `materials.csv`, и только колонки прибора, — для `--lib=infer`.
        ///
        /// Отдельный читатель, а не флаг у <see cref="ReadTruth"/>, нарочно:
        /// тот обязан ОТКАЗАТЬ без манифеста, а этому манифест не нужен и
        /// брать его нельзя. Один метод с двумя такими режимами рано или поздно
        /// прочитал бы истину там, где её знать не полагается.
        /// </summary>
        static bool ReadMatter(Options o, List<Sample> samples)
        {
            string materials = Path.Combine(o.Corpus, "materials.csv");
            if (!File.Exists(materials))
            {
                Console.Error.WriteLine("нет " + materials
                    + " — соберите его: python tools/CORPUS/scripts/mk_materials.py");
                return false;
            }

            var byKey = new Dictionary<string, Sample>(StringComparer.Ordinal);
            foreach (Sample s in samples)
            {
                byKey[s.Key] = s;
            }

            foreach (Dictionary<string, string> row in ReadTable(materials))
            {
                Sample s;
                if (!byKey.TryGetValue(Value(row, "spectrum"), out s))
                {
                    continue;
                }

                s.Crystal.AddRange(Split(Value(row, "crystal")));
                s.Shield.AddRange(Split(Value(row, "shield")));
            }

            return true;
        }

        /// <summary>Строки CSV словарями по шапке.</summary>
        static List<Dictionary<string, string>> ReadTable(string path)
        {
            var rows = new List<Dictionary<string, string>>();
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            if (lines.Length == 0)
            {
                return rows;
            }

            List<string> head = SplitCsv(lines[0].TrimStart('﻿'));
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Length == 0)
                {
                    continue;
                }

                List<string> cells = SplitCsv(lines[i]);
                var row = new Dictionary<string, string>(StringComparer.Ordinal);
                for (int c = 0; c < head.Count && c < cells.Count; c++)
                {
                    row[head[c]] = cells[c];
                }

                rows.Add(row);
            }

            return rows;
        }

        static string Value(Dictionary<string, string> row, string column)
        {
            string value;
            return row.TryGetValue(column, out value) ? value.Trim() : "";
        }

        static List<string> Split(string cell)
        {
            var parts = new List<string>();
            foreach (string piece in cell.Split(';'))
            {
                string trimmed = piece.Trim();
                if (trimmed.Length > 0)
                {
                    parts.Add(trimmed);
                }
            }

            return parts;
        }

        /// <summary>
        /// Монте-Карло-поверка характеристических пределов (S9) на живом
        /// спектре корпуса — тем же способом, каким Xu-2022 поверяли формулы:
        /// для каждого нуклида состава спектр разыгрывается заново пуассоном
        ///
        ///   * из модели БЕЗ нуклида — доля срабатываний «есть» обязана быть
        ///     около α = 5 % (ложные срабатывания);
        ///   * из модели С нуклидом на уровне его МДА — доля пропусков обязана
        ///     быть около β = 5 %.
        ///
        /// Библиотека, настройки и дрейф-сетка не меняются, поиск пиков не
        /// перезапускается: поверяется формула пределов, а не весь конвейер.
        /// Вне окна фита в розыгрыш идут сами данные — фит их не трогает.
        /// Только режим spline: в snip средние каналов не равны модели.
        /// </summary>
        static void ValidateLimits(ResultData rd, EnergySpectrum background, List<FsaComponent> library,
                                   FsaAnalyzer analyzer, FsaResult result, FsaEfficiency efficiency,
                                   Options o, string key)
        {
            if (o.Mode != "spline")
            {
                Console.WriteLine("  {0}: --limits-mc работает только с --mode=spline", key);
                return;
            }

            // Зерно фиксировано: прогон обязан воспроизводиться до последней
            // цифры, иначе два запуска дадут «разные» доли на одном коде.
            var rng = new Random(20260814);
            int channels = rd.EnergySpectrum.NumberOfChannels;
            int[] raw = rd.EnergySpectrum.Spectrum;
            double liveTime = result.LiveTime;
            double k1 = analyzer.LimitQuantileK;

            // НЕ вошедшие кандидаты поверяются одной серией: модель их не
            // содержит, то есть сама и есть их нулевая гипотеза, и N розыгрышей
            // полной модели проверяют ложные срабатывания у всех разом.
            var absent = new List<FsaCharacteristicLimit>();
            foreach (FsaCharacteristicLimit limit in result.CharacteristicLimits)
            {
                if (!limit.Detected && !limit.Degenerate
                    && !double.IsNaN(limit.DecisionThresholdRate)
                    && (o.McComponent == null
                        || string.Equals(limit.Name, o.McComponent, StringComparison.Ordinal)))
                {
                    absent.Add(limit);
                }
            }

            if (absent.Count > 0)
            {
                double[] muFull = new double[channels];
                for (int i = 0; i < channels; i++)
                {
                    muFull[i] = i < result.FirstChannel || i > result.LastChannel
                        ? raw[i]
                        : Math.Max(0.0, result.Model[i])
                          + (result.Background != null ? result.Background[i] : 0.0);
                }

                var falseByName = new Dictionary<string, int>(StringComparer.Ordinal);
                int failedRuns = 0;
                for (int run = 0; run < o.LimitsMc; run++)
                {
                    FsaResult replay = RunSynthetic(rd, background, library, analyzer, efficiency,
                                                    muFull, rng);
                    if (replay == null)
                    {
                        failedRuns++;
                        continue;
                    }

                    foreach (FsaCharacteristicLimit limit in absent)
                    {
                        double estimate;
                        if (Exceeded(replay, limit.Name, out estimate))
                        {
                            int have;
                            falseByName.TryGetValue(limit.Name, out have);
                            falseByName[limit.Name] = have + 1;
                        }
                    }
                }

                foreach (FsaCharacteristicLimit limit in absent)
                {
                    int fp;
                    falseByName.TryGetValue(limit.Name, out fp);
                    Console.WriteLine("  {0}: {1,-14} НЕ в составе; a*={2:E3} МДА={3:E3} имп/с;"
                                      + " ложных {4}/{5} (ждём ~5 %){6}",
                                      key, limit.Name, limit.DecisionThresholdRate,
                                      limit.DetectionLimitRate, fp, o.LimitsMc - failedRuns,
                                      failedRuns > 0 ? "; отказов " + failedRuns : "");
                }
            }

            foreach (FsaComponentResult c in result.Components)
            {
                if (c.Kind == FsaComponentKind.Nuisance)
                {
                    continue;
                }

                if (o.McComponent != null
                    && !string.Equals(c.Name, o.McComponent, StringComparison.Ordinal))
                {
                    continue;
                }

                double amplitude = c.CountRate * liveTime;
                if (!(amplitude > 0.0) || double.IsNaN(c.DecisionThresholdRate)
                    || double.IsNaN(c.DetectionLimitRate))
                {
                    Console.WriteLine("  {0}: {1} — пределов нет (вырождено или не в составе), пропуск",
                                      key, c.Name);
                    continue;
                }

                double mdaAmplitude = c.DetectionLimitRate * liveTime;
                double[] mu0 = new double[channels];
                double[] mu1 = new double[channels];
                for (int i = 0; i < channels; i++)
                {
                    if (i < result.FirstChannel || i > result.LastChannel)
                    {
                        // Вне окна фита модель молчит — туда идут сами данные.
                        mu0[i] = raw[i];
                        mu1[i] = raw[i];
                        continue;
                    }

                    double without = result.Model[i] - c.Curve[i];
                    if (without < 0.0)
                    {
                        without = 0.0;
                    }

                    double bg = result.Background != null ? result.Background[i] : 0.0;
                    mu0[i] = without + bg;
                    mu1[i] = mu0[i] + mdaAmplitude * (c.Curve[i] / amplitude);
                }

                int falsePositives = 0, detections = 0, failed = 0;
                var nullEstimates = new List<double>();
                var injectedEstimates = new List<double>();
                for (int run = 0; run < o.LimitsMc; run++)
                {
                    FsaResult replay = RunSynthetic(rd, background, library, analyzer, efficiency,
                                                    mu0, rng);
                    if (replay != null)
                    {
                        double estimate;
                        bool exceeded = Exceeded(replay, c.Name, out estimate);
                        nullEstimates.Add(estimate);
                        if (exceeded)
                        {
                            falsePositives++;
                        }
                    }
                    else
                    {
                        failed++;
                    }

                    replay = RunSynthetic(rd, background, library, analyzer, efficiency,
                                          mu1, rng);
                    if (replay != null)
                    {
                        double estimate;
                        if (Exceeded(replay, c.Name, out estimate))
                        {
                            detections++;
                        }

                        injectedEstimates.Add(estimate);
                    }
                    else
                    {
                        failed++;
                    }
                }

                // Пропуск пропуску рознь: нулевая оценка значит «отсев убил или
                // NNLS отдал коллинеарным соседям», ненулевая ниже порога —
                // «увидел, но мало». Формула пределов различий не знает, а
                // чинить их пришлось бы по-разному.
                int injectedZeros = 0;
                foreach (double v in injectedEstimates)
                {
                    if (!(v > 0.0))
                    {
                        injectedZeros++;
                    }
                }

                injectedEstimates.Sort();
                double injectedMedian = injectedEstimates.Count == 0 ? 0.0
                    : injectedEstimates[injectedEstimates.Count / 2];

                double meanNull = 0.0, sdNull = 0.0;
                foreach (double v in nullEstimates)
                {
                    meanNull += v;
                }

                if (nullEstimates.Count > 1)
                {
                    meanNull /= nullEstimates.Count;
                    foreach (double v in nullEstimates)
                    {
                        sdNull += (v - meanNull) * (v - meanNull);
                    }

                    sdNull = Math.Sqrt(sdNull / (nullEstimates.Count - 1));
                }

                // Предсказание формулы: σ0 = a*/k. Сравнение с измеренным
                // разбросом нулевых оценок — проверка самой σ0, отдельная от
                // доли срабатываний (порог может быть верен и при перекошенной
                // сигме, если перекос съел квантиль).
                double predictedSigma = c.DecisionThresholdRate / k1;
                Console.WriteLine("  {0}: {1,-14} a*={2:E3} МДА={3:E3} имп/с; ложных {4}/{5} (ждём ~5 %),"
                                  + " пропусков {6}/{7} (ждём ~5 %); σ0: формула {8:E3}, розыгрыш"
                                  + " {9:E3} (сред. {10:E3}); впрыск: нулевых {11}, медиана {12:E3}{13}",
                                  key, c.Name, c.DecisionThresholdRate, c.DetectionLimitRate,
                                  falsePositives, o.LimitsMc,
                                  o.LimitsMc - detections, o.LimitsMc,
                                  predictedSigma, sdNull, meanNull,
                                  injectedZeros, injectedMedian,
                                  failed > 0 ? "; отказов разбора " + failed : "");
            }
        }

        /// <summary>
        /// Один синтетический разбор: розыгрыш каналов пуассоном вокруг
        /// заданных средних, тот же анализатор, та же библиотека. null —
        /// разбор не удался.
        /// </summary>
        static FsaResult RunSynthetic(ResultData rd, EnergySpectrum background, List<FsaComponent> library,
                                      FsaAnalyzer analyzer, FsaEfficiency efficiency,
                                      double[] mean, Random rng)
        {
            EnergySpectrum synthetic = rd.EnergySpectrum.Clone();
            int[] counts = synthetic.Spectrum;
            for (int i = 0; i < counts.Length; i++)
            {
                counts[i] = SamplePoisson(rng, mean[i]);
            }

            return analyzer.Analyze(synthetic, background, rd.FwhmCalibration, library, efficiency);
        }

        /// <summary>
        /// Решение теста по строке пределов названного компонента: оценка выше
        /// её же порога решения. Кандидат без строки — «не обнаружен» с нулевой
        /// оценкой: образ не построился, и это честное решение теста.
        /// </summary>
        static bool Exceeded(FsaResult replay, string name, out double estimate)
        {
            estimate = 0.0;
            foreach (FsaCharacteristicLimit limit in replay.CharacteristicLimits)
            {
                if (string.Equals(limit.Name, name, StringComparison.Ordinal))
                {
                    estimate = limit.CountRate;
                    return !double.IsNaN(limit.DecisionThresholdRate)
                           && limit.CountRate > limit.DecisionThresholdRate;
                }
            }

            return false;
        }

        /// <summary>
        /// Пуассонов розыгрыш: точный (Кнут) до среднего 50, дальше нормальное
        /// приближение с округлением — на счетах корпуса (до миллионов в
        /// канале) точный метод стоил бы дороже самого разбора.
        /// </summary>
        static int SamplePoisson(Random rng, double mean)
        {
            if (!(mean > 0.0))
            {
                return 0;
            }

            if (mean > 50.0)
            {
                double u1 = 1.0 - rng.NextDouble();
                double u2 = rng.NextDouble();
                double gauss = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
                double value = Math.Round(mean + gauss * Math.Sqrt(mean));
                return value < 0.0 ? 0 : (int)value;
            }

            double limit = Math.Exp(-mean);
            double p = 1.0;
            int k = 0;
            do
            {
                k++;
                p *= rng.NextDouble();
            }
            while (p > limit);

            return k - 1;
        }

        static void Report(Row row, Options o)
        {
            if (o.Quiet)
            {
                return;
            }

            if (row.Error != null)
            {
                Console.WriteLine("{0,-22} {1,-10} {2,-8} ОШИБКА: {3}",
                                  row.Key, row.Det, row.Part, row.Error);
                return;
            }

            Console.WriteLine("{0,-22} {1,-10} {2,-8} chi2/ndf {3,8:F3}  пиков {4,3}  комп. {5,2}"
                              + "  матрица: {6,-52} {7,6:F0} мс{8}",
                              row.Key, row.Det, row.Part, row.Chi2Ndf, row.Peaks, row.LibrarySize,
                              MatrixNote(row), row.Ms,
                              row.GainOnGridEdge && row.OffsetOnGridEdge ? "  КРАЙ: усиление И ноль"
                              : row.GainOnGridEdge ? "  КРАЙ: усиление"
                              : row.OffsetOnGridEdge ? "  КРАЙ: ноль шкалы" : "");
        }

        /// <summary>
        /// Итог ПО ЧАСТЯМ. Общей строки по всему корпусу здесь нет нарочно:
        /// понятная часть считается с матрицей (образ полный), непонятная — из
        /// одних пиков, и одно число на обе означало бы среднее двух разных
        /// моделей.
        /// </summary>
        /// <summary>
        /// (`S111`) Пики второго прохода — одним файлом на прогон.
        ///
        /// Один файл, а не по группам: вопрос, ради которого он заведён, —
        /// «что осталось ПО ВСЕМУ корпусу», и сводить его руками из шестнадцати
        /// кусков значило бы заводить работу там, где её нет.
        /// </summary>
        static void WriteResidualPeaks(List<Row> rows, Options o)
        {
            if (string.IsNullOrEmpty(o.ResidualPeaks))
            {
                return;
            }

            int found = 0;
            using (var writer = new StreamWriter(o.ResidualPeaks, false, new UTF8Encoding(true)))
            {
                writer.WriteLine("spectrum,detector,part,energy_kev,channel,fwhm_kev,"
                                 + "net_counts,z_finder,z_data,negative_sum");
                foreach (Row row in rows)
                {
                    if (row.ResidualPeaks == null)
                    {
                        continue;
                    }

                    foreach (string line in row.ResidualPeaks)
                    {
                        writer.WriteLine(line);
                        found++;
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine("второй проход по остатку (`S111`): пиков {0} в {1} спектрах -> {2}",
                              found,
                              rows.Count(r => r.ResidualPeaks != null && r.ResidualPeaks.Count > 0),
                              Path.GetFullPath(o.ResidualPeaks));
        }

        static void Summary(List<Row> rows, Options o, double seconds)
        {
            WriteResidualPeaks(rows, o);
            Console.WriteLine();
            // Часы — про машину, ЦП — про код. Печатаются рядом нарочно: этот
            // прогон уже дорожал вчетверо незамеченным (50 -> 236 с, S39),
            // потому что «дольше» списывали на загрузку, а списать было не на
            // чем — числа-то не менялись. Сравнивать прогоны между собой надо
            // по ЦП-времени (T28).
            double cpuSeconds = 0.0;
            foreach (Row r in rows)
            {
                cpuSeconds += r.CpuMs / 1000.0;
            }

            Console.WriteLine("=== итог по частям корпуса ({0:n0} с на часах, {1:n0} с ЦП) ===",
                              seconds, cpuSeconds);
            // (`T85`) ДВЕ колонки вместо одной «с матр.»: она называлась так,
            // будто считает найденные матрицы, а считала ПРИМЕНЁННЫЕ. Числа
            // расходятся: `out_v6` — найдена 81, применена 80; `out_fz_lib` —
            // 81 и 78.
            Console.WriteLine("{0,-10} {1,8} {2,8} {3,9} {4,10} {5,10} {6,8} {7,8} {8,8}",
                              "часть", "спектров", "найдена", "примен.", "sum chi2", "медиана",
                              "ошибок", "кр.усил", "кр.ноль");
            foreach (string part in new[] { "known", "unknown" })
            {
                var of = new List<Row>();
                foreach (Row r in rows)
                {
                    if (r.Part == part)
                    {
                        of.Add(r);
                    }
                }

                if (of.Count == 0)
                {
                    continue;
                }

                int errors = 0, found = 0, applied = 0, gainEdge = 0, offsetEdge = 0;
                // `T38`: «нет матрицы» и «матрица есть, но образа ею не
                // построено» — РАЗНЫЕ состояния, и одно число их складывало.
                // Первое — тяжёлый отказ (`B14`: 37 спектров понятной части
                // считались из одних пиков, назвавшись понятными), второе —
                // норма (у спектра уцелели только производные компоненты,
                // матрицу они не трогают по построению). Пока их печатали
                // вместе, отказ можно было принять за норму — так `B14` и
                // прожила.
                //
                // ⛔ `T85`: половины `T38` было мало. Разведены были ОТКАЗ и
                // «недостача», а два состояния под колонкой так и остались
                // одним числом — колонка «с матр.» считала ПРИМЕНЕНИЕ, а
                // соседняя `matrix_note` писала про НАХОДКУ. Теперь каждая
                // колонка считает ровно то, чем подписана, и третья строка
                // называет спектры, у которых они разошлись.
                int noFile = 0;
                var foundNotApplied = new List<string>();
                var chi = new List<double>();
                foreach (Row r in of)
                {
                    if (r.Error != null)
                    {
                        errors++;
                        continue;
                    }

                    if (MatrixFound(r))
                    {
                        found++;
                        if (r.MatrixApplied)
                        {
                            applied++;
                        }
                        else
                        {
                            foundNotApplied.Add(r.Key);
                        }
                    }
                    else if (MatrixFailed(r))
                    {
                        // Только эти два — отказ. «Кривой нет» и «геометрии
                        // нет» — НОРМАЛЬНОЕ состояние непонятной части, и
                        // считать его отказом значит кричать на все 36 её
                        // спектров каждый прогон. Признак, который кричит
                        // всегда, читать перестают на второй день.
                        noFile++;
                    }
                    else if (r.MatrixApplied)
                    {
                        // ⛔ НЕВОЗМОЖНОЕ состояние: применить можно только
                        // найденное. Молчать о нём нельзя — это значило бы, что
                        // один из двух признаков считается не там, где кажется.
                        Console.WriteLine("{0,-10} ⛔ {1}: применена НЕНАЙДЕННАЯ матрица ({2})",
                                          "", r.Key, MatrixNote(r));
                    }

                    if (r.GainOnGridEdge)
                    {
                        gainEdge++;
                    }

                    if (r.OffsetOnGridEdge)
                    {
                        offsetEdge++;
                    }

                    chi.Add(r.Chi2Ndf);
                }

                chi.Sort();
                double sum = 0.0;
                foreach (double v in chi)
                {
                    sum += v;
                }

                double median = chi.Count == 0 ? 0.0
                    : (chi.Count % 2 == 1 ? chi[chi.Count / 2]
                       : 0.5 * (chi[chi.Count / 2 - 1] + chi[chi.Count / 2]));
                Console.WriteLine("{0,-10} {1,8} {2,8} {3,9} {4,10:F1} {5,10:F2} {6,8} {7,8} {8,8}",
                                  part, of.Count, found, applied, sum, median, errors,
                                  gainEdge, offsetEdge);
                if (noFile > 0)
                {
                    // ⛔ Печатается ОТДЕЛЬНОЙ строкой и только когда есть что
                    // печатать: это отказ, а не статистика. Разница с колонкой
                    // «найдена» в том, что там недостача может быть нормой.
                    Console.WriteLine("{0,-10} ⛔ БЕЗ МАТРИЦЫ (файла нет либо отпечаток не"
                                      + " сошёлся): {1} — узел кривой у них ЕСТЬ, а матрицы под"
                                      + " него нет, и считались они из одних пиков", "", noFile);
                }

                if (foundNotApplied.Count > 0)
                {
                    // (`T85`) ЧИТАТЕЛЬ расхождения двух колонок. Без него разница
                    // «найдена 81, применена 80» не объяснена в отчёте ни словом,
                    // и читающий волен принять её за отказ вроде `B20` — а это
                    // законное состояние разбора. Спектры называются поимённо:
                    // расхождение всегда касалось единиц, и список короток.
                    foundNotApplied.Sort(StringComparer.Ordinal);
                    Console.WriteLine("{0,-10} ⚠ НАЙДЕНА, НО НЕ ПРИМЕНЕНА: {1} — {2}", "",
                                      foundNotApplied.Count,
                                      string.Join(", ", foundNotApplied.ToArray()));
                    Console.WriteLine("{0,-10}   в отчётном фите уцелели только производные образы"
                                      + " (обратное рассеяние, наложения); матрица исправна и"
                                      + " работала на проходах до отсева", "");
                }
            }

            Console.WriteLine();
            Console.WriteLine("⚠ числа каждой строки принадлежат ТОЛЬКО своей части корпуса;");
            Console.WriteLine("  «понятная» считана с матрицей отклика, «непонятная» — из одних пиков.");
            Console.WriteLine("Фантомы и recall — {0}\\..\\score.py по этим же файлам:", o.Out);
            Console.WriteLine("  python tools/pie/score.py --mode={0} --out-dir={1} --part={2}",
                              o.Mode, o.Out, o.Part);
        }

        /// <summary>Файлы того же вида, что пишет `tools/pie`, — для `score.py`.</summary>
        static void Write(List<Row> rows, Options o)
        {
            var groups = new List<string>();
            foreach (Row r in rows)
            {
                if (!groups.Contains(r.Det))
                {
                    groups.Add(r.Det);
                }
            }

            foreach (string group in groups)
            {
                string prefix = Path.Combine(o.Out, group + "_" + o.Mode);
                using (var runs = new StreamWriter(prefix + "_runs.csv", false, new UTF8Encoding(true)))
                using (var comps = new StreamWriter(prefix + "_components.csv", false, new UTF8Encoding(true)))
                using (var limits = new StreamWriter(prefix + "_limits.csv", false, new UTF8Encoding(true)))
                {
                    // Новые колонки — только В КОНЕЦ строки: score.py читает
                    // по именам (DictReader), но чужой разбор по номерам колонок
                    // вставка в середину сломала бы молча.
                    //
                    // ⛔ `T85`: колонка на десятом месте ПЕРЕИМЕНОВАНА, `matrix`
                    // -> `matrix_applied`. Место её не сдвинулось (разбор по
                    // номерам цел), а называть она стала то, что и считала
                    // всегда: применение матрицы, а не её наличие. Наличие —
                    // новая `matrix_found` В КОНЦЕ строки. Разошлись они на
                    // снятых файлах: `out_v6` 81/80, `out_fz_lib` 81/78.
                    runs.WriteLine("spectrum,det,part,chi2ndf,gain,offset_ch,drift_edge,gain_edge,"
                                   + "offset_edge,matrix_applied,"
                                   + "matrix_note,cascade,efficiency,background,peaks,components,"
                                   + "ms,cpu_ms,near_sigmas,near_counts,error,chi2ndf_pois,bg_rejected,"
                                   + "model_residual_pct,library,library_note,matrix_found,"
                                   + "share_dropped_lines,share_offered_lines");
                    // ⛔ `share_pct` С 23.08.2026 — ДОЛЯ СЛОЯ (`S76`, решение
                    // Amber): вклад компонента в ПОЛНЫЙ счёт модели с разнесённой
                    // подложкой, ровно та же величина, что печатает легенда на
                    // экране. Прежде это был «пирог» по ПИКОВЫМ отсчётам среди
                    // нуклидных образов, у служебных ноль, — и про один и тот же
                    // компонент экран и эта таблица говорили РАЗНЫЕ числа под
                    // одним словом.
                    //
                    // ⛔ ЧИСЛА КОЛОНКИ СТАЛИ ДРУГИМИ, И ЗНАМЕНАТЕЛЬ У НИХ БОЛЬШЕ:
                    // доли ниже прежних. `score.py` отбирает обнаруженное по
                    // `--sthr` (умолчание 3 %), и порог этот выведен под СТАРУЮ
                    // меру — под новую его надо выводить заново развёрткой по
                    // корпусу, как выводился порог `S57`. Строка — `S90`; пока
                    // она открыта, recall и фантомы этой базы несравнимы с
                    // прежними даже при том же прогоне.
                    //
                    // `peak_share_pct` — доля пиковых отсчётов среди ВСЕХ образов
                    // (`S49`), мера ДРУГОГО вопроса и остаётся как была.
                    // ⛔ Колонки величины пределов подписаны НЕ «cps», и это не
                    // косметика (`S68`): вес линии в образе равен I/100 × ε(E) при
                    // профилях единичной площади, значит амплитуда выражена в
                    // РАСПАДАХ, а `amplitude/liveTime` есть распадов в секунду В ШКАЛЕ
                    // ПОДАННОЙ КРИВОЙ ЭФФЕКТИВНОСТИ — не зарегистрированные импульсы.
                    // На `Th232_29.07.2022.xml` разница была видна прямо: полная
                    // скорость счёта спектра 416.37, а у Th-232 предел 607.
                    // ⚠ Беккерелями это НЕ называется по другой причине и она
                    // остаётся в силе: абсолютный уровень кривой недостоверен
                    // (`E1`, `V1`).
                    comps.WriteLine("spectrum,det,part,component,kind,share_pct,z,decay_s,peak_counts,"
                                    + "dt_decay_s,mda_decay_s,zone_chi2ndf,zone_dd,zone_n,peak_share_pct");
                    // Пределы S9 — по ВСЕМ кандидатам библиотеки, включая не
                    // вошедших в состав: у «не обнаружен» без МДА нет смысла.
                    // `mda_peak_counts` (`S68`) — отсчёты образа в его пиковых окнах
                    // при амплитуде НА ПРЕДЕЛЕ: числитель той доли, которую легенда
                    // теперь и печатает вместо величины в имп/с. `total_yield_pct`
                    // (`S69`) — суммарный выход всех γ и X на СОБСТВЕННЫЙ распад
                    // нуклида, по нему легенда решает, показывать ли кандидата
                    // своей строкой; пусто — сборка библиотеки его не знает.
                    limits.WriteLine("spectrum,det,part,component,kind,detected,decay_s,"
                                     + "dt_decay_s,mda_decay_s,degenerate,collinearity,"
                                     + "mda_peak_counts,total_yield_pct");
                    foreach (Row r in rows)
                    {
                        if (r.Det != group)
                        {
                            continue;
                        }

                        runs.WriteLine(string.Join(",",
                            Csv(r.Key), Csv(r.Det), Csv(r.Part),
                            r.Error != null ? "ERROR" : F(r.Chi2Ndf, "F4"),
                            F(r.Gain, "F6"), F(r.OffsetChannels, "F3"),
                            r.DriftOnGridEdge ? "1" : "0",
                            r.GainOnGridEdge ? "1" : "0", r.OffsetOnGridEdge ? "1" : "0",
                            r.MatrixApplied ? "1" : "0", Csv(MatrixNote(r)),
                            r.CascadeUsed ? "1" : "0", r.EfficiencyUsed ? "1" : "0",
                            r.HasBackground ? "1" : "0",
                            r.Peaks.ToString(CultureInfo.InvariantCulture),
                            r.LibrarySize.ToString(CultureInfo.InvariantCulture),
                            F(r.Ms, "F0"), F(r.CpuMs, "F0"),
                            F(r.NearExcess, "F2"), F(r.NearCounts, "F0"),
                            Csv(r.Error ?? ""),
                            r.Error != null ? "" : F(r.Chi2NdfPoisson, "F4"),
                            Csv(r.BackgroundNote),
                            r.Error != null ? "" : F(100.0 * r.ModelResidual, "F3"),
                            Csv(o.Library), Csv(r.LibraryNote),
                            MatrixFound(r) ? "1" : "0",
                            r.ShareDropped.ToString(CultureInfo.InvariantCulture),
                            r.ShareOffered.ToString(CultureInfo.InvariantCulture)));

                        if (r.Result == null)
                        {
                            continue;
                        }

                        foreach (FsaComponentResult c in r.Result.Components)
                        {
                            comps.WriteLine(string.Join(",",
                                Csv(r.Key), Csv(r.Det), Csv(r.Part), Csv(c.Name),
                                c.Kind.ToString().ToLowerInvariant(),
                                F(c.SharePercent, "F3"), F(c.Z, "F2"),
                                F(c.CountRate, "E4"), F(c.PeakCounts, "F1"),
                                F(c.DecisionThresholdRate, "E4"), F(c.DetectionLimitRate, "E4"),
                                F(c.ZoneChi2Ndf, "F3"), F(c.ZoneDeltaD, "F2"),
                                c.ZoneChannels.ToString(CultureInfo.InvariantCulture),
                                F(c.PeakSharePercent, "F3")));
                        }

                        foreach (FsaCharacteristicLimit L in r.Result.CharacteristicLimits)
                        {
                            limits.WriteLine(string.Join(",",
                                Csv(r.Key), Csv(r.Det), Csv(r.Part), Csv(L.Name),
                                L.Kind.ToString().ToLowerInvariant(),
                                L.Detected ? "1" : "0",
                                F(L.CountRate, "E4"),
                                F(L.DecisionThresholdRate, "E4"), F(L.DetectionLimitRate, "E4"),
                                L.Degenerate ? "1" : "0", F(L.Collinearity, "F4"),
                                F(L.DetectionLimitPeakCounts, "F1"),
                                F(L.TotalYieldPercent, "F4")));
                        }
                    }
                }
            }

            if (o.Audit)
            {
                WriteAudit(rows, o);
            }

            Console.WriteLine();
            Console.WriteLine("записано групп: {0} -> {1}", groups.Count, Path.GetFullPath(o.Out));

            // ⛔ (`S103`) ЧИТАТЕЛЬ РЫЧАГА. Развёртка, чьи корпусные числа не
            // шелохнулись, обязана уметь сказать, ЧТО при этом двигалось, —
            // иначе «рычаг не важен» и «рычаг не доехал» неразличимы, а на этом
            // дереве уже дважды ловили второе (`S101`, `T65`). Печатается
            // всегда, когда режим включён, в том числе на контрольном плече с
            // нулём выброшенных: ноль при ненулевом знаменателе — это результат.
            if (FsaBand.DefaultMode == FsaBandMode.LibraryToFitByShare)
            {
                int dropped = 0, offered = 0, touched = 0, seen = 0;
                foreach (Row r in rows)
                {
                    if (r.Error != null)
                    {
                        continue;
                    }

                    seen++;
                    dropped += r.ShareDropped;
                    offered += r.ShareOffered;
                    if (r.ShareDropped > 0)
                    {
                        touched++;
                    }
                }

                Console.WriteLine();
                Console.WriteLine("опора по столбцу (`S103`), порог {0:F3}: выброшено {1} линий"
                                  + " из {2} подпороговых, тронуто {3} спектров из {4}",
                                  FsaBand.DefaultShareThreshold, dropped, offered, touched, seen);
            }
        }

        /// <summary>
        /// (S60) Сверка по линиям, которые обязаны быть: файл со всеми строками
        /// и итог ПОЛОСАМИ ЭНЕРГИИ.
        ///
        /// ⛔ Итог печатается медианой ОТНОШЕНИЯ (измерено/ожидание), а не
        /// медианой Z, и это не косметика. Z растёт со статистикой: на спектре в
        /// сто миллионов отсчётов он кричит там, где расхождение ничтожно, а на
        /// слабом молчит при расхождении вдвое. Отношение сравнимо поперёк
        /// корпуса, где счета разнятся в тысячи раз. Z печатается рядом — им
        /// читается ЗНАЧИМОСТЬ расхождения, а не его величина.
        ///
        /// ⚠ В итог идут только линии с чистотой ≥ 0.5, то есть те, где больше
        /// половины ожидаемой площади принадлежит своему компоненту. Иначе в
        /// сводку попадёт чужое расхождение под чужим именем.
        /// </summary>
        static void WriteAudit(List<Row> rows, Options o)
        {
            string path = Path.Combine(o.Out, "lines_" + o.Mode + ".csv");
            int bands = FsaLineAudit.Bands.Length - 1;
            var ratioAll = new List<double>[bands];
            var ratioMatrix = new List<double>[bands];
            var ratioNoMatrix = new List<double>[bands];
            var absZ = new List<double>[bands];
            for (int i = 0; i < bands; i++)
            {
                ratioAll[i] = new List<double>();
                ratioMatrix[i] = new List<double>();
                ratioNoMatrix[i] = new List<double>();
                absZ[i] = new List<double>();
            }

            int total = 0, obligatory = 0, agreed = 0, missing = 0;
            using (var file = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                // (`T85`) Колонка называет ПРИМЕНЕНИЕ: сверка ниже делит линии
                // на «с матрицей» и «без неё» по тому, чем построен образ, а не
                // по тому, лежала ли матрица на диске.
                file.WriteLine("spectrum,det,part,matrix_applied,component,energy_kev,lines,"
                               + "intensity_pct,"
                               + "expected,measured,sigma,z,ratio,purity,decision,obligatory");
                foreach (Row r in rows)
                {
                    if (r.Audit == null)
                    {
                        continue;
                    }

                    foreach (FsaLineAudit.LineCheck c in r.Audit)
                    {
                        total++;
                        file.WriteLine(string.Join(",",
                            Csv(r.Key), Csv(r.Det), Csv(r.Part), r.MatrixApplied ? "1" : "0",
                            Csv(c.Component), F(c.EnergyKev, "F2"),
                            c.Lines.ToString(CultureInfo.InvariantCulture),
                            F(c.IntensityPct, "F4"),
                            F(c.Expected, "F1"), F(c.Measured, "F1"), F(c.Sigma, "F1"),
                            F(c.Z, "F2"), F(c.Ratio, "F4"), F(c.Purity, "F3"),
                            F(c.DecisionThreshold, "F1"), c.Obligatory ? "1" : "0"));

                        if (!c.Obligatory)
                        {
                            continue;
                        }

                        obligatory++;
                        if (Math.Abs(c.Z) <= 3.0)
                        {
                            agreed++;
                        }

                        // «Обязана быть, а её нет»: измеренная площадь ниже
                        // порога решения. Это самый резкий сигнал сверки — он
                        // означает, что предсказание не подтвердилось вовсе.
                        if (c.Measured < c.DecisionThreshold)
                        {
                            missing++;
                        }

                        int band = FsaLineAudit.BandOf(c.EnergyKev);
                        if (band < 0 || !(c.Purity >= 0.5) || double.IsNaN(c.Ratio))
                        {
                            continue;
                        }

                        ratioAll[band].Add(c.Ratio);
                        absZ[band].Add(Math.Abs(c.Z));
                        if (r.MatrixApplied)
                        {
                            ratioMatrix[band].Add(c.Ratio);
                        }
                        else
                        {
                            ratioNoMatrix[band].Add(c.Ratio);
                        }
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine("=== S60: сверка по линиям, которые ОБЯЗАНЫ быть ===");
            Console.WriteLine("строк всего {0}; обязательных {1} (порог решения Карри, k = {2});",
                              total, obligatory, FsaLineAudit.DecisionK);
            Console.WriteLine("  из них |Z| <= 3: {0} ({1:P1}); НЕ подтвердилось вовсе: {2} ({3:P1})",
                              agreed, obligatory > 0 ? (double)agreed / obligatory : 0.0,
                              missing, obligatory > 0 ? (double)missing / obligatory : 0.0);
            Console.WriteLine();
            Console.WriteLine("{0,-12} {1,7} {2,10} {3,10} {4,10} {5,9}",
                              "полоса, кэВ", "линий", "изм/ожид", "с матрицей", "без неё", "мед.|Z|");
            for (int i = 0; i < bands; i++)
            {
                if (ratioAll[i].Count == 0)
                {
                    continue;
                }

                Console.WriteLine("{0,-12} {1,7} {2,10:F3} {3,10} {4,10} {5,9:F1}",
                                  FsaLineAudit.BandName(i), ratioAll[i].Count,
                                  FsaLineAudit.Median(ratioAll[i]),
                                  ratioMatrix[i].Count > 0
                                      ? FsaLineAudit.Median(ratioMatrix[i]).ToString("F3", CultureInfo.InvariantCulture)
                                      : "—",
                                  ratioNoMatrix[i].Count > 0
                                      ? FsaLineAudit.Median(ratioNoMatrix[i]).ToString("F3", CultureInfo.InvariantCulture)
                                      : "—",
                                  FsaLineAudit.Median(absZ[i]));
            }

            Console.WriteLine();
            Console.WriteLine("⚠ читать колонку «изм/ожид»: единица — модель предсказала площадь верно.");
            Console.WriteLine("  ХОД этой колонки по энергии и есть проверка матрицы — доля пика падает");
            Console.WriteLine("  с энергией, и ошибка в ней перекошена туда же. Одно число ничего не скажет.");
            Console.WriteLine("построчно: {0}", Path.GetFullPath(path));
        }

        static string F(double value, string format)
        {
            return double.IsNaN(value) || double.IsInfinity(value)
                ? "" : value.ToString(format, CultureInfo.InvariantCulture);
        }

        static string Csv(string value)
        {
            if (value == null)
            {
                return "";
            }

            return value.IndexOfAny(new[] { ',', '"', '\n' }) < 0
                ? value : "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>Разбор `parts.csv` с отбором по ключам запуска.</summary>
        static List<Sample> ReadParts(string path, Options o)
        {
            var samples = new List<Sample>();
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            for (int i = 1; i < lines.Length; i++)
            {
                List<string> cells = SplitCsv(lines[i]);
                if (cells.Count < 3 || cells[0].Length == 0)
                {
                    continue;
                }

                var sample = new Sample { Key = cells[0], Det = cells[1], Part = cells[2] };

                // Германий выброшен здесь, а не отбором вызывающего: приказ
                // Amber 08.08.2026 — новых задач по нему не заводить и в счёт
                // не брать. Ключа, который бы его вернул, нет нарочно.
                if (sample.Part == "excluded")
                {
                    continue;
                }

                if (o.Part != "all" && sample.Part != o.Part)
                {
                    continue;
                }

                if (o.Groups != null && !o.Groups.Contains(sample.Det))
                {
                    continue;
                }

                if (o.Only != null && !o.Only.Contains(sample.Key))
                {
                    continue;
                }

                samples.Add(sample);
                if (o.Limit > 0 && samples.Count >= o.Limit)
                {
                    break;
                }
            }

            return samples;
        }

        static List<string> SplitCsv(string line)
        {
            var cells = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"');
                            i++;
                        }
                        else
                        {
                            quoted = false;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else if (c == '"')
                {
                    quoted = true;
                }
                else if (c == ',')
                {
                    cells.Add(sb.ToString());
                    sb.Length = 0;
                }
                else
                {
                    sb.Append(c);
                }
            }

            cells.Add(sb.ToString());
            return cells;
        }

        /// <summary>
        /// Спектр читается ровно так же, как его читают `FsaCascadeProbe` и
        /// `FsaPaletteProbe`: с достройкой счёта и ПШПВ-калибровки умолчанием,
        /// как это делает `DocEnergySpectrum`. Иначе числа проб на одном файле
        /// не сойдутся, а разница будет не в том, что мерили.
        /// </summary>
        /// <summary>
        /// Узлы, которые сборка законно не знает и о которых кричать НЕ НАДО.
        ///
        /// `Pulses` — узел АУДИО-спектрометров (решение Amber 18.08.2026: в
        /// корпусе только Digital MCA, единственный аудио-прибор — ASN8).
        /// Лежит в 44 спектрах из 129 и ВЕЗДЕ пуст (`&lt;Pulses /&gt;`), теряться
        /// нечему. Держать его в крике значило бы ругаться на каждый третий
        /// спектр и приучить не смотреть на предупреждение — ровно тот вред,
        /// ради устранения которого читатель и заведён (родня `T47`).
        /// </summary>
        static readonly List<string> KnownHarmlessNodes = new List<string> { "Pulses" };

        static ResultData Load(string path)
        {
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;

            // T41: НЕИЗВЕСТНЫЙ ЭЛЕМЕНТ XML-десериализатор пропускает МОЛЧА, и это
            // уже стоило ложного вывода. 16.08.2026 в рабочем каталоге лежала
            // сборка СТАРШЕ исходников: `PowerFwhmCalibration` ей был неизвестен,
            // узел кривой ПШПВ выпал, `rd.FwhmCalibration` осталась null, проба
            // законно откатилась на калибровку прибора — и прогон отработал без
            // единой ошибки, выдав правдоподобные числа (понятная 1766.1 при
            // невязке 53 %), из которых был сделан вывод «дефект в самом узле».
            // На свежей сборке узел работает. Признак отказа теперь имеет
            // читателя: каждый пропущенный узел называется вслух вместе с ИМЕНЕМ
            // СПЕКТРА — этого и не хватало, чтобы увидеть причину, а не следствие.
            // ⚠ Подписка на `UnknownNode`, а НЕ на `UnknownElement`: первое
            // событие приходит на узел ЛЮБОГО вида, второе — только на элемент,
            // и оба они на неизвестный элемент срабатывают вместе. Одной
            // подписки хватает, а имена всё равно копятся без повторов.
            var skipped = new List<string>();
            XmlNodeEventHandler onUnknown = (sender, e) =>
            {
                if (e.NodeType == System.Xml.XmlNodeType.Element
                    && !string.IsNullOrEmpty(e.Name)
                    && !KnownHarmlessNodes.Contains(e.Name)
                    && !skipped.Contains(e.Name))
                {
                    skipped.Add(e.Name);
                }
            };
            serializer.UnknownNode += onUnknown;

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }

            serializer.UnknownNode -= onUnknown;

            if (skipped.Count > 0)
            {
                Console.Error.WriteLine("⚠ " + Path.GetFileNameWithoutExtension(path)
                                        + ": сборка не знает узлов "
                                        + string.Join(", ", skipped.ToArray())
                                        + " — они ПРОПУЩЕНЫ молча (T41: сборка старше исходников?)");
            }

            ResultData rd = file.ResultDataList[0];
            EnergySpectrum s = rd.EnergySpectrum;
            if (s != null && s.Spectrum != null && s.TotalPulseCount == 0)
            {
                long total = 0;
                for (int i = 0; i < s.Spectrum.Length; i++)
                {
                    total += s.Spectrum[i];
                }

                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
            }

            // ⛔ Прибор и его настройки поиска пиков — ОДНИМ правилом на все
            // пробы (`ProbeDeviceConfig`, строка `S82`). Прежде здесь стояла
            // своя копия, и она молча брала умолчания библиотеки: SNR 10 против
            // корпусных 4, диапазон от 30 кэВ против 15 и 20 у половины групп.
            // Отказ называется поимённо и НЕ глотается — иначе он выглядит как
            // работающий прогон, чем `S82` и была.
            string device = ProbeDeviceConfig.Attach(rd);
            if (device.Contains("НЕТ") || device.Contains("нет"))
            {
                Console.Error.WriteLine("⚠ " + Path.GetFileNameWithoutExtension(path) + ": " + device);
            }

            if (rd.FwhmCalibration == null
                && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                if (cfg.FwhmCalibration == null && rd.EnergySpectrum != null)
                {
                    cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(
                        cfg, rd.EnergySpectrum.EnergyCalibration);
                }

                if (cfg.FwhmCalibration != null)
                {
                    rd.FwhmCalibration = cfg.FwhmCalibration.Clone();
                }
            }

            return rd;
        }

        sealed class Options
        {
            public string Corpus = "corpus";
            public string Out = "out";
            public string Part = "all";
            public string Mode = "spline";
            public bool Matrix = true;
            public bool Cascade = true;
            public bool Xray = true;            // S27: K-рентген партнёром
            public bool Annihilation = true;    // S27: кванты 511 партнёром
            public bool Isomers = true;         // S27: изомеры по sandia_symbol
            public double WindowSec;            // S27: окно совпадения, с; 0 — умолчание
            public bool PileUp = true;
            public bool Backscatter = true;
            public bool Background = true;
            public bool Quiet;
            public bool Peaks;

            /// <summary>Сколько крупнейших невязок печатать на спектр (0 — не печатать).</summary>
            public int Residuals;

            /// <summary>(S111) Куда писать пики ВТОРОГО прохода — по остатку.</summary>
            public string ResidualPeaks = "";

            /// <summary>
            /// (`B17`) Делитель диапазона, задающий самый редкий шаг узлов
            /// континуума; 0 — ключ не задан, умолчание у анализатора.
            /// ⚠ (`T65`) Здесь СТОЯЛО 128 — вторая копия
            /// <c>FsaAnalyzer.ContinuumKnotDivisor</c>, то есть тот же заряд,
            /// что рванул у сетки дрейфа: числа совпадали, пока не разошлись.
            /// </summary>
            public int Knots;

            /// <summary>Розыгрышей Монте-Карло-поверки пределов S9 (0 — не поверять).</summary>
            public int LimitsMc;

            /// <summary>Поверять только этот компонент (`--limits-mc`); null — все.</summary>
            public string McComponent;

            /// <summary>Окно энергий, про которое спрашивают отдельно (V4: ~460 кэВ).</summary>
            public double NearFrom, NearTo;
            public int Limit;
            public double OffsetRangeKev;   // 0 — ключ не задан, умолчание у анализатора
            public int OffsetSteps;         // 0 — ключ не задан, умолчание у анализатора
            public double GainRange;        // 0 — ключ не задан, умолчание у анализатора
            public int GainSteps;           // 0 — ключ не задан, умолчание у анализатора

            // (`T65`) ЧИСЛА УМОЛЧАНИЙ ЗДЕСЬ НЕ ПОВТОРЯЮТСЯ. Стояли «(3.0)»,
            // «(9)», «(0.008)» — и устарели молча 24.08.2026, когда `S93`
            // расширил сетку до ±8 кэВ / ±2 % по 17 узлов.

            /// <summary>
            /// Порог Хубера в сигмах; отрицательный — умолчание
            /// анализатора (`T65`: число здесь не повторяется). Ноль
            /// ВЫКЛЮЧАЕТ перевзвешивание — это A-сторона S41:
            /// Хубер в решателе живёт с переноса из pie и входит в базу
            /// корпуса, так что мерится его ОТКЛЮЧЕНИЕ, а не включение.
            /// </summary>
            public double HuberM = -1.0;

            /// <summary>
            /// (S9 «б») Порог значимости отсева перед вторым проходом;
            /// отрицательный — умолчание анализатора (`T65`: число здесь
            /// не повторяется), ноль ВЫКЛЮЧАЕТ отсев целиком. Заведён,
            /// чтобы развести две ступени, каждая из
            /// которых могла занизить МДА слабого компонента: первый NNLS
            /// (сигнал уходит соседям) или «предварительный анализ состава»
            /// (компонент выброшен и второй проход считается без него).
            /// </summary>
            public double RefitZ = -1.0;

            /// <summary>(S47) Гейт образов вылета при матрице; A-сторона — `--no-escape-gate`.</summary>
            public bool EscapeGate = true;

            /// <summary>(S43) γ составного шума D = F + γ²F²; 0 — выключено.</summary>
            public double NoiseGamma;

            /// <summary>(S43) β коррелированности вычитаемого фона; 0 — выключено.</summary>
            public double NoiseBeta;

            /// <summary>(P6) Считать парциальные невязки (дорого: рефит на компонент).</summary>
            public bool Partial;

            /// <summary>
            /// (P6 «б») Гейт по ΔD&lt;0 с перефитом. С 14.08.2026 включён в
            /// анализаторе умолчанием (решение Amber) — A/B-сторона теперь
            /// `--no-pr-gate`.
            /// </summary>
            public bool PartialGate = true;

            /// <summary>
            /// (S45) Перекладывать фон на шкалу спектра перед вычитанием.
            /// Умолчание — ДА с 16.08.2026, вслед за анализатором: после того
            /// как фон той же настройки стал жить в шкале переднего плана
            /// (`build_corpus.same_setting`), перекладка перестала вредить.
            /// A/B-сторона — `--no-bg-rebin`; ключ `--bg-rebin` оставлен, чтобы
            /// прежние замеры воспроизводились дословно.
            ///
            /// ⚠ Умолчание держится ЗДЕСЬ, а не наследуется от анализатора:
            /// строка 16.08.2026 перекрывала `FsaAnalyzer` своим `false`, и
            /// прогон «с новым умолчанием» тихо повторил старые числа.
            /// </summary>
            public bool RebinBackground = true;

            /// <summary>
            /// (S56, S57) Чем задан состав библиотеки: `sample` — объявленной
            /// пробой (первый постулат Amber, умолчание с 18.08.2026), `peaks` —
            /// подписями поиска пиков (как было), `infer` — ВЫВЕДЕН из поиска
            /// пиков по цепочке родителя (`S57`, прибор в поле).
            ///
            /// ⛔ Три эти числа НЕ ОДНОГО СМЫСЛА и рядом не ставятся без оговорки.
            /// `sample` знает истину из `manifest.csv` — это верхняя граница
            /// того, что вывод может дать; `peaks` — нижняя, состав как есть.
            /// `infer` меряется ПРОТИВ ОБЕИХ: он обязан подойти к первой и
            /// заметно обойти вторую, иначе выводить нечего.
            /// </summary>
            public string Library = "sample";

            /// <summary>
            /// (S57) Порог доли ожидаемо-различимых линий; отрицательный —
            /// умолчание <c>FsaCompositionInference.DefaultCoverage</c>. Ключ
            /// заведён ради РАЗВЁРТКИ: величина порога обязана быть выведена
            /// замером по корпусу, а не назначена.
            /// </summary>
            public double InferTheta = -1.0;

            /// <summary>
            /// (S57) Второй путь в состав — неспутываемая главная линия.
            /// A/B-сторона <c>--no-infer-anchor</c>: якорь оправдан, только если он
            /// НАБИРАЕТ recall быстрее, чем набирает фантомов.
            /// </summary>
            public bool InferAnchors = true;

            /// <summary>
            /// (S57) Третье условие приёма — кандидат обязан принести СВОЮ
            /// структуру, а не сесть на чужую. A/B-сторона <c>--no-infer-novel</c>.
            /// </summary>
            public bool InferNovelty = true;

            /// <summary>
            /// (S65) Что делать с ОБОРВАННЫМ рядом: `--infer-head` /
            /// `--infer-head-only`. Умолчание — как было до 25.08.2026.
            /// </summary>
            public FsaChainCut InferCut = FsaChainCut.Whole;

            /// <summary>(S56) Атомные образы: рентген и пики вылета. A/B — `--no-atomic`.</summary>
            public bool Atomic = true;

            // ⛔ Поле вездесущих рядов снято 01.09.2026 вместе с самим механизмом
            // (решение Amber по `S110`): состав — ровно объявленное.

            /// <summary>
            /// (S70) Ряд связан равновесием: одна колонка, одна свободная
            /// амплитуда, относительные веса от ветвления. A/B-сторона
            /// `--no-equilibrium` возвращает свободную амплитуду каждому члену.
            /// </summary>
            public bool Equilibrium = true;

            /// <summary>
            /// (S70) Печатать СОСТАВ БИБЛИОТЕКИ построчно — мерка приёмки
            /// связки равновесия: она не смеет убирать ни одного компонента,
            /// кроме слияния членов ряда, и проверяется это сравнением двух
            /// таких распечаток, а не рассуждением. `--lib-dump`.
            /// </summary>
            public bool LibDump;

            /// <summary>
            /// (`S88`) Каталог, куда класть кривые ПО КАНАЛАМ на каждый
            /// разобранный спектр: измерение за вычетом фона, модель, континуум
            /// и по колонке на слой. Пусто — не выгружать.
            ///
            /// Заведено потому, что спор «модель кривая или спектр такой»
            /// решается только счётом по каналам, а до сегодня такую выгрузку
            /// умела ОДНА проба на ОДНОМ спектре (`FsaStackShot --dump=`), то
            /// есть на корпусе вопрос было нечем задать. Формат тот же, что у
            /// неё, — и разбирает обе `tools/CORPUS/scripts/wave_shape.py`.
            /// </summary>
            public string DumpCurves;

            /// <summary>
            /// (`S103`) Файл, куда выгрузить ПОВЕРКУ СТОЛБЦОВ ЛИНИЙ ниже
            /// `Min_Range`: по строке на линию — норма её столбца во взвешенной
            /// метрике фита, амплитуда владельца, площадь, которую линия кладёт
            /// в модель, и доля столбца, представимая сплайном континуума.
            /// Рядом кладётся `<имя>_spectra.csv` — по строке на спектр: сколько
            /// отсчётов лежит ниже `Min_Range` и чем они в модели описаны.
            /// Пусто — не считать вовсе (умолчание анализатора — ноль).
            ///
            /// ⚠ Ключ НИЧЕГО НЕ МЕНЯЕТ в разборе: поверка считается после
            /// отчётного фита по его же столбцам. Проверено сличением
            /// `components`/`limits` с прогоном без ключа.
            /// </summary>
            public string BandAudit;

            /// <summary>
            /// (`S88`) Густой край шага узлов континуума в ПШПВ; 0 — не трогать
            /// умолчание анализатора (4). ⚠ Абляция, а не настройка.
            /// </summary>
            public double KnotFwhm;

            /// <summary>
            /// (`S85`) Вес штрафа на излом континуума; отрицательный — не
            /// трогать умолчание анализатора.
            /// </summary>
            public double Roughness = -1.0;

            /// <summary>
            /// (`S98`) Полоса разбора, ключ `--band=`; `null` — не трогать
            /// умолчание анализатора (`FsaBand.DefaultMode`).
            /// </summary>
            public string BandName;

            /// <summary>
            /// (`S98`) Пол полосы библиотеки, кэВ, ключ `--band-floor=`;
            /// отрицательный — не трогать умолчание.
            /// </summary>
            public double BandFloor = -1.0;

            /// <summary>
            /// (`S98`/`S101`) Доля от максимума кривой, задающая пол полосы,
            /// ключ `--floor-frac=`; отрицательная — не трогать умолчание
            /// (<c>FsaBand.DefaultFloorFraction</c>).
            /// </summary>
            public double FloorFraction = -1.0;

            /// <summary>
            /// (`S103`) Порог доли континуума у опоры полосы по столбцу, ключ
            /// `--share-thr=`; отрицательный — не трогать умолчание
            /// (<c>FsaBand.DefaultShareThreshold</c> = 1.0, нейтральное).
            /// </summary>
            public double ShareThreshold = -1.0;

            /// <summary>(`S101`) Положительный контроль сторожа полосы,
            /// ключ `--band-selftest`; корпус при нём не читается.</summary>
            public bool BandSelfTest;

            /// <summary>(S60) Сверять линии, которые обязаны быть, — `--audit`.</summary>
            public bool Audit;
            public List<string> Groups;
            public List<string> Only;
        }

        sealed class Sample
        {
            public string Key;
            public string Det;
            public string Part;

            /// <summary>(S56) Метки рядов из `manifest.csv`: «Th-232», «U-238u».</summary>
            public readonly List<string> Chains = new List<string>();

            /// <summary>(S56) Одиночные нуклиды из `manifest.csv`: «40K», «176LU».</summary>
            public readonly List<string> Nuclides = new List<string>();

            /// <summary>(S56) Символы элементов кристалла из `materials.csv`.</summary>
            public readonly List<string> Crystal = new List<string>();

            /// <summary>(S56) Символы элементов пробы из `materials.csv`.</summary>
            public readonly List<string> SampleMatter = new List<string>();

            /// <summary>(S56) Символы элементов защиты и обвязки из `materials.csv`.</summary>
            public readonly List<string> Shield = new List<string>();
        }

        /// <summary>
        /// Показана ли уже строка полосы: заверение печатается один раз за
        /// прогон, а спектры разбираются в несколько потоков (`T105`).
        /// </summary>
        static int bandNoteShown;

        sealed class Row
        {
            public string Key;
            public string Det;
            public string Part;
            public string Error;

            /// <summary>
            /// (`T85`) Что стало с матрицей ДО разбора: найдена или почему нет.
            /// Печатной строки здесь не лежит нарочно — её собирает
            /// <see cref="Program.MatrixNote"/> из этого признака И из
            /// <see cref="MatrixApplied"/>, чтобы «найдена» и «применена» не
            /// могли снова разъехаться по двум разным словам.
            /// </summary>
            public MatrixState Matrix = MatrixState.Unknown;
            public string EfficiencyName = "";

            /// <summary>(S56) Чем задана библиотека и что в неё вошло.</summary>
            public string LibraryNote = "";

            /// <summary>(S60) Сверка по обязательным линиям; null — не считалась.</summary>
            public List<FsaLineAudit.LineCheck> Audit;

            /// <summary>
            /// (`S103`) Поверка столбцов подпороговых линий; null — не заказана.
            /// Рядом — то, чем полоса ниже `Min_Range` описана в модели: без
            /// этих чисел таблица линий отвечает на «чего стоит столбец», но не
            /// на «а кто же тогда держит отсчёты».
            /// </summary>
            public List<FsaLineColumn> LineColumns;

            /// <summary>
            /// (`S103`) ЧТО СДВИНУЛ РЫЧАГ ОПОРЫ ПО СТОЛБЦУ на этом спектре:
            /// выброшено линий и сколько их было подпороговых. Идёт в
            /// `runs.csv` отдельными колонками, а не в шапку прогона, потому
            /// что развёртка, вышедшая плоской по корпусным числам, обязана
            /// уметь ответить, сколько линий она при этом выбросила и у
            /// скольких спектров, — иначе «рычаг не важен» неотличимо от
            /// «рычаг не работал».
            /// </summary>
            public int ShareDropped;

            public int ShareOffered;
            public double MinRangeKev = double.NaN;
            public double CurveFloorKev = double.NaN;
            public int LinesBelowMinRange = -1;
            public double DataBelow;
            public double ContinuumBelow;
            public double ModelBelow;
            public double DataTotal;
            public int Peaks;
            public int LibrarySize;
            public double Chi2Ndf;

            /// <summary>χ²/ndf прежними весами — общая метрика A/B (S41/S43).</summary>
            public double Chi2NdfPoisson;

            /// <summary>Невязка модели ε, доля (в csv печатается в процентах) — S51.</summary>
            public double ModelResidual;
            public double Gain;
            public double OffsetChannels;
            public double Ms;

            /// <summary>
            /// Процессорное время разбора. Разбор однопоточный, поэтому в норме
            /// оно почти равно `Ms`; расхождение значит, что машину делили, а
            /// не что разбор подорожал. Между прогонами сравнивать надо ЭТО
            /// (T28, S39).
            /// </summary>
            public double CpuMs;

            /// <summary>Невязка в запрошенном окне (`--near=`): сигмы и отсчёты.</summary>
            public double NearExcess = double.NaN;
            public double NearCounts;

            /// <summary>(S111) Пики, найденные по остатку: готовые строки CSV.</summary>
            public List<string> ResidualPeaks;
            public bool GainOnGridEdge;
            public bool OffsetOnGridEdge;

            /// <summary>Любой из двух краёв — для итоговой таблицы.</summary>
            public bool DriftOnGridEdge
            {
                get { return this.GainOnGridEdge || this.OffsetOnGridEdge; }
            }
            /// <summary>
            /// (`T85`) Матрица ПРИМЕНЕНА: хоть один образ отчётного фита
            /// построен ею. Ложь при <see cref="MatrixState.Found"/> — законное
            /// состояние, а не отказ: см. <see cref="Program.MatrixNote"/>.
            /// </summary>
            public bool MatrixApplied;
            public bool CascadeUsed;
            public bool EfficiencyUsed;
            public bool HasBackground;

            /// <summary>(S44) Причина, по которой поданный фон не взят; пусто — взят.</summary>
            public string BackgroundNote = "";
            public FsaResult Result;
        }
    }
}
