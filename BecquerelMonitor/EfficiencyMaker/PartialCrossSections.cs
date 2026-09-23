using System;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>Канал взаимодействия гамма-кванта с веществом.</summary>
    public enum PhotonProcess
    {
        /// <summary>Когерентное (рэлеевское): направление меняет, энергию нет.</summary>
        Coherent,
        /// <summary>Некогерентное (комптоновское).</summary>
        Incoherent,
        Photoelectric,
        /// <summary>Рождение пары в поле ядра и в поле электрона вместе.</summary>
        PairProduction
    }

    /// <summary>
    /// Парциальные сечения по каналам взаимодействия, см2/г.
    ///
    /// Источник — NIST XCOM 3.1. Числа лежат в `matdb.sqlite`, берутся через
    /// <see cref="MaterialDatabase"/>.
    ///
    /// ЗАЧЕМ ОНИ НУЖНЫ ОТДЕЛЬНО. Полного ослабления для переноса мало.
    /// В сцинтилляторе канал поглощения — малая разность больших чисел: в CsI
    /// на 1332 кэВ комптон даёт 92.3 % ослабления, фотоэффект 5.2 %,
    /// когерентное 1.6 %, пары 0.9 %. Если считать фотоэффектом всё, кроме
    /// комптона, канал поглощения выходит 7.7 % вместо 5.2 % — завышен в
    /// полтора раза, и ровно во столько же завышается эффективность в пике.
    ///
    /// Пока таблица лежала в исходнике, в ней было ДЕВЯТЬ элементов, снятых
    /// руками через веб-форму: O, Na, Ge, Br, Sr, I, Cs, La, Bi. Их хватало на
    /// CsI, NaI, BGO, LaBr3 и SrI2, а CeBr3, CdTe, CZT и GSO считались тем
    /// самым грубым приближением. В базе все сто.
    /// </summary>
    public static class PartialCrossSections
    {
        public static bool HasElement(int z)
        {
            return MaterialDatabase.Has(z);
        }

        /// <summary>
        /// Сечение канала для элемента, см2/г. Интерполяция лог-лог, как у
        /// полного ослабления: линейная по значению завышала бы круто падающий
        /// фотоэффект между узлами. Исключение — участки с нулевым узлом
        /// (рождение пар ниже порога 1.022 МэВ): логарифм там брать не от
        /// чего, и такой участок интерполируется по значению линейно.
        /// </summary>
        public static double MassCrossSection(int z, double energyKev, PhotonProcess process)
        {
            MaterialDatabase.Element element;
            if (!(energyKev > 0.0) || !MaterialDatabase.TryGet(z, out element))
            {
                return 0.0;
            }

            int lo, hi;
            if (!MaterialDatabase.Bracket(element.EnergyKev, energyKev, out lo, out hi))
            {
                return 0.0;
            }

            return MassCrossSection(element, lo, hi, energyKev, Math.Log(energyKev), process);
        }

        /// <summary>
        /// ⚡ (`A43`) То же сечение, но элемент найден, пара узлов уже выбрана
        /// (<see cref="MaterialDatabase.Bracket"/>) и логарифм энергии передан
        /// готовым.
        ///
        /// Зачем такой вход. У элемента ОДНА сетка энергий на все каналы, а
        /// спрашивают их порознь: ветвление в кристалле берёт фотоэффект, комптон
        /// и пары тремя вызовами, ослабление без когерентного — полное и
        /// когерентное двумя. Каждый вызов заново искал по словарю элемент, заново
        /// вёл двоичный поиск по той же сетке и заново брал `Math.Log` от той же
        /// энергии. Здесь всё это делается по разу.
        ///
        /// ⛔ Арифметика прежняя до разряда: те же узлы, та же формула, тот же
        /// порядок. Приёмка — `MatrixDiffProbe` 0.000 %.
        /// </summary>
        public static double MassCrossSection(MaterialDatabase.Element element,
                                              int lo, int hi, double energyKev,
                                              double logEnergyKev, PhotonProcess process)
        {
            double a = Channel(element, lo, process);
            if (lo == hi)
            {
                return a;
            }

            double[] grid = element.EnergyKev;
            double x0 = grid[lo], x1 = grid[hi];
            double b = Channel(element, hi, process);
            if (!(x1 > x0))
            {
                // край поглощения: две точки на одной энергии, берётся верхняя
                return b;
            }

            // Логарифмы узлов и значений взяты из таблицы, а не посчитаны заново
            // (`T43`): их четыре на вызов, они от чисел, которые не меняются, и
            // в профиле это был главный поставщик математики ucrt. Результат
            // побитово прежний — та же функция от того же аргумента.
            double[] logGrid = element.LogEnergyKev;
            double f = (logEnergyKev - logGrid[lo]) / (logGrid[hi] - logGrid[lo]);
            if (!(a > 0.0) || !(b > 0.0))
            {
                // канал открывается не с нуля шкалы: рождение пар ниже 1.022 МэВ
                // тождественно нулевое, логарифм там брать не от чего
                return a + f * (b - a);
            }

            // Лог-лог, как и у полного ослабления. Линейная по значению
            // интерполяция круто падающего фотоэффекта между узлами его
            // ЗАВЫШАЕТ, а завышенный фотоэффект — это завышенная доля полного
            // поглощения, то есть завышенная эффективность в пике.
            double[] logChannel = element.LogChannels[(int)process];
            return Math.Exp(logChannel[lo] + f * (logChannel[hi] - logChannel[lo]));
        }

        /// <summary>
        /// ⛔ (`AMBER74`, П132 22.09.2026) ПОЛНОЕ ослабление элемента, см2/г,
        /// СУММОЙ КАНАЛОВ — тем же правилом, каким его считает кристалл.
        ///
        /// ЧТО БЫЛО НЕ ТАК. Слои сцены (проба, стенка, отражатель, оправа)
        /// брали полное ослабление одной лог-лог ПРЯМОЙ по `element.Total` — по
        /// СУММЕ пяти каналов в узлах. Сумма степенных с разными наклонами
        /// вогнута в лог-лог, и хорда лежит ВЫШЕ кривой: на участке, где хозяин
        /// меняется (фотоэффект → комптон), знак ошибки плюс. Кристалл
        /// тем временем считал по каналам, каждый своей прямой, и одна сцена
        /// жила по двум правилам.
        ///
        /// ЧИСЛО (замер П132 по `matdb`, ВЫБРОС УЗЛА в 10…3000 кэВ, 2081 узел:
        /// узел убран из сетки и восстановлен по двум соседям, обе схемы видят
        /// одни и те же узлы): прямая по сумме — медиана 0.604 %, 90-й
        /// процентиль 4.905 %, максимум 13.6 %; сумма каналов — медиана
        /// 0.388 %, 90-й 1.169 %, максимум 3.07 %. Среди узлов, где прямая
        /// ошибается больше 1 % (889 из 2081), знак ПЛЮС у 852, средняя
        /// |ошибка| 3.616 % против 0.770 % у суммы каналов. В серединах
        /// интервалов рабочей сетки (против лог-лог сплайна, метода XCOM):
        /// O 24.5 кэВ +4.10 %, C +4.35 %, I 245 +2.93 %, Cs +2.86 %, Lu +2.06 %,
        /// Pb +1.61 %, Fe 141 +1.08 %. Через пропускание слоя это вода
        /// маринелли 0.5 л на 22…26 кэВ −3.9…−8.6 % (линии Cd-109 и Am-241
        /// 26.3), кварц 1 см на 24.5 кэВ −13.9 %.
        ///
        /// ⚠ Пары здесь — ОБЫЧНОЙ интерполяцией канала, без пороговой формы
        /// (`S121`): пороговая нужна тому, кто РАЗЫГРЫВАЕТ канал, и она у него
        /// есть (<see cref="GeometryMaterial.LinearPair"/>, `Region.Pair`), а в
        /// полном ослаблении у порога пары — ничтожная доля суммы; это довод,
        /// которым закрыта ~~`S124`~~, и в этой части он в силе. Розыгрыш канала
        /// от рассогласования защищён срезом `pairMu > restMu`.
        /// </summary>
        public static double MassTotal(MaterialDatabase.Element element,
                                       int lo, int hi, double energyKev,
                                       double logEnergyKev)
        {
            return MassCrossSection(element, lo, hi, energyKev, logEnergyKev,
                                    PhotonProcess.Coherent)
                 + MassTotalWithoutCoherent(element, lo, hi, energyKev, logEnergyKev);
        }

        /// <summary>
        /// То же БЕЗ когерентного канала (`AMBER74`). Прежде это считалось
        /// вычитанием — «прямая по сумме минус канал когерентного», две разные
        /// интерполяции в одной разности, и остаток нёс ошибку первой целиком.
        /// Теперь обе половины по одному правилу, и тождество
        /// `Total = NoCoherent + Coherent` выполняется по построению.
        /// </summary>
        public static double MassTotalWithoutCoherent(MaterialDatabase.Element element,
                                                      int lo, int hi, double energyKev,
                                                      double logEnergyKev)
        {
            return MassCrossSection(element, lo, hi, energyKev, logEnergyKev,
                                    PhotonProcess.Incoherent)
                 + MassCrossSection(element, lo, hi, energyKev, logEnergyKev,
                                    PhotonProcess.Photoelectric)
                 + MassCrossSection(element, lo, hi, energyKev, logEnergyKev,
                                    PhotonProcess.PairProduction);
        }

        /// <summary>
        /// ⛔ (`S121`) Сечение рождения пар С ПОРОГОВОЙ ИНТЕРПОЛЯЦИЕЙ, раздельно
        /// по ядерному каналу и triplet, — то, чем эту величину подаёт XCOM.
        ///
        /// ЧТО БЫЛО НЕ ТАК. Общая ветка «один узел нулевой — интерполируй
        /// линейно» превращала участок от порога до первого открытого узла в
        /// прямую: у Cs между 1.022 и 1.250 МэВ она даёт в 1.100 МэВ
        /// 0.01602 барн/атом. Пороговое поведение сечения этому не отвечает
        /// ни в какой модели: у порога оно обязано подходить к нулю
        /// касательно, а не с конечным наклоном. Независимая пороговая
        /// аппроксимация Geant4 (ниже 1.5 МэВ сечение масштабируется как
        /// (E − 2mₑc²)²) от той же опоры 1.5 МэВ даёт 0.00540 барн/атом —
        /// в 2.97 раза меньше. Она здесь НЕ эталон, а проверка порядка.
        ///
        /// КАК СЧИТАЕТСЯ ТЕПЕРЬ. Ровно как фитирует XCOM (документация XCOM,
        /// глава 3): интерполируется не сечение, а частное
        /// σ(E)/(1 − E₀/E)³ — величина, у которой на пороге нет ни нуля, ни
        /// изломa, — после чего множитель возвращается на место. Пороги у
        /// каналов РАЗНЫЕ (ядро 1.022 МэВ, triplet в поле электрона
        /// 2.044 МэВ), поэтому и складываются они уже посчитанными, а не
        /// суммой в таблице: у суммы двух пороговых форм своей формы нет.
        ///
        /// Участок, у которого нижний узел лежит НА пороге (сечение там
        /// тождественно нулевое, частного не существует), берёт наклон
        /// частного у двух ближайших открытых узлов сверху; если второго
        /// узла нет, остаётся чистый пороговый множитель от единственного.
        ///
        /// Проверка сходимости — `PairThresholdProbe`: узел выбрасывается из
        /// сетки и восстанавливается по соседям, обеими схемами.
        /// </summary>
        public static double MassCrossSection(MaterialDatabase.Element element,
                                              int lo, int hi, double energyKev,
                                              double logEnergyKev, PhotonProcess process,
                                              bool thresholdPair)
        {
            if (!thresholdPair || process != PhotonProcess.PairProduction)
            {
                return MassCrossSection(element, lo, hi, energyKev, logEnergyKev, process);
            }

            return PairChannel(element, lo, hi, energyKev, logEnergyKev,
                               element.LogPairNuclearShape,
                               MaterialDatabase.PairNuclearThresholdKev)
                 + PairChannel(element, lo, hi, energyKev, logEnergyKev,
                               element.LogPairElectronShape,
                               MaterialDatabase.PairElectronThresholdKev);
        }

        /// <summary>
        /// Один канал рождения пар по пороговой величине (`S121`).
        /// <paramref name="logShape"/> — логарифмы σ/(1 − E₀/E)³ по узлам
        /// сетки, посчитанные при загрузке; NaN там, где канал закрыт.
        /// </summary>
        static double PairChannel(MaterialDatabase.Element element, int lo, int hi,
                                  double energyKev, double logEnergyKev,
                                  double[] logShape, double thresholdKev)
        {
            double shape = MaterialDatabase.PairThresholdShape(energyKev, thresholdKev);
            if (!(shape > 0.0) || logShape == null)
            {
                return 0.0;                 // ниже порога канала нет вовсе
            }

            double[] logGrid = element.LogEnergyKev;
            int p, q;
            if (lo != hi && Open(logShape, lo) && Open(logShape, hi)
                && logGrid[hi] > logGrid[lo])
            {
                p = lo;
                q = hi;                     // обычный участок: оба узла открыты
            }
            else if (Open(logShape, hi))
            {
                // Пороговый участок: снизу узел с тождественным нулём. Наклон
                // пороговой величины берётся у двух ближайших открытых узлов
                // СВЕРХУ и продолжается вниз; на самом узле (lo == hi) это
                // даёт ровно табличное значение.
                p = hi;
                q = hi + 1;
                if (!Open(logShape, q) || !(logGrid[q] > logGrid[p]))
                {
                    return Math.Exp(logShape[p]) * shape;
                }
            }
            else
            {
                return 0.0;                 // канал на этом участке ещё закрыт
            }

            double f = (logEnergyKev - logGrid[p]) / (logGrid[q] - logGrid[p]);
            return Math.Exp(logShape[p] + f * (logShape[q] - logShape[p])) * shape;
        }

        /// <summary>Открыт ли канал в узле: у закрытого в логарифме NaN.</summary>
        static bool Open(double[] logShape, int i)
        {
            return i >= 0 && i < logShape.Length
                && !double.IsNaN(logShape[i]) && !double.IsInfinity(logShape[i]);
        }

        static double Channel(MaterialDatabase.Element element, int i, PhotonProcess process)
        {
            switch (process)
            {
                case PhotonProcess.Coherent: return element.Channels[0][i];
                case PhotonProcess.Incoherent: return element.Channels[1][i];
                case PhotonProcess.Photoelectric: return element.Channels[2][i];
                default: return element.Channels[3][i] + element.Channels[4][i];
            }
        }
    }
}
