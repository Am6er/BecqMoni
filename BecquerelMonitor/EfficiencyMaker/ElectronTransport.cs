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
    ///    нет. ✅ (`AMBER274`, физика 28, 30.10.2026) Под ключом
    ///    <see cref="EfficiencySimulator.ElectronStraggling"/> (умолчание склада ВКЛ,
    ///    клеймо `estr=1`) потеря шага — средняя по CSDA с ионизационной частью,
    ///    разыгранной моделью Урбана (<see cref="EfficiencySimulator.ResidualAfter"/>),
    ///    во всех местах переноса: кристалл, слои обвязки, капсула и воздух у точки.
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
    ///    ⛔ (`AMBER274`, физика 29, полоса `phys29` 01.11.2026; решение Amber
    ///    30.10.2026 «Слои + кристалл сразу») Под ключом
    ///    <see cref="EfficiencySimulator.ElectronMixedTransport"/> (умолчание склада
    ///    ВКЛ, клеймо `emix=1`) Хайленда на шаг в кристалле НЕТ: формула не аддитивна
    ///    по пути, и «поверка по пику» П27 держалась на доле шага 0.1 — обратное
    ///    рассеяние η голого CsI при 1 МэВ шло 0.389 / 0.354 / 0.313 при доле 0.1 /
    ///    0.05 / 0.02 (критики 30.10.2026). Теперь в кристалле — та же смешанная
    ///    схема, что в слоях (шапка раздела `M13` ниже): жёсткие столкновения выше
    ///    20° по одному, мягкий шарнир по транспортному сечению ниже отсечки, сечения
    ///    при энергии середины шага; доля шага в кристалле —
    ///    <see cref="EfficiencySimulator.ElectronCrystalStepFraction"/> (0.05, по
    ///    сходимости η). Никакого множителя к ширине нет и не было: «калибровка П27» —
    ///    это выбор доли 0.1 и проверка ширины ×1 по пику 1461 кэВ (журнал П27 §3.2–3.3).
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

        /// <summary>
        /// (`AMBER274`, физика 29) Тот же предохранитель под ключом
        /// <see cref="ElectronMixedTransport"/>: жёсткие столкновения смешанной схемы в
        /// кристалле дробят шаги (в CsI их десятки на трек у поверхности), и при доле шага
        /// меньше 0.1 прежний потолок 400 обрезал треки (критик 30.10.2026: доля вошедших
        /// 624 кэВ при шаге 0.01 — 5.077 с потолком 400 против 5.165 с 4000). Упор считает
        /// <see cref="CountTransportStepCap"/>.
        /// </summary>
        const int TransportMaxStepsMixed = 4000;

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
        internal double CrystalRadiationLength()
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
        internal double LayerRadiationLength(GeometryMaterial material)
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
        // В КРИСТАЛЛЕ (<see cref="TransportElectron"/>) до физики 29 ничего не
        // менялось; ⛔ (`AMBER274`, физика 29) под ключом `ElectronMixedTransport` та же
        // схема идёт и в кристалле, а сечения слоёв и кристалла берутся при энергии
        // СЕРЕДИНЫ шага (у начала шага рассеяние занижено на ~6 % при доле 0.1 —
        // критик физики 30.10.2026). У Z > 30 (Cs, I) поправки Мотта нет — чистый
        // экранированный Резерфорд; η голого CsI/NaI против Tabata и шести физлистов
        // Geant4 — журнал `handover/handover-2026-11-01-amber274-phys29.md`.
        // Без ключа — ни одного лишнего случайного числа, ход прежний.
        //
        // ⛔ (`AMBER274`, физика 29, полоса `phys29b` 02.11.2026; решение Amber 02.11.2026
        // вопросником, дословно: «Сейчас, в физику 29 (Рекомендую)») ПОПРАВКА МОТТА ПРИ
        // Z > 30 — под ключом <see cref="ElectronMottHeavy"/> (клеймо `mott=1`). Повод: η
        // голых CsI и NaI на 3–4 % ниже среднего шести физлистов Geant4, а у Cs и I
        // отношение Мотта к Резерфорду R(θ) на средних углах 1.3…1.4 (у 20° — 1.13…1.17),
        // и Мак-Кинли—Фешбах (первый порядок по αZ) там негоден — он даёт R < 1 у 90°.
        // R — аналитическая подгонка Boschini et al. / Lijian et al. из Geant4
        // (`MottData`, числа `G4MottData.hh` без правок): R = Σ_j a_j(β) x^j, x = √μ,
        // μ = 1 − cos θ. Поправка — к ЯДЕРНОЙ части Z² множителя Z(Z+1); электронная
        // часть Z — без неё: R_eff = (Z·R + 1)/(Z + 1), и это вшито в коэффициенты
        // элемента (<see cref="ScatterElement.MottCoef"/>).
        // * Жёсткая часть: сечение выше отсечки — ТОЧНЫЙ интеграл ∫ R_eff dσ (замкнутые
        //   формы по степеням √μ, <see cref="MottMoments"/>), а не мажоранта; угол —
        //   отбором до принятия (кандидат по Резерфорду выше отсечки, принят с
        //   вероятностью R_eff/M, M — верхняя граница R_eff на [μ_c, 2] по 33 узлам с
        //   запасом на кривизну, <see cref="MottMajorant"/>). Так шаг обрывают только
        //   настоящие столкновения, и отказ кандидата шага не дробит.
        // * Мягкая часть: транспортное сечение ниже отсечки — тоже с R_eff
        //   (∫₀^{μ_c} μ R_eff dσ, те же замкнутые формы): у 20° R ≈ 1.15, и без неё
        //   мягкая ширина была бы занижена на ~5 %.
        // Z ≤ 30 — Мак-Кинли—Фешбах отбором по мажоранте, как было (в мягкой части
        // поправки у них по-прежнему нет); Z > 92 — Резерфорд. Без ключа ход прежний.
        //
        // ⛔ (`AMBER274`, физика 30, полоса `phys29c` 02.11.2026) ПРАВКИ ТРЁХ КРИТИКОВ ФИЗИКИ 29 —
        // под ключом <see cref="ElectronElasticRefine"/> (клеймо `eref=1`), части — биты рычага
        // замера <see cref="ElectronRefineParts"/> (журнал
        // `handover/handover-2026-11-02-amber274-phys29c.md`):
        // * бит 1 (Ф2) — e–e часть множителя Z(Z+1) ограничена кинематикой: первичным после
        //   столкновения с электроном считается более быстрый, его угол не больше θ_ee,
        //   cos²θ_ee = (T + 2m)/(T + 4m) (как `cosTetMaxElec` Geant4); у позитрона (Баба) —
        //   90°. Выше предела электронная доля 1/(Z + 1) (с подгонкой — 1/((Z + 1)·R_eff))
        //   отбрасывается тем же случайным числом, что отбор Мотта: лишнего числа нет;
        // * бит 2 (Ф3) — подгонка Мотта у ВСЕХ Z (1…92; Z > 92 — коэффициенты Z = 92, как
        //   `G4ScreeningMottCrossSection`): против DPWA (ELSEPA, `G4EMLOW8.8/dpwa`) у Z ≤ 8 она
        //   равна Мак-Кинли—Фешбаху в 0.4 %, а с Z ≈ 11 точнее его на больших углах (dσ/dΩ при
        //   90° и 1 МэВ: Al 1.000 против 0.980, Ca 1.000 / 0.953, Fe 1.001 / 0.922, Cu 1.001 /
        //   0.904), и мягкая часть получает ту же поправку (σ_tr ниже 20° у Fe 1 МэВ 1.000
        //   против 0.976 без неё);
        // * бит 4 (Ф4) — позитрон (пары и β⁺) без поправки ЭЛЕКТРОНА: ядерная часть — Мак-Кинли—
        //   Фешбах позитрона (знак члена παZβ обратный), мажоранта 1, мягкая часть — без
        //   поправки. Против DPWA e⁺ при 1 МэВ σ(>20°) Cs 0.884 (подгонка электрона давала
        //   1.594, без поправки — 1.297), σ_tr полное 0.921 (1.321, 1.199); у Al 0.990 / 1.006;
        // * бит 8 (Ф6) — доли элементов жёсткого столкновения, затёртые вложенным переносом
        //   (тормозное шага → `InCrystal(depth+1)` → вторичный электрон), пересчитываются
        //   перед столкновением той же чистой функцией без случайных чисел
        //   (<see cref="layerShareWrites"/>);
        // * бит 16 (Ф7) — сечения шага: на концах ПОЛНОГО шага (начало и конец по CSDA), путь
        //   до кандидата жёсткого — по мажоранте max(σ_нач, σ_кон), кандидат принимается с
        //   вероятностью σ_лин/σ_max (σ_лин — линейная по пути между концами; тем же числом
        //   выбирается элемент), отвергнутый — пустое столкновение; мягкое транспортное
        //   сечение — среднее линейного по ПРОЙДЕННОЙ части шага. Прежде сечения брались в
        //   середине полного шага, а шаг в CsI обрывался жёстким в ~70 % случаев — η при доле
        //   0.05 было на 0.5…0.8 % выше сошедшегося.
        // Ключ ВЫКЛ (или рычаг 0) — физика 29 до бита и без лишнего случайного числа.
        // ------------------------------------------------------------------

        /// <summary>
        /// (`M13`) Выше этого Z поправка Мак-Кинли—Фешбаха не применяется
        /// (она — первый порядок по αZ и годна до Z ≈ 30); дальше — чистый
        /// экранированный Резерфорд. В тяжёлом слое (Pb, W, сталь) обратное
        /// рассеяние диффузионное, и хвост там решает мало.
        /// </summary>
        const int MottMaxZ = 30;

        /// <summary>(`M13`) Элемент вещества слоя для розыгрыша упругих столкновений.</summary>
        internal sealed class ScatterElement
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

            /// <summary>
            /// (`AMBER274`, физика 29, ключ <see cref="ElectronMottHeavy"/>) Поправка Мотта
            /// подгонкой Geant4 (<see cref="MottData"/>) — у Z от <see cref="MottMaxZ"/> + 1
            /// до <see cref="MottData.MaxZ"/> под ключом; без ключа false.
            /// </summary>
            public bool MottFit;

            /// <summary>
            /// (`AMBER274`) Коэффициенты R_eff = (Z·R + 1)/(Z + 1) элемента, 30 чисел на месте
            /// j·6 + k: R_eff = Σ_j x^j Σ_k c_jk (β − β̄)^k (<see cref="MottData.BetaShift"/>).
            /// null без <see cref="MottFit"/>.
            /// (`AMBER274`, физика 30) У ЯДЕРНОЙ части (<see cref="Part"/> = 1) — коэффициенты самого R
            /// (электронная часть — отдельным элементом), а не R_eff.
            /// </summary>
            public double[] MottCoef;

            /// <summary>
            /// (`AMBER274`, физика 30, ключи <see cref="ElectronElasticRefine"/> и
            /// <see cref="ElectronScreeningDpwa"/>) Какая часть множителя Z(Z+1): 0 — обе вместе (как
            /// было: Z(Z+1), R_eff); 1 — ЯДЕРНАЯ Z² (экранирование и поправка — DPWA, подгонка Мотта,
            /// Мак-Кинли—Фешбах или Резерфорд); 2 — ЭЛЕКТРОННАЯ Z (e–e: экранирование Мольера, без
            /// поправки; под битом Ф2 — до кинематического предела угла).
            /// </summary>
            public int Part;

            /// <summary>
            /// (`AMBER274`, физика 30, Ф1) Узлы DPWA ядерной части для электрона и позитрона при
            /// отсечке смешанной схемы (<see cref="DpwaElement"/>); null — ключ DPWA выключен.
            /// </summary>
            public DpwaElement DpwaEl, DpwaPos;
        }

        /// <summary>
        /// ⛔ (`AMBER274`, физика 30, Ф1; решение Amber 02.11.2026 вопросником, дословно: «Сейчас, до
        /// склада (Рекомендую)») ЯДЕРНАЯ ЧАСТЬ УПРУГОГО РАССЕЯНИЯ ПО DPWA одного элемента и частицы:
        /// dσ/dμ = C(T)·Z²·P(√μ)/(μ + a)² (таблица `matdb.elastic_screening_dpwa`,
        /// <see cref="MaterialDatabase.ElasticScreeningOf"/>; постановка подгонки — журнал полосы
        /// `screen29`). На узлах заранее посчитаны (тем же `MottMoments`, в double) приведённые сечения
        /// при отсечке смешанной схемы: жёсткое Σ c_j h_j(a), мягкое транспортное Σ c_j s_j(a) и
        /// мажоранта P на [x_c, √2] (`MottMajorant`). Между узлами — линейно по ln T (ln a, c_j и эти
        /// три числа): мажоранта, интерполированная линейно, — верхняя граница интерполированного P
        /// (max выпуклой комбинации не больше комбинации максимумов). Вне сетки — крайний узел.
        /// </summary>
        internal sealed class DpwaElement
        {
            /// <summary>ln T узлов (кэВ), ln a, c0…c4 (5 на узел).</summary>
            public double[] LnT, LnA, C;

            /// <summary>На узлах: Σ c_j h_j(a), Σ c_j s_j(a), мажоранта P.</summary>
            public double[] Hard, Soft, Major;

            /// <summary>(n − 1)/(ln T_последний − ln T_первый) — прямой поиск узла.</summary>
            public double InvStep;

            /// <summary>Узел и доля <paramref name="u"/> для ln T (вне сетки — крайний узел).</summary>
            public int Locate(double lt, out double u)
            {
                int n = this.LnT.Length;
                if (!(lt > this.LnT[0]))
                {
                    u = 0.0;
                    return 0;
                }

                if (lt >= this.LnT[n - 1])
                {
                    u = 1.0;
                    return n - 2;
                }

                int k = (int)((lt - this.LnT[0]) * this.InvStep);
                if (k > n - 2)
                {
                    k = n - 2;
                }

                while (k > 0 && lt < this.LnT[k])
                {
                    k--;
                }

                while (k < n - 2 && lt >= this.LnT[k + 1])
                {
                    k++;
                }

                u = (lt - this.LnT[k]) / (this.LnT[k + 1] - this.LnT[k]);
                return k;
            }
        }

        /// <summary>
        /// (`AMBER274`, физика 30, Ф1) Узлы DPWA элемента <paramref name="z"/> при отсечке
        /// <paramref name="muCut"/>: таблица базы и приведённые сечения на узлах. Таблицы в базе нет —
        /// ОТКАЗ словами: ключ DPWA молча не гаснет.
        /// </summary>
        static DpwaElement BuildDpwaElement(int z, bool positron, double muCut)
        {
            MaterialDatabase.ElasticScreeningNodes nodes = MaterialDatabase.ElasticScreeningOf(z, positron);
            if (nodes == null)
            {
                throw new InvalidOperationException(
                    "matdb.sqlite has no DPWA elastic screening (table elastic_screening_dpwa) for Z="
                    + z.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + (positron ? " (e+)" : " (e-)")
                    + " — import it with tools/nucdb/import_dpwa_screening.py or switch the key off (dpwa=0)");
            }

            int n = nodes.LnT.Length;
            var d = new DpwaElement
            {
                LnT = nodes.LnT,
                LnA = nodes.LnA,
                C = nodes.C,
                Hard = new double[n],
                Soft = new double[n],
                Major = new double[n],
                InvStep = (n - 1) / (nodes.LnT[n - 1] - nodes.LnT[0]),
            };
            double xCut = Math.Sqrt(muCut);
            for (int k = 0; k < n; k++)
            {
                double a = Math.Exp(nodes.LnA[k]);
                int o = 5 * k;
                double h, s;
                MottMoments(nodes.C[o], nodes.C[o + 1], nodes.C[o + 2], nodes.C[o + 3], nodes.C[o + 4],
                            a, muCut, xCut, out h, out s);
                d.Hard[k] = h;
                d.Soft[k] = s;
                d.Major[k] = MottMajorant(nodes.C[o], nodes.C[o + 1], nodes.C[o + 2], nodes.C[o + 3], nodes.C[o + 4], xCut);
            }

            return d;
        }

        /// <summary>(`AMBER274`, физика 30, Ф3) Коэффициенты самого R элемента (ядерная часть), строка Z ≤ 92.</summary>
        static double[] MottCoefficientsNuclear(int z)
        {
            double[] c = new double[30];
            int row = Math.Min(z, MottData.MaxZ) * 30;
            for (int i = 0; i < 30; i++)
            {
                c[i] = MottData.Coef[row + i];
            }

            return c;
        }

        /// <summary>
        /// (`AMBER274`) R_eff = (Z·R + 1)/(Z + 1) элемента <paramref name="z"/> — строка
        /// <see cref="MottData.Coef"/>, ядерная часть Z² с поправкой, электронная Z — без.
        /// </summary>
        static double[] MottCoefficients(int z)
        {
            double[] c = new double[30];
            // (`AMBER274`, физика 30, Ф3) Z > 92 — строка Z = 92, как `G4ScreeningMottCrossSection`
            // (до физики 30 сюда такие Z не доходили: подгонка была только у 31…92).
            int row = Math.Min(z, MottData.MaxZ) * 30;
            for (int i = 0; i < 30; i++)
            {
                c[i] = z * MottData.Coef[row + i] / (z + 1.0);
            }

            c[0] += 1.0 / (z + 1.0);
            return c;
        }

        /// <summary>
        /// (`AMBER274`) Коэффициенты многочлена R_eff по x = √μ при скорости
        /// <paramref name="beta"/>: c_j = Σ_k b_jk (β − β̄)^k, схема Горнера.
        /// </summary>
        static void MottTerms(double[] b, double beta,
                              out double c0, out double c1, out double c2, out double c3, out double c4)
        {
            double d = beta - MottData.BetaShift;
            c0 = b[0] + d * (b[1] + d * (b[2] + d * (b[3] + d * (b[4] + d * b[5]))));
            c1 = b[6] + d * (b[7] + d * (b[8] + d * (b[9] + d * (b[10] + d * b[11]))));
            c2 = b[12] + d * (b[13] + d * (b[14] + d * (b[15] + d * (b[16] + d * b[17]))));
            c3 = b[18] + d * (b[19] + d * (b[20] + d * (b[21] + d * (b[22] + d * b[23]))));
            c4 = b[24] + d * (b[25] + d * (b[26] + d * (b[27] + d * (b[28] + d * b[29]))));
        }

        /// <summary>
        /// (`AMBER274`) Сечения смешанной схемы с поправкой подгонки: <paramref name="hard"/> —
        /// Σ_j c_j ∫_{μ_c}^{2} μ^{j/2} dμ/(μ + a)², <paramref name="softTransport"/> —
        /// Σ_j c_j ∫₀^{μ_c} μ·μ^{j/2} dμ/(μ + a)² (a = 2A). Замкнутые формы: чётные j —
        /// рациональные и логарифм, нечётные — через t = √μ и arctg; разность arctg у
        /// жёсткой части — одной arctg, без вычитания близких. Проверены квадратурой
        /// (journal `phys29b`, отн. 1e-12…1e-9 при a от 1e-5 до 3). При a ≫ μ_c (электрон
        /// ниже ~5 кэВ) мягкие формы вычитают близкие числа — в double годно, во float
        /// ядра GPU — погрешность там, где пробег — доли микрона.
        /// </summary>
        static void MottMoments(double c0, double c1, double c2, double c3, double c4,
                                double a, double muCut, double xCut,
                                out double hard, out double softTransport)
        {
            const double Root2 = 1.4142135623730951;
            double s = Math.Sqrt(a);
            double inXa = 1.0 / (muCut + a), in2a = 1.0 / (2.0 + a);

            double h0 = inXa - in2a;
            double lh = Math.Log((2.0 + a) * inXa);
            double h2 = lh - a * h0;
            double h4 = (2.0 - muCut) - 2.0 * a * lh + a * a * h0;
            double dAtan = Math.Atan((Root2 - xCut) * s / (a + Root2 * xCut));
            double h1 = dAtan / s - Root2 * in2a + xCut * inXa;
            double h3 = 2.0 * (Root2 - xCut) - 3.0 * s * dAtan + a * (Root2 * in2a - xCut * inXa);
            hard = c0 * h0 + c1 * h1 + c2 * h2 + c3 * h3 + c4 * h4;

            double ls = Math.Log((muCut + a) / a);
            double e = Math.Atan(xCut / s);
            double s0 = ls + a * inXa - 1.0;
            double s1 = 2.0 * xCut - 3.0 * s * e + a * xCut * inXa;
            double s2 = muCut - 2.0 * a * ls + a * muCut * inXa;
            double s3 = (2.0 / 3.0) * xCut * muCut - 4.0 * a * xCut + 5.0 * a * s * e - a * a * xCut * inXa;
            double s4 = 0.5 * muCut * muCut - 2.0 * a * muCut + 3.0 * a * a * ls - a * a * muCut * inXa;
            softTransport = c0 * s0 + c1 * s1 + c2 * s2 + c3 * s3 + c4 * s4;
        }

        /// <summary>
        /// (`AMBER274`) Верхняя граница R_eff(x) = c0 + c1 x + … + c4 x⁴ на [x_c, √2]:
        /// наибольшее из 33 равноотстоящих узлов плюс h²/8 · max|p″| (между узлами
        /// многочлен не выше ближайшего узла больше чем на это — по Тейлору у вершины).
        /// Запас против точного максимума — не больше 2.7 % у Z 31…92 от 1 кэВ до 5 МэВ
        /// (замер полосы), нарушений нет.
        /// </summary>
        static double MottMajorant(double c0, double c1, double c2, double c3, double c4, double xCut)
        {
            const double Root2 = 1.4142135623730951;
            double h = (Root2 - xCut) / 32.0;
            double best = -1e30;
            for (int i = 0; i <= 32; i++)
            {
                double x = xCut + i * h;
                double p = c0 + x * (c1 + x * (c2 + x * (c3 + x * c4)));
                if (p > best)
                {
                    best = p;
                }
            }

            double curvature = 2.0 * Math.Abs(c2) + 6.0 * Math.Abs(c3) * Root2 + 24.0 * Math.Abs(c4);
            return best + 0.125 * h * h * curvature;
        }

        /// <summary>
        /// (`AMBER274`) Предел попыток отбора угла с поправкой подгонки: средняя доля
        /// принятых — ⟨R_eff⟩/M ≥ ~0.2, и 1000 попыток не выбираются никогда; выбрались —
        /// счётчик <see cref="CountMottTriesCap"/>, принят последний кандидат.
        /// </summary>
        const int MottMaxTries = 1000;

        // (`M13`) Кэш элементов на вещество — по тому же ключу, что
        // <see cref="LayerRadiationLength"/>; и рабочий массив долей жёстких
        // сечений (на экземпляр — симулятор однопоточный).
        readonly Dictionary<GeometryMaterial, ScatterElement[]> layerScatterCache =
            new Dictionary<GeometryMaterial, ScatterElement[]>();
        // (`AMBER274`, физика 29) Тот же массив служит и КРИСТАЛЛУ (`TransportElectron`).
        // ⚠ (`AMBER274`, физика 30, Ф6/Н1 критиков физики 29) Прежний довод «между розыгрышем
        // пути и столкновением шаг по кристаллу ничего не зовёт» НЕВЕРЕН: тормозное шага
        // (`StepBremsstrahlung` → `EmitBremsstrahlung` → `InCrystal(depth + 1)`) рождает
        // вторичный электрон, и его перенос (`TransportElectron` / `TransportInLayers`)
        // пишет в этот же массив (и может заменить его длиннее) — столкновение брало чужие
        // доли (Na вместо I: CsI 2572 из 3.5·10⁷ столкновений). Под ключом
        // <see cref="ElectronElasticRefine"/> (бит 8) запись считается
        // (<see cref="layerShareWrites"/>), и затёртые доли перед столкновением
        // пересчитываются той же чистой функцией, без случайных чисел.
        double[] layerHardShare = new double[8];

        // (`AMBER274`, физика 30, Ф7) Доли жёстких сечений элементов на КОНЦЕ полного шага
        // (начало — `layerHardShare`); растёт вместе с ним.
        double[] layerHardShareEnd = new double[8];

        // (`AMBER274`, физика 30, Ф6) Число записей в рабочие массивы долей
        // (<see cref="LayerElasticStep"/>): разошлось со снятым после розыгрыша шага — доли
        // затёр вложенный перенос. На ход и поток случайных чисел не влияет.
        long layerShareWrites;

        // (`AMBER274`, `phys29b`) Под каким `ElectronMottHeavy` собран `layerScatterCache`;
        // (физика 30) и под каким признаком подгонки у всех Z (Ф3).
        bool layerScatterCacheMott;
        bool layerScatterCacheAllZ, layerScatterCacheSplit, layerScatterCacheDpwa;
        double layerScatterCacheCutDeg = double.NaN;

        /// <summary>
        /// (`AMBER274`, физика 30) Биты рычага <see cref="ElectronRefineParts"/> — части ключа
        /// <see cref="ElectronElasticRefine"/> (шапка раздела `M13` выше).
        /// </summary>
        internal const int RefineEeLimit = 1, RefineMottAllZ = 2, RefinePositron = 4,
                           RefineShareRecompute = 8, RefineStepMajorant = 16, RefineLayerStepLimit = 32,
                           RefineAll = 63;

        /// <summary>
        /// (`AMBER274`, физика 30, бит 32 — находка полосы `k40rc50`) Толщина тела области, см: наименьший
        /// размер (по z и поперёк — 2·полуширина бруса, стенка кольца или диаметр цилиндра). Опора предела
        /// шага в слое (<see cref="ElectronLayerStepMaxFraction"/>): у тонкого по сравнению с шагом слоя
        /// (воздух 50 мм при 700 кэВ — шаг 24 см) точка шарнира почти всегда за границей, и электрон
        /// проходил слой совсем без мягкого отклонения (доля θ > 10° на лице корпуса 0.108 против 0.219 у
        /// Geant4) — как DSMAX тела у PENELOPE.
        /// </summary>
        static double RegionThicknessCm(Region r)
        {
            double t = r.ZMax - r.ZMin;
            double side = r.IsBox ? 2.0 * Math.Min(r.AX, r.AY) : (r.RIn > 0.0 ? r.ROut - r.RIn : 2.0 * r.ROut);
            if (r.Shape != 0)
            {
                side = r.SideThickness;             // (`AMBER282`) область формы: толщина задана сценой
            }

            return Math.Min(t, side);
        }

        /// <summary>(`AMBER274`, физика 30) Действует ли часть <paramref name="bit"/> ключа.</summary>
        bool Refine(int bit)
        {
            return this.ElectronElasticRefine && (this.ElectronRefineParts & bit) != 0;
        }

        /// <summary>(`M13`) Элементы вещества слоя с плотностями ядер, кэш на вещество.</summary>
        internal ScatterElement[] LayerScatterElements(GeometryMaterial material)
        {
            // (`AMBER274`, `phys29b`) Кэш помнит, под каким ключом Мотта собран: ключ,
            // выставленный пробой ПОСЛЕ первого обращения, иначе был бы мёртв (`S130`).
            // (`AMBER274`, физика 30) Тем же правилом — под какими частями ключа
            // `ElectronElasticRefine`, ключом DPWA и отсечкой (узлы DPWA посчитаны при ней).
            bool split = this.ElectronElasticRefine || this.ElectronScreeningDpwa;
            bool allZ = this.ElectronMottHeavy && this.Refine(RefineMottAllZ);
            double cutDeg = this.LayerHardCutoffDeg;
            if (this.layerScatterCacheMott != this.ElectronMottHeavy || this.layerScatterCacheAllZ != allZ
                || this.layerScatterCacheSplit != split || this.layerScatterCacheDpwa != this.ElectronScreeningDpwa
                || !(this.layerScatterCacheCutDeg == cutDeg))
            {
                this.layerScatterCache.Clear();
                this.layerScatterCacheMott = this.ElectronMottHeavy;
                this.layerScatterCacheAllZ = allZ;
                this.layerScatterCacheSplit = split;
                this.layerScatterCacheDpwa = this.ElectronScreeningDpwa;
                this.layerScatterCacheCutDeg = cutDeg;
            }

            ScatterElement[] found;
            if (this.layerScatterCache.TryGetValue(material, out found))
            {
                return found;
            }

            double muCut = this.LayerHardCutoffMu();
            List<ScatterElement> list = new List<ScatterElement>();
            Dictionary<int, double> mass = MaterialDatabase.AtomicMass;
            foreach (KeyValuePair<int, double> pair in material.Fractions)
            {
                double a;
                if (pair.Value > 0.0 && mass.TryGetValue(pair.Key, out a) && a > 0.0)
                {
                    int z = pair.Key;
                    double atoms = material.Density * pair.Value / a * Avogadro;
                    if (split)
                    {
                        // (`AMBER274`, физика 30) Ядерная часть Z² и электронная Z — порознь: у ядерной
                        // своя поправка (DPWA / подгонка Мотта — Ф3: у всех Z / Мак-Кинли—Фешбах), у
                        // электронной — экранирование Мольера без поправки и предел угла e–e (Ф2).
                        bool fitNuc = allZ ? z >= 1 : this.ElectronMottHeavy && z > MottMaxZ && z <= MottData.MaxZ;
                        list.Add(new ScatterElement
                        {
                            Z = z,
                            AtomsPerCm3 = atoms,
                            Z13 = Math.Pow(z, 1.0 / 3.0),
                            ZZ1 = (double)z * z,
                            Mott = z <= MottMaxZ,
                            MottFit = fitNuc,
                            MottCoef = fitNuc ? MottCoefficientsNuclear(z) : null,
                            Part = 1,
                            DpwaEl = this.ElectronScreeningDpwa ? BuildDpwaElement(z, false, muCut) : null,
                            DpwaPos = this.ElectronScreeningDpwa ? BuildDpwaElement(z, true, muCut) : null,
                        });
                        list.Add(new ScatterElement
                        {
                            Z = z,
                            AtomsPerCm3 = atoms,
                            Z13 = Math.Pow(z, 1.0 / 3.0),
                            ZZ1 = z,
                            Part = 2,
                        });
                        continue;
                    }

                    // (`AMBER274`, физика 29) Подгонка Мотта — у Z > 30 под ключом.
                    bool fit = this.ElectronMottHeavy && z > MottMaxZ && z <= MottData.MaxZ;
                    list.Add(new ScatterElement
                    {
                        Z = z,
                        AtomsPerCm3 = atoms,
                        Z13 = Math.Pow(z, 1.0 / 3.0),
                        ZZ1 = z * (z + 1.0),
                        Mott = z <= MottMaxZ,
                        MottFit = fit,
                        MottCoef = fit ? MottCoefficients(z) : null,
                    });
                }
            }

            found = list.ToArray();
            if (this.layerHardShare.Length < found.Length)
            {
                this.layerHardShare = new double[found.Length];
            }

            if (this.layerHardShareEnd.Length < found.Length)
            {
                this.layerHardShareEnd = new double[found.Length];
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
            // (`AMBER274`, физика 30, Ф6) Запись в рабочий массив долей — счётчиком.
            this.layerShareWrites++;
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
                if (e.MottFit)
                {
                    // (`AMBER274`, физика 29) Z > 30: точные сечения с R_eff (`MottMoments`).
                    double c0, c1, c2, c3, c4, hFit, sFit;
                    MottTerms(e.MottCoef, beta, out c0, out c1, out c2, out c3, out c4);
                    MottMoments(c0, c1, c2, c3, c4, a2, muCut, Math.Sqrt(muCut), out hFit, out sFit);
                    double hm = pref * hFit;
                    hardShare[i] = hm;
                    hard += hm;
                    trSoft += pref * sFit;
                    continue;
                }

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
            if (e.MottFit)
            {
                // (`AMBER274`, физика 29) Z > 30: сечение уже точное (`LayerElasticStep`),
                // столкновение состоялось — угол отбором до принятия, R_eff/M.
                double c0, c1, c2, c3, c4;
                MottTerms(e.MottCoef, beta, out c0, out c1, out c2, out c3, out c4);
                double xCut = Math.Sqrt(muCut);
                double majorantFit = MottMajorant(c0, c1, c2, c3, c4, xCut);
                double muFit = muCut;
                for (int tries = 1; ; tries++)
                {
                    muFit = 1.0 / (lo - this.Uniform() * (lo - hi)) - a2;
                    if (muFit < muCut)
                    {
                        muFit = muCut;
                    }
                    else if (muFit > 2.0)
                    {
                        muFit = 2.0;
                    }

                    double x = Math.Sqrt(muFit);
                    double r = c0 + x * (c1 + x * (c2 + x * (c3 + x * c4)));
                    if (r > majorantFit)
                    {
                        this.CountMottMajorantExceeded++;
                    }

                    if (this.Uniform() * majorantFit <= r)
                    {
                        break;
                    }

                    this.CountMottRejected++;
                    if (tries >= MottMaxTries)
                    {
                        this.CountMottTriesCap++;
                        break;
                    }
                }

                this.Rotate(ref ux, ref uy, ref uz, 1.0 - muFit);
                return true;
            }

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

        // ------------------------------------------------------------------
        // (`AMBER274`, физика 30) РАЗДЕЛЬНЫЕ ЧАСТИ Z² И Z — ключи `ElectronElasticRefine` и
        // `ElectronScreeningDpwa` (шапка раздела `M13` выше). Прежние `LayerElasticStep` и
        // `LayerHardCollision` под ключами не зовутся: элементы разделены (`ScatterElement.Part`).
        // ------------------------------------------------------------------

        /// <summary>
        /// (`AMBER274`, физика 30, Ф2) Предел 1 − cos угла e–e части: первичным после столкновения
        /// с атомным электроном считается более быстрый, его угол — не больше θ_ee,
        /// cos²θ_ee = (T + 2m)/(T + 4m) (как `cosTetMaxElec` Geant4); у позитрона (Баба) частицы
        /// различимы, и угол позитрона — не больше 90° (μ ≤ 1). Без бита Ф2 — 2 (предела нет).
        /// </summary>
        double EeLimitMu(double tKev, bool positron)
        {
            if (!this.Refine(RefineEeLimit))
            {
                return 2.0;
            }

            return positron
                ? 1.0
                : 1.0 - Math.Sqrt((tKev + 2.0 * ElectronMassKev) / (tKev + 4.0 * ElectronMassKev));
        }

        /// <summary>
        /// (`AMBER274`, физика 30, Н4 критиков) Транспортное сечение экранированного Резерфорда ниже
        /// отсечки в приведённом виде: ∫₀^{μ_c} μ dμ/(μ + a)² = ln(1 + r) − r/(1 + r), r = μ_c/a.
        /// При a ≫ μ_c (электрон в несколько кэВ) замкнутая форма вычитает близкие числа — там ряд
        /// Σ_{n≥2} (−1)ⁿ (n − 1) rⁿ/n (12 членов при r &lt; 0.1: отн. 1e-12); ядро GPU — тем же
        /// порядком действий.
        /// </summary>
        static double SoftScreened(double a, double muCut)
        {
            double r = muCut / a;
            if (r < 0.1)
            {
                double term = r * r, sum = 0.0, sign = 1.0;
                for (int n = 2; n <= 13; n++)
                {
                    sum += sign * (n - 1) * term / n;
                    term *= r;
                    sign = -sign;
                }

                return sum;
            }

            return Math.Log((muCut + a) / a) + a / (muCut + a) - 1.0;
        }

        /// <summary>
        /// (`AMBER274`, физика 30) Сечения смешанной схемы при энергии <paramref name="tKev"/> у
        /// РАЗДЕЛЁННЫХ элементов (<see cref="ScatterElement.Part"/> 1 и 2) — то же, что
        /// <see cref="LayerElasticStep"/>, но:
        /// * ядерная часть Z²: DPWA (узлы <see cref="DpwaElement"/>, линейно по ln T), иначе подгонка
        ///   Мотта (сам R), иначе у позитрона под битом Ф4 — Резерфорд с мажорантой 1 (отбор
        ///   Мак-Кинли—Фешбаха позитрона — в столкновении), иначе Мак-Кинли—Фешбах электрона по
        ///   мажоранте, иначе Резерфорд;
        /// * электронная часть Z: экранирование Мольера, без поправки; жёсткая — до предела угла e–e
        ///   (<see cref="EeLimitMu"/>, Ф2) точным интегралом; мягкая — <see cref="SoftScreened"/>.
        /// </summary>
        void RefinedElasticStep(ScatterElement[] elements, double tKev, double muCut, bool positron,
                                out double hardPerCm, out double softTransportPerCm, double[] hardShare)
        {
            this.layerShareWrites++;
            bool posMcf = positron && this.Refine(RefinePositron);
            double gamma = 1.0 + tKev / ElectronMassKev;
            double beta2 = 1.0 - 1.0 / (gamma * gamma);
            double beta = Math.Sqrt(beta2);
            double p2 = tKev * (tKev + 2.0 * ElectronMassKev);                  // (кэВ/c)²
            double common = 2.0 * Math.PI * ClassicalRadiusCm * ClassicalRadiusCm
                            * ElectronMassKev * ElectronMassKev / (beta2 * p2);   // см²
            double eeTop = Math.Min(2.0, this.EeLimitMu(tKev, positron));
            double lt = 0.0;
            bool ltDone = false;
            double hard = 0.0, trSoft = 0.0;
            for (int i = 0; i < elements.Length; i++)
            {
                ScatterElement e = elements[i];
                double pref = e.AtomsPerCm3 * common * e.ZZ1;                      // 1/см
                double h, s;
                if (e.Part == 2)
                {
                    double a2e = ScreeningTwoA(e.Z, e.Z13, beta2, beta * gamma);
                    h = eeTop > muCut ? pref * (1.0 / (muCut + a2e) - 1.0 / (eeTop + a2e)) : 0.0;
                    s = pref * SoftScreened(a2e, muCut);
                }
                else
                {
                    DpwaElement d = positron ? e.DpwaPos : e.DpwaEl;
                    if (d != null)
                    {
                        if (!ltDone)
                        {
                            lt = Math.Log(tKev);
                            ltDone = true;
                        }

                        double u;
                        int k = d.Locate(lt, out u);
                        h = pref * (d.Hard[k] + u * (d.Hard[k + 1] - d.Hard[k]));
                        s = pref * (d.Soft[k] + u * (d.Soft[k + 1] - d.Soft[k]));
                    }
                    else if (e.MottFit && !posMcf)
                    {
                        double a2 = ScreeningTwoA(e.Z, e.Z13, beta2, beta * gamma);
                        double c0, c1, c2, c3, c4, hFit, sFit;
                        MottTerms(e.MottCoef, beta, out c0, out c1, out c2, out c3, out c4);
                        MottMoments(c0, c1, c2, c3, c4, a2, muCut, Math.Sqrt(muCut), out hFit, out sFit);
                        h = pref * hFit;
                        s = pref * sFit;
                    }
                    else
                    {
                        double a2 = ScreeningTwoA(e.Z, e.Z13, beta2, beta * gamma);
                        double majorant = !posMcf && e.Mott
                            ? 1.0 + Math.PI * ScatteringData.FineStructure * e.Z * beta / 4.0 : 1.0;
                        h = pref * (1.0 / (muCut + a2) - 1.0 / (2.0 + a2)) * majorant;
                        s = pref * SoftScreened(a2, muCut);
                    }
                }

                hardShare[i] = h;
                hard += h;
                trSoft += s;
            }

            hardPerCm = hard;
            softTransportPerCm = trSoft > 0.0 ? trSoft : 0.0;
        }

        /// <summary>
        /// (`AMBER274`, физика 30) Одно жёсткое столкновение у разделённых элементов: элемент — по
        /// долям, линейно интерполированным между концами шага (<paramref name="shareA"/> —
        /// начало, <paramref name="shareB"/> — конец, <paramref name="w"/> — пройденная доля
        /// полного шага; без Ф7 — один массив и w = 0); одно равномерное число на
        /// <paramref name="majorant"/> выбирает и элемент, и — если оно выше суммы долей — ПУСТОЕ
        /// столкновение (Ф7: мажоранта шага). Без мажоранты (<paramref name="allowEmpty"/> false)
        /// остаток округления уходит последнему элементу, как у <see cref="LayerHardCollision"/>.
        /// Угол — <see cref="RefinedAngle"/>. Возвращает true, если поворот состоялся.
        /// </summary>
        bool RefinedHardCollision(ScatterElement[] elements, double[] shareA, double[] shareB, double w,
                                  double majorant, bool allowEmpty, double tKev, double muCut, bool positron,
                                  ref double ux, ref double uy, ref double uz)
        {
            double pick = this.Uniform() * majorant;
            int i = 0;
            for (; i < elements.Length; i++)
            {
                pick -= shareA[i] + (shareB[i] - shareA[i]) * w;
                if (pick <= 0.0)
                {
                    break;
                }
            }

            if (i >= elements.Length)
            {
                if (allowEmpty)
                {
                    this.CountMajorantEmpty++;
                    return false;
                }

                i = elements.Length - 1;
            }

            return this.RefinedAngle(elements[i], tKev, muCut, positron, ref ux, ref uy, ref uz);
        }

        /// <summary>
        /// (`AMBER274`, физика 30) Угол жёсткого столкновения на части элемента
        /// <paramref name="e"/>: электронная — обращением экранированного Резерфорда (Мольер) на
        /// [μ_c, предел e–e]; ядерная — DPWA или подгонка Мотта отбором до принятия (P/M), у
        /// позитрона под битом Ф4 — Мак-Кинли—Фешбах позитрона R = 1 − β²s² − παZβ s(1 − s) отбором
        /// по мажоранте 1 (отвергнутое — пустое), иначе Мак-Кинли—Фешбах электрона, иначе Резерфорд.
        /// </summary>
        bool RefinedAngle(ScatterElement e, double tKev, double muCut, bool positron,
                          ref double ux, ref double uy, ref double uz)
        {
            double gamma = 1.0 + tKev / ElectronMassKev;
            double beta2 = 1.0 - 1.0 / (gamma * gamma);
            double beta = Math.Sqrt(beta2);
            if (e.Part == 2)
            {
                double a2e = ScreeningTwoA(e.Z, e.Z13, beta2, beta * gamma);
                double top = Math.Min(2.0, this.EeLimitMu(tKev, positron));
                double loE = 1.0 / (muCut + a2e), hiE = 1.0 / (top + a2e);
                double muE = 1.0 / (loE - this.Uniform() * (loE - hiE)) - a2e;
                if (muE < muCut)
                {
                    muE = muCut;
                }
                else if (muE > top)
                {
                    muE = top;
                }

                this.Rotate(ref ux, ref uy, ref uz, 1.0 - muE);
                return true;
            }

            bool posMcf = positron && this.Refine(RefinePositron);
            DpwaElement d = positron ? e.DpwaPos : e.DpwaEl;
            double a2, c0, c1, c2, c3, c4, majorantFit;
            if (d != null)
            {
                double u;
                int k = d.Locate(Math.Log(tKev), out u);
                a2 = Math.Exp(d.LnA[k] + u * (d.LnA[k + 1] - d.LnA[k]));
                int o = 5 * k, o1 = o + 5;
                c0 = d.C[o] + u * (d.C[o1] - d.C[o]);
                c1 = d.C[o + 1] + u * (d.C[o1 + 1] - d.C[o + 1]);
                c2 = d.C[o + 2] + u * (d.C[o1 + 2] - d.C[o + 2]);
                c3 = d.C[o + 3] + u * (d.C[o1 + 3] - d.C[o + 3]);
                c4 = d.C[o + 4] + u * (d.C[o1 + 4] - d.C[o + 4]);
                majorantFit = d.Major[k] + u * (d.Major[k + 1] - d.Major[k]);
            }
            else if (e.MottFit && !posMcf)
            {
                a2 = ScreeningTwoA(e.Z, e.Z13, beta2, beta * gamma);
                MottTerms(e.MottCoef, beta, out c0, out c1, out c2, out c3, out c4);
                majorantFit = MottMajorant(c0, c1, c2, c3, c4, Math.Sqrt(muCut));
            }
            else
            {
                a2 = ScreeningTwoA(e.Z, e.Z13, beta2, beta * gamma);
                double loR = 1.0 / (muCut + a2), hiR = 1.0 / (2.0 + a2);
                double mu = 1.0 / (loR - this.Uniform() * (loR - hiR)) - a2;
                if (mu < muCut)
                {
                    mu = muCut;
                }
                else if (mu > 2.0)
                {
                    mu = 2.0;
                }

                if (posMcf || e.Mott)
                {
                    double s = Math.Sqrt(0.5 * mu);
                    double paz = Math.PI * ScatteringData.FineStructure * e.Z * beta;
                    double r = posMcf
                        ? 1.0 - beta2 * s * s - paz * s * (1.0 - s)
                        : 1.0 - beta2 * s * s + paz * s * (1.0 - s);
                    if (this.Uniform() * (posMcf ? 1.0 : 1.0 + 0.25 * paz) > r)
                    {
                        return false;
                    }
                }

                this.Rotate(ref ux, ref uy, ref uz, 1.0 - mu);
                return true;
            }

            // DPWA и подгонка Мотта: отбор до принятия, кандидат — Резерфорд с экранированием a2.
            double lo = 1.0 / (muCut + a2), hi = 1.0 / (2.0 + a2);
            double muFit = muCut;
            for (int tries = 1; ; tries++)
            {
                muFit = 1.0 / (lo - this.Uniform() * (lo - hi)) - a2;
                if (muFit < muCut)
                {
                    muFit = muCut;
                }
                else if (muFit > 2.0)
                {
                    muFit = 2.0;
                }

                double x = Math.Sqrt(muFit);
                double p = c0 + x * (c1 + x * (c2 + x * (c3 + x * c4)));
                if (p > majorantFit)
                {
                    this.CountMottMajorantExceeded++;
                }

                if (this.Uniform() * majorantFit <= p)
                {
                    break;
                }

                this.CountMottRejected++;
                if (tries >= MottMaxTries)
                {
                    this.CountMottTriesCap++;
                    break;
                }
            }

            this.Rotate(ref ux, ref uy, ref uz, 1.0 - muFit);
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
            if (r.Shape != 0)
            {
                d = Math.Min(d, ShapedInnerDistance(r, x, y, rad));     // (`AMBER282`)
            }
            else if (r.IsBox)
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
                if (h.Shape != 0)
                {
                    dxy = ShapedOuterDistance(h, x, y, rad);            // (`AMBER282`)
                }
                else if (h.IsBox)
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
        /// (`AMBER282`) Расстояние в плане от точки ВНУТРИ области формы до её границы — точно для
        /// выпуклого многоугольника (наименьшее по граням) и круга, дырка — снаружи: у многоугольника
        /// нижняя граница max(n·p − d) (не больше истинного расстояния до выпуклого тела), у круга —
        /// rad − RIn. Нижняя граница пути до выхода — то, что требует ранний выход.
        /// </summary>
        static double ShapedInnerDistance(Region r, double x, double y, double rad)
        {
            double d;
            if ((r.Shape & ShapeOuterPolygon) != 0)
            {
                d = double.MaxValue;
                for (int j = 0; j < r.PolyNX.Length; j++)
                {
                    d = Math.Min(d, r.PolyD[j] - (r.PolyNX[j] * x + r.PolyNY[j] * y));
                }
            }
            else
            {
                d = r.ROut - rad;
            }

            if ((r.Shape & ShapeHolePolygon) != 0)
            {
                d = Math.Min(d, PolygonOutsideBound(r.HoleNX, r.HoleNY, r.HoleD, x, y));
            }
            else if (r.RIn > 0.0)
            {
                d = Math.Min(d, rad - r.RIn);
            }

            return d;
        }

        /// <summary>
        /// (`AMBER282`) Расстояние в плане от точки СНАРУЖИ тела области формы до этого тела (ноль —
        /// точка в теле), нижняя граница: снаружи наружного многоугольника — max(n·p − d), внутри дырки
        /// — наименьшее по граням дырки (точно), у круга — как у кольца.
        /// </summary>
        static double ShapedOuterDistance(Region h, double x, double y, double rad)
        {
            double outside = (h.Shape & ShapeOuterPolygon) != 0
                ? PolygonOutsideBound(h.PolyNX, h.PolyNY, h.PolyD, x, y)
                : rad - h.ROut;
            double inHole;
            if ((h.Shape & ShapeHolePolygon) != 0)
            {
                inHole = double.MaxValue;
                for (int j = 0; j < h.HoleNX.Length; j++)
                {
                    inHole = Math.Min(inHole, h.HoleD[j] - (h.HoleNX[j] * x + h.HoleNY[j] * y));
                }
            }
            else
            {
                inHole = h.RIn - rad;
            }

            return Math.Max(0.0, Math.Max(outside, inHole));
        }

        /// <summary>(`AMBER282`) max(n·p − d) по граням выпуклого многоугольника: снаружи — нижняя граница расстояния до него, внутри — отрицательно.</summary>
        static double PolygonOutsideBound(double[] nx, double[] ny, double[] d, double x, double y)
        {
            double m = double.MinValue;
            for (int j = 0; j < nx.Length; j++)
            {
                m = Math.Max(m, nx[j] * x + ny[j] * y - d[j]);
            }

            return m;
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
        /// (`M13`, П111 19.09.2026) Косинус угла КВАНТА ТОРМОЗНОГО к электрону —
        /// формула 2BS Коха—Моца (Rev. Mod. Phys. 31, 920 (1959)), розыгрыш отбором
        /// по Белаеву (PIRS-0203) — переписан с `G4Generator2BS::SampleDirection`
        /// Geant4 11.4.2 до последнего знака (у арбитра `option4` им направляется
        /// тормозное обеих моделей — Зельцера—Бергера и релятивистской):
        /// <paramref name="tKev"/> — кинетическая энергия электрона ДО излучения,
        /// <paramref name="kKev"/> — энергия кванта, <paramref name="z"/> —
        /// (эффективный) заряд ядра вещества. Переменная y — квадрат приведённого
        /// угла (E₀θ)², плотность ∝ 1/(1+y)² на [0, y_max], отбор функцией
        /// 4x − (1+r)² − ((1+r²) − x)·ln(δ + f_Z/(1+y)²), где r = E/E₀ (полные
        /// энергии после и до), x = 4yr/(1+y)², f_Z = Z^{1/3}(Z+1)^{1/3}/111²,
        /// δ = 0 (как у Geant4). Зачем: у модифицированного Цая (<see cref="TsaiCosine"/>)
        /// в заднюю полусферу летит 4.1 % квантов при 1000 кэВ и 15 % при 100, у
        /// 2BS — 5.7 % и 23 % (×1.4…1.5); в толстой мишени PTFE это ×0.65 нашей
        /// доли назад против арбитра (П111 §3).
        /// </summary>
        double Brem2BSCosine(double tKev, double kKev, double z)
        {
            double energy = tKev + ElectronMassKev;                  // полная энергия до
            double final = energy - kKev;
            if (!(final > ElectronMassKev) || !(z >= 1.0))
            {
                return this.TsaiCosine(tKev);                        // квант унёс всё — предела у формулы нет
            }

            double ratio = final / energy;
            double ratio1 = (1.0 + ratio) * (1.0 + ratio);
            double ratio2 = 1.0 + ratio * ratio;
            double gamma = energy / ElectronMassKev;
            double beta = Math.Sqrt((gamma - 1.0) * (gamma + 1.0)) / gamma;
            double fz = 0.00008116224 * Math.Pow(z, 1.0 / 3.0) * Math.Pow(z + 1.0, 1.0 / 3.0);
            double yMax = 2.0 * beta * (1.0 + beta) * gamma * gamma;
            double gMax = Math.Max(Brem2BSReject(0.0, ratio, ratio1, ratio2, fz),
                                   Brem2BSReject(yMax, ratio, ratio1, ratio2, fz));
            double y;
            int guard = 0;
            do
            {
                double q = this.Uniform();
                y = q * yMax / (1.0 + yMax * (1.0 - q));
            }
            while ((this.Uniform() * gMax > Brem2BSReject(y, ratio, ratio1, ratio2, fz) || y > yMax) && ++guard < 1000);

            return 1.0 - 2.0 * y / yMax;
        }

        /// <summary>Функция отбора 2BS (`G4Generator2BS::RejectionFunction`, δ = 0).</summary>
        static double Brem2BSReject(double y, double ratio, double ratio1, double ratio2, double fz)
        {
            double y2 = (1.0 + y) * (1.0 + y);
            double x = 4.0 * y * ratio / y2;
            return 4.0 * x - ratio1 - (ratio2 - x) * Math.Log(fz / y2);
        }

        /// <summary>
        /// (П111) Эффективный Z вещества слоя для углового розыгрыша 2BS
        /// (<see cref="Brem2BSCosine"/>): средний Z элементов с весом их вклада в
        /// тормозное, w·Z²/A (у PTFE — 8.6: фтор даёт 82 % выхода; у Al — 13).
        /// Зависимость 2BS от Z слабая (только через f_Z под логарифмом; Z 9 и 13
        /// неразличимы розыгрышем в 200 тыс.), поэтому одно число на вещество, а
        /// не розыгрыш элемента на каждый квант. Кэш на экземпляр.
        /// </summary>
        internal double LayerBremZ(GeometryMaterial material)
        {
            double z;
            if (this.layerBremZCache.TryGetValue(material, out z))
            {
                return z;
            }

            double num = 0.0, den = 0.0;
            Dictionary<int, double> mass = MaterialDatabase.AtomicMass;
            foreach (KeyValuePair<int, double> pair in material.Fractions)
            {
                double a;
                if (pair.Value > 0.0 && mass.TryGetValue(pair.Key, out a) && a > 0.0)
                {
                    double w = pair.Value * pair.Key * pair.Key / a;
                    num += w * pair.Key;
                    den += w;
                }
            }

            z = den > 0.0 ? num / den : 0.0;
            this.layerBremZCache[material] = z;
            return z;
        }

        /// <summary>(П111) Кэш <see cref="LayerBremZ"/> и Z текущего вещества у переноса в слоях.</summary>
        readonly Dictionary<GeometryMaterial, double> layerBremZCache = new Dictionary<GeometryMaterial, double>();
        double layerBremZ;

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
                                 ref double radiated, ref double lost, bool positron = false)
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

            // (`AMBER274`, физика 29) Под ключом `ElectronMixedTransport` в кристалле — своя
            // доля шага (`ElectronCrystalStepFraction`, выбрана по сходимости η); без ключа —
            // общая, как было. (физика 30, Н10 критиков) Рычаг замера без бита 2 (кристалл без
            // смешанной схемы) — кристалл физики 28 целиком: общая доля и прежний потолок шагов
            // (прежде — Хайленд с долей 0.05 и потолком 4000, не физика 28).
            bool mixedCrystal = this.ElectronMixedTransport && (this.ElectronMixedParts & 2) != 0;
            double fraction = mixedCrystal ? this.ElectronCrystalStepFraction : this.ElectronStepFraction;
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

            // (`AMBER274`, физика 29) Смешанная схема в кристалле — под ключом
            // `ElectronMixedTransport` (бит 2 рычага замера `ElectronMixedParts`):
            // элементы вещества кристалла и отсечка — те же, что у слоёв
            // (`LayerScatterElements`, `LayerHardCutoffMu`). Кристалл без опознанных
            // элементов — Хайленд, как без ключа. Без ключа ни одного лишнего
            // случайного числа и прежний потолок шагов.
            bool mixed = mixedCrystal;
            ScatterElement[] elements = null;
            double muCut = 0.0;
            if (mixed)
            {
                elements = this.LayerScatterElements(this.geometry.Crystal);
                muCut = this.LayerHardCutoffMu();
                mixed = elements.Length > 0;
            }

            // (`AMBER274`, физика 30) Разделённые части Z² и Z (`ElectronElasticRefine`,
            // `ElectronScreeningDpwa`) — свои функции сечений и столкновения; Ф7 — сечения на
            // концах полного шага и мажоранта; Ф6 — пересчёт затёртых долей.
            bool split = mixed && (this.ElectronElasticRefine || this.ElectronScreeningDpwa);
            bool stepMajorant = split && this.Refine(RefineStepMajorant);
            bool shareFix = this.Refine(RefineShareRecompute);

            int maxSteps = mixedCrystal ? TransportMaxStepsMixed : TransportMaxSteps;
            double t = te;
            int step = 0;
            for (; step < maxSteps && t > TransportCutKev; step++)
            {
                double stepG = residual * fraction;                          // г/см²
                double stepCm = stepG / density;

                // (`AMBER274`, физика 29) Путь до жёсткого столкновения — по сечению при
                // энергии середины шага; выпало раньше конца шага — шаг обрывается на нём.
                double hardPerCm = 0.0, softTransportPerCm = 0.0;
                bool hardHit = false;
                // (`AMBER274`, физика 30) Что нужно столкновению в конце шага: энергии концов (Ф7) или
                // середины, пройденная доля, мажоранта, отметка записи долей (Ф6).
                double tXs = 0.0, tA = 0.0, tB = 0.0, wStep = 0.0;
                long shareMark = 0;
                if (mixed && stepMajorant)
                {
                    // (`AMBER274`, физика 30, Ф7) Сечения на концах ПОЛНОГО шага по CSDA; путь до
                    // кандидата — по мажоранте max(σ_нач, σ_кон); мягкая часть — среднее линейного по
                    // пройденной части шага.
                    this.CountCrystalSteps++;
                    tA = t;
                    tB = ElectronData.EnergyOfRange(this.electron, residual - stepG);
                    if (tB < TransportCutKev)
                    {
                        tB = TransportCutKev;
                    }

                    double hA, hB, sA, sB;
                    this.RefinedElasticStep(elements, tA, muCut, positron, out hA, out sA, this.layerHardShare);
                    this.RefinedElasticStep(elements, tB, muCut, positron, out hB, out sB, this.layerHardShareEnd);
                    shareMark = this.layerShareWrites;
                    hardPerCm = Math.Max(hA, hB);
                    double fullCm = stepCm;
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

                    wStep = stepCm < fullCm ? stepCm / fullCm : 1.0;
                    softTransportPerCm = sA + (sB - sA) * 0.5 * wStep;
                    if (!(softTransportPerCm > 0.0))
                    {
                        softTransportPerCm = 0.0;
                    }
                }
                else if (mixed)
                {
                    this.CountCrystalSteps++;
                    tXs = ElectronData.EnergyOfRange(this.electron, residual - 0.5 * stepG);
                    if (tXs < TransportCutKev)
                    {
                        tXs = TransportCutKev;
                    }

                    if (split)
                    {
                        this.RefinedElasticStep(elements, tXs, muCut, positron,
                                                out hardPerCm, out softTransportPerCm, this.layerHardShare);
                    }
                    else
                    {
                        this.LayerElasticStep(elements, tXs, muCut,
                                              out hardPerCm, out softTransportPerCm, this.layerHardShare);
                    }

                    shareMark = this.layerShareWrites;
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

                // Прямой ход до шарнира.
                double first = stepCm * this.Uniform();
                double toEdge = this.CrystalPath(x, y, z, ux, uy, uz);
                if (first >= toEdge)
                {
                    // (`AMBER274`, физика 28) Энергия на грани — с разыгранной потерей пути до неё.
                    if (!this.EscapeOrReturn(ref x, ref y, ref z, ref ux, ref uy, ref uz, toEdge,
                                             this.ResidualAfter(this.electron, t, residual, toEdge * density),
                                             te - radiated - escaped,
                                             depth, ref residual, ref t, ref escaped, positron))
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

                // (`AMBER274`, физика 29) Под ключом ширина мягкого шарнира — средний
                // 1 − cos θ мягких столкновений на длину шага, 1 − exp(−s·Σnσ_tr,soft)
                // (аддитивно по пути); без ключа — Хайленд на весь шаг, как было.
                double theta0 = mixed
                    ? Math.Sqrt(Math.Max(0.0, 1.0 - Math.Exp(-stepCm * softTransportPerCm)))
                    : HighlandTheta0(tMid, stepG / x0);
                this.Rotate(ref ux, ref uy, ref uz, 1.0 - this.SampleHingeMu(theta0));

                // Прямой ход до конца шага.
                double second = stepCm - first;
                toEdge = this.CrystalPath(x, y, z, ux, uy, uz);
                if (second >= toEdge)
                {
                    if (!this.EscapeOrReturn(ref x, ref y, ref z, ref ux, ref uy, ref uz, toEdge,
                                             this.ResidualAfter(this.electron, t, residual, (first + toEdge) * density),
                                             te - radiated - escaped,
                                             depth, ref residual, ref t, ref escaped, positron))
                    {
                        return escaped;
                    }

                    continue;
                }

                x += ux * second;
                y += uy * second;
                z += uz * second;
                residual = this.ResidualAfter(this.electron, t, residual, stepG);
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

                // (`AMBER274`, физика 29) Жёсткое столкновение в конце оборванного шага —
                // при энергии его конца; пустое (мажоранта Мотта) направления не меняет.
                // После раннего выхода: погибающему внутри поворот не нужен.
                if (hardHit && t > TransportCutKev
                    && this.CrystalHardCollision(elements, split, stepMajorant, shareFix, shareMark,
                                                 tXs, tA, tB, wStep, hardPerCm, t, muCut, positron,
                                                 ref ux, ref uy, ref uz))
                {
                    this.CountCrystalHardCollisions++;
                }
            }

            if (step >= maxSteps && t > TransportCutKev)
            {
                this.CountTransportStepCap++;
            }

            return escaped;
        }

        /// <summary>
        /// (`AMBER274`, физика 30) Жёсткое столкновение в конце шага — кристалла и слоёв. Без ключей
        /// физики 30 — прежнее <see cref="LayerHardCollision"/>; с разделёнными частями —
        /// <see cref="RefinedHardCollision"/> (с Ф7 — доли концов шага, интерполированные на
        /// пройденную долю <paramref name="w"/>, и мажоранта; без Ф7 — доли середины шага).
        /// ⛔ Ф6: вложенный перенос (тормозное шага → вторичный электрон) пишет в те же рабочие
        /// массивы долей; разошлась отметка записи <paramref name="shareMark"/> — доли пересчитываются
        /// той же чистой функцией при тех же энергиях, без случайных чисел
        /// (<see cref="CountShareRecomputed"/>).
        /// </summary>
        bool CrystalHardCollision(ScatterElement[] elements, bool split, bool stepMajorant, bool shareFix,
                                  long shareMark, double tXs, double tA, double tB, double w, double hardPerCm,
                                  double t, double muCut, bool positron, ref double ux, ref double uy, ref double uz)
        {
            if (shareFix && this.layerShareWrites != shareMark)
            {
                double h, s;
                this.CountShareRecomputed++;
                if (stepMajorant)
                {
                    this.RefinedElasticStep(elements, tA, muCut, positron, out h, out s, this.layerHardShare);
                    this.RefinedElasticStep(elements, tB, muCut, positron, out h, out s, this.layerHardShareEnd);
                }
                else if (split)
                {
                    this.RefinedElasticStep(elements, tXs, muCut, positron, out h, out s, this.layerHardShare);
                }
                else
                {
                    this.LayerElasticStep(elements, tXs, muCut, out h, out s, this.layerHardShare);
                }
            }

            if (stepMajorant)
            {
                return this.RefinedHardCollision(elements, this.layerHardShare, this.layerHardShareEnd, w, hardPerCm,
                                                 true, t, muCut, positron, ref ux, ref uy, ref uz);
            }

            if (split)
            {
                return this.RefinedHardCollision(elements, this.layerHardShare, this.layerHardShare, 0.0, hardPerCm,
                                                 false, t, muCut, positron, ref ux, ref uy, ref uz);
            }

            return this.LayerHardCollision(elements, this.layerHardShare, hardPerCm, t, muCut, ref ux, ref uy, ref uz);
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
                            ref double residual, ref double t, ref double escaped, bool positron = false)
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
            bool back = this.TransportInLayers(ref x, ref y, ref z, ref ux, ref uy, ref uz, ref tBack, depth, positron);
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
                               ref double t, int depth, bool positron = false)
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

            // (`AMBER274`, физика 29) Под ключом `ElectronMixedTransport` (бит 1 рычага
            // замера `ElectronMixedParts`) сечения смешанной схемы — при энергии СЕРЕДИНЫ
            // шага; потолок шагов — `TransportMaxStepsMixed`. Без ключа — как было.
            bool midXs = this.ElectronMixedTransport && (this.ElectronMixedParts & 1) != 0;
            int maxSteps = this.ElectronMixedTransport ? TransportMaxStepsMixed : TransportMaxSteps;
            // (`AMBER274`, физика 30) Разделённые части Z² и Z, Ф7 и Ф6 — как в кристалле
            // (`TransportElectron`, `CrystalHardCollision`).
            bool split = mixed && (this.ElectronElasticRefine || this.ElectronScreeningDpwa);
            bool stepMajorant = split && this.Refine(RefineStepMajorant);
            bool shareFix = this.Refine(RefineShareRecompute);
            int step = 0;
            Region known = null;
            for (; step < maxSteps && t > TransportCutKev; step++)
            {
                // ⚡ (`AMBER315`) После глубокого шага область та же: весь шаг прошёл дальше запаса от её
                // границ, и поиск области (по кэшу луча или перебором) вернул бы её же.
                Region here;
                if (known != null)
                {
                    here = known;
                    known = null;
                    this.CountAt++;
                }
                else
                {
                    here = this.At(x, y, z);
                }

                if (here != null && here.IsCrystal)
                {
                    return true;
                }

                double density = here != null && here.Material != null ? here.Material.Density : 0.0;

                // ⚡ (`AMBER315`, полоса `a315`) ГЛУБОКИЙ ШАГ. Расстояние до границы вдоль луча на шаге
                // читается только сравнениями «отрезок шага ≥ расстояния» и «луч ушёл из сцены». Если шаг
                // короче расстояния до ближайшей границы области по ЛЮБОМУ направлению
                // (`RegionNearestFace`, оно же считается ниже для раннего выхода) на запас
                // `LayerDeepMarginCm` — оба сравнения ложны заранее, и разбор луча (до двух на шаг) не
                // нужен. То же, когда электрон погибнет в области (ранний выход ниже): расстояние там не
                // читается вовсе. Потолок шага считается теми же выражениями, что ниже; жёсткое
                // столкновение шаг только укорачивает. Случайных чисел решение не тянет, счётчик шага и
                // память кэша луча ведутся как прежде (`StepToBoundaryUnread`) — числа те же до бита.
                bool deep = false, faceKnown = false, rangeKnown = false;
                double face = 0.0, range = 0.0;
                if (LayerDeepStep && !MeasureCollectCost && density > 0.0)
                {
                    face = this.RegionNearestFace(here, x, y, z);
                    faceKnown = true;
                    if (face > LayerDeepMarginCm)
                    {
                        range = ElectronData.RangeOf(this.CarryMedium(here.Material), t);
                        rangeKnown = true;
                        if (range > 0.0)
                        {
                            double reach = range * fraction / density;
                            if (this.Refine(RefineLayerStepLimit))
                            {
                                double capDeep = this.ElectronLayerStepMaxFraction * RegionThicknessCm(here);
                                if (capDeep > 0.0 && reach > capDeep)
                                {
                                    reach = capDeep;
                                }
                            }

                            deep = range / density <= face || reach < face - LayerDeepMarginCm;
                        }
                    }
                }

                double toNext;
                if (deep)
                {
                    this.StepToBoundaryUnread(x, y, z, ux, uy, uz);
                    toNext = double.PositiveInfinity;
                }
                else
                {
                    toNext = this.StepToBoundary(x, y, z, ux, uy, uz);
                    if (toNext >= double.MaxValue)
                    {
                        this.CountLayerLeftScene++;
                        this.layersLeftScene = true;
                        return false;           // ушёл из сцены
                    }
                }

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
                double residual = rangeKnown ? range : ElectronData.RangeOf(medium, t);          // г/см²
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
                    // (П111) Эффективный Z вещества — для углового розыгрыша 2BS
                    // (<see cref="ElectronLayerBremAngular2BS"/>); без ключа не читается.
                    this.layerBremZ = this.ElectronLayerBremAngular2BS ? this.LayerBremZ(here.Material) : 0.0;
                }

                // Ранний выход, как в кристалле: пробег короче расстояния до
                // ближайшей границы области — погибнет в ней при любой траектории.
                if (residual / density <= (faceKnown ? face : this.RegionNearestFace(here, x, y, z)))
                {
                    // (П106) Погибнет здесь — остаток тормозного толстой мишенью
                    // в точке гибели, как `RestBremsstrahlung` в кристалле.
                    if (lbrem && bremTable != null)
                    {
                        this.LayerRestBremsstrahlung(x, y, z, ux, uy, uz, t, tEntry, bremTable, ref radiated);
                    }

                    this.SumLayerPathG += residual;                  // (П111) остаток пробега — весь в этом веществе
                    return false;
                }

                double stepG = residual * fraction;
                double stepCm = stepG / density;

                // (`AMBER274`, физика 30, бит 32 `ElectronElasticRefine`) Шаг в слое — не длиннее доли
                // толщины тела области (`RegionThicknessCm`, DSMAX PENELOPE): иначе тонкий слой
                // проходится без мягкого отклонения (шарнир за границей).
                if (this.Refine(RefineLayerStepLimit))
                {
                    double capCm = this.ElectronLayerStepMaxFraction * RegionThicknessCm(here);
                    if (capCm > 0.0 && stepCm > capCm)
                    {
                        stepCm = capCm;
                        stepG = stepCm * density;
                        this.CountLayerStepLimited++;
                    }
                }

                // (`M13`) Смешанная схема: расстояние до жёсткого столкновения;
                // выпало раньше конца шага — шаг обрывается на нём. Мягкий
                // шарнир — по транспортному сечению ниже отсечки на длину шага.
                ScatterElement[] elements = null;
                double hardPerCm = 0.0, softTransportPerCm = 0.0;
                bool hardHit = false;
                double tXs = t, tA = 0.0, tB = 0.0, wStep = 0.0;
                long shareMark = 0;
                if (mixed && stepMajorant)
                {
                    // (`AMBER274`, физика 30, Ф7) Как в кристалле: сечения на концах полного шага,
                    // мажоранта, мягкая часть — среднее линейного по пройденной части.
                    this.CountLayerSteps++;
                    elements = this.LayerScatterElements(here.Material);
                    if (elements.Length > 0)
                    {
                        tA = t;
                        tB = ElectronData.EnergyOfRange(medium, residual - stepG);
                        if (tB < TransportCutKev)
                        {
                            tB = TransportCutKev;
                        }

                        double hA, hB, sA, sB;
                        this.RefinedElasticStep(elements, tA, muCut, positron, out hA, out sA, this.layerHardShare);
                        this.RefinedElasticStep(elements, tB, muCut, positron, out hB, out sB, this.layerHardShareEnd);
                        shareMark = this.layerShareWrites;
                        hardPerCm = Math.Max(hA, hB);
                        double fullCm = stepCm;
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

                        wStep = stepCm < fullCm ? stepCm / fullCm : 1.0;
                        softTransportPerCm = sA + (sB - sA) * 0.5 * wStep;
                        if (!(softTransportPerCm > 0.0))
                        {
                            softTransportPerCm = 0.0;
                        }
                    }
                }
                else if (mixed)
                {
                    this.CountLayerSteps++;
                    elements = this.LayerScatterElements(here.Material);
                    if (elements.Length > 0)
                    {
                        // (`AMBER274`, физика 29) Энергия сечений — середина шага (под ключом).
                        if (midXs)
                        {
                            tXs = ElectronData.EnergyOfRange(medium, residual - 0.5 * stepG);
                            if (tXs < TransportCutKev)
                            {
                                tXs = TransportCutKev;
                            }
                        }

                        if (split)
                        {
                            this.RefinedElasticStep(elements, tXs, muCut, positron, out hardPerCm, out softTransportPerCm, this.layerHardShare);
                        }
                        else
                        {
                            this.LayerElasticStep(elements, tXs, muCut, out hardPerCm, out softTransportPerCm, this.layerHardShare);
                        }

                        shareMark = this.layerShareWrites;
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
                    t = ElectronData.EnergyOfRange(medium, this.ResidualAfter(medium, t, residual, toNext * density));
                    this.SumLayerPathG += toNext * density;      // (П111) счётчик пути в веществе
                    continue;
                }

                x += ux * first;
                y += uy * first;
                z += uz * first;
                this.SumLayerPathG += first * density;               // (П111) счётчик пути в веществе

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
                if (deep)
                {
                    this.StepToBoundaryUnread(x, y, z, ux, uy, uz);     // (`AMBER315`) глубокий шаг
                    toNext = double.PositiveInfinity;
                }
                else
                {
                    toNext = this.StepToBoundary(x, y, z, ux, uy, uz);
                    if (toNext >= double.MaxValue)
                    {
                        this.CountLayerLeftScene++;
                        this.layersLeftScene = true;
                        return false;
                    }
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
                    t = ElectronData.EnergyOfRange(medium, this.ResidualAfter(medium, t, residual, (first + toNext) * density));
                    this.SumLayerPathG += toNext * density;      // (П111) счётчик пути в веществе
                    continue;
                }

                x += ux * second;
                y += uy * second;
                z += uz * second;
                this.SumLayerPathG += second * density;              // (П111) счётчик пути в веществе

                // (П106) Тормозное второго отрезка — в его конце, по направлению
                // после шарнира.
                if (lbrem && bremTable != null && second > 0.0)
                {
                    this.LayerStepBremsstrahlung(x, y, z, ux, uy, uz,
                                                 LayerMidEnergy(medium, residual - first * density, 0.5 * second * density),
                                                 second * density, bremAnchor, tEntry, bremTable, ref radiated);
                }

                residual = this.ResidualAfter(medium, t, residual, stepG);
                t = ElectronData.EnergyOfRange(medium, residual);

                // (`M13`) Жёсткое столкновение в конце оборванного шага — при
                // энергии его конца; пустое (мажоранта Мотта) направления не меняет.
                // (`AMBER274`, физика 30) Тем же `CrystalHardCollision`, что в кристалле.
                if (hardHit && t > TransportCutKev
                    && this.CrystalHardCollision(elements, split, stepMajorant, shareFix, shareMark,
                                                 tXs, tA, tB, wStep, hardPerCm, t, muCut, positron,
                                                 ref ux, ref uy, ref uz))
                {
                    this.CountLayerHardCollisions++;
                }

                if (deep)
                {
                    known = here;           // (`AMBER315`) шаг кончился в той же области
                }
            }

            if (step >= maxSteps && t > TransportCutKev)
            {
                this.CountTransportStepCap++;
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
        /// по электрону (Цай; под ключом <see cref="ElectronLayerBremAngular2BS"/> —
        /// 2BS Коха—Моца, <see cref="Brem2BSCosine"/>), зажим суммой, приёмник —
        /// очередь обхода заноса (<see cref="layerBremPush"/>) либо очередь вылетов
        /// (<see cref="NoteEscape"/>), когда она открыта; иначе унесён — его
        /// энергия уже в уносе электрона (как у `LayerBremsstrahlung`).
        /// </summary>
        void LayerEmitBremsstrahlung(double x, double y, double z, double ux, double uy, double uz,
                                     double k, double tKev, double cap, ref double radiated)
        {
            double ax = ux, ay = uy, az = uz;
            // (П111) Косинус разыгрывается ДО зажима, как и было у Цая, — число
            // обращений к ГСЧ у выключенного ключа прежнее.
            this.Rotate(ref ax, ref ay, ref az, this.ElectronLayerBremAngular2BS
                                                    ? this.Brem2BSCosine(tKev, k, this.layerBremZ)
                                                    : this.TsaiCosine(tKev));
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

        // ------------------------------------------------------------------
        // (`AMBER274`, физика 28) СТРАГГЛИНГ ПОТЕРЬ — модель Урбана
        // ------------------------------------------------------------------

        /// <summary>Ниже этой средней потери флуктуаций нет (`minLoss` Geant4, 10 эВ), кэВ.</summary>
        const double StragglingMinLossKev = 0.01;

        /// <summary>Энергия ионизации модели (`fEnergy0fluct` Geant4, 10 эВ), кэВ.</summary>
        const double StragglingE0Kev = 0.01;

        /// <summary>Ниже этой энергии после шага электрон остановился (`LowestElectronEnergy` option4, 100 эВ), кэВ.</summary>
        const double StragglingLowestKev = 0.1;

        // Постоянные `G4UniversalFluctuation` Geant4 11.4 (`rate`, `fw`, `a0`, `nmaxCont`).
        const double UrbanRate = 0.56;
        const double UrbanFw = 4.0;
        const double UrbanA0 = 42.0;
        const double UrbanNmaxCont = 8.0;

        /// <summary>
        /// (`AMBER274`, физика 28) Счётчики страгглинга: шагов с разыгранной потерей и
        /// остановок по ней (энергия после шага не выше <see cref="StragglingLowestKev"/>).
        /// Диагностика — на розыгрыш не влияют; у выключенного ключа нули (ключ доехал до
        /// переноса — положительный контроль <see cref="ResponseMatrixOptions.ElectronStraggling"/>).
        /// </summary>
        public long CountStragglingSteps, CountStragglingStops;

        /// <summary>
        /// (`AMBER274`, физика 28) Счётчик-диагностика: перенос в слоях кончился уходом из сцены
        /// (луч без границ впереди). Паритет ЦП↔GPU раздела электрона пробы сверяет его.
        /// </summary>
        public long CountLayerLeftScene;

        /// <summary>
        /// (`AMBER274`, физика 28) Последний перенос в слоях (<see cref="TransportInLayers"/>)
        /// кончился уходом из сцены. Сбрасывает его <see cref="CarriedElectronEnters"/> до
        /// переноса; читает позитрон пробы (<see cref="PositronSourceDeposit"/>): прежде «ушёл»
        /// решалось ПОВТОРНЫМ запросом геометрии из точки остановки, и во float GPU этот запрос
        /// расходился с ЦП (у позитронов ниже 20 кэВ после капсулы аннигиляций в сцене на GPU
        /// выходило на 1.5 % больше; паритет `BetaSectionProbeA274 --esrc-stats`).
        /// </summary>
        bool layersLeftScene;

        /// <summary>
        /// (`AMBER274`, физика 28) Постоянные модели Урбана вещества — как
        /// `G4IonisParamMat::ComputeFluctModel`: I, ln I, f₁, f₂, E₁, E₂, ln E₁, ln E₂ (кэВ).
        /// Z_эфф — по массовым долям; при Z_эфф &gt; 2.1 f₂ = 2/Z, E₂ = 10·Z² эВ,
        /// ln E₁ = (ln I − f₂ ln E₂)/f₁; при 1.1 &lt; Z ≤ 2.1 — f₂ = 0, E₂ = 40 эВ; ниже — E₂ = 10 эВ.
        /// Считается один раз на вещество (одинаковое содержимое — гонка безвредна).
        /// </summary>
        internal static double[] UrbanParams(ElectronData.Material m)
        {
            double[] u = m.Urban;
            if (u != null)
            {
                return u;
            }

            double ipot = m.PotentialEv * 1e-3;
            double zeff = m.ZeffMass;
            double logI = Math.Log(ipot);
            double f1 = 1.0, f2 = 0.0, e2, logE1 = logI;
            if (zeff > 2.1)
            {
                f2 = 2.0 / zeff;
                f1 = 1.0 - f2;
                e2 = 0.01 * zeff * zeff;
                logE1 = (logI - f2 * Math.Log(e2)) / f1;
            }
            else if (zeff > 1.1)
            {
                e2 = 0.04;
            }
            else
            {
                e2 = 0.01;
            }

            u = new[] { ipot, logI, f1, f2, Math.Exp(logE1), e2, logE1, Math.Log(e2) };
            m.Urban = u;
            return u;
        }

        /// <summary>
        /// (`AMBER274`, физика 28) ОСТАТОЧНЫЙ ПРОБЕГ ПОСЛЕ ОТРЕЗКА ПУТИ <paramref name="pathG"/>
        /// (г/см²) электрона энергии <paramref name="t"/> с остаточным пробегом
        /// <paramref name="residual"/> в веществе <paramref name="medium"/>. Без ключа
        /// <see cref="ElectronStraggling"/> — <c>residual − pathG</c>, побитово прежнее
        /// выражение и ни одного случайного числа. С ключом (как `G4VEnergyLossProcess::
        /// AlongStepDoIt` option4): путь до конца пробега — остановился; средняя потеря —
        /// разность энергий по CSDA; её радиационная часть (<see cref="ElectronData.RadShareOf"/>
        /// при энергии начала) остаётся средней, ионизационная разыгрывается моделью Урбана
        /// (<see cref="UrbanLoss"/>); энергия после ниже 100 эВ — остановился (0); иначе —
        /// пробег от НОВОЙ энергии. Вещество без I (вшитый эталон сверки) — без разброса.
        /// </summary>
        internal double ResidualAfter(ElectronData.Material medium, double t, double residual, double pathG)
        {
            if (!this.ElectronStraggling || !(medium.PotentialEv > 0.0))
            {
                return residual - pathG;
            }

            double after = residual - pathG;
            if (!(after > 0.0))
            {
                return after;               // путь не короче пробега — остановился
            }

            double meanLoss = t - ElectronData.EnergyOfRange(medium, after);
            if (!(meanLoss >= StragglingMinLossKev))
            {
                return after;
            }

            this.CountStragglingSteps++;
            double radiative = meanLoss * ElectronData.RadShareOf(medium, t);
            double loss = radiative + this.UrbanLoss(medium, t, meanLoss - radiative);
            double tEnd = t - loss;
            if (!(tEnd > StragglingLowestKev))
            {
                this.CountStragglingStops++;
                return 0.0;
            }

            return ElectronData.RangeOf(medium, tEnd);
        }

        /// <summary>
        /// (`AMBER274`, физика 28) Разыгранная ИОНИЗАЦИОННАЯ потеря электрона энергии
        /// <paramref name="t"/> при средней <paramref name="meanLoss"/> (кэВ) — построчно
        /// `G4UniversalFluctuation::SampleFluctuations` (ветка не тяжёлых частиц: гауссов Бор
        /// не берётся) и `G4UrbanFluctuation::SampleGlandz` Geant4 11.4. Верх передачи
        /// tcut = tmax Мёллера (T/2): δ-электронов перенос не рождает, их энергия — в потере
        /// шага, поэтому флуктуирует полная ионизационная потеря.
        /// </summary>
        double UrbanLoss(ElectronData.Material m, double t, double meanLoss)
        {
            double[] u = UrbanParams(m);
            double ipot = u[0], ipotLog = u[1], f1 = u[2], f2 = u[3];
            double e1Fluct = u[4], e2Fluct = u[5], e1Log = u[6], e2Log = u[7];
            double tcut = 0.5 * t;
            double e0 = StragglingE0Kev;
            if (tcut <= e0)
            {
                return meanLoss;
            }

            double gam = t / ElectronMassKev + 1.0;
            double gam2 = gam * gam;
            double beta2 = 1.0 - 1.0 / gam2;
            double scaling = Math.Min(1.0 + 0.5 / tcut, 1.5);
            meanLoss /= scaling;
            double w2 = tcut > ipot ? Math.Log(2.0 * ElectronMassKev * beta2 * gam2) - beta2 : 0.0;

            double a1 = 0.0, a2 = 0.0;
            double e1 = e1Fluct, e2 = e2Fluct;
            if (tcut > ipot && w2 > ipotLog)
            {
                if (w2 > e2Log)
                {
                    double c = meanLoss * (1.0 - UrbanRate) / (w2 - ipotLog);
                    a1 = c * f1 * (w2 - e1Log) / e1Fluct;
                    a2 = c * f2 * (w2 - e2Log) / e2Fluct;
                }
                else
                {
                    a1 = meanLoss * (1.0 - UrbanRate) / e1;
                }

                if (a1 < UrbanA0)
                {
                    double fwnow = 0.5 + (UrbanFw - 0.5) * Math.Sqrt(a1 / UrbanA0);
                    a1 /= fwnow;
                    e1 *= fwnow;
                }
                else
                {
                    a1 /= UrbanFw;
                    e1 *= UrbanFw;
                }
            }

            double w1 = tcut / e0;
            double a3 = UrbanRate * meanLoss * (tcut - e0) / (e0 * tcut * Math.Log(w1));
            if (a1 + a2 <= 0.0)
            {
                a3 /= UrbanRate;
            }

            double loss = 0.0, emean = 0.0, sig2e = 0.0;
            if (a1 > 0.0)
            {
                this.UrbanExcitation(a1, e1, ref emean, ref loss, ref sig2e);
            }

            if (a2 > 0.0)
            {
                this.UrbanExcitation(a2, e2, ref emean, ref loss, ref sig2e);
            }

            if (sig2e > 0.0)
            {
                this.UrbanGauss(emean, sig2e, ref loss);
            }

            if (a3 > 0.0)
            {
                emean = 0.0;
                sig2e = 0.0;
                double p3 = a3, alfa = 1.0;
                if (a3 > UrbanNmaxCont)
                {
                    alfa = w1 * (UrbanNmaxCont + a3) / (w1 * UrbanNmaxCont + a3);
                    double alfa1 = alfa * Math.Log(alfa) / (alfa - 1.0);
                    double namean = a3 * w1 * (alfa - 1.0) / ((w1 - 1.0) * alfa);
                    emean += namean * e0 * alfa1;
                    sig2e += e0 * e0 * namean * (alfa - alfa1 * alfa1);
                    p3 -= namean;
                }

                double w3 = alfa * e0;
                if (tcut > w3)
                {
                    double w = (tcut - w3) / tcut;
                    int nnb = this.PoissonG4(p3);
                    for (int k = 0; k < nnb; k++)
                    {
                        loss += w3 / (1.0 - w * this.Uniform());
                    }
                }

                if (sig2e > 0.0)
                {
                    this.UrbanGauss(emean, sig2e, ref loss);
                }
            }

            return loss * scaling;
        }

        /// <summary>(`AMBER274`) `G4UniversalFluctuation::AddExcitation`: много столкновений — в гауссову сумму, мало — Пуассоном.</summary>
        void UrbanExcitation(double ax, double ex, ref double eav, ref double eloss, ref double esig2)
        {
            if (ax > UrbanNmaxCont)
            {
                eav += ax * ex;
                esig2 += ax * ex * ex;
            }
            else
            {
                int p = this.PoissonG4(ax);
                if (p > 0)
                {
                    eloss += ((p + 1) - 2.0 * this.Uniform()) * ex;
                }
            }
        }

        /// <summary>(`AMBER274`) `G4UniversalFluctuation::SampleGauss`: гаусс, обрезанный на [0, 2·среднее].</summary>
        void UrbanGauss(double eav, double esig2, ref double eloss)
        {
            double x = eav;
            double sig = Math.Sqrt(esig2);
            if (eav < 0.25 * sig)
            {
                x += (2.0 * this.Uniform() - 1.0) * eav;
            }
            else
            {
                int guard = 0;
                do
                {
                    x = eav + sig * this.Gaussian();
                }
                while ((x < 0.0 || x > 2.0 * eav) && ++guard < 1000);
                if (guard >= 1000)
                {
                    x = eav;
                }
            }

            eloss += x;
        }

        /// <summary>(`AMBER274`) Стандартная нормальная — Бокс—Мюллер, два равномерных на вызов (без памяти).</summary>
        double Gaussian()
        {
            double r = Math.Sqrt(-2.0 * Math.Log(this.Uniform()));
            return r * Math.Cos(2.0 * Math.PI * this.Uniform());
        }

        /// <summary>
        /// (`AMBER274`) Пуассон как `G4Poisson`: до среднего 16 — обращением функции
        /// распределения одним равномерным (сумма — в double, потолок 100), выше — гауссом.
        /// </summary>
        int PoissonG4(double mean)
        {
            if (!(mean > 0.0))
            {
                return 0;
            }

            if (mean <= 16.0)
            {
                double position = this.Uniform();
                double value = Math.Exp(-mean);
                double sum = value;
                int number = 0;
                while (sum <= position && number < 100)
                {
                    number++;
                    value *= mean / number;
                    sum += value;
                }

                return number;
            }

            double g = mean + this.Gaussian() * Math.Sqrt(mean) + 0.5;
            return g <= 0.0 ? 0 : (int)Math.Min(g, 2.0e9);
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
