using System;
using System.Collections.Generic;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// ⛔ (`A72`, П27 12.09.2026; решение Amber 12.09.2026, вопросником,
    /// дословно: «Вести электрон переносом») ПЕРЕНОС ЭЛЕКТРОНА ПО КРИСТАЛЛУ —
    /// вторая половина <see cref="EfficiencySimulator"/>, под ключом
    /// <see cref="EfficiencySimulator.ElectronTransport"/> (умолчание склада
    /// ВКЛ с 13.09.2026 — физика 17, П37; клеймо `etr=1`, хвост файла `ETRN`).
    ///
    /// ЧТО БЫЛО НЕ ТАК. Вылет электрона считался ЭФФЕКТИВНОЙ ГЛУБИНОЙ
    /// (<see cref="EfficiencySimulator.ElectronLoss"/>, ветка без ключа):
    /// изотропное направление, прямой путь до грани, порогово-линейная доля
    /// пробега с мягкой добавкой (~~`A63`~~) и подгоняемый показатель расхода
    /// (~~`A70`~~). Три допущения неверны каждое, и П20 §3 измерила цену на
    /// голых кристаллах ~1 см³ против Geant4 при 1461 кэВ: пик +5.7 % (RC103
    /// CsI 10×10×10) и +5.5 % (OBS CsI 7×7×30), полоса 0–25 %E −13…−17 %,
    /// полосы 50–100 %E +7…+9 % — при сходящейся полной эффективности. Наклон
    /// эффективной глубины подгоняет ПИК, но разводит полосы в разные стороны:
    /// неверна ФОРМА уноса, а не его величина.
    ///
    /// ЧТО СДЕЛАНО. Электрон ведётся сгущённой историей (condensed history,
    /// класс I — вторичных δ-электронов нет, потери непрерывны):
    ///
    /// 1. Направление рождения — по процессу, а не изотропно:
    ///    * фотоэффект — Заутер—Гаврила (K-оболочка, тот же розыгрыш, что у
    ///      `G4SauterGavrilaAngularDistribution`), относительно направления
    ///      поглощённого кванта;
    ///    * комптон — из кинематики: импульс электрона = импульс кванта до
    ///      рассеяния минус после, направления оба известны в точке события;
    ///    * пара — по существующей схеме деления энергии, направление
    ///      лептона — модифицированный Цай (`G4ModifiedTsai`), вперёд с углом
    ///      ~mc²/E;
    ///    * электроны оже-каскада (EADL) — изотропно, как и положено.
    /// 2. Шаг по ПРОБЕГУ: остаточный пробег CSDA из ESTAR
    ///    (<see cref="ElectronData.RangeOf"/>, `matdb`), шаг —
    ///    <see cref="EfficiencySimulator.ElectronStepFraction"/> остаточного
    ///    пробега; энергия в конце шага — обратной таблицей
    ///    <see cref="ElectronData.EnergyOfRange"/>. Разброса потерь (Ландау)
    ///    нет.
    /// 3. Многократное рассеяние — «случайный шарнир» (random hinge, PENELOPE):
    ///    шаг проходится прямо до случайной точки, там направление
    ///    поворачивается на угол всего шага, дальше снова прямо. Ширина угла —
    ///    формула Хайленда (PDG) по радиационной длине кристалла, посчитанной
    ///    из состава по Цаю (`CsI` 8.39 г/см², `NaI` 9.49 — сходится с PDG);
    ///    распределение по 1−cos θ — экспонента со средним θ₀², обрезанная на
    ///    2 (в пределе больших θ₀ — изотропия, в пределе малых — гауссова
    ///    ширина Хайленда). ⚠ Ширина ПРОВЕРЕНА, а не принята: развёртка
    ///    множителем ×1.5/×2/×3 (П27 §4) на RC103 1461 кэВ уводит пик с
    ///    +1.5 % до +3.9/+4.8/+5.3 % против Geant4, а на 59.5 кэВ полосы не
    ///    двигает вовсе — форма уноса внизу шкалы решается не рассеянием, а
    ///    раздельным каскадом (`LightCascadeSplit`, `kdip=1`).
    /// 4. Вылет через грань — РЕАЛЬНЫЙ: прямой отрезок шага пересёк грань
    ///    кристалла (<see cref="EfficiencySimulator.CrystalPath"/>) — электрон
    ///    уходит с той энергией, что отвечает остаточному пробегу в точке
    ///    выхода. Без ключа <see cref="EfficiencySimulator.ElectronLayerTransport"/>
    ///    обратно из обвязки электрон не возвращается (как и позитрон в
    ///    `PositronStop`).
    ///    ✅ (`AMBER44`, П94 17.09.2026; задача Amber 15.09.2026, решение
    ///    вопросником, дословно: «Перенос в слоях обвязки») Под ключом
    ///    <see cref="EfficiencySimulator.ElectronLayerTransport"/> (клеймо
    ///    `eltr=1`, хвост `ELTR`; ВКЛ умолчанием с 18.09.2026 — физика 19, П97,
    ///    единый счёт склада по решению Amber «ВКЛ сейчас, единый счёт ночью»;
    ///    `--eltr=0` — абляция) электрон на грани НЕ списывается: он ведётся дальше ТЕМ ЖЕ
    ///    шагом и шарниром в веществе слоя обвязки
    ///    (<see cref="TransportInLayers"/>: ESTAR слоя по составу из `matdb` —
    ///    <see cref="EfficiencySimulator.CarryMedium"/>, радиационная длина
    ///    слоя по Цаю — <see cref="LayerRadiationLength"/>, переходы между
    ///    слоями по пересечениям геометрии, пустота — по прямой без потерь);
    ///    вылет из сцены или гибель в слое — конец; возврат в кристалл — с
    ///    остатком энергии и направлением, дальше обычный перенос по
    ///    кристаллу от точки входа. Тормозное в слое — как у заноса `M3`/П44:
    ///    толстая мишень вещества слоя в точке выхода
    ///    (<see cref="LayerBremsstrahlung"/>), кванты — в очередь вылетов
    ///    (<see cref="EfficiencySimulator.NoteEscape"/>), когда она открыта.
    ///    ⛔ ТА ЖЕ машинерия ведёт и ЗАНОС — электрон, рождённый в слое
    ///    (<see cref="EfficiencySimulator.ElectronCarryDeposit"/>): под ключом
    ///    обход обвязки по прямой с `ElectronCarryDetour = 0.7` заменён этим же
    ///    переносом, а дошедший электрон отдаётся переносу по кристаллу
    ///    (<see cref="EfficiencySimulator.ElectronLoss"/> с направлением входа)
    ///    вместо куска `AddLight` (держатель `M12`, П92 §4). Цена приближений
    ///    без ключа измерена П55 §3.2 и П92 §3.1: 1 см³ CsI, 1461 кэВ — нижняя
    ///    четверть −6.3 %, 0–100 кэВ −15 %; шельф 32–42 при 59.5 +5.6 %.
    /// 5. Тормозное — КАК БЫЛО: кванты разыгрываются в точке рождения по
    ///    <see cref="ThickTargetBrem"/>, излучённое зажимает уносимую энергию
    ///    (`M3` — отдельная строка, не трогается).
    ///    ✅ (`M3`, П44 13.09.2026) Под ключом
    ///    <see cref="EfficiencySimulator.BremAlongPath"/> кванты рождаются
    ///    НА ШАГАХ переноса — в точке шарнира, тонкой мишенью при энергии
    ///    середины шага (<see cref="ThickTargetBrem.StepPhotons"/>), уровень
    ///    подтянут к ESTAR множителем по начальной энергии; направление —
    ///    изотропное (уровень 1) или по электрону модифицированным Цаем
    ///    (уровень 2). Электрон, который по раннему выходу погибнет внутри,
    ///    отдаёт остаток тормозного толстой мишенью в точке выхода: его
    ///    остаточный пробег короче расстояния до грани, и для КВАНТА (пробег
    ///    сантиметры) смещение точки на доли миллиметра ничего не решает.
    ///    Энергия электрона на шаге — по CSDA, то есть средняя радиационная
    ///    потеря в ней уже сидит; разыгранный квант её не вычитает второй раз,
    ///    а зажимается суммой (радиация + унос ≤ T), как в точечной ветке.
    ///
    /// ЦЕНА. Горячее ядро: <see cref="EfficiencySimulator.ElectronLoss"/>
    /// зовётся на каждое взаимодействие. Чтобы платить только там, где вылет
    /// возможен, перед первым шагом и после каждого проверяется расстояние до
    /// ближайшей грани (<see cref="CrystalNearestFace"/>): остаточный пробег
    /// короче него — электрон погибнет внутри при любой траектории, и остаток
    /// кладётся на месте без единого розыгрыша. На крупном кристалле это
    /// почти каждый электрон. Цена на узел измерена П27 §5.
    ///
    /// ⛔ ВЫКЛЮЧЕННЫЙ КЛЮЧ НЕ ТЯНЕТ НИ ОДНОГО СЛУЧАЙНОГО ЧИСЛА и оставляет
    /// прежний результат до последнего бита (приёмка `MatrixDiffProbe`
    /// 0.000/0.000, П27 §2). Включённый — тянет свои (направление рождения,
    /// точка шарнира, угол, азимут на каждом шаге), поток другой.
    /// </summary>
    public sealed partial class EfficiencySimulator
    {
        /// <summary>Откуда у электрона направление рождения (`A72`).</summary>
        enum ElectronBirth
        {
            /// <summary>Изотропно — оже-электроны каскада, неизвестный процесс.</summary>
            Isotropic,

            /// <summary>Фотоэлектрон: Заутер—Гаврила относительно направления кванта.</summary>
            Photo,

            /// <summary>Направление задано вызывающим (комптон — из кинематики).</summary>
            Given,

            /// <summary>Лептон пары: модифицированный Цай относительно направления кванта.</summary>
            Pair,
        }

        /// <summary>Ниже этой энергии электрон кладётся на месте, кэВ.</summary>
        const double TransportCutKev = 1.0;

        /// <summary>
        /// Предохранитель от бесконечного шага: остаточный пробег убывает на
        /// долю шага за шаг, и от 3 МэВ до 1 кэВ при доле 0.1 нужно ~120
        /// шагов; ранний выход по ближайшей грани обрывает почти все треки
        /// много раньше.
        /// </summary>
        const int TransportMaxSteps = 400;

        /// <summary>Радиационная длина кристалла, г/см²; отрицательная — не считалась.</summary>
        double crystalRadiationLength = -1.0;

        /// <summary>
        /// Радиационная длина вещества кристалла по Цаю (PDG, «Passage of
        /// particles through matter», ф. 34.25–34.26), г/см². Состав — массовые
        /// доли <see cref="GeometryMaterial.Fractions"/>, массы —
        /// <see cref="MaterialDatabase.AtomicMass"/>. Смесь — по обратным
        /// длинам с массовыми долями. Проверено на CsI (8.39) и NaI (9.49)
        /// против таблицы PDG — сходится в третьем знаке.
        /// </summary>
        double CrystalRadiationLength()
        {
            if (this.crystalRadiationLength > 0.0)
            {
                return this.crystalRadiationLength;
            }

            double inverse = 0.0;
            Dictionary<int, double> mass = MaterialDatabase.AtomicMass;
            foreach (KeyValuePair<int, double> pair in this.geometry.Crystal.Fractions)
            {
                double a;
                if (pair.Value > 0.0 && mass.TryGetValue(pair.Key, out a) && a > 0.0)
                {
                    inverse += pair.Value / TsaiRadiationLength(pair.Key, a);
                }
            }

            // Состава нет (сцена без вещества) — вылета всё равно не будет:
            // без `electron` ветка переноса не заходит. Единица — чтобы не
            // делить на ноль, если всё же зайдёт.
            this.crystalRadiationLength = inverse > 0.0 ? 1.0 / inverse : 1.0;
            return this.crystalRadiationLength;
        }

        // (`AMBER44`, П94) Радиационные длины веществ обвязки — кэш на экземпляр,
        // по тому же ключу, что <see cref="CarryMedium"/> и <see cref="LayerBrem"/>.
        readonly Dictionary<GeometryMaterial, double> layerRadiationCache =
            new Dictionary<GeometryMaterial, double>();

        /// <summary>
        /// (`AMBER44`, П94) Радиационная длина вещества СЛОЯ обвязки, г/см² —
        /// тем же счётом по Цаю, что <see cref="CrystalRadiationLength"/>, по
        /// массовым долям слоя. Нужна ширине Хайленда у переноса электрона в
        /// слое (<see cref="TransportInLayers"/>). Состава нет — единица (шаг
        /// в таком слое всё равно не делается: плотности нет).
        /// </summary>
        double LayerRadiationLength(GeometryMaterial material)
        {
            double x0;
            if (this.layerRadiationCache.TryGetValue(material, out x0))
            {
                return x0;
            }

            double inverse = 0.0;
            Dictionary<int, double> mass = MaterialDatabase.AtomicMass;
            foreach (KeyValuePair<int, double> pair in material.Fractions)
            {
                double a;
                if (pair.Value > 0.0 && mass.TryGetValue(pair.Key, out a) && a > 0.0)
                {
                    inverse += pair.Value / TsaiRadiationLength(pair.Key, a);
                }
            }

            x0 = inverse > 0.0 ? 1.0 / inverse : 1.0;
            this.layerRadiationCache[material] = x0;
            return x0;
        }

        // ------------------------------------------------------------------
        // (`M13`, П100 18.09.2026) СМЕШАННАЯ СХЕМА УПРУГОГО РАССЕЯНИЯ В СЛОЯХ
        // ОБВЯЗКИ — под ключом <see cref="ElectronLayerMixedScattering"/>.
        //
        // ЧТО БЫЛО НЕ ТАК. Шарнир Хайленда несёт гауссово ядро многократного
        // рассеяния; хвоста ОДНОКРАТНОГО рассеяния на большие углы (∝ 1/θ⁴)
        // у него нет. Замер П100 (`diag/hinge_vs_rutherford.txt`): за один
        // шаг переноса (0.1 пробега CSDA) в PTFE при 500 кэВ вероятность
        // отклонения > 90° у шарнира 4.2e-5, у экранированного Резерфорда
        // 6.1e-3 (×146; при 1000 кэВ ×650), при том что средний 1 − cos θ
        // шага у обоих сходится в 5…14 %. В ТЯЖЁЛОМ веществе (CsI) шарнир
        // за шаг почти изотропен (θ₀ ≈ 0.9 рад) — обратное рассеяние там
        // диффузионное, и П27 поверила его по пику; в ЛЁГКОМ (PTFE, Al, MgO)
        // заметную долю обратного рассеяния даёт именно хвост, и η выходило
        // ×0.6…0.8 к Табате (П94 §5.5).
        //
        // ЧТО СДЕЛАНО (класс II по упругому рассеянию, как у PENELOPE):
        // угол отсечки θ_c (<see cref="LayerHardCutoffDeg"/>, 20°) делит
        // столкновения на ЖЁСТКИЕ (1 − cos θ > 1 − cos θ_c) и МЯГКИЕ.
        // * Жёсткие разыгрываются ПО ОДНОМУ: свободный пробег между ними —
        //   экспонента с Σ n_i σ_i(θ > θ_c) по элементам слоя, сечение —
        //   экранированное Резерфорда (Вентцель) с экранированием Мольера
        //   (A = (ħ/2pa)²(1.13 + 3.76(αZ/β)²), a = 0.885 a₀ Z^(−1/3)) и
        //   множителем Z(Z+1) — тем же, что у Мольера/Хайленда; угол — точным
        //   обращением ∫ dx/(x + 2A)² выше отсечки; поправка Мотта —
        //   Мак-Кинли—Фешбаха (Z ≤ 30, т. е. вся лёгкая обвязка) отбором по
        //   мажоранте 1 + παZβ/4, отвергнутое — пустое столкновение. Жёсткое
        //   столкновение, выпавшее раньше конца шага, ОБРЫВАЕТ шаг: шаг
        //   укорачивается до него, мягкий шарнир считается на укороченный шаг,
        //   поворот жёсткого — в его конце.
        // * Мягкие — тем же случайным шарниром, но ширина — из ТРАНСПОРТНОГО
        //   сечения ниже отсечки: средний 1 − cos θ шага длины s равен
        //   1 − exp(−s·Σ n_i σ_tr,soft,i), σ_tr,soft = ∫₀^{μ_c} μ dσ по тому же
        //   экранированному Резерфорду. Полное среднеквадратичное отклонение
        //   (мягкое + жёсткое) — транспортное сечение Резерфорда целиком; на
        //   шаге 0.1 пробега оно сходится с θ₀² Хайленда в 5…14 % в лёгком
        //   веществе (замер), так что ширина шага в целом сохранена.
        //   ⚠ ПОЧЕМУ НЕ «Хайленд × √f_soft» (первая редакция П100): формула
        //   Хайленда НЕ аддитивна по шагу — логарифмическая скобка при
        //   дроблении шага теряет вклад хвоста, и η ВЫКЛ падало 0.062 → 0.027
        //   при шаге 0.1 → 0.01 пробега, ВКЛ в той редакции — 0.070 → 0.056
        //   (`handover/p100-m13/tables/lr_variantA_*`). Транспортное сечение
        //   аддитивно по построению (⟨cos⟩ перемножается), и η под ключом
        //   от шага не зависит (замер там же).
        // В КРИСТАЛЛЕ (<see cref="TransportElectron"/>) ничего не меняется.
        // Без ключа — ни одного лишнего случайного числа, ход прежний.
        // ------------------------------------------------------------------

        /// <summary>
        /// (`M13`) Выше этого Z поправка Мак-Кинли—Фешбаха не применяется
        /// (она — первый порядок по αZ и годна до Z ≈ 30); дальше — чистый
        /// экранированный Резерфорд. В тяжёлом слое (Pb, W, сталь) обратное
        /// рассеяние диффузионное, и хвост там решает мало.
        /// </summary>
        const int MottMaxZ = 30;

        /// <summary>(`M13`) Элемент вещества слоя для розыгрыша упругих столкновений.</summary>
        sealed class ScatterElement
        {
            /// <summary>Заряд ядра.</summary>
            public int Z;

            /// <summary>Ядер в см³ вещества слоя.</summary>
            public double AtomsPerCm3;

            /// <summary>Z^(1/3) — для радиуса экранирования.</summary>
            public double Z13;

            /// <summary>Z(Z+1) — ядро и атомные электроны, как у Мольера.</summary>
            public double ZZ1;

            /// <summary>Поправка Мотта применима (Z ≤ <see cref="MottMaxZ"/>).</summary>
            public bool Mott;
        }

        // (`M13`) Кэш элементов на вещество — по тому же ключу, что
        // <see cref="LayerRadiationLength"/>; и рабочий массив долей жёстких
        // сечений (на экземпляр — симулятор однопоточный).
        readonly Dictionary<GeometryMaterial, ScatterElement[]> layerScatterCache =
            new Dictionary<GeometryMaterial, ScatterElement[]>();
        double[] layerHardShare = new double[8];

        /// <summary>(`M13`) Элементы вещества слоя с плотностями ядер, кэш на вещество.</summary>
        ScatterElement[] LayerScatterElements(GeometryMaterial material)
        {
            ScatterElement[] found;
            if (this.layerScatterCache.TryGetValue(material, out found))
            {
                return found;
            }

            List<ScatterElement> list = new List<ScatterElement>();
            Dictionary<int, double> mass = MaterialDatabase.AtomicMass;
            foreach (KeyValuePair<int, double> pair in material.Fractions)
            {
                double a;
                if (pair.Value > 0.0 && mass.TryGetValue(pair.Key, out a) && a > 0.0)
                {
                    list.Add(new ScatterElement
                    {
                        Z = pair.Key,
                        AtomsPerCm3 = material.Density * pair.Value / a * Avogadro,
                        Z13 = Math.Pow(pair.Key, 1.0 / 3.0),
                        ZZ1 = pair.Key * (pair.Key + 1.0),
                        Mott = pair.Key <= MottMaxZ,
                    });
                }
            }

            found = list.ToArray();
            if (this.layerHardShare.Length < found.Length)
            {
                this.layerHardShare = new double[found.Length];
            }

            this.layerScatterCache[material] = found;
            return found;
        }

        /// <summary>(`M13`) 1 − cos отсечки смешанной схемы из <see cref="LayerHardCutoffDeg"/>.</summary>
        double LayerHardCutoffMu()
        {
            double deg = this.LayerHardCutoffDeg;
            if (!(deg > 0.0) || deg >= 180.0)
            {
                deg = 20.0;
            }

            return 1.0 - Math.Cos(deg * Math.PI / 180.0);
        }

        /// <summary>
        /// (`M13`) Удвоенный параметр экранирования Мольера 2A = 2(ħ/2pa)²
        /// (1.13 + 3.76 (αZ/β)²), a = 0.885 a₀ Z^(−1/3): сечение
        /// dσ/d(1 − cos θ) ∝ 1/(1 − cos θ + 2A)².
        /// </summary>
        static double ScreeningTwoA(int z, double z13, double beta2, double betaGamma)
        {
            double chi = ScatteringData.FineStructure * z13 / (1.77 * betaGamma);   // ħ/(2 p a)
            double az = ScatteringData.FineStructure * z;
            return 2.0 * chi * chi * (1.13 + 3.76 * az * az / beta2);
        }

        /// <summary>
        /// (`M13`) Сечения упругого рассеяния слоя при энергии <paramref name="tKev"/>:
        /// <paramref name="hardPerCm"/> — Σ n_i σ_i выше отсечки с мажорантой
        /// Мотта, 1/см (обратный свободный пробег до кандидата жёсткого
        /// столкновения); <paramref name="hardShare"/>[i] — вклад элемента в
        /// него (выбор элемента); <paramref name="softTransportPerCm"/> —
        /// транспортное сечение Σ n_i ∫₀^{μ_c} μ dσ_i НИЖЕ отсечки, 1/см:
        /// средний 1 − cos θ мягкого шарнира на шаг s равен 1 − exp(−s·Σnσ_tr,soft)
        /// — АДДИТИВНО по шагу (замер П100: ширина Хайленда × √f_soft не
        /// аддитивна, и η падало вдвое при шаге 0.1 → 0.01).
        /// </summary>
        void LayerElasticStep(ScatterElement[] elements, double tKev, double muCut,
                              out double hardPerCm, out double softTransportPerCm, double[] hardShare)
        {
            double gamma = 1.0 + tKev / ElectronMassKev;
            double beta2 = 1.0 - 1.0 / (gamma * gamma);
            double beta = Math.Sqrt(beta2);
            double p2 = tKev * (tKev + 2.0 * ElectronMassKev);                  // (кэВ/c)²
            double common = 2.0 * Math.PI * ClassicalRadiusCm * ClassicalRadiusCm
                            * ElectronMassKev * ElectronMassKev / (beta2 * p2);   // см²
            double hard = 0.0, trSoft = 0.0;
            for (int i = 0; i < elements.Length; i++)
            {
                ScatterElement e = elements[i];
                double a2 = ScreeningTwoA(e.Z, e.Z13, beta2, beta * gamma);
                double pref = e.AtomsPerCm3 * common * e.ZZ1;                      // 1/см
                double majorant = e.Mott ? 1.0 + Math.PI * ScatteringData.FineStructure * e.Z * beta / 4.0 : 1.0;
                double h = pref * (1.0 / (muCut + a2) - 1.0 / (2.0 + a2)) * majorant;
                hardShare[i] = h;
                hard += h;

                // Транспортное сечение НИЖЕ отсечки, 1/см:
                // ∫₀^X x dx/(x + 2A)² = ln((X + 2A)/2A) + 2A/(X + 2A) − 1.
                trSoft += pref * (Math.Log((muCut + a2) / a2) + a2 / (muCut + a2) - 1.0);
            }

            hardPerCm = hard;
            softTransportPerCm = trSoft > 0.0 ? trSoft : 0.0;
        }

        /// <summary>
        /// (`M13`) Одно жёсткое столкновение: элемент — по доле в
        /// <paramref name="hardShare"/>, угол — обращением экранированного
        /// Резерфорда выше отсечки, отбор Мак-Кинли—Фешбаха по мажоранте
        /// (отвергнутое — пустое столкновение, направление не меняется).
        /// Возвращает true, если поворот состоялся.
        /// </summary>
        bool LayerHardCollision(ScatterElement[] elements, double[] hardShare, double hardPerCm,
                                double tKev, double muCut, ref double ux, ref double uy, ref double uz)
        {
            double pick = this.Uniform() * hardPerCm;
            int i = 0;
            for (; i < elements.Length - 1; i++)
            {
                pick -= hardShare[i];
                if (pick <= 0.0)
                {
                    break;
                }
            }

            ScatterElement e = elements[i];
            double gamma = 1.0 + tKev / ElectronMassKev;
            double beta2 = 1.0 - 1.0 / (gamma * gamma);
            double beta = Math.Sqrt(beta2);
            double a2 = ScreeningTwoA(e.Z, e.Z13, beta2, beta * gamma);
            double lo = 1.0 / (muCut + a2), hi = 1.0 / (2.0 + a2);
            double mu = 1.0 / (lo - this.Uniform() * (lo - hi)) - a2;
            if (mu < muCut)
            {
                mu = muCut;
            }
            else if (mu > 2.0)
            {
                mu = 2.0;
            }

            if (e.Mott)
            {
                // Мак-Кинли—Фешбах для электрона: R = 1 − β² s² + παZβ s(1 − s),
                // s = sin(θ/2) = √(μ/2); R ≤ 1 + παZβ/4.
                double s = Math.Sqrt(0.5 * mu);
                double paz = Math.PI * ScatteringData.FineStructure * e.Z * beta;
                double r = 1.0 - beta2 * s * s + paz * s * (1.0 - s);
                if (this.Uniform() * (1.0 + 0.25 * paz) > r)
                {
                    return false;
                }
            }

            this.Rotate(ref ux, ref uy, ref uz, 1.0 - mu);
            return true;
        }

        /// <summary>Радиационная длина элемента по Цаю, г/см².</summary>
        static double TsaiRadiationLength(int z, double a)
        {
            double lrad, lradPrime;
            switch (z)
            {
                case 1: lrad = 5.31; lradPrime = 6.144; break;
                case 2: lrad = 4.79; lradPrime = 5.621; break;
                case 3: lrad = 4.74; lradPrime = 5.805; break;
                case 4: lrad = 4.71; lradPrime = 5.924; break;
                default:
                    lrad = Math.Log(184.15 * Math.Pow(z, -1.0 / 3.0));
                    lradPrime = Math.Log(1194.0 * Math.Pow(z, -2.0 / 3.0));
                    break;
            }

            double alpha = z / 137.035999;
            double a2 = alpha * alpha;
            double f = a2 * (1.0 / (1.0 + a2) + 0.20206 - 0.0369 * a2 + 0.0083 * a2 * a2
                             - 0.002 * a2 * a2 * a2);
            return 716.408 * a / (z * (double)z * (lrad - f) + z * lradPrime);
        }

        /// <summary>
        /// Расстояние от точки внутри кристалла до ближайшей его грани, см —
        /// нижняя граница пути до выхода по ЛЮБОМУ направлению. Внутренний
        /// радиус кольца не смотрится — как и в <see cref="CrystalPath"/>.
        /// </summary>
        double CrystalNearestFace(double x, double y, double z)
        {
            Region c = this.crystal;
            double d = Math.Min(z - c.ZMin, c.ZMax - z);
            if (c.IsBox)
            {
                d = Math.Min(d, Math.Min(c.AX - Math.Abs(x), c.AY - Math.Abs(y)));
            }
            else
            {
                d = Math.Min(d, c.ROut - Math.Sqrt(x * x + y * y));
            }

            return d > 0.0 ? d : 0.0;
        }

        /// <summary>
        /// (`AMBER44`, П94) То же для ЛЮБОЙ области сцены: расстояние от точки
        /// внутри области до ближайшей её границы, см, — нижняя граница пути до
        /// выхода из неё по любому направлению. ⚠ Области сцены ВЛОЖЕНЫ и
        /// перекрываются, область точки — ПЕРВАЯ по списку из накрывающих
        /// (<see cref="At"/>): боковой отражатель — брус, накрывающий и
        /// кристалл, корпус накрывает отражатель. Значит, границы области —
        /// не только её собственные стенки, но и тела всех областей, стоящих
        /// в списке РАНЬШЕ (дыры в ней); до дыры берётся евклидово расстояние
        /// до её тела. У кольца смотрится и внутренний радиус. Ранний выход
        /// переноса в слое (<see cref="TransportInLayers"/>): пробег короче —
        /// электрон погибнет в этой области при любой траектории, кристалла
        /// ему не видать. Без этого на 59.5 кэВ каждый фотоэлектрон пробы и
        /// оправы шёл десятками шагов с разбором луча на каждом (измерено:
        /// ×3 к цене истории на RC103, ×5 на ASN16 с пробой Lu₂O₃).
        /// </summary>
        double RegionNearestFace(Region r, double x, double y, double z)
        {
            double d = Math.Min(z - r.ZMin, r.ZMax - z);
            double rad = Math.Sqrt(x * x + y * y);
            if (r.IsBox)
            {
                d = Math.Min(d, Math.Min(r.AX - Math.Abs(x), r.AY - Math.Abs(y)));
            }
            else
            {
                d = Math.Min(d, r.ROut - rad);
                if (r.RIn > 0.0)
                {
                    d = Math.Min(d, rad - r.RIn);
                }
            }

            // Дыры: области, стоящие в списке раньше этой.
            Region[] all = this.regionArray;
            for (int i = 0; i < all.Length && d > 0.0; i++)
            {
                Region h = all[i];
                if (h == r)
                {
                    break;
                }

                double dz = Math.Max(0.0, Math.Max(h.ZMin - z, z - h.ZMax));
                double dxy;
                if (h.IsBox)
                {
                    double dx = Math.Max(0.0, Math.Abs(x) - h.AX);
                    double dy = Math.Max(0.0, Math.Abs(y) - h.AY);
                    dxy = Math.Sqrt(dx * dx + dy * dy);
                }
                else
                {
                    dxy = Math.Max(0.0, Math.Max(rad - h.ROut, h.RIn - rad));
                }

                d = Math.Min(d, Math.Sqrt(dxy * dxy + dz * dz));
            }

            return d > 0.0 ? d : 0.0;
        }

        /// <summary>
        /// Ширина θ₀ многократного рассеяния на шаге <paramref name="xOverX0"/>
        /// радиационных длин при кинетической энергии <paramref name="tKev"/> —
        /// формула Хайленда в редакции PDG (с β² под логарифмом), радианы.
        /// Скобка зажата снизу: формула подогнана для 10⁻³ &lt; x/X₀ &lt; 100, а
        /// на самых коротких шагах логарифм увёл бы её в ноль и ниже.
        /// </summary>
        static double HighlandTheta0(double tKev, double xOverX0)
        {
            double gamma = 1.0 + tKev / ElectronMassKev;
            double beta2 = 1.0 - 1.0 / (gamma * gamma);
            double p = Math.Sqrt(tKev * (tKev + 2.0 * ElectronMassKev));    // кэВ/c
            double betaP = Math.Sqrt(beta2) * p;                            // кэВ
            double bracket = 1.0 + 0.038 * Math.Log(xOverX0 / beta2);
            if (bracket < 0.25)
            {
                bracket = 0.25;
            }

            return 13600.0 / betaP * Math.Sqrt(xOverX0) * bracket;
        }

        /// <summary>
        /// Розыгрыш 1−cos θ отклонения шага: экспонента со средним θ₀²,
        /// обрезанная на 2. Для малых θ₀ это в точности гауссово (рэлеевское)
        /// распределение пространственного угла с проекционной шириной θ₀; для
        /// больших — переходит в изотропию, куда и уходит трек мягкого
        /// электрона.
        /// </summary>
        double SampleHingeMu(double theta0)
        {
            double s = theta0 * theta0;
            if (s > 50.0)
            {
                return 2.0 * this.Uniform();
            }

            double r = this.Uniform();
            double mu = -s * Math.Log(1.0 - r * (1.0 - Math.Exp(-2.0 / s)));
            return mu > 2.0 ? 2.0 : (mu < 0.0 ? 0.0 : mu);
        }

        /// <summary>
        /// Косинус угла фотоэлектрона к направлению кванта — Заутер—Гаврила
        /// для K-оболочки, розыгрыш отбором как в Geant4
        /// (`G4SauterGavrilaAngularDistribution::SampleDirection`).
        /// </summary>
        double SauterCosine(double tKev)
        {
            double tau = tKev / ElectronMassKev;
            if (tau > 1000.0)
            {
                return 1.0;
            }

            double gamma = tau + 1.0;
            double beta = Math.Sqrt(tau * (tau + 2.0)) / gamma;
            double a = (1.0 - beta) / beta;
            double ap2 = a + 2.0;
            double b = 0.5 * beta * gamma * (gamma - 1.0) * (gamma - 2.0);
            double grej = 2.0 * (1.0 + a * b) / a;
            double z, g;
            int guard = 0;
            do
            {
                double q = this.Uniform();
                z = 2.0 * a * (2.0 * q + ap2 * Math.Sqrt(q)) / (ap2 * ap2 - 4.0 * q);
                g = (2.0 - z) * (1.0 / (a + z) + b);
            }
            while (g < this.Uniform() * grej && ++guard < 1000);

            return 1.0 - z;
        }

        /// <summary>
        /// Косинус угла лептона пары к направлению кванта — модифицированный
        /// Цай (`G4ModifiedTsai::SampleCosTheta`), <paramref name="tKev"/> —
        /// кинетическая энергия лептона.
        /// </summary>
        double TsaiCosine(double tKev)
        {
            const double A1 = 1.6, A2 = A1 / 3.0, Border = 0.25;
            double uMax = 2.0 * (1.0 + tKev / ElectronMassKev);
            double u;
            int guard = 0;
            do
            {
                double uu = -Math.Log(this.Uniform() * this.Uniform());
                u = Border > this.Uniform() ? uu * A1 : uu * A2;
            }
            while (u > uMax && ++guard < 1000);

            return 1.0 - 2.0 * u * u / (uMax * uMax);
        }

        /// <summary>
        /// Направление рождения электрона по процессу (<see cref="ElectronBirth"/>):
        /// опорное направление <paramref name="rx"/>… — квант для `Photo`/`Pair`,
        /// сам электрон для `Given`; для `Isotropic` не читается.
        /// </summary>
        void ElectronBirthDirection(ElectronBirth birth, double te, double rx, double ry, double rz,
                                    out double ux, out double uy, out double uz)
        {
            switch (birth)
            {
                case ElectronBirth.Photo:
                    ux = rx; uy = ry; uz = rz;
                    this.Rotate(ref ux, ref uy, ref uz, this.SauterCosine(te));
                    return;
                case ElectronBirth.Pair:
                    ux = rx; uy = ry; uz = rz;
                    this.Rotate(ref ux, ref uy, ref uz, this.TsaiCosine(te));
                    return;
                case ElectronBirth.Given:
                    double norm = Math.Sqrt(rx * rx + ry * ry + rz * rz);
                    if (norm > 1e-12)
                    {
                        ux = rx / norm; uy = ry / norm; uz = rz / norm;
                        return;
                    }

                    break;
            }

            this.Isotropic(out ux, out uy, out uz);
        }

        /// <summary>
        /// Направление комптоновского электрона из кинематики: импульс кванта
        /// до рассеяния минус после (кэВ/c, направления единичные). Нормировка
        /// — в <see cref="ElectronBirthDirection"/>; нулевой вектор (квант не
        /// отклонился) там же уходит в изотропию.
        /// </summary>
        static void ComptonElectronDirection(double e, double ux0, double uy0, double uz0,
                                             double scattered, double ux1, double uy1, double uz1,
                                             out double dx, out double dy, out double dz)
        {
            dx = e * ux0 - scattered * ux1;
            dy = e * uy0 - scattered * uy1;
            dz = e * uz0 - scattered * uz1;
        }

        /// <summary>
        /// Провести электрон кинетической энергии <paramref name="te"/> из точки
        /// (x, y, z) в направлении (ux, uy, uz) до гибели или до грани.
        /// Возвращает энергию, унесённую через грань (0 — погиб внутри), не
        /// больше `te − radiated` (то, что осталось после тормозного).
        ///
        /// (`M3`, П44) <paramref name="alongPath"/> — тормозное рождается на
        /// шагах здесь (<see cref="StepBremsstrahlung"/>), излучённое копится в
        /// <paramref name="radiated"/>, вылет квантов — в <paramref name="lost"/>;
        /// без него ни одного лишнего розыгрыша, ход тот же, что до П44.
        ///
        /// (`AMBER44`, П94) Под ключом <see cref="ElectronLayerTransport"/>
        /// электрон, пересёкший грань, ведётся в слоях обвязки
        /// (<see cref="EscapeOrReturn"/>) и может ВЕРНУТЬСЯ: тогда перенос по
        /// кристаллу продолжается от точки входа с остатком энергии, а унесённым
        /// считается лишь то, что осело снаружи. Без ключа ни одного лишнего
        /// розыгрыша: вылет — конец, как было.
        /// </summary>
        double TransportElectron(double x, double y, double z, double ux, double uy, double uz,
                                 double te, bool alongPath, int depth,
                                 ref double radiated, ref double lost)
        {
            double density = this.geometry.Crystal.Density;
            double x0 = this.CrystalRadiationLength();
            double residual = ElectronData.RangeOf(this.electron, te);     // г/см²
            if (!(residual > 0.0) || !(density > 0.0))
            {
                return 0.0;
            }

            // Ранний выход: пробег короче расстояния до ближайшей грани —
            // погибнет внутри при любой траектории.
            if (!this.ElectronTransportNoEarlyExit
                && residual / density <= this.CrystalNearestFace(x, y, z))
            {
                if (alongPath)
                {
                    this.RestBremsstrahlung(x, y, z, ux, uy, uz, te, te, depth, ref radiated, ref lost);
                }

                return 0.0;
            }

            double fraction = this.ElectronStepFraction;
            if (!(fraction > 0.0) || fraction > 1.0)
            {
                fraction = 1.0;
            }

            // (`M3`) Подтяжка уровня к ESTAR — по НАЧАЛЬНОЙ энергии, одна на
            // весь путь: так интеграл шагов даёт число квантов толстой мишени.
            double anchor = alongPath ? this.bremTable.Anchor(te) : 1.0;

            // (`AMBER44`) Унесено через грани и осело СНАРУЖИ; без ключа —
            // энергия единственного вылета, как было.
            double escaped = 0.0;

            double t = te;
            for (int step = 0; step < TransportMaxSteps && t > TransportCutKev; step++)
            {
                double stepG = residual * fraction;                          // г/см²
                double stepCm = stepG / density;

                // Прямой ход до шарнира.
                double first = stepCm * this.Uniform();
                double toEdge = this.CrystalPath(x, y, z, ux, uy, uz);
                if (first >= toEdge)
                {
                    if (!this.EscapeOrReturn(ref x, ref y, ref z, ref ux, ref uy, ref uz, toEdge,
                                             residual - toEdge * density, te - radiated - escaped,
                                             depth, ref residual, ref t, ref escaped))
                    {
                        return escaped;
                    }

                    continue;               // вернулся — новый шаг от точки входа
                }

                x += ux * first;
                y += uy * first;
                z += uz * first;

                // Поворот на угол всего шага, ширина — по энергии в его середине.
                double tMid = ElectronData.EnergyOfRange(this.electron, residual - 0.5 * stepG);
                if (tMid < TransportCutKev)
                {
                    tMid = TransportCutKev;
                }

                if (alongPath)
                {
                    // (`M3`, П44) Тормозное ШАГА — в точке шарнира, при энергии
                    // середины шага, по направлению электрона ДО поворота.
                    this.StepBremsstrahlung(x, y, z, ux, uy, uz, tMid, stepG, anchor, te, depth,
                                            ref radiated, ref lost);
                }

                double theta0 = HighlandTheta0(tMid, stepG / x0);
                this.Rotate(ref ux, ref uy, ref uz, 1.0 - this.SampleHingeMu(theta0));

                // Прямой ход до конца шага.
                double second = stepCm - first;
                toEdge = this.CrystalPath(x, y, z, ux, uy, uz);
                if (second >= toEdge)
                {
                    if (!this.EscapeOrReturn(ref x, ref y, ref z, ref ux, ref uy, ref uz, toEdge,
                                             residual - (first + toEdge) * density, te - radiated - escaped,
                                             depth, ref residual, ref t, ref escaped))
                    {
                        return escaped;
                    }

                    continue;
                }

                x += ux * second;
                y += uy * second;
                z += uz * second;
                residual -= stepG;
                t = ElectronData.EnergyOfRange(this.electron, residual);

                if (!this.ElectronTransportNoEarlyExit
                    && residual / density <= this.CrystalNearestFace(x, y, z))
                {
                    if (alongPath)
                    {
                        this.RestBremsstrahlung(x, y, z, ux, uy, uz, t, te, depth, ref radiated, ref lost);
                    }

                    return escaped;
                }
            }

            return escaped;
        }

        /// <summary>
        /// (`AMBER44`, П94) Электрон дошёл до грани кристалла на расстоянии
        /// <paramref name="toEdge"/> по лучу с остаточным пробегом
        /// <paramref name="residualAtFace"/> (г/см²). Без ключа
        /// <see cref="ElectronLayerTransport"/> — прежний вылет: энергия грани
        /// (<see cref="EscapeEnergy"/>, не больше <paramref name="cap"/>)
        /// прибавляется к <paramref name="escaped"/>, и это конец (false).
        /// Под ключом электрон переводится на грань, чуть наружу (сдвиг 1e-7,
        /// как у <see cref="EfficiencySimulator.NoteEscape"/>), излучает
        /// тормозное вещества слоя (<see cref="LayerBremsstrahlung"/>) и
        /// ведётся в слоях (<see cref="TransportInLayers"/>). Вернулся — в
        /// <paramref name="escaped"/> ложится только осевшее снаружи, точка,
        /// направление, энергия <paramref name="t"/> и остаточный пробег
        /// <paramref name="residual"/> становятся точкой входа (чуть внутри), и
        /// перенос по кристаллу продолжается (true). Не вернулся — унесено всё,
        /// что вышло (false).
        /// </summary>
        bool EscapeOrReturn(ref double x, ref double y, ref double z,
                            ref double ux, ref double uy, ref double uz,
                            double toEdge, double residualAtFace, double cap, int depth,
                            ref double residual, ref double t, ref double escaped)
        {
            double tExit = this.EscapeEnergy(residualAtFace, cap);
            if (!this.ElectronLayerTransport || !(tExit > TransportCutKev))
            {
                escaped += tExit;
                return false;
            }

            // (`M13`, П106) Население: занесённый электрон — только сам перенос
            // из `CarriedElectronDeposit` (глубина 0 под меткой); всё, что
            // родилось в кристалле (в том числе от его тормозного, глубина ≥ 1),
            // — своё, как `GetLogicalVolumeAtVertex` у арбитра. Рычаги замера
            // `LayerReturnOwn` / `LayerReturnCarried` списывают население на
            // грани, как без ключа; умолчанием оба ВКЛ — ход прежний.
            bool carried = this.carriedInCrystal && depth == 0;
            if (carried ? !this.LayerReturnCarried : !this.LayerReturnOwn)
            {
                escaped += tExit;
                return false;
            }

            double advance = toEdge + 1e-7;
            x += ux * advance;
            y += uy * advance;
            z += uz * advance;
            // (П106) Грань выхода своего электрона — для счётчиков и рычага
            // `LayerReturnKill` 2/3; случайных чисел не тянет.
            int exitFace = carried ? 0 : this.CrystalFaceId(x, y, z);

            // ⛔ КЭШ ЛУЧА — СНИМОК И ВОЗВРАТ (П94 §7.1). `At` доверяет
            // разобранному лучу, если точка лежит на нём в 10 нм и
            // `along > 1e-7`, — верно, пока луч разбирал сам обход, идущий по
            // нему. Точка выхода электрона к лучу кванта не относится, а
            // совпасть с ним в 10 нм на 20 млн историй успевает (измерено на
            // голом кристалле: 6 бинов по одному отсчёту, а ключ там обязан
            // быть инертен); и наоборот — луч электрона, оставшийся в кэше,
            // сбил бы обход кванта. Потому кэш кванта прячется на время
            // переноса электрона и возвращается тем же (`SaveRay`/`RestoreRay`):
            // обход кванта продолжается ровно тем кэшем, что без ключа.
            this.SaveRay();
            this.CountLayerEscapes++;
            if (carried)
            {
                this.CountLayerEscapesCarried++;
            }

            // (П106) Рычаг `LayerExitBremsstrahlung` false — своему электрону
            // тормозное слоя не разыгрывать (зеркало `killescbrem`); у занесённого,
            // выходящего из кристалла, тормозное снаружи глушит `LayerBornBremsstrahlung`
            // (зеркало `killoutbrem`: его родословная — «рождён вне кристалла»).
            bool bremAllowed = carried ? this.LayerBornBremsstrahlung : this.LayerExitBremsstrahlung;
            // (`M13`, П106) Под ключом `ElectronLayerBremAlongPath` тормозное
            // рождается ПО ХОДУ переноса в слоях (`TransportInLayers`), а не толстой
            // мишенью в точке выхода; кванты — в очередь вылетов, когда она открыта.
            double tOut = tExit - (bremAllowed && !this.ElectronLayerBremAlongPath ? this.LayerBremsstrahlung(x, y, z, tExit) : 0.0);
            double tBack = tOut;
            this.layerBremPush = null;
            this.layerBremEnabled = bremAllowed;
            bool back = this.TransportInLayers(ref x, ref y, ref z, ref ux, ref uy, ref uz, ref tBack, depth);
            this.RestoreRay();
            if (!back)
            {
                escaped += tExit;
                return false;
            }

            // (П106) Возврат своего электрона: грань входа против грани выхода
            // (счётчики) и рычаг `LayerReturnKill` — списать вернувшегося на
            // входе (всё, что вышло, осталось снаружи; тормозное выхода уже
            // разыграно — как `killret*` у арбитра).
            if (!carried)
            {
                int entryFace = this.CrystalFaceId(x, y, z);
                bool same = entryFace == exitFace;
                if (same)
                {
                    this.CountLayerReturnsSameFace++;
                }
                else
                {
                    this.CountLayerReturnsOtherFace++;
                }

                if (this.LayerReturnKill == 1 || (this.LayerReturnKill == 2 && same) || (this.LayerReturnKill == 3 && !same))
                {
                    this.CountLayerReturnsKilled++;
                    escaped += tExit;
                    return false;
                }
            }
            else
            {
                this.CountLayerReturnsCarried++;
            }

            // Вернулся: осело снаружи `tExit − tBack` (тормозное слоя — тоже
            // снаружи), дальше — перенос по кристаллу с остатком.
            this.CountLayerReturns++;
            this.SumLayerReturnKev += tBack;
            escaped += tExit - tBack;
            t = tBack;
            residual = ElectronData.RangeOf(this.electron, t);
            return residual > 0.0;
        }

        /// <summary>
        /// (`M13`, П106) Грань кристалла, на которой (или у которой) лежит
        /// точка: ближайшая по расстоянию поверхность. Брус: 1 z-min, 2 z-max,
        /// 3 −x, 4 +x, 5 −y, 6 +y; цилиндр/кольцо: 1 z-min, 2 z-max, 3 наружный
        /// бок, 4 внутренний. Только для счётчиков и рычага `LayerReturnKill`
        /// — на ход переноса не влияет.
        /// </summary>
        int CrystalFaceId(double x, double y, double z)
        {
            Region c = this.crystal;
            int best = 1;
            double d = Math.Abs(z - c.ZMin);
            double dz1 = Math.Abs(c.ZMax - z);
            if (dz1 < d) { d = dz1; best = 2; }
            if (c.IsBox)
            {
                double dxm = Math.Abs(x + c.AX), dxp = Math.Abs(c.AX - x);
                double dym = Math.Abs(y + c.AY), dyp = Math.Abs(c.AY - y);
                if (dxm < d) { d = dxm; best = 3; }
                if (dxp < d) { d = dxp; best = 4; }
                if (dym < d) { d = dym; best = 5; }
                if (dyp < d) { d = dyp; best = 6; }
            }
            else
            {
                double rad = Math.Sqrt(x * x + y * y);
                double dro = Math.Abs(c.ROut - rad);
                if (dro < d) { d = dro; best = 3; }
                if (c.RIn > 0.0)
                {
                    double dri = Math.Abs(rad - c.RIn);
                    if (dri < d) { d = dri; best = 4; }
                }
            }

            return best;
        }

        /// <summary>
        /// (`AMBER44`, П94) ТОРМОЗНОЕ ЭЛЕКТРОНА, ВЫШЕДШЕГО В СЛОЙ ОБВЯЗКИ —
        /// как у заноса `M3`/П44 (<see cref="EfficiencySimulator.OutsideBremsstrahlung"/>):
        /// толстая мишень вещества слоя в точке выхода, число и энергии квантов по
        /// сечениям слоя (<see cref="EfficiencySimulator.LayerBrem"/>), направление
        /// изотропное, сумма не больше энергии электрона. Квант кладётся в очередь
        /// вылетов (<see cref="EfficiencySimulator.NoteEscape"/>), если она
        /// открыта, — оттуда его поведёт обход обвязки; иначе (взвешенная ветка,
        /// где вылетевшие кванты не ведутся вовсе) он просто унесён — его
        /// энергия уже вошла в унос электрона. Те же ворота, что у заноса:
        /// <see cref="EfficiencySimulator.ElectronAnyMaterial"/> и
        /// <see cref="EfficiencySimulator.Bremsstrahlung"/>; без них ноль и ни
        /// одного розыгрыша. Точка в пустоте (зазор у грани) — вещества нет,
        /// ноль. Возвращает излучённую энергию, кэВ.
        /// </summary>
        double LayerBremsstrahlung(double x, double y, double z, double te)
        {
            const double MinKev = 5.0;
            if (!this.ElectronAnyMaterial || !this.Bremsstrahlung || !(te > MinKev))
            {
                return 0.0;
            }

            Region here = this.At(x, y, z);
            if (here == null || here.IsCrystal || here.Material == null)
            {
                return 0.0;
            }

            ThickTargetBrem table = this.LayerBrem(here.Material);
            if (table == null)
            {
                return 0.0;
            }

            double radiated = 0.0;
            int n = this.Poisson(table.Photons(te));
            for (int i = 0; i < n; i++)
            {
                double k = table.SampleKev(te, this.Uniform());
                double ax, ay, az;
                this.Isotropic(out ax, out ay, out az);
                double kUse = Math.Min(k, te - radiated);
                if (!(kUse > 0.0))
                {
                    continue;
                }

                radiated += kUse;
                this.CountBremPhotons++;
                this.SumBremKev += kUse;
                if (this.escapeCollect)
                {
                    this.NoteEscape(x, y, z, ax, ay, az, kUse);
                }
            }

            return radiated;
        }

        /// <summary>
        /// (`AMBER44`, П94; решение Amber 15.09.2026 «Перенос в слоях обвязки»)
        /// ПЕРЕНОС ЭЛЕКТРОНА ВНЕ КРИСТАЛЛА — в слоях обвязки, зазорах, пробе —
        /// от точки (x, y, z) (в слое либо чуть снаружи грани кристалла) в
        /// направлении (ux, uy, uz) с кинетической энергией <paramref name="t"/>
        /// до входа в кристалл, гибели или выхода из сцены. Возвращает true,
        /// если электрон ВОШЁЛ в кристалл; тогда точка (чуть внутри грани),
        /// направление и энергия — состояние на входе.
        ///
        /// Та же сгущённая история, что по кристаллу (<see cref="TransportElectron"/>):
        /// шаг — доля <see cref="ElectronStepFraction"/> остаточного пробега
        /// CSDA по таблице ESTAR ВЕЩЕСТВА СЛОЯ (<see cref="CarryMedium"/>: состав
        /// из `matdb` под `ecomp`, иначе вода); случайный шарнир с шириной
        /// Хайленда по радиационной длине слоя (<see cref="LayerRadiationLength"/>);
        /// граница области внутри отрезка — переход в соседнюю область на
        /// пересечении (<see cref="StepToBoundary"/>) с энергией по остатку
        /// пробега, дальше шаг считается заново по таблице нового вещества.
        /// Пустота (области нет или плотности нет) — по прямой без потерь до
        /// следующей границы. Разброса потерь и δ-электронов нет — как в
        /// кристалле. Тормозное здесь без ключа НЕ разыгрывается: у заноса оно
        /// снято в точке рождения (<see cref="OutsideBremsstrahlung"/>), у
        /// возврата — в точке выхода (<see cref="LayerBremsstrahlung"/>), толстой
        /// мишенью, как у `M3`. (`M13`, П106 19.09.2026) Под ключом
        /// <see cref="ElectronLayerBremAlongPath"/> (сделан ВЫКЛ) тормозное
        /// рождается ЗДЕСЬ, на шагах, тонкой мишенью вещества текущего слоя по
        /// направлению электрона (<see cref="LayerStepBremsstrahlung"/>), остаток
        /// у погибающего в слое — толстой мишенью в точке гибели
        /// (<see cref="LayerRestBremsstrahlung"/>), а толстые мишени в точках
        /// рождения и выхода не разыгрываются: у Geant4 (П106 §4) тормозное
        /// электронов обвязки даёт в 0–50 кэВ при 2614 на RC103 вдвое меньше,
        /// чем наша толстая мишень, — электрон из 1 мм PTFE / 1 мм Al уходит в
        /// пустоту или в кристалл, не дорадировав, и светит вперёд, не изотропно.
        ///
        /// ⚠ Обход зовёт <see cref="StepToBoundary"/> с НОВЫМ направлением на
        /// каждом шарнире — кэш луча собирается заново (O(областей)); электронов
        /// в обвязке 2…10 % историй континуума, шагов у каждого единицы.
        ///
        /// (`M13`, П100 18.09.2026) Под ключом <see cref="ElectronLayerMixedScattering"/>
        /// упругое рассеяние в слое — СМЕШАННОЕ (шапка раздела выше): на каждом
        /// шаге разыгрывается расстояние до жёсткого столкновения (выше угла
        /// отсечки, по экранированному Резерфорду с поправкой Мотта); выпало
        /// раньше конца шага — шаг обрывается на нём, и в его конце электрон
        /// поворачивается на жёсткий угол; мягкий шарнир того же шага — с
        /// шириной Хайленда × √(доля транспортного сечения ниже отсечки).
        /// Без ключа — прежний ход без единого лишнего случайного числа.
        /// </summary>
        bool TransportInLayers(ref double x, ref double y, ref double z,
                               ref double ux, ref double uy, ref double uz,
                               ref double t, int depth)
        {
            // ⚠ Зовущий обязан спрятать кэш луча кванта (`SaveRay`) и вернуть
            // его после (`RestoreRay`): здесь луч разбирается заново лучами
            // электрона, и кэш кванта после этого чужой (П94 §7.1).
            double fraction = this.ElectronStepFraction;
            if (!(fraction > 0.0) || fraction > 1.0)
            {
                fraction = 1.0;
            }

            // (`M13`) Смешанная схема: отсечка одна на весь перенос.
            bool mixed = this.ElectronLayerMixedScattering;
            double muCut = mixed ? this.LayerHardCutoffMu() : 0.0;

            // (`M13`, П106) Тормозное ПО ХОДУ переноса (ключ `ElectronLayerBremAlongPath`,
            // те же ворота, что у толстой мишени слоя, плюс рычаг замера
            // `layerBremEnabled`): таблица тонкой мишени и якорь ESTAR — на
            // вещество, пересчитываются при смене вещества (якорь — по энергии
            // входа в него); излучённое зажато энергией входа в перенос. Без ключа
            // — ни одной ветки и ни одного случайного числа.
            bool lbrem = this.ElectronLayerBremAlongPath && this.layerBremEnabled
                         && this.ElectronAnyMaterial && this.Bremsstrahlung;
            ThickTargetBrem bremTable = null;
            GeometryMaterial bremMaterial = null;
            double bremAnchor = 1.0, radiated = 0.0, tEntry = t;

            for (int step = 0; step < TransportMaxSteps && t > TransportCutKev; step++)
            {
                Region here = this.At(x, y, z);
                if (here != null && here.IsCrystal)
                {
                    return true;
                }

                double toNext = this.StepToBoundary(x, y, z, ux, uy, uz);
                if (toNext >= double.MaxValue)
                {
                    return false;           // ушёл из сцены
                }

                double density = here != null && here.Material != null ? here.Material.Density : 0.0;
                if (!(density > 0.0))
                {
                    // Пустота — по прямой до следующей границы, без потерь.
                    double through = toNext + 1e-7;
                    x += ux * through;
                    y += uy * through;
                    z += uz * through;
                    continue;
                }

                ElectronData.Material medium = this.CarryMedium(here.Material);
                double x0 = this.LayerRadiationLength(here.Material);
                double residual = ElectronData.RangeOf(medium, t);          // г/см²
                if (!(residual > 0.0))
                {
                    return false;
                }

                // (П106) Смена вещества — своя таблица тормозного и якорь по
                // энергии входа в вещество (в кристалле якорь — по начальной
                // энергии на весь путь; здесь путь составной).
                if (lbrem && !ReferenceEquals(here.Material, bremMaterial))
                {
                    bremMaterial = here.Material;
                    bremTable = this.LayerBrem(here.Material);
                    bremAnchor = bremTable != null ? bremTable.Anchor(t) : 1.0;
                }

                // Ранний выход, как в кристалле: пробег короче расстояния до
                // ближайшей границы области — погибнет в ней при любой траектории.
                if (residual / density <= this.RegionNearestFace(here, x, y, z))
                {
                    // (П106) Погибнет здесь — остаток тормозного толстой мишенью
                    // в точке гибели, как `RestBremsstrahlung` в кристалле.
                    if (lbrem && bremTable != null)
                    {
                        this.LayerRestBremsstrahlung(x, y, z, ux, uy, uz, t, tEntry, bremTable, ref radiated);
                    }

                    return false;
                }

                double stepG = residual * fraction;
                double stepCm = stepG / density;

                // (`M13`) Смешанная схема: расстояние до жёсткого столкновения;
                // выпало раньше конца шага — шаг обрывается на нём. Мягкий
                // шарнир — по транспортному сечению ниже отсечки на длину шага.
                ScatterElement[] elements = null;
                double hardPerCm = 0.0, softTransportPerCm = 0.0;
                bool hardHit = false;
                if (mixed)
                {
                    this.CountLayerSteps++;
                    elements = this.LayerScatterElements(here.Material);
                    if (elements.Length > 0)
                    {
                        this.LayerElasticStep(elements, t, muCut, out hardPerCm, out softTransportPerCm, this.layerHardShare);
                        if (hardPerCm > 0.0)
                        {
                            double toHard = -Math.Log(this.Uniform()) / hardPerCm;
                            if (toHard < stepCm)
                            {
                                stepCm = toHard;
                                stepG = stepCm * density;
                                hardHit = true;
                            }
                        }
                    }
                }

                // Прямой ход до шарнира; граница области раньше — переход.
                double first = stepCm * this.Uniform();
                if (first >= toNext)
                {
                    // (П106) Тормозное ПРОЙДЕННОГО отрезка до границы — в точке
                    // перехода, при энергии его середины. ⚠ Первая редакция ключа
                    // излучала только в шарнире, за ВЕСЬ шаг, и отрезки, обрезанные
                    // границей, не светили вовсе: в слоях 1 мм при шаге 0.3…0.5 мм
                    // это большинство шагов — выход ключа был 0.55…0.80 от арбитра.
                    if (lbrem && bremTable != null && toNext > 0.0)
                    {
                        this.LayerStepBremsstrahlung(x + ux * toNext, y + uy * toNext, z + uz * toNext, ux, uy, uz,
                                                     LayerMidEnergy(medium, residual, 0.5 * toNext * density),
                                                     toNext * density, bremAnchor, tEntry, bremTable, ref radiated);
                    }

                    double through = toNext + 1e-7;
                    x += ux * through;
                    y += uy * through;
                    z += uz * through;
                    t = ElectronData.EnergyOfRange(medium, residual - toNext * density);
                    continue;
                }

                x += ux * first;
                y += uy * first;
                z += uz * first;

                double tMid = ElectronData.EnergyOfRange(medium, residual - 0.5 * stepG);
                if (tMid < TransportCutKev)
                {
                    tMid = TransportCutKev;
                }

                // (`M13`, П106) Тормозное ПЕРВОГО ОТРЕЗКА шага — в точке шарнира,
                // тонкой мишенью вещества слоя при энергии середины отрезка, по
                // направлению электрона ДО поворота; второй отрезок излучает в своём
                // конце (или на границе, если обрезан). Так число квантов
                // пропорционально пути, ФАКТИЧЕСКИ пройденному в веществе.
                if (lbrem && bremTable != null)
                {
                    this.LayerStepBremsstrahlung(x, y, z, ux, uy, uz,
                                                 LayerMidEnergy(medium, residual, 0.5 * first * density),
                                                 first * density, bremAnchor, tEntry, bremTable, ref radiated);
                }

                // (`M13`) Под ключом ширина мягкого шарнира — средний 1 − cos θ
                // мягких столкновений на длину шага, 1 − exp(−s·Σnσ_tr,soft)
                // (аддитивно по шагу); без ключа — Хайленд на весь шаг, как было.
                // Слой без опознанных элементов (состава нет) — Хайленд, как без ключа.
                double theta0 = mixed && elements != null && elements.Length > 0
                    ? Math.Sqrt(Math.Max(0.0, 1.0 - Math.Exp(-stepCm * softTransportPerCm)))
                    : HighlandTheta0(tMid, stepG / x0);
                this.Rotate(ref ux, ref uy, ref uz, 1.0 - this.SampleHingeMu(theta0));

                // Прямой ход до конца шага — новым лучом.
                double second = stepCm - first;
                toNext = this.StepToBoundary(x, y, z, ux, uy, uz);
                if (toNext >= double.MaxValue)
                {
                    return false;
                }

                if (second >= toNext)
                {
                    // (П106) Тормозное второго отрезка, обрезанного границей, — в
                    // точке перехода, по новому направлению.
                    if (lbrem && bremTable != null && toNext > 0.0)
                    {
                        this.LayerStepBremsstrahlung(x + ux * toNext, y + uy * toNext, z + uz * toNext, ux, uy, uz,
                                                     LayerMidEnergy(medium, residual - first * density, 0.5 * toNext * density),
                                                     toNext * density, bremAnchor, tEntry, bremTable, ref radiated);
                    }

                    double through = toNext + 1e-7;
                    x += ux * through;
                    y += uy * through;
                    z += uz * through;
                    t = ElectronData.EnergyOfRange(medium, residual - (first + toNext) * density);
                    continue;
                }

                x += ux * second;
                y += uy * second;
                z += uz * second;

                // (П106) Тормозное второго отрезка — в его конце, по направлению
                // после шарнира.
                if (lbrem && bremTable != null && second > 0.0)
                {
                    this.LayerStepBremsstrahlung(x, y, z, ux, uy, uz,
                                                 LayerMidEnergy(medium, residual - first * density, 0.5 * second * density),
                                                 second * density, bremAnchor, tEntry, bremTable, ref radiated);
                }

                residual -= stepG;
                t = ElectronData.EnergyOfRange(medium, residual);

                // (`M13`) Жёсткое столкновение в конце оборванного шага — при
                // энергии его конца; пустое (мажоранта Мотта) направления не меняет.
                if (hardHit && t > TransportCutKev
                    && this.LayerHardCollision(elements, this.layerHardShare, hardPerCm, t, muCut,
                                               ref ux, ref uy, ref uz))
                {
                    this.CountLayerHardCollisions++;
                }
            }

            return false;
        }

        /// <summary>
        /// (`M13`, П106) Энергия электрона в середине отрезка пути в слое, кэВ:
        /// остаточный пробег <paramref name="residualG"/> минус половина отрезка
        /// <paramref name="halfG"/> (г/см²), не ниже порога переноса.
        /// </summary>
        static double LayerMidEnergy(ElectronData.Material medium, double residualG, double halfG)
        {
            double t = ElectronData.EnergyOfRange(medium, Math.Max(0.0, residualG - halfG));
            return t < TransportCutKev ? TransportCutKev : t;
        }

        /// <summary>
        /// (`M13`, П106) Кванты тормозного ОДНОГО ОТРЕЗКА переноса В СЛОЕ обвязки
        /// (ключ <see cref="ElectronLayerBremAlongPath"/>): тонкая мишень
        /// вещества слоя при энергии <paramref name="tKev"/> на пути
        /// <paramref name="stepG"/> г/см², уровень — якорь ESTAR по энергии
        /// входа в вещество; направление — по электрону (модифицированный Цай,
        /// как `bpath=2`); сумма квантов не больше энергии входа в перенос
        /// <paramref name="cap"/> за вычетом уже излучённого. Квант — в приёмник
        /// (<see cref="LayerEmitBremsstrahlung"/>).
        /// </summary>
        void LayerStepBremsstrahlung(double x, double y, double z, double ux, double uy, double uz,
                                     double tKev, double stepG, double anchor, double cap,
                                     ThickTargetBrem table, ref double radiated)
        {
            int n = this.Poisson(table.StepPhotons(tKev, stepG, anchor));
            for (int i = 0; i < n; i++)
            {
                double k = table.SampleStepKev(tKev, this.Uniform());
                this.LayerEmitBremsstrahlung(x, y, z, ux, uy, uz, k, tKev, cap, ref radiated);
            }
        }

        /// <summary>
        /// (`M13`, П106) Остаток тормозного электрона, который погибнет в слое
        /// (ранний выход по ближайшей границе области): толстая мишень вещества
        /// слоя от текущей энергии <paramref name="tKev"/> в точке гибели — как
        /// <see cref="RestBremsstrahlung"/> в кристалле.
        /// </summary>
        void LayerRestBremsstrahlung(double x, double y, double z, double ux, double uy, double uz,
                                     double tKev, double cap, ThickTargetBrem table, ref double radiated)
        {
            if (!(tKev > table.MinKev))
            {
                return;
            }

            int n = this.Poisson(table.Photons(tKev));
            for (int i = 0; i < n; i++)
            {
                double k = table.SampleKev(tKev, this.Uniform());
                this.LayerEmitBremsstrahlung(x, y, z, ux, uy, uz, k, tKev, cap, ref radiated);
            }
        }

        /// <summary>
        /// (`M13`, П106) Один квант тормозного из точки (x, y, z) в слое: направление
        /// по электрону (Цай), зажим суммой, приёмник — очередь обхода заноса
        /// (<see cref="layerBremPush"/>) либо очередь вылетов
        /// (<see cref="NoteEscape"/>), когда она открыта; иначе унесён — его
        /// энергия уже в уносе электрона (как у `LayerBremsstrahlung`).
        /// </summary>
        void LayerEmitBremsstrahlung(double x, double y, double z, double ux, double uy, double uz,
                                     double k, double tKev, double cap, ref double radiated)
        {
            double ax = ux, ay = uy, az = uz;
            this.Rotate(ref ax, ref ay, ref az, this.TsaiCosine(tKev));
            double kUse = Math.Min(k, cap - radiated);
            if (!(kUse > 0.0))
            {
                return;
            }

            radiated += kUse;
            this.CountLayerBremPhotons++;
            this.SumLayerBremKev += kUse;
            if (this.layerBremPush != null)
            {
                this.layerBremPush(x, y, z, ax, ay, az, kUse);
            }
            else if (this.escapeCollect)
            {
                this.NoteEscape(x, y, z, ax, ay, az, kUse);
            }
        }

        /// <summary>
        /// (`M3`, П44) Кванты тормозного ОДНОГО ШАГА переноса: среднее число —
        /// тонкая мишень при энергии <paramref name="tKev"/> на пути
        /// <paramref name="stepG"/> г/см² (<see cref="ThickTargetBrem.StepPhotons"/>),
        /// энергия — <see cref="ThickTargetBrem.SampleStepKev"/>, направление —
        /// изотропное (уровень 1) либо по электрону модифицированным Цаем
        /// (уровень 2, как `G4SeltzerBergerModel`). Сумма квантов не больше
        /// начальной энергии <paramref name="te"/> за вычетом уже излучённого —
        /// тот же зажим, что у точечной ветки; розыгрыши делаются всегда.
        /// </summary>
        void StepBremsstrahlung(double x, double y, double z, double ux, double uy, double uz,
                                double tKev, double stepG, double anchor, double te, int depth,
                                ref double radiated, ref double lost)
        {
            int n = this.Poisson(this.bremTable.StepPhotons(tKev, stepG, anchor));
            for (int i = 0; i < n; i++)
            {
                double k = this.bremTable.SampleStepKev(tKev, this.Uniform());
                this.EmitBremsstrahlung(x, y, z, ux, uy, uz, k, tKev, te, depth, ref radiated, ref lost);
            }
        }

        /// <summary>
        /// (`M3`, П44) Остаток тормозного электрона, который погибнет внутри
        /// (ранний выход по ближайшей грани): толстая мишень от текущей
        /// энергии <paramref name="tKev"/> в точке выхода из переноса.
        /// </summary>
        void RestBremsstrahlung(double x, double y, double z, double ux, double uy, double uz,
                                double tKev, double te, int depth,
                                ref double radiated, ref double lost)
        {
            if (!(tKev > this.bremTable.MinKev))
            {
                return;
            }

            int n = this.Poisson(this.bremTable.Photons(tKev));
            for (int i = 0; i < n; i++)
            {
                double k = this.bremTable.SampleKev(tKev, this.Uniform());
                this.EmitBremsstrahlung(x, y, z, ux, uy, uz, k, tKev, te, depth, ref radiated, ref lost);
            }
        }

        /// <summary>Один квант тормозного из точки (x, y, z): направление по уровню ключа, зажим, проводка.</summary>
        void EmitBremsstrahlung(double x, double y, double z, double ux, double uy, double uz,
                                double k, double tKev, double te, int depth,
                                ref double radiated, ref double lost)
        {
            double ax, ay, az;
            if (this.BremAlongPath >= 2)
            {
                ax = ux; ay = uy; az = uz;
                this.Rotate(ref ax, ref ay, ref az, this.TsaiCosine(tKev));
            }
            else
            {
                this.Isotropic(out ax, out ay, out az);
            }

            double kUse = Math.Min(k, te - radiated);
            if (!(kUse > 0.0))
            {
                return;
            }

            radiated += kUse;
            this.CountBremPhotons++;
            this.SumBremKev += kUse;
            lost += this.InCrystal(x, y, z, ax, ay, az, kUse, depth + 1);
        }

        /// <summary>Энергия электрона на грани по остаточному пробегу, кэВ, не больше зажима.</summary>
        double EscapeEnergy(double residualG, double cap)
        {
            if (!(residualG > 0.0))
            {
                return 0.0;
            }

            double t = ElectronData.EnergyOfRange(this.electron, residualG);
            return t < cap ? (t > 0.0 ? t : 0.0) : (cap > 0.0 ? cap : 0.0);
        }
    }
}
