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
    /// Что осталось приближением и где это заметно:
    /// * когерентное (рэлеевское) рассеяние отдельно не выделено — таблица даёт
    ///   только полное ослабление. По дороге к кристаллу оно засчитывается как
    ///   поглощение (фотон на самом деле лишь чуть отклоняется), внутри
    ///   кристалла — как фотопоглощение. Обе ошибки малы там, где велики: у
    ///   низких энергий доля фотопоглощения и так близка к единице;
    /// * связь электронов в комптоновском сечении не учтена (чистая
    ///   Клейн — Нишина). Ниже 100 кэВ это завышает комптон, но там правит
    ///   фотопоглощение;
    /// * вылет характеристического рентгена кристалла после фотопоглощения не
    ///   моделируется: у иода это 28-33 кэВ, заметно только у самых тонких
    ///   кристаллов и у самых низких энергий.
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

        readonly GeometryModel geometry;
        readonly List<Region> regions = new List<Region>();
        Region crystal;
        double sphereZ, sphereR;         // объемлющая сфера детектора — для сужения конуса
        Sampler source;
        ulong state;
        bool crystalHasPartials;
        ElectronData.Material electron;

        public EfficiencySimulator(GeometryModel model)
        {
            this.geometry = model;
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

        void Build()
        {
            GeometryModel g = this.geometry;
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
                this.AddBox(ax, ay, -tfr, 0.0, g.Reflector, false);
                this.AddBox(ax + tsr, ay + tsr, -tfr, hc, g.Reflector, false);
                this.AddBox(ax + tsr + tsc, ay + tsr + tsc, -(tfr + tfc), -tfr, g.Cladding, false);
                this.AddBox(ax + tsr + tsc, ay + tsr + tsc, -tfr, hc, g.Cladding, false);
                if (this.MountingInFront && tm > 0.0)
                {
                    this.AddBox(ax + tsr + tsc, ay + tsr + tsc, zFace, -(tfr + tfc), g.Cladding, false);
                }
                else if (tm > 0.0)
                {
                    this.AddBox(ax + tsr + tsc, ay + tsr + tsc, hc, hc + tm, g.Cladding, false);
                }

                double bx = ax + tsr + tsc, by = ay + tsr + tsc;
                transverse = Math.Sqrt(bx * bx + by * by);
            }
            else
            {
                this.Add(0.0, rc, 0.0, hc, g.Crystal, true);
                this.Add(0.0, rc, -tfr, 0.0, g.Reflector, false);
                this.Add(rc, rc + tsr, -tfr, hc, g.Reflector, false);
                this.Add(0.0, rDet, -(tfr + tfc), -tfr, g.Cladding, false);
                this.Add(rc + tsr, rDet, -tfr, hc, g.Cladding, false);
                if (this.MountingInFront && tm > 0.0)
                {
                    this.Add(0.0, rDet, zFace, -(tfr + tfc), g.Cladding, false);
                }
                else if (tm > 0.0)
                {
                    this.Add(0.0, rDet, hc, hc + tm, g.Cladding, false);
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

                case GeometrySourceType.Cylinder:
                {
                    double rOut = 0.5 * g.BeakerDiameter;
                    double rIn = Math.Max(0.0, rOut - g.BeakerSideWallThickness);
                    double zWallTop = zFace - g.BeakerToDetectorDistance;
                    double zWallBottom = zWallTop - g.BeakerEndWallThickness;
                    double zSrcTop = zWallBottom;
                    double zSrcBottom = zSrcTop - g.SourceHeight;
                    this.Add(0.0, rOut, zWallBottom, zWallTop, g.BeakerWall, false);
                    this.Add(rIn, rOut, zSrcBottom, zSrcTop, g.BeakerWall, false);
                    this.Add(0.0, rIn, zSrcBottom, zSrcTop, g.Source, false);
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

                    this.Add(0.0, rh + ths, zCeiling - the, zCeiling, g.BeakerWall, false);
                    this.Add(rh, rh + ths, zCeiling, zCeiling + hh, g.BeakerWall, false);
                    this.Add(rSrcOut, rOut, zSrc0, zSrc0 + hs, g.BeakerWall, false);
                    this.Add(0.0, rh + ths, zSrc0, zCeiling - the, g.Source, false);
                    this.Add(rh + ths, rSrcOut, zSrc0, zSrc0 + hs, g.Source, false);
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
                    tau += here.Material.LinearAttenuation(energyKev) * step;
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
                // остальное достаётся паре электрон-позитрон
                double escaped = lost + this.ElectronLoss(x, y, z, e - 2.0 * ElectronMassKev, depth);
                for (int k = 0; k < 2; k++)
                {
                    double ax, ay, az;
                    this.Isotropic(out ax, out ay, out az);
                    escaped += this.InCrystal(x, y, z, ax, ay, az, ElectronMassKev, depth + 1);
                }

                return escaped;
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
                if (this.ToCrystal(ref px, ref py, ref pz, ux, uy, uz, energyKev, out tau))
                {
                    if (this.ScoreEntranceOnly)
                    {
                        score = weight * Math.Exp(-tau);
                    }
                    else
                    {
                        double escaped = this.InCrystal(px, py, pz, ux, uy, uz, energyKev, 0);
                        if (escaped <= this.PeakHalfWidthKev)
                        {
                            score = weight * Math.Exp(-tau);
                        }
                    }
                }

                sum += score;
                sum2 += score * score;
            }

            double mean = sum / n;
            double variance = Math.Max(0.0, sum2 / n - mean * mean);
            relativeError = mean > 0.0 ? Math.Sqrt(variance / n) / mean * 100.0 : 0.0;
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

            return string.Join("; ", parts.ToArray());
        }
    }
}
