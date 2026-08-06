using System;
using System.Collections.Generic;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// Расчёт эффективности регистрации в пике полного поглощения по геометрии,
    /// методом Монте-Карло. Ось z — ось детектора, начало — передний торец
    /// кристалла, кристалл лежит в z от 0 до высоты, проба — в отрицательных z.
    ///
    /// Две части, разные по способу счёта:
    ///
    /// 1. От точки вылета до кристалла фотон ведётся ДЕТЕРМИНИРОВАННО: считается
    ///    оптическая толщина по всем слоям (проба, стенка стакана, зазор,
    ///    отражатель, корпус) и вес умножается на exp(-tau). Для пика полного
    ///    поглощения это точно, а не приближение: рассеявшийся по дороге фотон
    ///    теряет энергию и в пик уже не попадёт ни при каких обстоятельствах.
    ///
    /// 2. В кристалле — обычный розыгрыш: длина пробега по mu_total, выбор типа
    ///    взаимодействия, комптоновское рассеяние по Клейну — Нишине с
    ///    продолжением истории, рождение пар с двумя квантами 511 кэВ. В пик
    ///    попадает история, из которой НИЧЕГО не вылетело.
    ///
    /// Что уже снято из прежних приближений: когерентное рассеяние выделено
    /// своим каналом и не убивает квант по дороге к кристаллу
    /// (<see cref="CoherentPassesThrough"/>); вылет характеристического
    /// K-рентгена моделируется (<see cref="XrayEscape"/>), и доля K-оболочки
    /// берётся по энергии из EPICS2017, а не константой со скачка на крае;
    /// однократный комптон в ближних слоях разыгрывается
    /// (<see cref="SingleScatter"/>).
    ///
    /// Что осталось приближением и где это заметно:
    /// * связь электронов в комптоновском сечении не учтена (чистая
    ///   Клейн — Нишина). Ниже 100 кэВ это завышает комптон, но там правит
    ///   фотопоглощение;
    /// * L-флуоресценция не моделируется: L-рентген тяжёлых кристаллов — это
    ///   4-6 кэВ, наружу он не выходит ниоткуда, кроме самой поверхности.
    ///
    /// Один экземпляр — один поток: и генератор (state), и ленивая сборка
    /// сцены (EnsureBuilt) без замков. Параллельный счёт заводит по
    /// экземпляру на поток (см. EfficiencyCalculation.Run).
    /// </summary>
    public sealed class EfficiencySimulator
    {
        // Судьба электрона (ElectronEscape, Bremsstrahlung) считается отдельно:
        // пока она не учтена, расчёт молча кладёт всю энергию электрона на
        // месте, а на деле электрон вблизи границы уходит наружу, и тормозной
        // квант тоже может уйти. Обе потери растут с энергией — там же, где
        // расчёт расходится с измерением сильнее всего.

        const double ElectronMassKev = 510.99895;
        const double ClassicalRadiusCm = 2.8179403262e-13;
        const double Avogadro = 6.02214076e23;
        const double Eps = 1e-9;

        /// <summary>Историй на точку кривой.</summary>
        public int Histories = 200000;

        public int Seed = 20260803;

        /// <summary>
        /// Доля когерентного (рэлеевского) рассеяния в полном ослаблении
        /// кристалла — для оценки того, во что обходится его отсутствие в
        /// таблице. Ноль (умолчание) — прежнее поведение: когерентное молча
        /// сидит внутри фотопоглощения. Величина здесь ставится руками; это
        /// инструмент измерения чувствительности, а не физическая модель.
        /// </summary>
        public double CoherentFractionOfTotal;

        /// <summary>
        /// Ставить оправу детектора перед торцом кристалла (иначе — за ним).
        /// В файле геометрии LSRM указана только толщина, без указания места.
        ///
        /// ВЫКЛЮЧЕНО, и это больше не догадка: на чертеже конструктора геометрий
        /// LSRM (GMaster, вкладка Detector) источник стоит сверху, передние
        /// толщины отражателя и корпуса подписаны у верхнего торца, а толщина
        /// оправы — у нижнего. Оправа за кристаллом.
        ///
        /// Цена ошибки, если бы выбрали иначе (ASN16 в маринелли, оправа 0.2 см):
        /// -6.2 % на 50 кэВ, -3.9 % на 662, -3.3 % на 2614. Для источника
        /// спереди оправа сзади не поглощает ничего вовсе, и её толщина на
        /// расчёт не влияет — у маринелли влияет, потому что проба охватывает
        /// детектор и часть её оказывается позади кристалла.
        /// </summary>
        public bool MountingInFront;

        /// <summary>
        /// Считать не пик полного поглощения, а долю квантов, ДОШЕДШИХ до
        /// кристалла без ослабления. Нужно, чтобы отделить геометрию от физики:
        /// для точечного источника на оси эта доля равна телесному углу и
        /// проверяется формулой, безо всякого переноса.
        /// </summary>
        public bool ScoreEntranceOnly;

        /// <summary>
        /// Учитывать вылет самого электрона через близкую границу кристалла.
        /// Разыгрывается изотропное направление, пробег берётся из ESTAR
        /// (<see cref="ElectronData"/>), путь до границы считается по прямой.
        ///
        /// ПО УМОЛЧАНИЮ ВЫКЛЮЧЕНО. Проверку измерением поправка не прошла: она
        /// обязана быть тем больше, чем мельче кристалл, и она такая и есть —
        /// на 662 кэВ она снимает 14 % у RC103 (1 см³) и 6 % у ASN16 (16 см³).
        /// А измерения требуют ровно обратного: у RC103 расчёт и так сходится с
        /// заводским коэффициентом (1.009), завышен именно ASN16 (1.162). С
        /// поправкой разброс между детекторами не сжимается, а растёт с 15 % до
        /// 26 %. Ключ оставлен измерительным: чтобы поправка стала физикой, не
        /// хватает данных о ветвлении и обратном рассеянии электрона в тяжёлом
        /// веществе (см. <see cref="ElectronDetour"/>).
        /// </summary>
        public bool ElectronEscape;

        /// <summary>
        /// Учитывать вылет тормозного кванта, рождённого электроном. Спектр —
        /// толстомишенный, dN/dk ~ 1/k, нормировка привязана к выходу излучения
        /// из ESTAR, так что средняя излучённая энергия равна табличной.
        /// </summary>
        public bool Bremsstrahlung = true;

        /// <summary>
        /// Отношение средней глубины проникновения электрона к пробегу CSDA
        /// (detour factor). Единица — прямолинейное торможение: электрон
        /// уходит дальше всего, вылет получается наибольшим из возможных. У
        /// тяжёлых сцинтилляторов настоящая величина меньше, и меньше вылет;
        /// единица здесь стоит нарочно, чтобы поправка была ВЕРХНЕЙ оценкой, а
        /// не подгонкой. Уменьшение до 0.3 порядка вещей не меняет: у мелкого
        /// кристалла поправка всё равно больше, чем у крупного.
        /// </summary>
        public double ElectronDetour = 1.0;

        /// <summary>
        /// Сколько энергии событие может потерять и всё-таки остаться в пике,
        /// кэВ. Пик имеет ширину, и утечка в единицы кэВ из него не выводит.
        /// Ноль (умолчание) — прежний строгий счёт: в пик идёт только история,
        /// из которой не вылетело ничего.
        /// </summary>
        public double PeakHalfWidthKev;

        /// <summary>
        /// Учитывать вылет характеристического рентгена. Выше K-края квант
        /// выбивает электрон именно с K-оболочки, атом отвечает квантом Kα или
        /// Kβ, и тот может уйти наружу — событие покидает пик полного
        /// поглощения и садится в escape-пик.
        ///
        /// Эффект узкий по шкале, но крупный: у иодида цезия он включается
        /// скачком на 33.2 кэВ (край иода) и на 36.0 (край цезия), на 40 кэВ
        /// снимает четверть событий, к 200 кэВ сходит на нет. Ровно там стоят
        /// опорные линии калибровки — 59.5 америция, 81 бария, 122 кобальта.
        ///
        /// Считается только K: L-рентген тяжёлых сцинтилляторов — это 4-5 кэВ,
        /// его пробег десятки микрон, и наружу он не выходит ниоткуда, кроме
        /// самой поверхности.
        /// </summary>
        public bool XrayEscape = true;

        /// <summary>
        /// Не считать когерентное рассеяние потерей на пути к кристаллу.
        ///
        /// Рэлеевское рассеяние энергию не меняет: квант после него даёт тот же
        /// отсчёт в пике полного поглощения, если попал в кристалл. А попадает
        /// он почти наверняка, когда рассеялся в окне или оболочке — они в
        /// миллиметрах от кристалла и видны из точки рассеяния под большим
        /// углом. Убивать такой квант, как делает формула узкого пучка, —
        /// ошибка известного знака: она занижает эффективность, и тем сильнее,
        /// чем ниже энергия и толще окно.
        ///
        /// Что осталось за скобками: малоугловой комптон (тоже не сразу выводит
        /// из пика) и то, что для ДАЛЬНЕГО рассеивателя поправка завышает — там
        /// рассеянное в кристалл уже не возвращается. Второе мало: доля
        /// когерентного в воде падает с 13 % на 28 кэВ до 1 % на 200.
        /// </summary>
        public bool CoherentPassesThrough = true;

        /// <summary>
        /// Брать долю K-оболочки в фотопоглощении ПО ЭНЕРГИИ из пооболочечных
        /// сечений EPICS2017 (<see cref="MaterialDatabase.PhotoShellOf"/>), а
        /// не константой со скачка сечения на K-крае.
        ///
        /// Константа — это значение ровно НА крае, а доля с энергией растёт:
        /// у иода 0.834 на 33.2 кэВ, 0.842 на 40, 0.858 на 90. Константа
        /// занижала вылет рентгена на 1–3 % вероятности всюду выше края —
        /// ровно тот остаток «+7 % на 40 кэВ», что записан в
        /// database/scheme.md §9а A-2. Ключ измерительный: выключенный, он
        /// возвращает прежнее поведение до последнего бита.
        /// </summary>
        public bool KFractionByEnergy = true;

        /// <summary>
        /// Разыгрывать ОДНО комптоновское рассеяние на пути к кристаллу.
        ///
        /// Формула узкого пучка `exp(-tau)` считает потерянным всё, что
        /// провзаимодействовало. Для когерентного это чинится вычетом канала
        /// (<see cref="CoherentPassesThrough"/>), но комптон на малый угол тоже
        /// из пика не выводит: при 60 кэВ рассеяние на 10° отнимает 0.2 %
        /// энергии. А главное — рассеиватель в миллиметрах от кристалла (окно,
        /// оболочка, стенка стакана) виден из точки рассеяния под большим углом,
        /// и рассеянное вперёд туда и приходит.
        ///
        /// С 06.08.2026 рассеяние разыгрывается и у квантов, чей луч прошёл
        /// МИМО кристалла: на упоре таких большинство, и без них полная
        /// эффективность (сумма отклика) занижалась на ~15 % — это вскрыла
        /// сверка каскадного суммирования с новой TCCFCALC и Geant4-арбитром
        /// (tools/tccfcalc2/README.md, §8). В пик такие истории попадают
        /// только при ненулевом допуске, как и прежде; выигрывает канал
        /// континуума и всё, что на нём стоит (ε_полная, CF).
        ///
        /// Считается ОДНО рассеяние: после него квант ведётся до кристалла уже
        /// поглощающей проводкой. Второе и дальше отброшены сознательно — их
        /// вклад меньше и знак у него тот же, так что поправка остаётся НИЖНЕЙ
        /// оценкой, а не подгонкой.
        /// </summary>
        public bool SingleScatter = true;

        readonly GeometryModel geometry;
        readonly List<Region> regions = new List<Region>();
        Region crystal;
        double sphereZ, sphereR;         // объемлющая сфера детектора — для сужения конуса
        Sampler source;
        ulong state;
        bool crystalHasPartials;
        ElectronData.Material electron;

        /// <summary>Элементы кристалла, у которых есть данные о K-флуоресценции.</summary>
        int[] fluoZ;
        double[] fluoFraction;
        MaterialDatabase.Fluorescence[] fluoData;

        /// <summary>
        /// Пооболочечный фотоэффект тех же элементов; null в ячейке — данных
        /// EPICS для элемента нет, доля K берётся константой, как раньше.
        /// </summary>
        MaterialDatabase.PhotoShellModel[] fluoShells;

        public EfficiencySimulator(GeometryModel model)
        {
            // Сцена строится в САНТИМЕТРАХ, а модель хранит миллиметры. Пересчёт
            // здесь, один раз и на входе: весь расчёт стоит на массовых
            // коэффициентах ослабления в см²/г и плотностях в г/см³, и путать
            // единицы длины внутри нельзя — пробег в миллиметрах при сечении на
            // сантиметр даёт кристалл, прозрачный вдесятеро.
            this.geometry = model == null ? null : model.InCentimeters();
        }

        /// <summary>
        /// Сцена собирается лениво: настройки (например, где стоит оправа)
        /// выставляются после конструктора, а на них она и опирается.
        /// </summary>
        void EnsureBuilt()
        {
            if (this.regions.Count > 0)
            {
                return;
            }

            this.Build();
            this.crystalHasPartials = this.CrystalHasPartials();
            this.electron = ElectronData.Match(this.geometry.Crystal);
            this.BuildFluorescence();
        }

        /// <summary>
        /// Собрать список элементов кристалла, у которых есть K-флуоресценция.
        /// Лёгких (кислород, натрий, кремний) в нём нет и быть не должно:
        /// K-край у них ниже сетки сечений, а рентген в килоэлектронвольт
        /// поглощается в микронах от места рождения.
        /// </summary>
        void BuildFluorescence()
        {
            List<int> zs = new List<int>();
            List<double> fractions = new List<double>();
            List<MaterialDatabase.Fluorescence> data = new List<MaterialDatabase.Fluorescence>();
            List<MaterialDatabase.PhotoShellModel> shells = new List<MaterialDatabase.PhotoShellModel>();
            foreach (KeyValuePair<int, double> pair in this.geometry.Crystal.Fractions)
            {
                if (!(pair.Value > 0.0))
                {
                    continue;
                }

                MaterialDatabase.Fluorescence f = MaterialDatabase.FluorescenceOf(pair.Key);
                if (f == null)
                {
                    continue;
                }

                zs.Add(pair.Key);
                fractions.Add(pair.Value);
                data.Add(f);
                shells.Add(this.KFractionByEnergy
                    ? MaterialDatabase.PhotoShellOf(pair.Key)
                    : null);
            }

            this.fluoZ = zs.ToArray();
            this.fluoFraction = fractions.ToArray();
            this.fluoData = data.ToArray();
            this.fluoShells = shells.ToArray();
        }

        /// <summary>
        /// Название материала кристалла в таблице ESTAR, или пустая строка, если
        /// состава там нет: тогда судьба электрона не считается вовсе.
        /// </summary>
        public string ElectronMaterialName
        {
            get
            {
                this.EnsureBuilt();
                return this.electron == null ? "" : this.electron.Name;
            }
        }

        /// <summary>Считается ли кристалл по парциальным сечениям, а не приближением.</summary>
        public bool UsesPartialCrossSections
        {
            get
            {
                this.EnsureBuilt();
                return this.crystalHasPartials;
            }
        }

        // ------------------------------------------------------------------
        // Сцена
        // ------------------------------------------------------------------

        /// <summary>
        /// Область сцены: либо коаксиальное кольцо (RIn..ROut), либо
        /// прямоугольный брус (|x| &lt;= AX, |y| &lt;= AY). Области вкладываются
        /// друг в друга, и поиск идёт по порядку: побеждает первая, в которую
        /// точка попала, поэтому кристалл кладётся раньше своей обвязки.
        /// </summary>
        sealed class Region
        {
            public bool IsBox;
            public double RIn, ROut;      // кольцо
            public double AX, AY;         // полуразмеры бруса
            public double ZMin, ZMax;
            public GeometryMaterial Material;
            public bool IsCrystal;

            public bool Contains(double x, double y, double z)
            {
                if (z < this.ZMin - Eps || z >= this.ZMax - Eps)
                {
                    return false;
                }

                if (this.IsBox)
                {
                    return Math.Abs(x) < this.AX - Eps && Math.Abs(y) < this.AY - Eps;
                }

                double r = Math.Sqrt(x * x + y * y);
                return r >= this.RIn - Eps && r < this.ROut - Eps;
            }
        }

        void Add(double rIn, double rOut, double zMin, double zMax,
                 GeometryMaterial material, bool isCrystal)
        {
            if (!(rOut > rIn + Eps) || !(zMax > zMin + Eps) || material == null
                || !(material.Density > 0.0))
            {
                return;
            }

            this.Register(new Region
            {
                RIn = rIn,
                ROut = rOut,
                ZMin = zMin,
                ZMax = zMax,
                Material = material,
                IsCrystal = isCrystal,
            }, isCrystal);
        }

        /// <summary>Брус с полуразмерами ax, ay. Вкладывается: порядок значим.</summary>
        void AddBox(double ax, double ay, double zMin, double zMax,
                    GeometryMaterial material, bool isCrystal)
        {
            if (!(ax > Eps) || !(ay > Eps) || !(zMax > zMin + Eps) || material == null
                || !(material.Density > 0.0))
            {
                return;
            }

            this.Register(new Region
            {
                IsBox = true,
                AX = ax,
                AY = ay,
                ZMin = zMin,
                ZMax = zMax,
                Material = material,
                IsCrystal = isCrystal,
            }, isCrystal);
        }

        void Register(Region region, bool isCrystal)
        {
            this.regions.Add(region);
            if (isCrystal)
            {
                this.crystal = region;
            }
        }

        /// <summary>
        /// Вещество слоя или ПУСТОТА, если его в файле нет.
        ///
        /// Пустота, а не «ничего»: области сцены вложены и ищутся по порядку, и
        /// пропущенный слой не исчезает, а замещается слоем СНАРУЖИ. Забыли
        /// плотность отражателя — и на его месте оказывается алюминий корпуса,
        /// который тяжелее; расчёт доводится до конца и выдаёт чужую кривую.
        /// Поэтому слой всё равно занимает своё место, но не поглощает.
        /// О самой пропаже говорит GeometryModel.Warnings.
        /// </summary>
        static GeometryMaterial OrVacuum(GeometryMaterial material)
        {
            if (material != null && material.Density > 0.0 && material.Fractions.Count > 0)
            {
                return material;
            }

            GeometryMaterial vacuum = new GeometryMaterial { Name = "vacuum", Density = 1e-10 };
            vacuum.Fractions[1] = 1.0;
            return vacuum;
        }

        void Build()
        {
            GeometryModel g = this.geometry;
            GeometryMaterial reflector = OrVacuum(g.Reflector);
            GeometryMaterial cladding = OrVacuum(g.Cladding);
            GeometryMaterial beakerWall = OrVacuum(g.BeakerWall);
            GeometryMaterial sample = OrVacuum(g.Source);
            double rc = 0.5 * g.CrystalDiameter;
            double hc = g.CrystalHeight;
            double tfr = g.FrontReflectorThickness, tsr = g.SideReflectorThickness;
            double tfc = g.FrontCladdingThickness, tsc = g.SideCladdingThickness;
            // Оправа детектора. В файле геометрии это одна толщина без указания,
            // где она стоит; MountingInFront решает, ставить её перед торцом
            // (тогда квант её проходит) или за кристаллом. Ключ введён как
            // измерительный: у прогона без неё остаётся ровный сдвиг вверх.
            double tm = Math.Max(0.0, g.MountingThickness);
            double rDet = rc + tsr + tsc;
            double zFace = -(tfr + tfc) - (this.MountingInFront ? tm : 0.0);

            // Кристалл и его обвязка. Области вкладываются, порядок значим:
            // кристалл кладётся первым, чтобы точка внутри него доставалась ему,
            // а не объемлющему слою.
            double transverse;
            if (g.Shape == CrystalShape.Box)
            {
                double ax = 0.5 * g.CrystalBoxX, ay = 0.5 * g.CrystalBoxY;
                hc = g.CrystalBoxZ;
                this.AddBox(ax, ay, 0.0, hc, g.Crystal, true);
                this.AddBox(ax, ay, -tfr, 0.0, reflector, false);
                this.AddBox(ax + tsr, ay + tsr, -tfr, hc, reflector, false);
                this.AddBox(ax + tsr + tsc, ay + tsr + tsc, -(tfr + tfc), -tfr, cladding, false);
                this.AddBox(ax + tsr + tsc, ay + tsr + tsc, -tfr, hc, cladding, false);
                if (this.MountingInFront && tm > 0.0)
                {
                    this.AddBox(ax + tsr + tsc, ay + tsr + tsc, zFace, -(tfr + tfc), cladding, false);
                }
                else if (tm > 0.0)
                {
                    this.AddBox(ax + tsr + tsc, ay + tsr + tsc, hc, hc + tm, cladding, false);
                }

                double bx = ax + tsr + tsc, by = ay + tsr + tsc;
                transverse = Math.Sqrt(bx * bx + by * by);
            }
            else
            {
                this.Add(0.0, rc, 0.0, hc, g.Crystal, true);
                this.Add(0.0, rc, -tfr, 0.0, reflector, false);
                this.Add(rc, rc + tsr, -tfr, hc, reflector, false);
                this.Add(0.0, rDet, -(tfr + tfc), -tfr, cladding, false);
                this.Add(rc + tsr, rDet, -tfr, hc, cladding, false);
                if (this.MountingInFront && tm > 0.0)
                {
                    this.Add(0.0, rDet, zFace, -(tfr + tfc), cladding, false);
                }
                else if (tm > 0.0)
                {
                    this.Add(0.0, rDet, hc, hc + tm, cladding, false);
                }

                transverse = rDet;
            }

            this.sphereZ = 0.5 * hc;
            this.sphereR = Math.Sqrt(transverse * transverse
                                     + Math.Pow(0.5 * hc + tfr + tfc, 2.0)) + 1e-3;

            switch (g.SourceType)
            {
                case GeometrySourceType.Point:
                    this.source = new PointSampler(zFace - g.PointDistance);
                    break;

                case GeometrySourceType.Box:
                {
                    // Прямоугольная кювета: та же раскладка, что у цилиндра, но
                    // дно прямоугольное. Стороны в модели ПОЛНЫЕ, области
                    // строятся по половинам.
                    double axOut = 0.5 * g.BoxSourceX, ayOut = 0.5 * g.BoxSourceY;
                    double axIn = Math.Max(0.0, axOut - g.BoxSideWallThickness);
                    double ayIn = Math.Max(0.0, ayOut - g.BoxSideWallThickness);
                    double zWallTop = zFace - g.BoxToDetectorDistance;
                    double zWallBottom = zWallTop - g.BoxEndWallThickness;
                    double zSrcTop = zWallBottom;
                    double zSrcBottom = zSrcTop - g.BoxSourceHeight;
                    this.AddBox(axOut, ayOut, zWallBottom, zWallTop, beakerWall, false);
                    this.AddBox(axOut, ayOut, zSrcBottom, zSrcTop, beakerWall, false);
                    this.AddBox(axIn, ayIn, zSrcBottom, zSrcTop, sample, false);
                    this.source = new BoxSampler(axIn, ayIn, zSrcBottom, zSrcTop);
                    break;
                }

                case GeometrySourceType.Cylinder:
                {
                    double rOut = 0.5 * g.BeakerDiameter;
                    double rIn = Math.Max(0.0, rOut - g.BeakerSideWallThickness);
                    double zWallTop = zFace - g.BeakerToDetectorDistance;
                    double zWallBottom = zWallTop - g.BeakerEndWallThickness;
                    double zSrcTop = zWallBottom;
                    double zSrcBottom = zSrcTop - g.SourceHeight;
                    this.Add(0.0, rOut, zWallBottom, zWallTop, beakerWall, false);
                    this.Add(rIn, rOut, zSrcBottom, zSrcTop, beakerWall, false);
                    this.Add(0.0, rIn, zSrcBottom, zSrcTop, sample, false);
                    this.source = new CylinderSampler(rIn, zSrcBottom, zSrcTop);
                    break;
                }

                default:
                {
                    // Стакан Маринелли: проба охватывает детектор. Колодец —
                    // глухое отверстие, детектор входит в него; расстояние до
                    // детектора отмеряется от внутреннего потолка колодца.
                    double rh = 0.5 * g.MarinelliHoleDiameter;
                    double ths = g.MarinelliHoleSideThickness;
                    double the = g.MarinelliHoleEndWallThickness;
                    double rOut = Math.Max(0.5 * g.MarinelliBeakerDiameter, rh + ths + 0.1);
                    double rSrcOut = Math.Max(rh + ths, rOut - g.MarinelliSideThickness);
                    double hs = g.MarinelliSourceHeight;
                    double hh = g.MarinelliHoleHeight;

                    double zCeiling = zFace - g.MarinelliToDetectorDistance;
                    double cap = Math.Max(0.0, hs - hh);        // проба над потолком колодца
                    double zSrc0 = zCeiling - the - cap;

                    this.Add(0.0, rh + ths, zCeiling - the, zCeiling, beakerWall, false);
                    this.Add(rh, rh + ths, zCeiling, zCeiling + hh, beakerWall, false);
                    this.Add(rSrcOut, rOut, zSrc0, zSrc0 + hs, beakerWall, false);
                    this.Add(0.0, rh + ths, zSrc0, zCeiling - the, sample, false);
                    this.Add(rh + ths, rSrcOut, zSrc0, zSrc0 + hs, sample, false);
                    this.source = new MarinelliSampler(rh + ths, rSrcOut, zSrc0, zSrc0 + hs,
                                                       zCeiling - the);
                    break;
                }
            }
        }

        // ------------------------------------------------------------------
        // Розыгрыш точки вылета
        // ------------------------------------------------------------------

        abstract class Sampler
        {
            public abstract void Next(EfficiencySimulator s, out double x, out double y, out double z);
        }

        sealed class PointSampler : Sampler
        {
            readonly double z;

            public PointSampler(double z)
            {
                this.z = z;
            }

            public override void Next(EfficiencySimulator s, out double x, out double y, out double z)
            {
                x = 0.0;
                y = 0.0;
                z = this.z;
            }
        }

        /// <summary>
        /// Точка внутри прямоугольной кюветы. Равномерно по объёму — здесь это
        /// просто три независимых равномерных числа, в отличие от цилиндра, где
        /// радиус приходится брать корнем.
        /// </summary>
        sealed class BoxSampler : Sampler
        {
            readonly double ax, ay, z0, z1;

            public BoxSampler(double ax, double ay, double z0, double z1)
            {
                this.ax = ax;
                this.ay = ay;
                this.z0 = z0;
                this.z1 = z1;
            }

            public override void Next(EfficiencySimulator s, out double x, out double y, out double z)
            {
                x = this.ax * (2.0 * s.Uniform() - 1.0);
                y = this.ay * (2.0 * s.Uniform() - 1.0);
                z = this.z0 + (this.z1 - this.z0) * s.Uniform();
            }
        }

        sealed class CylinderSampler : Sampler
        {
            readonly double r, z0, z1;

            public CylinderSampler(double r, double z0, double z1)
            {
                this.r = r;
                this.z0 = z0;
                this.z1 = z1;
            }

            public override void Next(EfficiencySimulator s, out double x, out double y, out double z)
            {
                // равномерно по объёму: радиус по корню, иначе центр перевешен
                double rr = this.r * Math.Sqrt(s.Uniform());
                double phi = 2.0 * Math.PI * s.Uniform();
                x = rr * Math.Cos(phi);
                y = rr * Math.Sin(phi);
                z = this.z0 + (this.z1 - this.z0) * s.Uniform();
            }
        }

        sealed class MarinelliSampler : Sampler
        {
            readonly double rIn, rOut, z0, z1, zCap;
            readonly double capFraction;

            public MarinelliSampler(double rIn, double rOut, double z0, double z1, double zCap)
            {
                this.rIn = rIn;
                this.rOut = rOut;
                this.z0 = z0;
                this.z1 = z1;
                this.zCap = zCap;
                double annulus = (rOut * rOut - rIn * rIn) * (z1 - z0);
                double cap = rIn * rIn * Math.Max(0.0, zCap - z0);
                this.capFraction = (annulus + cap) > 0.0 ? cap / (annulus + cap) : 0.0;
            }

            public override void Next(EfficiencySimulator s, out double x, out double y, out double z)
            {
                double rr, zz;
                if (s.Uniform() < this.capFraction)
                {
                    rr = this.rIn * Math.Sqrt(s.Uniform());
                    zz = this.z0 + (this.zCap - this.z0) * s.Uniform();
                }
                else
                {
                    double a = this.rIn * this.rIn;
                    double b = this.rOut * this.rOut;
                    rr = Math.Sqrt(a + (b - a) * s.Uniform());
                    zz = this.z0 + (this.z1 - this.z0) * s.Uniform();
                }

                double phi = 2.0 * Math.PI * s.Uniform();
                x = rr * Math.Cos(phi);
                y = rr * Math.Sin(phi);
                z = zz;
            }
        }

        // ------------------------------------------------------------------
        // Сечения
        // ------------------------------------------------------------------

        /// <summary>Полное сечение Клейна — Нишины на электрон, см².</summary>
        public static double KleinNishinaTotal(double energyKev)
        {
            double a = energyKev / ElectronMassKev;
            if (!(a > 0.0))
            {
                return 0.0;
            }

            double t1 = (1.0 + a) / (a * a) * (2.0 * (1.0 + a) / (1.0 + 2.0 * a)
                                               - Math.Log(1.0 + 2.0 * a) / a);
            double t2 = Math.Log(1.0 + 2.0 * a) / (2.0 * a);
            double t3 = (1.0 + 3.0 * a) / ((1.0 + 2.0 * a) * (1.0 + 2.0 * a));
            return 2.0 * Math.PI * ClassicalRadiusCm * ClassicalRadiusCm * (t1 + t2 - t3);
        }

        /// <summary>
        /// Каналы взаимодействия в кристалле, 1/см.
        ///
        /// Если для всех элементов кристалла есть парциальные сечения
        /// (<see cref="PartialCrossSections"/>) — берутся они. Это единственный
        /// правильный путь: канал поглощения в сцинтилляторе есть малая разность
        /// больших чисел, и получать его вычитанием комптона из полного нельзя.
        /// В CsI на 1332 кэВ настоящий фотоэффект — 5.2 % полного ослабления, а
        /// вычитание даёт 7.7 %, в полтора раза больше.
        ///
        /// Когерентное рассеяние во взаимодействия НЕ входит: энергии оно не
        /// оставляет, а направление меняет на малый угол. Считать его
        /// поглощением — ровно та ошибка, ради снятия которой таблица заведена.
        ///
        /// Запасной путь (элемента нет в таблице) — прежнее приближение: комптон
        /// по Клейну — Нишине, остаток делится между фотоэффектом и парами
        /// размазанным порогом. Оно завышает поглощение и оставлено только
        /// чтобы расчёт не падал на неизвестном кристалле.
        /// </summary>
        void CrystalChannels(double energyKev, out double photo, out double compton, out double pair)
        {
            GeometryMaterial m = this.geometry.Crystal;
            if (this.crystalHasPartials)
            {
                photo = 0.0;
                compton = 0.0;
                pair = 0.0;
                foreach (KeyValuePair<int, double> f in m.Fractions)
                {
                    photo += f.Value * PartialCrossSections.MassCrossSection(
                        f.Key, energyKev, PhotonProcess.Photoelectric);
                    compton += f.Value * PartialCrossSections.MassCrossSection(
                        f.Key, energyKev, PhotonProcess.Incoherent);
                    pair += f.Value * PartialCrossSections.MassCrossSection(
                        f.Key, energyKev, PhotonProcess.PairProduction);
                }

                photo *= m.Density;
                compton *= m.Density;
                pair *= m.Density;
                return;
            }

            double total = m.LinearAttenuation(energyKev);
            compton = KleinNishinaTotal(energyKev) * m.ElectronDensity();
            if (compton > total)
            {
                compton = total;
            }

            // Когерентное рассеяние энергии не оставляет: если его выделить,
            // оно уходит из канала поглощения совсем. Сейчас оно неотделимо и
            // молча числится фотопоглощением — а фотопоглощение в середине
            // шкалы само мало, и потому такая добавка искажает ветвление
            // сильнее всего именно там.
            double rest = Math.Max(0.0, total - compton - this.CoherentFractionOfTotal * total);
            double ramp = 0.0;
            if (energyKev > 2.0 * ElectronMassKev)
            {
                ramp = Math.Min(1.0, (energyKev - 2.0 * ElectronMassKev) / (1500.0 - 2.0 * ElectronMassKev));
            }

            pair = ramp * rest;
            photo = rest - pair;
        }

        /// <summary>Есть ли парциальные сечения для всех элементов кристалла.</summary>
        bool CrystalHasPartials()
        {
            if (this.geometry.Crystal.Fractions.Count == 0)
            {
                return false;
            }

            foreach (KeyValuePair<int, double> f in this.geometry.Crystal.Fractions)
            {
                if (f.Value > 0.0 && !PartialCrossSections.HasElement(f.Key))
                {
                    return false;
                }
            }

            return true;
        }

        // ------------------------------------------------------------------
        // Трассировка
        // ------------------------------------------------------------------

        Region At(double x, double y, double z)
        {
            for (int i = 0; i < this.regions.Count; i++)
            {
                if (this.regions[i].Contains(x, y, z))
                {
                    return this.regions[i];
                }
            }

            return null;
        }

        /// <summary>Расстояние до ближайшей границы любой области, вдоль луча.</summary>
        double StepToBoundary(double x, double y, double z, double ux, double uy, double uz)
        {
            double best = double.MaxValue;
            for (int i = 0; i < this.regions.Count; i++)
            {
                Region g = this.regions[i];
                Plane(z, uz, g.ZMin, ref best);
                Plane(z, uz, g.ZMax, ref best);
                if (g.IsBox)
                {
                    Plane(x, ux, g.AX, ref best);
                    Plane(x, ux, -g.AX, ref best);
                    Plane(y, uy, g.AY, ref best);
                    Plane(y, uy, -g.AY, ref best);
                }
                else
                {
                    Cylinder(x, y, ux, uy, g.RIn, ref best);
                    Cylinder(x, y, ux, uy, g.ROut, ref best);
                }
            }

            return best;
        }

        static void Plane(double z, double uz, double plane, ref double best)
        {
            if (Math.Abs(uz) < Eps)
            {
                return;
            }

            double t = (plane - z) / uz;
            if (t > 1e-7 && t < best)
            {
                best = t;
            }
        }

        static void Cylinder(double x, double y, double ux, double uy, double radius, ref double best)
        {
            if (!(radius > 0.0))
            {
                return;
            }

            double a = ux * ux + uy * uy;
            if (a < Eps)
            {
                return;
            }

            double b = 2.0 * (x * ux + y * uy);
            double c = x * x + y * y - radius * radius;
            double disc = b * b - 4.0 * a * c;
            if (disc < 0.0)
            {
                return;
            }

            double sq = Math.Sqrt(disc);
            double t1 = (-b - sq) / (2.0 * a);
            double t2 = (-b + sq) / (2.0 * a);
            if (t1 > 1e-7 && t1 < best)
            {
                best = t1;
            }

            if (t2 > 1e-7 && t2 < best)
            {
                best = t2;
            }
        }

        /// <summary>
        /// Ведёт фотон до кристалла, копя оптическую толщину. Возвращает false,
        /// если кристалл не встретился.
        /// </summary>
        bool ToCrystal(ref double x, ref double y, ref double z,
                       double ux, double uy, double uz, double energyKev, out double tau)
        {
            tau = 0.0;
            double travelled = 0.0;
            double limit = 40.0 * this.sphereR + 200.0;
            for (int guard = 0; guard < 200; guard++)
            {
                Region here = this.At(x, y, z);
                if (here != null && here.IsCrystal)
                {
                    return true;
                }

                double step = this.StepToBoundary(x, y, z, ux, uy, uz);
                if (step >= double.MaxValue || travelled + step > limit)
                {
                    return false;
                }

                if (here != null)
                {
                    tau += (this.CoherentPassesThrough
                            ? here.Material.LinearAttenuationWithoutCoherent(energyKev)
                            : here.Material.LinearAttenuation(energyKev)) * step;
                    if (tau > 60.0)
                    {
                        return false;      // exp(-60) — заведомо ноль
                    }
                }

                double advance = step + 1e-7;
                x += ux * advance;
                y += uy * advance;
                z += uz * advance;
                travelled += advance;
            }

            return false;
        }

        /// <summary>
        /// Вклад историй, рассеявшихся ОДИН раз по дороге к кристаллу.
        ///
        /// Прямой вклад считается весом `exp(-tau)`, где tau — оптическая
        /// толщина по каналам, которые квант убивают. Значит доля
        /// `1 - exp(-tau)` — это те истории, что провзаимодействовали, и они
        /// сейчас просто теряются. Часть из них — комптон, и такой квант никуда
        /// не делся: он летит дальше с другой энергией.
        ///
        /// Точка взаимодействия разыгрывается по той же экспоненте, но
        /// нормированной на условие «взаимодействие произошло»; в этой точке
        /// доля комптона равна mu_неког/mu_убив. Дальше — угол по Клейну —
        /// Нишине, новая энергия и обычная проводка до кристалла.
        ///
        /// Возвращает добавку к счёту (0, если рассеяние не состоялось или
        /// рассеянный квант до кристалла не дошёл либо не поглотился целиком).
        /// </summary>
        double ScatteredScore(double x, double y, double z,
                              double ux, double uy, double uz,
                              double energyKev, double tauKill)
        {
            double weight, scattered, escaped;
            if (!this.ScatteredContribution(x, y, z, ux, uy, uz, energyKev, tauKill,
                                            out weight, out scattered, out escaped))
            {
                return 0.0;
            }

            if (escaped > this.PeakHalfWidthKev)
            {
                return 0.0;
            }

            // Событие попадает в пик РАССЕЯННОЙ энергии, а не исходной: в пик
            // полного поглощения линии оно годится только тогда, когда потеря
            // укладывается в ЕГО ШИРИНУ. Отсюда важное: при нулевом допуске
            // (<see cref="PeakHalfWidthKev"/> = 0) эта поправка не даёт ничего
            // вовсе, и так и должно быть — у детектора с бесконечным
            // разрешением рассеянный квант в пик линии не попадает. Величина
            // поправки зависит от разрешения прибора, а его в модели геометрии
            // нет.
            if (energyKev - scattered > this.PeakHalfWidthKev)
            {
                return 0.0;
            }

            return weight;
        }

        /// <summary>
        /// Тот же однократно рассеявшийся квант, но без отсечек по допуску:
        /// возвращает вес, энергию ПОСЛЕ рассеяния и то, сколько из неё
        /// вылетело. Пиковая ветвь (<see cref="ScatteredScore"/>) навешивает
        /// свои две отсечки поверх, отклик — раскладывает по бинам поглощённой
        /// энергии `scattered - escaped`. Разделение нужно потому, что
        /// «попало в пик» и «сколько поглотилось» — разные вопросы к одной
        /// истории, а розыгрыш у них обязан быть один.
        /// </summary>
        bool ScatteredContribution(double x, double y, double z,
                                   double ux, double uy, double uz,
                                   double energyKev, double tauKill,
                                   out double weight, out double scatteredEnergy,
                                   out double escapedEnergy)
        {
            weight = 0.0;
            scatteredEnergy = 0.0;
            escapedEnergy = 0.0;
            if (!this.SingleScatter || !(tauKill > 1e-6))
            {
                return false;
            }

            double interacted = 1.0 - Math.Exp(-tauKill);
            // точка первого взаимодействия: tau_целевое из усечённой экспоненты
            double tauTarget = -Math.Log(1.0 - this.Uniform() * interacted);

            double px = x, py = y, pz = z;
            double accumulated = 0.0;
            Region here = null;
            double travelled = 0.0;
            double limit = 40.0 * this.sphereR + 200.0;
            for (int guard = 0; guard < 200; guard++)
            {
                here = this.At(px, py, pz);
                if (here != null && here.IsCrystal)
                {
                    return false;             // до кристалла не рассеялся
                }

                double step = this.StepToBoundary(px, py, pz, ux, uy, uz);
                if (step >= double.MaxValue || travelled + step > limit)
                {
                    return false;
                }

                if (here != null)
                {
                    double mu = this.CoherentPassesThrough
                        ? here.Material.LinearAttenuationWithoutCoherent(energyKev)
                        : here.Material.LinearAttenuation(energyKev);
                    if (mu > 0.0 && accumulated + mu * step >= tauTarget)
                    {
                        double advance = (tauTarget - accumulated) / mu;
                        px += ux * advance;
                        py += uy * advance;
                        pz += uz * advance;
                        double incoherent = here.Material.LinearIncoherent(energyKev);
                        double share = incoherent / mu;
                        if (!(share > 0.0))
                        {
                            return false;     // взаимодействие было, но не комптон
                        }

                        double cos = this.ComptonCosine(energyKev);
                        double scattered = energyKev
                            / (1.0 + energyKev / ElectronMassKev * (1.0 - cos));
                        double sx = ux, sy = uy, sz = uz;
                        this.Rotate(ref sx, ref sy, ref sz, cos);

                        double tau2;
                        if (!this.ToCrystal(ref px, ref py, ref pz, sx, sy, sz, scattered, out tau2))
                        {
                            return false;
                        }

                        weight = interacted * share * Math.Exp(-tau2);
                        scatteredEnergy = scattered;
                        escapedEnergy = this.InCrystal(px, py, pz, sx, sy, sz, scattered, 0);
                        return true;
                    }

                    accumulated += mu * step;
                }

                double next = step + 1e-7;
                px += ux * next;
                py += uy * next;
                pz += uz * next;
                travelled += next;
            }

            return false;
        }

        /// <summary>
        /// Оптическая толщина убивающих каналов вдоль луча ДО ВЫХОДА из сцены.
        /// Нужна лучам, прошедшим мимо кристалла: у них нет tau «до кристалла»,
        /// а рассеяться в кристалл они могут из любого слоя по дороге.
        /// </summary>
        double KillDepthToExit(double x, double y, double z,
                               double ux, double uy, double uz, double energyKev)
        {
            double tau = 0.0;
            double travelled = 0.0;
            double limit = 40.0 * this.sphereR + 200.0;
            for (int guard = 0; guard < 200; guard++)
            {
                Region here = this.At(x, y, z);
                double step = this.StepToBoundary(x, y, z, ux, uy, uz);
                if (step >= double.MaxValue || travelled + step > limit)
                {
                    return tau;
                }

                if (here != null)
                {
                    tau += (this.CoherentPassesThrough
                            ? here.Material.LinearAttenuationWithoutCoherent(energyKev)
                            : here.Material.LinearAttenuation(energyKev)) * step;
                    if (tau > 60.0)
                    {
                        return 60.0;
                    }
                }

                double advance = step + 1e-7;
                x += ux * advance;
                y += uy * advance;
                z += uz * advance;
                travelled += advance;
            }

            return tau;
        }

        /// <summary>Длина пути внутри кристалла от точки в направлении.</summary>
        double CrystalPath(double x, double y, double z, double ux, double uy, double uz)
        {
            Region c = this.crystal;
            double best = double.MaxValue;
            Plane(z, uz, c.ZMin, ref best);
            Plane(z, uz, c.ZMax, ref best);
            if (c.IsBox)
            {
                Plane(x, ux, c.AX, ref best);
                Plane(x, ux, -c.AX, ref best);
                Plane(y, uy, c.AY, ref best);
                Plane(y, uy, -c.AY, ref best);
            }
            else
            {
                Cylinder(x, y, ux, uy, c.ROut, ref best);
            }

            return best >= double.MaxValue ? 0.0 : best;
        }

        // ------------------------------------------------------------------
        // Розыгрыш в кристалле
        // ------------------------------------------------------------------

        /// <summary>
        /// Ведёт фотон внутри кристалла. Возвращает энергию, ВЫЛЕТЕВШУЮ наружу
        /// (0 — всё поглотилось, история попадает в пик полного поглощения).
        /// </summary>
        double InCrystal(double x, double y, double z, double ux, double uy, double uz,
                         double energyKev, int depth)
        {
            if (depth > 12)
            {
                return energyKev;
            }

            // lost копит энергию, ушедшую из кристалла на предыдущих шагах: это
            // тормозные кванты и сами электроны от уже отработанных
            // взаимодействий. Всё, что возвращается, — сумма таких потерь.
            double lost = 0.0;
            double e = energyKev;
            for (int step = 0; step < 200; step++)
            {
                double photo, compton, pair;
                this.CrystalChannels(e, out photo, out compton, out pair);
                double total = photo + compton + pair;
                if (!(total > 0.0))
                {
                    return lost + e;
                }

                double path = this.CrystalPath(x, y, z, ux, uy, uz);
                double free = -Math.Log(1.0 - this.Uniform()) / total;
                if (free >= path)
                {
                    return lost + e;                // вылетел
                }

                x += ux * free;
                y += uy * free;
                z += uz * free;

                double pick = this.Uniform() * total;
                if (pick < photo)
                {
                    // Энергия делится надвое: характеристический квант, если
                    // атом ответил им, и всё остальное — на электроны (сам
                    // фотоэлектрон, оже-каскад, мягкие линии). Сумма всегда
                    // равна энергии поглощённого кванта, ничего не теряется и
                    // не появляется.
                    double xray = this.SampleFluorescence(e);
                    if (xray > 0.0)
                    {
                        double kx, ky, kz;
                        this.Isotropic(out kx, out ky, out kz);
                        // Порядок вызовов ТОТ ЖЕ, что был до раскладки по
                        // каналам: сначала электрон, потом рентгеновский квант.
                        // Оба тянут случайные числа, и перестановка уводит
                        // поток — матрица выходит другой, а кривая
                        // эффективности перестаёт быть побитово прежней.
                        double electron = this.ElectronLoss(x, y, z, e - xray, depth);
                        double gone = this.InCrystal(x, y, z, kx, ky, kz, xray, depth + 1);
                        this.lossXray += gone;
                        return lost + electron + gone;
                    }

                    // фотоэлектрон уносит почти всю энергию кванта
                    return lost + this.ElectronLoss(x, y, z, e, depth);
                }

                if (pick < photo + compton)
                {
                    double cos = this.ComptonCosine(e);
                    double scattered = e / (1.0 + e / ElectronMassKev * (1.0 - cos));
                    this.Rotate(ref ux, ref uy, ref uz, cos);
                    lost += this.ElectronLoss(x, y, z, e - scattered, depth);
                    e = scattered;
                    if (e < 1.0)
                    {
                        return lost;                // остаток осел на месте
                    }

                    continue;
                }

                // рождение пары: 1022 кэВ уходит в два кванта аннигиляции,
                // остальное достаётся паре электрон-позитрон. Кванты летят
                // СТРОГО в противоположные стороны (импульс покоящейся пары
                // нулевой): разыгранные независимо, они завышали бы совпадение
                // «оба поглотились»/«оба вылетели» и портили соотношение
                // одиночного и двойного вылета.
                double escaped = lost + this.ElectronLoss(x, y, z, e - 2.0 * ElectronMassKev, depth);
                double ax, ay, az;
                this.Isotropic(out ax, out ay, out az);
                double first = this.InCrystal(x, y, z, ax, ay, az, ElectronMassKev, depth + 1);
                double second = this.InCrystal(x, y, z, -ax, -ay, -az, ElectronMassKev, depth + 1);
                this.lossAnnihilation += first + second;
                return escaped + first + second;
            }

            return lost + e;
        }

        /// <summary>
        /// Сколько энергии уходит из кристалла вместе с электроном кинетической
        /// энергии <paramref name="te"/>, рождённым в точке (x, y, z).
        ///
        /// Две независимые статьи расхода:
        ///
        /// 1. Тормозное излучение. Полная излучённая энергия задана выходом из
        ///    ESTAR: &lt;E_rad&gt; = Y(Te)·Te. Спектр берётся толстомишенный,
        ///    dN/dk = C/k на [k_min, Te]; из условия на среднюю энергию
        ///    C = Y·Te/(Te - k_min), число квантов = C·ln(Te/k_min). Каждый
        ///    разыгранный квант ведётся дальше обычной трассировкой и может
        ///    вылететь, а может поглотиться.
        ///
        /// 2. Вылет самого электрона. Направление изотропно, до границы идём по
        ///    прямой; если пробега CSDA хватает, чтобы её достать, наружу
        ///    уносится энергия, отвечающая ОСТАТКУ пробега. Пробег CSDA уже
        ///    включает радиационные потери, поэтому статьи не вычитаются друг из
        ///    друга — это разные вопросы к одной величине.
        ///
        /// Что здесь приближение: направление вылета электрона на самом деле
        /// вперёд по кванту, а не изотропно; путь не прямая (см.
        /// <see cref="ElectronDetour"/>); тормозной квант испускается в точке
        /// рождения электрона, а не вдоль его пути.
        /// </summary>
        double ElectronLoss(double x, double y, double z, double te, int depth)
        {
            if (this.electron == null || !(te > 1.0) || depth > 12)
            {
                return 0.0;
            }

            double lost = 0.0;

            if (this.Bremsstrahlung)
            {
                const double MinKev = 5.0;      // ниже кванту не выйти ниоткуда
                if (te > MinKev)
                {
                    double c = ElectronData.YieldOf(this.electron, te) * te / (te - MinKev);
                    int n = this.Poisson(c * Math.Log(te / MinKev));
                    for (int i = 0; i < n; i++)
                    {
                        double k = MinKev * Math.Pow(te / MinKev, this.Uniform());
                        double ax, ay, az;
                        this.Isotropic(out ax, out ay, out az);
                        lost += this.InCrystal(x, y, z, ax, ay, az, k, depth + 1);
                    }
                }
            }

            if (this.ElectronEscape)
            {
                double density = this.geometry.Crystal.Density;
                double range = ElectronData.RangeOf(this.electron, te) / density;   // см
                double ax, ay, az;
                this.Isotropic(out ax, out ay, out az);
                double toEdge = this.CrystalPath(x, y, z, ax, ay, az);
                double used = toEdge / Math.Max(1e-6, this.ElectronDetour);
                if (used < range)
                {
                    lost += ElectronData.EnergyOfRange(this.electron, (range - used) * density);
                }
            }

            return lost;
        }

        /// <summary>
        /// Разыграть характеристический квант при фотопоглощении кванта энергии
        /// <paramref name="energyKev"/>. Ноль — атом ответил оже-электроном,
        /// поглощение на другой оболочке или элементе без данных.
        ///
        /// Розыгрыш в три шага, и первый — самый важный: сначала выбирается,
        /// НА КАКОМ элементе произошло поглощение, с весом w·σ_фото(E). Брать
        /// просто массовую долю нельзя — у иодида цезия доли почти равны, а
        /// края разнесены на 2.8 кэВ, и между ними поглощает только цезий.
        /// Дальше — попала ли дырка в K-оболочку (доля из скачка сечения на
        /// крае) и ответил ли атом квантом (выход флуоресценции).
        /// </summary>
        double SampleFluorescence(double energyKev)
        {
            if (!this.XrayEscape || this.fluoZ == null || this.fluoZ.Length == 0)
            {
                return 0.0;
            }

            // вес элемента — его вклад в фотопоглощение на этой энергии
            double sum = 0.0;
            double[] weight = new double[this.fluoZ.Length];
            for (int i = 0; i < this.fluoZ.Length; i++)
            {
                if (energyKev <= this.fluoData[i].KEdgeKev)
                {
                    continue;               // K-оболочка ещё недоступна
                }

                weight[i] = this.fluoFraction[i] * PartialCrossSections.MassCrossSection(
                    this.fluoZ[i], energyKev, PhotonProcess.Photoelectric);
                sum += weight[i];
            }

            if (!(sum > 0.0))
            {
                return 0.0;
            }

            // Знаменатель — полное фотопоглощение вещества, включая элементы без
            // K-края на этой энергии: они тоже поглощают, и их доля обязана
            // уменьшать вероятность рентгена, а не выпадать из счёта.
            double all = 0.0;
            foreach (KeyValuePair<int, double> pair in this.geometry.Crystal.Fractions)
            {
                all += pair.Value * PartialCrossSections.MassCrossSection(
                    pair.Key, energyKev, PhotonProcess.Photoelectric);
            }

            if (!(all > 0.0) || this.Uniform() * all >= sum)
            {
                return 0.0;
            }

            double pick = this.Uniform() * sum;
            int k = 0;
            while (k < weight.Length - 1 && pick >= weight[k])
            {
                pick -= weight[k];
                k++;
            }

            MaterialDatabase.Fluorescence f = this.fluoData[k];

            // Доля K-оболочки: по энергии из EPICS2017, если данные есть;
            // иначе — константа со скачка на крае, как раньше. Число случайных
            // чисел от выбора не меняется — меняется только порог сравнения.
            double kFraction = this.fluoShells[k] != null
                ? this.fluoShells[k].KFraction(energyKev)
                : f.KFraction;
            if (this.Uniform() >= kFraction * f.OmegaK)
            {
                return 0.0;                 // не K-оболочка или оже-электрон
            }

            double line = this.Uniform();
            double acc = 0.0;
            for (int i = 0; i < f.LineWeight.Length; i++)
            {
                acc += f.LineWeight[i];
                if (line < acc)
                {
                    return f.LineKev[i];
                }
            }

            return f.LineKev[f.LineKev.Length - 1];
        }

        /// <summary>Пуассон по Кнуту: среднее у нас всегда меньше единицы.</summary>
        int Poisson(double mean)
        {
            if (!(mean > 0.0))
            {
                return 0;
            }

            if (mean > 20.0)
            {
                mean = 20.0;
            }

            double limit = Math.Exp(-mean), p = 1.0;
            int k = 0;
            while (k < 64)
            {
                p *= this.Uniform();
                if (p <= limit)
                {
                    break;
                }

                k++;
            }

            return k;
        }

        /// <summary>Косинус угла комптоновского рассеяния, метод Кана.</summary>
        double ComptonCosine(double energyKev)
        {
            double a = energyKev / ElectronMassKev;
            double a1 = 1.0 + 2.0 * a;
            for (int guard = 0; guard < 1000; guard++)
            {
                double r1 = this.Uniform(), r2 = this.Uniform(), r3 = this.Uniform();
                double ratio;
                if (r1 <= (1.0 + 2.0 * a) / (9.0 + 2.0 * a))
                {
                    ratio = 1.0 + 2.0 * a * r2;
                    if (r3 <= 4.0 * (1.0 / ratio - 1.0 / (ratio * ratio)))
                    {
                        return 1.0 - (ratio - 1.0) / a;
                    }
                }
                else
                {
                    ratio = a1 / (1.0 + 2.0 * a * r2);
                    double cos = 1.0 - (ratio - 1.0) / a;
                    if (r3 <= 0.5 * (cos * cos + 1.0 / ratio))
                    {
                        return cos;
                    }
                }
            }

            return 1.0;
        }

        void Rotate(ref double ux, ref double uy, ref double uz, double cos)
        {
            if (cos > 1.0) cos = 1.0;
            if (cos < -1.0) cos = -1.0;
            double sin = Math.Sqrt(Math.Max(0.0, 1.0 - cos * cos));
            double phi = 2.0 * Math.PI * this.Uniform();
            double cp = Math.Cos(phi), sp = Math.Sin(phi);

            double perp = Math.Sqrt(ux * ux + uy * uy);
            double nx, ny, nz;
            if (perp < 1e-8)
            {
                nx = sin * cp;
                ny = sin * sp;
                nz = cos * (uz >= 0.0 ? 1.0 : -1.0);
            }
            else
            {
                nx = ux * cos + sin * (ux * uz * cp - uy * sp) / perp;
                ny = uy * cos + sin * (uy * uz * cp + ux * sp) / perp;
                nz = uz * cos - sin * perp * cp;
            }

            double norm = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            ux = nx / norm;
            uy = ny / norm;
            uz = nz / norm;
        }

        void Isotropic(out double ux, out double uy, out double uz)
        {
            double cos = 2.0 * this.Uniform() - 1.0;
            double sin = Math.Sqrt(Math.Max(0.0, 1.0 - cos * cos));
            double phi = 2.0 * Math.PI * this.Uniform();
            ux = sin * Math.Cos(phi);
            uy = sin * Math.Sin(phi);
            uz = cos;
        }

        // ------------------------------------------------------------------
        // Кривая
        // ------------------------------------------------------------------

        /// <summary>Эффективность в пике полного поглощения и её погрешность, доля.</summary>
        public double Efficiency(double energyKev, out double relativeError)
        {
            return this.Run(energyKev, null, 0.0, out relativeError);
        }

        /// <summary>
        /// ПОЛНАЯ эффективность: вероятность кванту оставить в кристалле хоть
        /// что-нибудь. Нужна каскадному суммированию (F1): вынос из пика
        /// определяется тем, что квант-партнёр ЗАДЕЛ кристалл, а не тем, что
        /// он поглотился целиком.
        ///
        /// Счёт аналоговый и отдельный от пиковой ветки: квант ведётся по всем
        /// областям с настоящими взаимодействиями — сколько угодно комптонов
        /// подряд, в пробе, стенках и оправе, пока не поглотится, не заденет
        /// кристалл или не уйдёт из сцены. Взвешенная проводка пиковой ветки
        /// (exp(−τ) плюс ОДНО рассеяние) полную эффективность занижает: на
        /// упоре сверка с Geant4 давала −12…−15 % — многократное рассеяние и
        /// возврат из-за кристалла там не мелочь (tools/tccfcalc2/README.md §8).
        ///
        /// Когерентное рассеяние считается прозрачным (пролёт без отклонения):
        /// пробег берётся по ослаблению БЕЗ когерентного, как и в проводке.
        /// </summary>
        public double TotalEfficiency(double energyKev, out double relativeError)
        {
            this.EnsureBuilt();
            double sum = 0.0, sum2 = 0.0;
            int n = Math.Max(1000, this.Histories);
            double limit = 40.0 * this.sphereR + 200.0;
            for (int i = 0; i < n; i++)
            {
                double x, y, z;
                this.source.Next(this, out x, out y, out z);
                double dz = this.sphereZ - z;
                double dist = Math.Sqrt(x * x + y * y + dz * dz);
                double weight = 1.0;
                double ux, uy, uz;
                if (dist > this.sphereR)
                {
                    double cosMax = Math.Sqrt(Math.Max(0.0, 1.0 - this.sphereR * this.sphereR / (dist * dist)));
                    weight = 0.5 * (1.0 - cosMax);
                    this.InCone(-x / dist, -y / dist, dz / dist, cosMax, out ux, out uy, out uz);
                }
                else
                {
                    this.Isotropic(out ux, out uy, out uz);
                }

                double e = energyKev;
                double travelled = 0.0;
                double score = 0.0;
                for (int guard = 0; guard < 400 && e > 1.0; guard++)
                {
                    Region here = this.At(x, y, z);
                    if (here != null && here.IsCrystal)
                    {
                        // Внутри кристалла: любое взаимодействие из каналов —
                        // отсчёт (когерентное в каналы не входит).
                        double photo, compton, pair;
                        this.CrystalChannels(e, out photo, out compton, out pair);
                        double mu = photo + compton + pair;
                        double path = this.CrystalPath(x, y, z, ux, uy, uz);
                        double free = mu > 0.0 ? -Math.Log(1.0 - this.Uniform()) / mu : double.MaxValue;
                        if (free < path)
                        {
                            score = weight;
                            break;
                        }

                        double advance = path + 1e-7;
                        x += ux * advance;
                        y += uy * advance;
                        z += uz * advance;
                        travelled += advance;
                        continue;
                    }

                    double step = this.StepToBoundary(x, y, z, ux, uy, uz);
                    if (step >= double.MaxValue || travelled + step > limit)
                    {
                        break;              // ушёл из сцены
                    }

                    double muKill = here == null ? 0.0
                        : here.Material.LinearAttenuationWithoutCoherent(e);
                    if (muKill > 0.0)
                    {
                        double free = -Math.Log(1.0 - this.Uniform()) / muKill;
                        if (free < step)
                        {
                            x += ux * free;
                            y += uy * free;
                            z += uz * free;
                            travelled += free;
                            double incoherent = here.Material.LinearIncoherent(e);
                            if (this.Uniform() * muKill >= incoherent)
                            {
                                break;      // фотопоглощение или пары вне кристалла
                            }

                            double cos = this.ComptonCosine(e);
                            e = e / (1.0 + e / ElectronMassKev * (1.0 - cos));
                            this.Rotate(ref ux, ref uy, ref uz, cos);
                            continue;
                        }
                    }

                    double next = step + 1e-7;
                    x += ux * next;
                    y += uy * next;
                    z += uz * next;
                    travelled += next;
                }

                sum += score;
                sum2 += score * score;
            }

            double mean = sum / n;
            double variance = Math.Max(0.0, sum2 / n - mean * mean);
            relativeError = mean > 0.0 ? Math.Sqrt(variance / n) / mean * 100.0 : 0.0;
            return mean;
        }

        /// <summary>
        /// Отклик детектора: распределение ПОГЛОЩЁННОЙ энергии, доля на бин, за
        /// ОДИН прогон историй.
        ///
        /// Длина массива считается ПО ТОМУ ЖЕ ПРАВИЛУ, по которому раскладка
        /// выбирает бин, поэтому последний бин — всегда пик полного поглощения.
        /// Раньше длина бралась как `ceil(E/шаг)+1`, а бин пика — как
        /// `(int)(E/шаг + 0.5)`; у энергии, не кратной шагу, это разные индексы,
        /// и последний бин оставался пустым. Ошибка проявлялась не всегда: при
        /// удачно легших энергиях узлов оба правила давали одно и то же.
        ///
        /// Зачем отдельный метод, а не сканирование порога `PeakHalfWidthKev`:
        /// сканирование даёт то же самое (условие `вылетело ≤ w` — это функция
        /// распределения), но повторяет перенос на каждый бин. При полутора
        /// тысячах бинов это полторы тысячи прогонов вместо одного.
        /// </summary>
        public double[] Response(double energyKev, double binKev, out double relativeError)
        {
            if (!(energyKev > 0.0) || !(binKev > 0.0))
            {
                throw new ArgumentOutOfRangeException("binKev");
            }

            double[] histogram = new double[PeakBin(energyKev, binKev) + 1];
            this.Run(energyKev, histogram, binKev, out relativeError);
            return histogram;
        }

        /// <summary>
        /// Каналы отклика: по какой ПРИЧИНЕ история не попала в пик полного
        /// поглощения. Порядок — номер строки в <see cref="ResponseByChannel"/>.
        /// </summary>
        public enum ResponseChannel
        {
            /// <summary>Полное поглощение: не вылетело ничего.</summary>
            Peak = 0,
            /// <summary>Утечка рассеянного кванта, электрона или тормозного.</summary>
            Compton = 1,
            /// <summary>Ушёл хотя бы один аннигиляционный квант 511 кэВ.</summary>
            Escape511 = 2,
            /// <summary>Ушёл характеристический K-рентген кристалла.</summary>
            EscapeXray = 3
        }

        /// <summary>Сколько каналов у отклика.</summary>
        public const int ResponseChannelCount = 4;

        /// <summary>
        /// Тот же отклик, разложенный по каналам исхода: `[канал][бин]`. Сумма
        /// каналов побитово равна обычному <see cref="Response"/> — история
        /// кладётся ровно в один канал, а розыгрыш от разложения не меняется.
        ///
        /// Канал выбирается НЕ по величине вылетевшей энергии, а по метке,
        /// поставленной в точке события: комптон способен унести ровно 511 кэВ
        /// случайно, и такая история села бы в чужой канал. Метки копятся по
        /// статьям расхода, и берётся та, что унесла больше, — история, где
        /// ушли и рентген, и рассеянный квант, принадлежит тому, чей вклад в
        /// недобор больше.
        /// </summary>
        public double[][] ResponseByChannel(double energyKev, double binKev, out double relativeError)
        {
            if (!(energyKev > 0.0) || !(binKev > 0.0))
            {
                throw new ArgumentOutOfRangeException("binKev");
            }

            int bins = PeakBin(energyKev, binKev) + 1;
            double[][] channels = new double[ResponseChannelCount][];
            for (int c = 0; c < ResponseChannelCount; c++)
            {
                channels[c] = new double[bins];
            }

            this.channelHistograms = channels;
            try
            {
                double[] total = new double[bins];
                this.Run(energyKev, total, binKev, out relativeError);
            }
            finally
            {
                this.channelHistograms = null;
            }

            return channels;
        }

        // Раскладка по каналам включается на время прогона. Поле, а не
        // параметр: раскладка нужна только матрице отклика, а `Run` зовут ещё
        // кривая и сканирование порога, и тащить сквозь них лишний аргумент
        // значило бы менять три подписи ради одного потребителя.
        double[][] channelHistograms;

        // Метки исхода текущей истории, кэВ. Обнуляются перед каждой.
        double lossAnnihilation;
        double lossXray;

        /// <summary>
        /// Канал текущей истории по меткам, набранным в точках событий.
        /// Ничего не вылетело — пик; иначе побеждает статья, унёсшая больше.
        /// </summary>
        ResponseChannel ChannelOf(double escaped)
        {
            if (!(escaped > this.PeakHalfWidthKev))
            {
                return ResponseChannel.Peak;
            }

            double rest = escaped - this.lossAnnihilation - this.lossXray;
            if (this.lossAnnihilation >= this.lossXray && this.lossAnnihilation >= rest)
            {
                return ResponseChannel.Escape511;
            }

            return this.lossXray >= rest ? ResponseChannel.EscapeXray : ResponseChannel.Compton;
        }

        /// <summary>
        /// Номер бина, в который попадает полное поглощение. Тем же правилом
        /// пользуется <see cref="Deposit"/> — иначе пик оказывается не в
        /// последнем бине.
        /// </summary>
        public static int PeakBin(double energyKev, double binKev)
        {
            return (int)(energyKev / binKev + 0.5);
        }

        /// <summary>
        /// Вклад в бин поглощённой энергии. Ноль отбрасывается: история, из
        /// которой вылетело всё, отсчёта не даёт вовсе, и класть её в нулевой
        /// бин значило бы считать несобытие событием.
        /// </summary>
        static void Deposit(double[] histogram, double binKev, double deposited, double weight)
        {
            if (!(deposited > 0.0) || !(weight > 0.0))
            {
                return;
            }

            int bin = (int)(deposited / binKev + 0.5);
            if (bin < 0)
            {
                bin = 0;
            }

            if (bin >= histogram.Length)
            {
                bin = histogram.Length - 1;
            }

            histogram[bin] += weight;
        }

        /// <summary>
        /// Общий цикл историй. `histogram == null` — считается только пик, и это
        /// в точности прежнее поведение; иначе та же история дополнительно
        /// раскладывается по бинам поглощённой энергии.
        /// </summary>
        double Run(double energyKev, double[] histogram, double binKev, out double relativeError)
        {
            this.EnsureBuilt();
            double sum = 0.0, sum2 = 0.0;
            int n = Math.Max(1000, this.Histories);
            for (int i = 0; i < n; i++)
            {
                double x, y, z;
                this.source.Next(this, out x, out y, out z);

                // Направление разыгрывается не по всей сфере, а в конусе,
                // накрывающем детектор: иначе на дальней геометрии почти все
                // истории уходят мимо и статистика набирается впустую.
                double dz = this.sphereZ - z;
                double dist = Math.Sqrt(x * x + y * y + dz * dz);
                double weight = 1.0;
                double ux, uy, uz;
                if (dist > this.sphereR)
                {
                    double cosMax = Math.Sqrt(Math.Max(0.0, 1.0 - this.sphereR * this.sphereR / (dist * dist)));
                    weight = 0.5 * (1.0 - cosMax);
                    this.InCone(-x / dist, -y / dist, dz / dist, cosMax, out ux, out uy, out uz);
                }
                else
                {
                    this.Isotropic(out ux, out uy, out uz);
                }

                double px = x, py = y, pz = z, tau;
                double score = 0.0;
                bool reached = this.ToCrystal(ref px, ref py, ref pz, ux, uy, uz, energyKev, out tau);
                if (!reached && !this.ScoreEntranceOnly && this.SingleScatter)
                {
                    // Луч прошёл мимо кристалла. Прямого вклада нет, но квант
                    // мог рассеяться в пробе или обвязке и завернуть в
                    // кристалл — на упоре таких лучей большинство, и без этой
                    // ветки полная эффективность занижалась на ~15 %
                    // (сверка CF, tools/tccfcalc2/README.md §8). «Убивающая»
                    // толщина здесь — весь путь луча до выхода из сцены.
                    double tauMiss = this.KillDepthToExit(x, y, z, ux, uy, uz, energyKev);
                    if (histogram == null)
                    {
                        score += weight * this.ScatteredScore(x, y, z, ux, uy, uz, energyKev, tauMiss);
                    }
                    else
                    {
                        this.lossAnnihilation = 0.0;
                        this.lossXray = 0.0;
                        double sw, scattered, sEscaped;
                        if (this.ScatteredContribution(x, y, z, ux, uy, uz, energyKev, tauMiss,
                                                       out sw, out scattered, out sEscaped))
                        {
                            Deposit(histogram, binKev, scattered - sEscaped, weight * sw);
                            if (this.channelHistograms != null)
                            {
                                ResponseChannel channel = this.ChannelOf(sEscaped);
                                if (channel == ResponseChannel.Peak)
                                {
                                    channel = ResponseChannel.Compton;
                                }

                                Deposit(this.channelHistograms[(int)channel],
                                        binKev, scattered - sEscaped, weight * sw);
                            }
                        }
                    }
                }

                if (reached)
                {
                    if (this.ScoreEntranceOnly)
                    {
                        score = weight * Math.Exp(-tau);
                    }
                    else
                    {
                        this.lossAnnihilation = 0.0;
                        this.lossXray = 0.0;
                        double escaped = this.InCrystal(px, py, pz, ux, uy, uz, energyKev, 0);
                        if (escaped <= this.PeakHalfWidthKev)
                        {
                            score = weight * Math.Exp(-tau);
                        }

                        // Отклик берёт ту же историю целиком, а не один бит
                        // «попало в пик»: сколько энергии осталось в кристалле,
                        // уже посчитано, и раскладывание по бинам стоит одного
                        // сложения. Розыгрыш от этого не меняется — гистограмма
                        // не тянет ни одного случайного числа, поэтому кривая
                        // остаётся побитово прежней.
                        if (histogram != null)
                        {
                            double share = weight * Math.Exp(-tau);
                            Deposit(histogram, binKev, energyKev - escaped, share);
                            if (this.channelHistograms != null)
                            {
                                Deposit(this.channelHistograms[(int)this.ChannelOf(escaped)],
                                        binKev, energyKev - escaped, share);
                            }
                        }
                    }

                    // Прямой вклад — это доля exp(-tau), не провзаимодействовавшая
                    // по дороге. Остаток 1 - exp(-tau) сейчас теряется целиком, а
                    // часть его — комптон на малый угол, и такой квант доходит.
                    if (!this.ScoreEntranceOnly)
                    {
                        if (histogram == null)
                        {
                            score += weight * this.ScatteredScore(x, y, z, ux, uy, uz, energyKev, tau);
                        }
                        else
                        {
                            this.lossAnnihilation = 0.0;
                            this.lossXray = 0.0;
                            double sw, scattered, sEscaped;
                            if (this.ScatteredContribution(x, y, z, ux, uy, uz, energyKev, tau,
                                                           out sw, out scattered, out sEscaped))
                            {
                                // Рассеявшийся квант приносит СВОЮ энергию, а не
                                // энергию линии: в отклике он и должен лечь ниже
                                // по шкале, а не в пик.
                                Deposit(histogram, binKev, scattered - sEscaped, weight * sw);
                                if (this.channelHistograms != null)
                                {
                                    // Квант рассеялся ДО кристалла и принёс
                                    // меньше энергии линии — в пик он не попадёт
                                    // при любом исходе внутри. Канал берётся по
                                    // тем же меткам: если внутри ушёл рентген
                                    // или аннигиляционный квант, история
                                    // принадлежит им, иначе это недобор.
                                    ResponseChannel channel = this.ChannelOf(sEscaped);
                                    if (channel == ResponseChannel.Peak)
                                    {
                                        channel = ResponseChannel.Compton;
                                    }

                                    Deposit(this.channelHistograms[(int)channel],
                                            binKev, scattered - sEscaped, weight * sw);
                                }
                            }
                        }
                    }
                }

                sum += score;
                sum2 += score * score;
            }

            double mean = sum / n;
            double variance = Math.Max(0.0, sum2 / n - mean * mean);
            relativeError = mean > 0.0 ? Math.Sqrt(variance / n) / mean * 100.0 : 0.0;

            // Бины копят сумму весов, а величина отклика — среднее по историям,
            // ровно как возвращаемая эффективность. Без этого деления отклик
            // выходит больше единицы и вообще не вероятность.
            if (histogram != null)
            {
                for (int b = 0; b < histogram.Length; b++)
                {
                    histogram[b] /= n;
                }
            }

            if (this.channelHistograms != null)
            {
                foreach (double[] channel in this.channelHistograms)
                {
                    for (int b = 0; b < channel.Length; b++)
                    {
                        channel[b] /= n;
                    }
                }
            }

            return mean;
        }

        void InCone(double ax, double ay, double az, double cosMax,
                    out double ux, out double uy, out double uz)
        {
            double cos = cosMax + (1.0 - cosMax) * this.Uniform();
            ux = ax;
            uy = ay;
            uz = az;
            this.Rotate(ref ux, ref uy, ref uz, cos);
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Начать новый поток случайных чисел с заданного состояния.
        ///
        /// Нужно для счёта точек кривой в несколько потоков: точки считаются
        /// одновременно, и если бы они брали числа из ОДНОЙ последовательности,
        /// результат зависел бы от того, кто успел раньше. Своё состояние на
        /// точку делает её ответом на вопрос «зерно и номер точки», а не «зерно,
        /// номер и порядок выполнения».
        ///
        /// Первые выдачи отбрасываются: xorshift с бедным по битам состоянием
        /// первые несколько шагов выдаёт заметно связанные числа.
        /// </summary>
        public void ResetStream(ulong seed)
        {
            this.state = seed | 1UL;
            for (int i = 0; i < 16; i++)
            {
                this.Uniform();
            }
        }

        double Uniform()
        {
            // xorshift64*: воспроизводимо и без зависимостей
            if (this.state == 0UL)
            {
                this.state = (ulong)this.Seed | 1UL;
            }

            this.state ^= this.state >> 12;
            this.state ^= this.state << 25;
            this.state ^= this.state >> 27;
            ulong r = this.state * 2685821657736338717UL;
            return ((r >> 11) + 0.5) * (1.0 / 9007199254740992.0);
        }

        public string DescribeScene()
        {
            this.EnsureBuilt();
            List<string> parts = new List<string>();
            foreach (Region r in this.regions)
            {
                // У бруса радиусов нет вовсе, и печатать их нулями — врать в
                // журнале прогона, который теперь видит пользователь.
                string shape = r.IsBox
                    ? string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "{0:F2}x{1:F2}", 2.0 * r.AX, 2.0 * r.AY)
                    : string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "r[{0:F2}..{1:F2}]", r.RIn, r.ROut);
                parts.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0} {1} z[{2:F2}..{3:F2}]{4}",
                    r.Material.Name, shape, r.ZMin, r.ZMax, r.IsCrystal ? " *" : ""));
            }

            // Единица названа явно: сцена строится в сантиметрах, а геометрию
            // выше в том же журнале печатают в миллиметрах, и два ряда чисел,
            // отличающихся вдесятеро, без подписи читаются как ошибка.
            return "cm: " + string.Join("; ", parts.ToArray());
        }
    }
}
