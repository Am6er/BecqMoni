# Зацепка подтвердилась: максимумы 159–623 кэВ — каскадные сумм-пики чужой схемы распада

**Третий файл по разбору внешнего рецензента, 11.09.2026.** Предыдущие:
[выдача по каналам](response-channels-for-review-2026-09-11.md) и
[ответ на разбор](response-channels-review-answer-2026-09-11.md).

Он указал место: «каскадные поправки для Ba-131 могут добавлять энергии, которых
нет в `component.Lines`», и расписал три проверки. **Все три выполнены. Гипотеза
верна, механизм установлен точно, место в коде названо строкой.** Ниже — числа,
исходники целиком и принятые поправки.

---

## 1. Проверка (2): стадии образа — где появляются лишние энергии

Гистограмма образа `Ba-131` читается **отражением из кэша разбора**
(`FsaAnalyzer.deposits`), то есть это ровно то, что пошло в фит, а не
пересчитанная копия. `Values` — гистограмма целиком, `SumPart` — **только
каскадные добавки**, поэтому «после обычных линий» = `Values − SumPart`.

**Штатный разбор:**

```
=== СТАДИИ ОБРАЗА «Ba-131» (бин 2.00 кэВ) ===
линий в образе: 1
длина гистограммы: 1006 бинов (верх 2010.0 кэВ)
каскадных добавок в гистограмме: есть

     бин        кэВ     только линии  каскадные суммы            всего
      15       30.0      4.5658E-002      0.0000E+000      4.5658E-002
      16       32.0      1.2595E-002      0.0000E+000      1.2595E-002
      78      156.0      3.7495E-007      9.8685E-005      9.9060E-005
      79      158.0      1.8869E-006      4.4448E-003      4.4467E-003
      80      160.0      7.5071E-006      3.8464E-004      3.9215E-004
      81      162.0      5.2127E-006      8.4304E-004      8.4825E-004
     125      250.0      3.5551E-006      1.5485E-003      1.5520E-003
     126      252.0      6.3150E-006      5.9165E-004      5.9797E-004
```

Видно прямо: при 158 и 250 кэВ вклад «только линии» на три порядка меньше
каскадной добавки. **Длина гистограммы — 1006 бинов вместо 16**, потому что
`SumTopEnergy` поднимает верх образа до самой высокой суммы.

## 2. Что именно добавил сумматор — его собственный отчёт

`FsaCascadeSummer.Describe` печатает каждую добавку с породившей её парой. Для
`Ba-131` (24 добавки, показаны первые по площади):

```
   E сумм, кэВ        слагаемые, кэВ         нуклид        площадь
       157.89   123.80+30.97            Ba-131       3.2510E-003
       157.53   123.80+30.63            Ba-131       1.6743E-003
       250.11   216.09+30.97            Ba-131       1.4107E-003
       623.27   496.32+123.80           Ba-131       1.1676E-003
       530.23   496.32+30.97            Ba-131       1.1596E-003
       162.16   123.80+35.09            Ba-131       9.3488E-004
       249.75   216.09+30.63            Ba-131       7.2677E-004
       529.87   496.32+30.63            Ba-131       5.9401E-004
       407.19   373.26+30.97            Ba-131       4.5792E-004
       254.37   216.09+35.09            Ba-131       4.0596E-004
       534.48   496.32+35.09            Ba-131       3.3160E-004
       406.83   373.26+30.63            Ba-131       2.3588E-004
       191.59   123.80+30.63+30.97      Ba-131       1.1540E-004
       377.33   216.09+30.97+123.80     Ba-131       9.8477E-005

Раскладка CF по линиям (A_ист = A_набл · CF)
     E, кэВ   нуклид          CF     вынос     влёт   прямая площадь
      30.97   Ba-131        1.2200    0.1804   0.0000    7.0455E-002
```

Сверка с вашей таблицей — совпадение по всем четырём, причём слагаемые те же:

| ваш максимум | ваша сумма | наш сумм-пик | наблюдённый максимум ленты |
|---|---|---|---|
| 159.1 | 123.805 + 34.994 = 158.799 | **157.89 = 123.80 + 30.97** | 159.1 |
| 251.6 | 216.078 + 35.817 = 251.895 | **250.11 = 216.09 + 30.97** | 251.6 |
| 405.1 | 373.246 + 30.979 = 404.225 | **407.19 = 373.26 + 30.97** | 405.1 |
| 530.8 | 496.326 + 34.994 = 531.320 | **530.23 = 496.32 + 30.97** | 530.8 |
| 622.9 (у вас не разобран) | — | **623.27 = 496.32 + 123.80** | 622.9 |

⚠ Различие в том, какой рентген складывается: у вас 34.99 / 35.82 (Kβ), у нас в
парах чаще 30.97 / 30.63 (Kα). Обе группы в схеме есть, и обе участвуют — в
отчёте видны и 162.16 = 123.80 + 35.09, и 254.37 = 216.09 + 35.09. Наблюдаемый
максимум ленты — их свёртка с разрешением 21 кэВ, поэтому он стоит между.

## 3. Проверка (1): выключение каскада — максимумы исчезают

Выключены **обе** половины (`CascadeSumming = CascadeSumPeaks = false`) — вы
верно заметили, что одного `CF = 1` мало.

| величина | штатно | каскад выключен |
|---|---|---|
| длина гистограммы образа | 1006 бинов, верх 2010.0 кэВ | **16 бинов, верх 30.0 кэВ** |
| лента компонента | от −5.7 до **909.5** кэВ | от −5.7 до **59.6** кэВ |
| максимумы ленты | 30.1 / 159.1 / 251.6 / 405.1 / 530.8 / 622.9 | **только 29.8** |
| площадь ленты | 30 382 009.9 | 24 549 137.9 |
| χ²/ndf разбора | 37.173 | **33.989** |

**Проверка (3) не понадобилась:** пики без каскада не выжили, значит уширение и
передача массивов ни при чём — как вы и предсказывали, суммируя монотонные хвосты
псевдо-Войта нельзя получить вершины при 159, 252 и 531.

⚠ Отдельно стоит отметить последнюю строку: эти фиктивные суммы **ухудшали**
согласие (37.173 против 33.989). То есть они не «подгоняли модель под данные» —
они ей мешали.

## 4. Место ошибки в коде — одной строкой

Цепочка ровно такая, как вы предположили:

1. `DistinctNuclides(component)` берёт **имена** нуклидов у линий образа — здесь
   единственное имя `Ba-131`;
2. `Data("Ba-131")` загружает по этому имени **полную схему распада** из базы;
3. `CollectSumPeaks` перебирает **`data.Pairs` — все совпадающие пары схемы**, а
   не пары линий образа;
4. единственная проверка на «своё» — попала ли сумма в окно линии компонента
   (`absorbed`), то есть отсекается совпадение суммы с линией, но **не
   проверяется, есть ли в образе сами слагаемые**;
5. `SumTopEnergy` расширяет гистограмму до самой высокой суммы, и добавки
   ложатся `AccumulateSumPeaks` в канал полного поглощения.

Ключевое место — здесь (полный метод в приложении):

```csharp
foreach (double[] pair in data.Pairs)          // ← пары ИЗ БАЗЫ, а не из образа
{
    double energy = this.ApparentSum(pair[0], pair[1]);
    if (this.PeakEfficiency(energy) <= 0.0) continue;

    bool absorbed = false;
    foreach (FsaLine line in component.Lines)  // ← проверяется только совпадение
    {                                          //   суммы с линией образа
        if (Belongs(line, nuclide) && Math.Abs(line.Energy - energy) < SumWindowKev)
        {
            absorbed = true;
            break;
        }
    }

    if (absorbed) continue;

    double area = scale * this.PairArea(data, pair);
    if (area > floor)
    {
        sumPeaks.Add(new SumPeak(energy, area, nuclide, pair[0], pair[1]));
    }
    ...
}
```

**Когда образ и схема согласованы — это верно и нужно** (Lu-176, ряды Th-232 и
Ra-226 так и считаются, и сумм-пики там наблюдаются). Ошибка появляется, когда
образ беднее схемы: тогда модель рисует полные поглощения линий, которых в ней
нет.

⚠ И ваше замечание про «не станут физическими пиками источника Cs-137» — верно
вдвойне: схема взята не только чужая по составу, но и чужая по нуклиду. Реальный
источник даёт K-рентген **бария** (Ba-137m → Ba-137), а `Ba-131` в библиотеке
этого прибора — запись 30.973, то есть Kα1 **цезия** (Ba-131 → Cs-131).

Заведено пунктом (5) внутренней задачи `AMBER16`, теперь с механизмом. Правки не
делали: у неё цена — числа всего корпуса (каскад работает у всех рядов), а смену
базы сравнения объявляет владелец проекта.

## 5. Принятые поправки

1. **«16 бинов, не 17»** — ваша правка верна, и теперь это напечатано машиной:
   `длина гистограммы: 16 бинов (верх 30.0 кэВ)` при выключенном каскаде. В
   первом файле стояло 17 — арифметическая ошибка (`(int)(30.973/2 + 0.5) + 1 = 16`).
2. **Трасса `2.6776`** — принято: это **ушедшая** энергия в истории, где родился
   квант 4.6155 кэВ, а не энергия линии. В столбцах трассы это разные поля
   (`рентген=4.6155`, `ушло_рентгена=2.6776`), но в тексте §2 второго файла они
   были слиты в одну фразу «энергии 2.68…5.02 кэВ» — формулировка неточная.
   И вы правы про историю с остатком `6.0194 = 4.9229 + 1.0965`: канал описывает
   **преобладающую помеченную статью потерь**, а не чистый выход одного кванта, —
   это и делает его непрерывную часть законной.
3. **43.69 %** — принято: это изменение результата при выключении вычислительной
   ветви, а не измеренная физическая доля внешнего рассеяния. Взвешенная и
   аналоговая части перекрываются по назначению и сшиваются, поэтому долю
   происхождения так не получить.
4. **Сумма отклика** — принято: объяснение механизма не отвечает, какая из двух
   сумм верна. Нужен отдельный опыт — единый аналоговый расчёт при той же
   физике; у нас его нет, и утверждать мы ничего не будем.
5. **`BinOf`, смешение полного поглощения с аппаратным окном, `Stretch`** —
   признаны, исправлений пока нет, и вы правы, что переименование канала и
   починка сборки шаблона **не требуют пересчёта транспорта**. В задаче это
   разделено.

---

## Приложение. Запрошенные методы целиком

Порядок: как имя нуклида превращается в схему, как считается поправка, как
собираются суммы, как они попадают в гистограмму.

### FsaCascadeSummer.For

```csharp
        public Correction For(FsaComponent component)
        {
            if (component == null || component.Lines == null || component.Lines.Count == 0)
            {
                return null;
            }

            Correction correction;
            if (this.corrections.TryGetValue(component, out correction))
            {
                return correction;
            }

            correction = this.Compute(component);
            this.corrections[component] = correction;
            return correction;
        }
```

### FsaCascadeSummer.DistinctNuclides / Belongs

```csharp
        static IEnumerable<string> DistinctNuclides(FsaComponent component)
        {
            List<string> names = new List<string>();
            foreach (FsaLine line in component.Lines)
            {
                string name = line.Nuclide ?? "";
                if (name.Length > 0 && !names.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    names.Add(name);
                }
            }

            return names;
        }

        static bool Belongs(FsaLine line, string nuclide)
        {
            return string.Equals(line.Nuclide ?? "", nuclide, StringComparison.OrdinalIgnoreCase);
        }

```

### FsaCascadeSummer.Data — имя нуклида превращается в схему распада

```csharp
        NuclideData Data(string nuclide)
        {
            string key = ParentKey(nuclide);
            if (key == null)
            {
                return null;
            }

            // Выключатель прежнего поведения (S27, пункт «изомеры»): до правки
            // `Nucid` возвращал на именах вида «Ba-137m» null, и такой
            // компонент оставался без поправки молча. Ключ нужен, чтобы цену
            // именно этой половины можно было снять отдельно.
            if (!this.withIsomers && key.StartsWith(IsomerPrefix, StringComparison.Ordinal))
            {
                return null;
            }

            NuclideData ready;
            if (this.augmented.TryGetValue(key, out ready))
            {
                return ready;
            }

            NuclideData raw = BaseData(key);
            ready = this.Augment(key, raw);
            this.augmented[key] = ready;
            return ready;
        }
```

### FsaCascadeSummer.Compute

```csharp
        Correction Compute(FsaComponent component)
        {
            int count = component.Lines.Count;
            double[] factors = new double[count];
            for (int i = 0; i < count; i++)
            {
                factors[i] = 1.0;
            }

            List<SumPeak> sumPeaks = new List<SumPeak>();
            List<SumContinuum> continua = new List<SumContinuum>();
            List<LineNote> notes = new List<LineNote>();
            bool any = false;

            // Пары совпадений живут ВНУТРИ одного нуклида: у компонента-цепочки
            // линии разных дочерних, и мешать их каскады нельзя.
            foreach (string nuclide in DistinctNuclides(component))
            {
                NuclideData data = Data(nuclide);
                if (data == null)
                {
                    continue;
                }

                // Масштаб нормировки: интенсивности компонента даны на распад
                // РОДИТЕЛЯ цепочки, база — на распад самого нуклида. Отношение
                // берётся по самой сильной сошедшейся линии; в CF оно
                // сокращается, а площади сумм-пиков без него уехали бы.
                double scale = Scale(component, nuclide, data);
                double strongest = 0.0;
                for (int i = 0; i < count; i++)
                {
                    FsaLine line = component.Lines[i];
                    if (!Belongs(line, nuclide))
                    {
                        continue;
                    }

                    double area = line.Intensity / 100.0 * this.PeakEfficiency(line.Energy);
                    if (area > strongest)
                    {
                        strongest = area;
                    }

                    double baseEnergy;
                    if (!Match(data.Intensity, line.Energy, out baseEnergy))
                    {
                        continue;
                    }

                    // В образ идёт ОБРАТНАЯ величина: см. Correction.LineFactors.
                    double loss, inShare, direct;
                    double cf = this.CoincidenceFactor(data, baseEnergy,
                                                       out loss, out inShare, out direct);
                    notes.Add(new LineNote
                    {
                        Nuclide = nuclide,
                        EnergyKev = line.Energy,
                        Cf = cf,
                        Loss = loss,
                        InShare = inShare,
                        DirectArea = direct
                    });

                    if (cf > 0.0 && Math.Abs(cf - 1.0) > 1.0E-6)
                    {
                        factors[i] = 1.0 / cf;
                        any = true;
                    }
                }

                this.CollectSumPeaks(component, nuclide, data, scale, strongest, sumPeaks, continua);
            }

            if (sumPeaks.Count > 0)
            {
                any = true;
                sumPeaks.Sort((a, b) => b.Area.CompareTo(a.Area));
                if (sumPeaks.Count > MaxSumPeaks)
                {
                    sumPeaks.RemoveRange(MaxSumPeaks, sumPeaks.Count - MaxSumPeaks);
                }
            }

            if (continua.Count > 0)
            {
                any = true;
            }

            return new Correction
            {
                LineFactors = factors,
                SumPeaks = sumPeaks,
                SumContinua = continua,
                Notes = notes,
                Any = any
            };
```

### FsaCascadeSummer.CollectSumPeaks

```csharp
        void CollectSumPeaks(FsaComponent component, string nuclide, NuclideData data,
                             double scale, double strongest, List<SumPeak> sumPeaks,
                             List<SumContinuum> continua)
        {
            if (!(scale > 0.0))
            {
                return;
            }

            double floor = strongest * SumPeakFloor;

            // ⛔ У ТРОЕК СВОЙ ПОРОГ, И СЧИТАЕТСЯ ОН ОТ СИЛЬНЕЙШЕЙ ПАРЫ
            // (`S113`, решение Amber 10.09.2026 вопросником: «Свой порог
            // тройкам»).
            //
            // Прежде тройная площадь судилась тем же `floor`, что и парная, —
            // долей от сильнейшей ЛИНИИ компонента. Но тройная меньше парной
            // ровно на `третий.Value · ε_p(третьего)`, то есть на порядок-два,
            // и порог срезал их ВСЕ: журнал `LogTriples` на `ASN16_Lu176`
            // показал 13 029 рассмотренных троек и НОЛЬ прошедших, а у самой
            // близкой (603.35 = 201.82+88.35+306.88) площадь 4.009E-6 против
            // порога 4.557E-6 — ниже на 12 %. Механизм жил с 13.08.2026 и всё
            // это время был мёртв целиком.
            //
            // Мерка тройки — сильнейшая ПАРНАЯ сумма того же компонента: она
            // одного рода с тройкой (обе — суммы, обе несут эффективности), и
            // отношение «тройная к сильнейшей парной» отвечает на тот же
            // вопрос, на который у пар отвечает доля от сильнейшей линии.
            //
            // ⚠ Правка трогает ТОЛЬКО тройки: `floor` парных остаётся прежним
            // бит в бит, и цена ограничена вкладом троек — по Geant4 это 0.8 %
            // от суммы пары (четыре события из 200 000 на тройную 596.95).
            double strongestPair = 0.0;
            foreach (double[] pair in data.Pairs)
            {
                double pairArea = scale * this.PairArea(data, pair);
                if (pairArea > strongestPair)
                {
                    strongestPair = pairArea;
                }
            }

            double tripleFloor = strongestPair * SumPeakFloor;
            foreach (double[] pair in data.Pairs)
            {
                // Энергия сумм-пика — ВИДИМАЯ (по свету, S20): именно на это
                // место шкалы событие ложится, и именно с этим местом надо
                // сверять окна линий компонента.
                double energy = this.ApparentSum(pair[0], pair[1]);
                if (this.PeakEfficiency(energy) <= 0.0)
                {
                    continue;
                }

                bool absorbed = false;
                foreach (FsaLine line in component.Lines)
                {
                    if (Belongs(line, nuclide) && Math.Abs(line.Energy - energy) < SumWindowKev)
                    {
                        absorbed = true;
                        break;
                    }
                }

                if (absorbed)
                {
                    continue;
                }

                double area = scale * this.PairArea(data, pair);
                if (area > floor)
                {
                    sumPeaks.Add(new SumPeak(energy, area, nuclide, pair[0], pair[1]));
                }

                this.CollectTripleSums(component, nuclide, data, pair, scale, tripleFloor, sumPeaks,
                                       continua);
            }
```

### FsaAnalyzer.SumTopEnergy

```csharp
        static double SumTopEnergy(FsaCascadeSummer.Correction correction, double start)
        {
            double top = start;
            if (correction == null)
            {
                return top;
            }

            if (correction.SumPeaks != null)
            {
                foreach (FsaCascadeSummer.SumPeak peak in correction.SumPeaks)
                {
                    if (peak.Energy > top)
                    {
                        top = peak.Energy;
                    }
                }
            }

            if (correction.SumContinua != null)
            {
                foreach (FsaCascadeSummer.SumContinuum band in correction.SumContinua)
                {
                    double edge = band.ShiftKev + band.ThirdKev;
                    if (edge > top)
                    {
                        top = edge;
                    }
                }
            }

            return top;
```

### FsaAnalyzer.AccumulateSumPeaks

```csharp
        bool AccumulateSumPeaks(double[] deposit, double[][] channels,
                                FsaCascadeSummer.Correction correction,
                                bool withContinuum)
        {
            int peakChannel = (int)EfficiencyMaker.EfficiencySimulator.ResponseChannel.Peak;
            bool any = false;
            foreach (FsaCascadeSummer.SumPeak peak in correction.SumPeaks)
            {
                double peakEfficiency = this.cascade.PeakEfficiency(peak.Energy);
                if (!(peakEfficiency > 0.0))
                {
                    continue;
                }

                this.ResponseMatrix.AccumulateChannel(
                    channels != null ? channels[peakChannel] : deposit,
                    peak.Energy, peak.Area / peakEfficiency, peakChannel);
                any = true;
            }

            // Сумм-континуум (S19): пара поглощена целиком, третий квант оставил
            // часть себя. Кладётся откликом ТРЕТЬЕГО кванта без пикового канала,
            // сдвинутым на видимую сумму пары, — то есть тем же образом, каким
            // третий квант лёг бы сам по себе, только приподнятым по шкале.
            // Пиковый канал исключён нарочно: полное поглощение третьего уже
            // посчитано тройным сумм-пиком, и класть его сюда значило бы
            // задвоить.
            if (withContinuum && correction.SumContinua != null)
            {
                int channelCount = EfficiencyMaker.EfficiencySimulator.ResponseChannelCount;
                foreach (FsaCascadeSummer.SumContinuum band in correction.SumContinua)
                {
                    if (!(band.Weight > 0.0))
                    {
                        continue;
                    }

                    for (int c = 0; c < channelCount; c++)
                    {
                        if (c == peakChannel)
                        {
                            continue;
                        }

                        this.ResponseMatrix.AccumulateShifted(
                            channels != null ? channels[c] : deposit,
                            band.ThirdKev, band.Weight, c, band.ShiftKev);
                    }

                    any = true;
                }
            }

            return any;
        }
```

