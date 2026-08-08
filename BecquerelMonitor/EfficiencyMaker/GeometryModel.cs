using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Serialization;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// Вещество: массовые доли элементов и плотность. Массовый коэффициент
    /// ослабления смеси — сумма по элементам с массовыми весами (правило
    /// аддитивности Брэгга).
    /// </summary>
    /// <summary>Массовая доля одного элемента — форма записи для XML.</summary>
    public sealed class GeometryElementFraction
    {
        [XmlAttribute]
        public int Z;

        [XmlAttribute]
        public double Fraction;
    }

    public sealed class GeometryMaterial
    {
        public string Name = "";

        public double Density;                       // г/см3

        /// <summary>Z -> массовая доля.</summary>
        [XmlIgnore]
        public readonly Dictionary<int, double> Fractions = new Dictionary<int, double>();

        /// <summary>
        /// Тот же состав списком — потому что вещество теперь ХРАНИТСЯ, а не
        /// разбирается каждый раз из файла `.in`.
        ///
        /// `XmlSerializer` не умеет ни `Dictionary`, ни `readonly`, а состав
        /// восстановить больше не из чего: у вещества нет поля формулы, и
        /// вещество из чужого файла в библиотеке материалов может не значиться.
        /// Поэтому доли пишутся как есть.
        /// </summary>
        [XmlArray("Fractions")]
        [XmlArrayItem("Element")]
        public GeometryElementFraction[] FractionList
        {
            get
            {
                List<GeometryElementFraction> list = new List<GeometryElementFraction>();
                foreach (KeyValuePair<int, double> pair in this.Fractions)
                {
                    list.Add(new GeometryElementFraction { Z = pair.Key, Fraction = pair.Value });
                }

                list.Sort((a, b) => a.Z.CompareTo(b.Z));
                return list.ToArray();
            }
            set
            {
                this.Fractions.Clear();
                if (value == null)
                {
                    return;
                }

                foreach (GeometryElementFraction item in value)
                {
                    if (item != null && item.Z > 0)
                    {
                        this.Fractions[item.Z] = item.Fraction;
                    }
                }
            }
        }

        public GeometryMaterial Clone()
        {
            GeometryMaterial copy = new GeometryMaterial
            {
                Name = this.Name,
                Density = this.Density,
            };

            foreach (KeyValuePair<int, double> pair in this.Fractions)
            {
                copy.Fractions[pair.Key] = pair.Value;
            }

            return copy;
        }

        /// <summary>Линейный коэффициент ослабления, 1/см.</summary>
        public double LinearAttenuation(double energyKev)
        {
            double massAttenuation = 0.0;
            foreach (KeyValuePair<int, double> pair in this.Fractions)
            {
                massAttenuation += pair.Value * AttenuationData.MassAttenuation(pair.Key, energyKev);
            }

            return massAttenuation * this.Density;
        }

        /// <summary>
        /// Ослабление БЕЗ когерентного рассеяния, 1/см. Для вещества, стоящего
        /// на пути кванта к кристаллу.
        ///
        /// Полное ослабление — это формула узкого пучка: она считает только те
        /// кванты, которые ни разу не провзаимодействовали. Для эффективности
        /// в пике так можно ровно тогда, когда взаимодействие выводит квант из
        /// дела. У рэлеевского рассеяния это не так: **энергия не меняется
        /// вовсе**, и если квант после него попал в кристалл, он даст точно
        /// такой же отсчёт в пике полного поглощения. Считать его поглощённым —
        /// прямая ошибка, а не приближение.
        ///
        /// Попадёт ли он в кристалл, решает геометрия: рассеиватель в
        /// миллиметрах от кристалла (окно, оболочка, отражатель) виден из точки
        /// рассеяния под большим углом, и почти всё рассеянное вперёд туда и
        /// приходит. Для дальней пробы это уже не так, и там поправка завышает
        /// — но она мала: доля когерентного в воде падает с 13 % на 28 кэВ до
        /// 1 % на 200.
        ///
        /// Малоугловой комптон из пика тоже выводит не сразу (на 60 кэВ угол
        /// 10° отнимает 0.2 % энергии), но здесь он НЕ учитывается: для этого
        /// нужен розыгрыш рассеяния, а не поправка к коэффициенту.
        ///
        /// Если парциальных сечений для элемента нет, берётся полное
        /// ослабление: занизить пропускание безопаснее, чем угадать вычет.
        /// </summary>
        public double LinearAttenuationWithoutCoherent(double energyKev)
        {
            double massAttenuation = 0.0;
            foreach (KeyValuePair<int, double> pair in this.Fractions)
            {
                double value = AttenuationData.MassAttenuation(pair.Key, energyKev);
                if (PartialCrossSections.HasElement(pair.Key))
                {
                    value -= PartialCrossSections.MassCrossSection(
                        pair.Key, energyKev, PhotonProcess.Coherent);
                }

                massAttenuation += pair.Value * Math.Max(0.0, value);
            }

            return massAttenuation * this.Density;
        }

        /// <summary>
        /// Только некогерентное (комптоновское) рассеяние, 1/см. Нужно, чтобы
        /// разыграть ОДНО рассеяние на пути к кристаллу: квант после него не
        /// потерян, он летит дальше с другой энергией и может дойти.
        ///
        /// Если парциальных сечений для элемента нет, его вклад считается
        /// нулевым: лучше не разыграть рассеяние, чем разыграть выдуманное.
        /// </summary>
        public double LinearIncoherent(double energyKev)
        {
            double massAttenuation = 0.0;
            foreach (KeyValuePair<int, double> pair in this.Fractions)
            {
                if (PartialCrossSections.HasElement(pair.Key))
                {
                    massAttenuation += pair.Value * PartialCrossSections.MassCrossSection(
                        pair.Key, energyKev, PhotonProcess.Incoherent);
                }
            }

            return massAttenuation * this.Density;
        }

        /// <summary>
        /// Только КОГЕРЕНТНОЕ (рэлеевское) рассеяние, 1/см. Нужно, чтобы
        /// разыграть его отдельным каналом: энергия не меняется, направление
        /// меняется на угол по форм-фактору
        /// (<see cref="EfficiencySimulator.RayleighScatter"/>).
        ///
        /// Если парциальных сечений для элемента нет, его вклад нулевой — как
        /// и у <see cref="LinearIncoherent"/>: лучше не разыграть рассеяние,
        /// чем разыграть выдуманное.
        /// </summary>
        public double LinearCoherent(double energyKev)
        {
            double massAttenuation = 0.0;
            foreach (KeyValuePair<int, double> pair in this.Fractions)
            {
                if (PartialCrossSections.HasElement(pair.Key))
                {
                    massAttenuation += pair.Value * PartialCrossSections.MassCrossSection(
                        pair.Key, energyKev, PhotonProcess.Coherent);
                }
            }

            return massAttenuation * this.Density;
        }

        /// <summary>Электронов на см³ — для сечения Клейна — Нишины.</summary>
        public double ElectronDensity()
        {
            const double Avogadro = 6.02214076e23;
            double perGram = 0.0;
            foreach (KeyValuePair<int, double> pair in this.Fractions)
            {
                double mass;
                if (!AttenuationData.AtomicMass.TryGetValue(pair.Key, out mass) || !(mass > 0.0))
                {
                    continue;
                }

                perGram += pair.Value * pair.Key * Avogadro / mass;
            }

            return perGram * this.Density;
        }

        /// <summary>Все ли элементы вещества есть в таблице ослабления.</summary>
        public bool IsKnown(out int missingZ)
        {
            foreach (KeyValuePair<int, double> pair in this.Fractions)
            {
                if (pair.Value > 0.0 && !AttenuationData.HasElement(pair.Key))
                {
                    missingZ = pair.Key;
                    return false;
                }
            }

            missingZ = 0;
            return true;
        }
    }

    public enum GeometrySourceType
    {
        Point,
        Cylinder,
        Marinelli,

        /// <summary>
        /// Прямоугольная кювета — НАШЕ расширение формата. В файлах ЛСРМ такого
        /// источника нет, и их программа файл с ним прочитает как точечный:
        /// `SourceType = BOX` ей неизвестен. Всё, что относится к этой форме,
        /// пишется ключами `SB_*` — те ЛСРМ тоже не читает.
        /// </summary>
        Box
    }

    /// <summary>Форма кристалла.</summary>
    public enum CrystalShape
    {
        Cylinder,
        /// <summary>Прямоугольный параллелепипед: длинная сторона вдоль оси.</summary>
        Box
    }

    /// <summary>
    /// Модель геометрии из файла `.in` конструктора геометрий LSRM
    /// (GeometryMaster). Формат — плоский список `ключ = значение единица`
    /// с комментариями `//`; в файле присутствуют ВСЕ блоки (коаксиальный и
    /// сцинтилляционный детектор, три типа источника), а работает тот, что
    /// назван в DetectorType и SourceType.
    ///
    /// Разбирается сцинтилляционная ветвь: коаксиальные детекторы (HPGe) вне
    /// предмета — там пик разрешается сам, и задача другая.
    /// </summary>
    public sealed class GeometryModel
    {
        /// <summary>
        /// Сколько миллиметров в сантиметре. Все размеры модели — МИЛЛИМЕТРЫ:
        /// так их задаёт производитель детектора и так их набирает человек
        /// (0.13 см отражателя читаются как 1.3 мм без запинки). Плотности
        /// остаются в г/см3 — это единица самих таблиц ослабления, и
        /// пересчитывать её значило бы менять числа NIST.
        ///
        /// Сантиметры остались ровно на двух границах, и обе явные: расчёт
        /// переноса (<see cref="EfficiencySimulator"/>) и формат `.in`
        /// конструктора геометрий LSRM. Обе зовут <see cref="InCentimeters"/>.
        /// </summary>
        public const double MmPerCm = 10.0;

        public string Name = "";

        public bool IsScintillator;

        public GeometrySourceType SourceType;

        // Кристалл, мм
        public double CrystalDiameter;
        public double CrystalHeight;

        /// <summary>
        /// Форма кристалла. Формат `.in` конструктора геометрий LSRM умеет
        /// только цилиндры, и прямоугольные сцинтилляторы там приводят к
        /// цилиндру равного объёма. Это не безобидно: равный объём и даже
        /// равная площадь торца не дают равной СРЕДНЕЙ ХОРДЫ, а именно она
        /// задаёт вероятность взаимодействия при боковом облучении. У ASN16
        /// параллелепипед 1.5x1.8x6.0 имеет хорду 4V/S = 1.440 см против
        /// 1.602 см у равнообъёмного цилиндра — на 10 % тоньше, и в стакане
        /// Маринелли, где кванты идут сбоку, цилиндр завышает эффективность.
        ///
        /// Читается из необязательных ключей DS_CrystalBoxX/Y/Z (наше
        /// расширение формата; в файлах LSRM их нет, и тогда форма
        /// цилиндрическая).
        /// </summary>
        public CrystalShape Shape = CrystalShape.Cylinder;

        public double CrystalBoxX;
        public double CrystalBoxY;
        public double CrystalBoxZ;

        /// <summary>
        /// Разрешение прибора: ПШПВ на 662 кэВ, в процентах. Ноль — не задано.
        ///
        /// Это НЕ параметр геометрии, но без него у поправки на однократное
        /// рассеяние (<c>EfficiencySimulator.SingleScatter</c>) нет допуска:
        /// рассеянный на малый угол квант остаётся в пике линии только тогда,
        /// когда потеря укладывается в ширину пика, а ширина — свойство
        /// прибора. При нуле поправка не даёт ничего, и расчёт занижает низ
        /// шкалы примерно на 10 % на 28 кэВ (сверка с TCCFCALC,
        /// tools/tccfcalc2/old-dll-journal.md, §5.2).
        ///
        /// Читается из необязательного ключа `DS_Fwhm662` (наше расширение
        /// формата `.in`, в процентах; файлы LSRM его не содержат). Ход с
        /// энергией берётся корневым: ПШПВ(E) = ПШПВ(662)·√(E/662) — обычная
        /// статистика света сцинтиллятора; своей ПШПВ-калибровки у геометрии
        /// нет, а для допуска поправки точной формы и не нужно.
        /// </summary>
        public double FwhmAt662Percent;

        /// <summary>
        /// Допуск пика для энергии: половина ПШПВ(E), кэВ. Ноль, если
        /// разрешение не задано, — тогда счёт прежний, строгий.
        /// </summary>
        public double PeakHalfWidthKev(double energyKev)
        {
            if (!(this.FwhmAt662Percent > 0.0) || !(energyKev > 0.0))
            {
                return 0.0;
            }

            // ПШПВ(E) = ПШПВ%(662)/100 · 662 · √(E/662) = %/100 · √(662·E)
            return 0.5 * this.FwhmAt662Percent / 100.0 * Math.Sqrt(662.0 * energyKev);
        }
        public double FrontReflectorThickness;
        public double SideReflectorThickness;
        public double FrontCladdingThickness;
        public double SideCladdingThickness;
        public double MountingThickness;

        // Источник, мм
        public double PointDistance;

        public double BeakerToDetectorDistance;
        public double BeakerDiameter;
        public double BeakerHeight;
        public double BeakerSideWallThickness;
        public double BeakerEndWallThickness;
        public double SourceHeight;

        public double MarinelliBeakerDiameter;
        public double MarinelliBeakerHeight;
        public double MarinelliHoleDiameter;
        public double MarinelliHoleHeight;
        public double MarinelliSideThickness;
        public double MarinelliEndWallThickness;
        public double MarinelliHoleSideThickness;
        public double MarinelliHoleEndWallThickness;
        public double MarinelliSourceHeight;
        public double MarinelliToDetectorDistance;

        // Прямоугольная кювета: то же, что цилиндрическая, но дно не круг, а
        // прямоугольник. Стороны — ПОЛНЫЕ, не половины: так их меряют на
        // приборе. Стенка одной толщины со всех четырёх сторон.
        public double BoxSourceX;
        public double BoxSourceY;
        public double BoxSourceHeight;
        public double BoxToDetectorDistance;
        public double BoxSideWallThickness;
        public double BoxEndWallThickness;

        public GeometryMaterial Crystal = new GeometryMaterial();
        public GeometryMaterial Reflector = new GeometryMaterial();
        public GeometryMaterial Cladding = new GeometryMaterial();
        public GeometryMaterial BeakerWall = new GeometryMaterial();
        public GeometryMaterial Source = new GeometryMaterial();

        /// <summary>
        /// Полная копия. Нужна там, где геометрию РАЗМНОЖАЮТ: дублирование
        /// конфигурации эффективности и копия её в файл спектра. Копируются и
        /// вещества — иначе две конфигурации правились бы за одно.
        ///
        /// Разбор файла (Raw, Warnings) не копируется: он к сохранённой
        /// геометрии не относится.
        /// </summary>
        public GeometryModel Clone()
        {
            // MemberwiseClone — чтобы не забыть ни одного из сорока полей при
            // следующей правке модели. Всё, что ссылка, перекрывается ниже.
            GeometryModel copy = (GeometryModel)this.MemberwiseClone();
            copy.Crystal = this.Crystal.Clone();
            copy.Reflector = this.Reflector.Clone();
            copy.Cladding = this.Cladding.Clone();
            copy.BeakerWall = this.BeakerWall.Clone();
            copy.Source = this.Source.Clone();
            copy.Raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            copy.Warnings = new List<string>();
            return copy;
        }

        /// <summary>
        /// Копия, у которой ВСЕ длины домножены на коэффициент. Плотности и
        /// составы остаются как есть — они не длины.
        ///
        /// Перечисление полей здесь ручное, и это осознанно: MemberwiseClone
        /// в <see cref="Clone"/> прикрывает от забытого поля при копировании, а
        /// здесь забытое поле — это размер, который останется в чужих единицах,
        /// и расчёт молча выдаст кривую другой геометрии. Единственная защита —
        /// держать список рядом с объявлением полей; при добавлении размера
        /// дописывать надо оба места.
        /// </summary>
        public GeometryModel Scaled(double factor)
        {
            GeometryModel g = this.Clone();

            // Clone разбор файла намеренно не переносит, а здесь он нужен:
            // пересчёт в сантиметры делается ровно перед записью `.in`, и
            // писатель берёт из Raw чужие блоки файла, которых мы не показываем.
            // Потерять их значило бы подменить их своими умолчаниями.
            foreach (KeyValuePair<string, string> pair in this.Raw)
            {
                g.Raw[pair.Key] = pair.Value;
            }

            g.Warnings.AddRange(this.Warnings);

            g.CrystalDiameter *= factor;
            g.CrystalHeight *= factor;
            g.CrystalBoxX *= factor;
            g.CrystalBoxY *= factor;
            g.CrystalBoxZ *= factor;
            g.FrontReflectorThickness *= factor;
            g.SideReflectorThickness *= factor;
            g.FrontCladdingThickness *= factor;
            g.SideCladdingThickness *= factor;
            g.MountingThickness *= factor;

            g.PointDistance *= factor;

            g.BeakerToDetectorDistance *= factor;
            g.BeakerDiameter *= factor;
            g.BeakerHeight *= factor;
            g.BeakerSideWallThickness *= factor;
            g.BeakerEndWallThickness *= factor;
            g.SourceHeight *= factor;

            g.MarinelliBeakerDiameter *= factor;
            g.MarinelliBeakerHeight *= factor;
            g.MarinelliHoleDiameter *= factor;
            g.MarinelliHoleHeight *= factor;
            g.MarinelliSideThickness *= factor;
            g.MarinelliEndWallThickness *= factor;
            g.MarinelliHoleSideThickness *= factor;
            g.MarinelliHoleEndWallThickness *= factor;
            g.MarinelliSourceHeight *= factor;
            g.MarinelliToDetectorDistance *= factor;

            g.BoxSourceX *= factor;
            g.BoxSourceY *= factor;
            g.BoxSourceHeight *= factor;
            g.BoxToDetectorDistance *= factor;
            g.BoxSideWallThickness *= factor;
            g.BoxEndWallThickness *= factor;
            return g;
        }

        /// <summary>
        /// Та же геометрия в сантиметрах. Зовут двое: расчёт переноса (сечения
        /// в см²/г, плотности в г/см³) и запись файла `.in`, где единица см по
        /// формату. Больше нигде сантиметров быть не должно.
        /// </summary>
        public GeometryModel InCentimeters()
        {
            return this.Scaled(1.0 / MmPerCm);
        }

        /// <summary>
        /// Все пары «ключ = значение» разобранного файла как есть.
        ///
        /// Нужны при ЗАПИСИ: редактор правит сцинтилляционную ветвь, а в файле
        /// есть ещё коаксиальная и описания воздуха, которые мы не читаем и не
        /// показываем. Перегенерировать их из ничего значило бы подменить чужие
        /// числа своими умолчаниями, поэтому они переносятся отсюда дословно.
        ///
        /// НЕ ХРАНЯТСЯ. Живут ровно столько, сколько длится сеанс разбора файла:
        /// геометрия переехала в конфиг устройства, обратной записи в `.in`
        /// больше нет, и тащить в конфиг три сотни чужих ключей незачем.
        /// </summary>
        [XmlIgnore]
        public Dictionary<string, string> Raw =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static readonly Regex Line = new Regex(@"^\s*([A-Za-z_][A-Za-z0-9_\[\]\.]*)\s*=\s*(.+?)\s*$",
                                               RegexOptions.Compiled);

        public static GeometryModel Load(string path)
        {
            Dictionary<string, string> kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in File.ReadAllLines(path))
            {
                int comment = raw.IndexOf("//", StringComparison.Ordinal);
                string text = comment >= 0 ? raw.Substring(0, comment) : raw;
                Match m = Line.Match(text);
                if (m.Success)
                {
                    kv[m.Groups[1].Value] = m.Groups[2].Value;
                }
            }

            GeometryModel g = new GeometryModel();
            foreach (KeyValuePair<string, string> pair in kv)
            {
                g.Raw[pair.Key] = pair.Value;
            }

            g.Name = Path.GetFileNameWithoutExtension(path);
            g.IsScintillator = Get(kv, "DetectorType").IndexOf("SCINT", StringComparison.OrdinalIgnoreCase) >= 0;

            string source = Get(kv, "SourceType").ToUpperInvariant();
            g.SourceType = source.StartsWith("MARINELLI") ? GeometrySourceType.Marinelli
                : source.StartsWith("CYLINDER") ? GeometrySourceType.Cylinder
                : source.StartsWith("BOX") ? GeometrySourceType.Box
                : GeometrySourceType.Point;

            // Размеры читаются через Len: в файле они в сантиметрах, а модель
            // держит миллиметры. Обычный Num остаётся для того, что длиной не
            // является, — номеров элементов и массовых долей.
            g.CrystalDiameter = Len(kv, "DS_CrystalDiameter");
            g.CrystalHeight = Len(kv, "DS_CrystalHeight");
            g.FrontReflectorThickness = Len(kv, "DS_CrystalFrontReflectorThickness");
            g.SideReflectorThickness = Len(kv, "DS_CrystalSideReflectorThickness");
            g.FrontCladdingThickness = Len(kv, "DS_CrystalFrontCladdingThickness");
            g.SideCladdingThickness = Len(kv, "DS_CrystalSideCladdingThickness");
            g.MountingThickness = Len(kv, "DS_DetectorMountingThickness");

            g.CrystalBoxX = Len(kv, "DS_CrystalBoxX");
            g.CrystalBoxY = Len(kv, "DS_CrystalBoxY");
            g.CrystalBoxZ = Len(kv, "DS_CrystalBoxZ");
            if (g.CrystalBoxX > 0.0 && g.CrystalBoxY > 0.0 && g.CrystalBoxZ > 0.0)
            {
                g.Shape = CrystalShape.Box;
            }

            // Проценты, не длина: через Num, а не Len.
            g.FwhmAt662Percent = Num(kv, "DS_Fwhm662");

            g.PointDistance = Len(kv, "pdistance");

            g.BeakerToDetectorDistance = Len(kv, "SC_BeakerToDetectorFrontDistance");
            g.BeakerDiameter = Len(kv, "SC_BeakerDiameter");
            g.BeakerHeight = Len(kv, "SC_BeakerHeight");
            g.BeakerSideWallThickness = Len(kv, "SC_BeakerSideWallThickness");
            g.BeakerEndWallThickness = Len(kv, "SC_BeakerEndWallThickness");
            g.SourceHeight = Len(kv, "SC_SourceHeight");

            g.MarinelliBeakerDiameter = Len(kv, "SM_BeakerDiameter");
            g.MarinelliBeakerHeight = Len(kv, "SM_BeakerHeight");
            g.MarinelliHoleDiameter = Len(kv, "SM_BeakerHoleDiameter");
            g.MarinelliHoleHeight = Len(kv, "SM_BeakerHoleHeight");
            g.MarinelliSideThickness = Len(kv, "SM_BeakerSideThickness");
            g.MarinelliEndWallThickness = Len(kv, "SM_BeakerEndWallThickness");
            g.MarinelliHoleSideThickness = Len(kv, "SM_BeakerHoleSideThickness");
            g.MarinelliHoleEndWallThickness = Len(kv, "SM_BeakerHoleEndWallThickness");
            g.MarinelliSourceHeight = Len(kv, "SM_SourceHeight");
            // У Маринелли своё расстояние до детектора: в файле есть оба ключа,
            // и брать цилиндрический для маринеллевской геометрии нельзя.
            g.MarinelliToDetectorDistance = Len(kv, "SM_BeakerToDetectorFrontDistance");

            // Прямоугольная кювета — наше расширение, ключей SB_ в файлах ЛСРМ
            // нет. Если их нет и здесь, поля останутся нулями, а тип источника
            // прочитается как точечный: BOX им тоже неизвестен.
            g.BoxSourceX = Len(kv, "SB_SourceX");
            g.BoxSourceY = Len(kv, "SB_SourceY");
            g.BoxSourceHeight = Len(kv, "SB_SourceHeight");
            g.BoxToDetectorDistance = Len(kv, "SB_BoxToDetectorFrontDistance");
            g.BoxSideWallThickness = Len(kv, "SB_BoxSideWallThickness");
            g.BoxEndWallThickness = Len(kv, "SB_BoxEndWallThickness");

            // Ключ типа долей у отражателя называется DS_FractionTypeReflector,
            // без Crystal, — в отличие от остальных. Так в формате.
            g.Crystal = Material(kv, "DS_", "Crystal", "M_DS_Crystal.MName",
                                 "DS_FractionTypeCrystal", g.Warnings);
            g.Reflector = Material(kv, "DS_", "CrystalReflector", "M_DS_Reflector.MName",
                                   "DS_FractionTypeReflector", g.Warnings);
            g.Cladding = Material(kv, "DS_", "CrystalCladding", "M_DS_Crystal_Cladding.MName",
                                  "DS_FractionTypeCrystalCladding", g.Warnings);

            string prefix = g.SourceType == GeometrySourceType.Marinelli ? "SM_" : "SC_";
            g.BeakerWall = Material(kv, prefix, "Wall", "M_" + prefix + "Beaker.MName",
                                    prefix + "FractionTypeWall", g.Warnings);
            g.Source = Material(kv, prefix, "Source", "M_" + prefix + "Source.MName",
                                prefix + "FractionTypeSource", g.Warnings);
            g.CheckLayers();
            return g;
        }

        /// <summary>
        /// Что в разобранном файле выглядит подозрительно. Пусто, если всё ясно.
        ///
        /// Заводится не «на всякий случай»: у обеих проверок ниже есть читатель
        /// — расчёт печатает это в журнал прогона, а конструктор кривой в свой.
        ///
        /// Не хранится: это итог РАЗБОРА файла, а не свойство геометрии.
        /// </summary>
        [XmlIgnore]
        public List<string> Warnings = new List<string>();

        /// <summary>
        /// Слой с толщиной, но без вещества. Разбирать это молча нельзя: области
        /// сцены вложены и ищутся по порядку, поэтому слой без плотности не
        /// исчезает, а ЗАМЕЩАЕТСЯ слоем снаружи — забыл плотность отражателя, и
        /// на его месте оказался алюминий корпуса, который тяжелее. Расчёт при
        /// этом доводится до конца и выдаёт правдоподобную, но чужую кривую.
        /// </summary>
        void CheckLayers()
        {
            Action<double, GeometryMaterial, string> check = (thickness, material, caption) =>
            {
                if (thickness > 0.0 && (material == null || !(material.Density > 0.0)
                                        || material.Fractions.Count == 0))
                {
                    this.Warnings.Add(string.Format(CultureInfo.InvariantCulture,
                        Properties.Resources.GeometryWarningNoMaterial, caption, thickness));
                }
            };

            check(Math.Max(this.FrontReflectorThickness, this.SideReflectorThickness),
                  this.Reflector, Properties.Resources.GeometryEditorReflectorMaterial);
            check(Math.Max(this.FrontCladdingThickness, this.SideCladdingThickness),
                  this.Cladding, Properties.Resources.GeometryEditorCladdingMaterial);

            double wall;
            double sample;
            switch (this.SourceType)
            {
                case GeometrySourceType.Marinelli:
                    wall = Math.Max(this.MarinelliSideThickness, this.MarinelliHoleSideThickness);
                    sample = this.MarinelliSourceHeight;
                    break;
                case GeometrySourceType.Box:
                    wall = Math.Max(this.BoxSideWallThickness, this.BoxEndWallThickness);
                    sample = this.BoxSourceHeight;
                    break;
                case GeometrySourceType.Cylinder:
                    wall = Math.Max(this.BeakerSideWallThickness, this.BeakerEndWallThickness);
                    sample = this.SourceHeight;
                    break;
                default:
                    wall = 0.0;
                    sample = 0.0;      // точечный источник вещества не имеет
                    break;
            }

            check(wall, this.BeakerWall, Properties.Resources.GeometryEditorWallMaterial);
            check(sample, this.Source, Properties.Resources.GeometryEditorSourceMaterial);
        }

        static string Get(Dictionary<string, string> kv, string key)
        {
            string v;
            return kv.TryGetValue(key, out v) ? v : "";
        }

        /// <summary>
        /// Размер из файла в миллиметрах: «5.03 cm» -> 50.3. Единица в файле
        /// всегда см — так задан формат LSRM.
        /// </summary>
        static double Len(Dictionary<string, string> kv, string key)
        {
            return Num(kv, key) * MmPerCm;
        }

        /// <summary>Значение с единицей: «5.03 cm» -> 5.03, как записано.</summary>
        static double Num(Dictionary<string, string> kv, string key)
        {
            string v = Get(kv, key);
            if (v.Length == 0)
            {
                return 0.0;
            }

            Match m = Regex.Match(v, @"^\s*(-?[0-9.]+(?:[eE][-+]?[0-9]+)?)");
            double value;
            return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float,
                                                CultureInfo.InvariantCulture, out value)
                ? value : 0.0;
        }

        /// <summary>
        /// Вещество собирается из троек, разложенных по файлу:
        /// `<prefix>Ro<part>` — плотность, `<prefix>Z<part>[i]` — номер элемента,
        /// `<prefix>Fractions<part>[i]` — его массовая доля.
        ///
        /// Тип долей задан ключом `<prefix>FractionType<part>`. Во всех восьми
        /// поставочных файлах он MASS — так же, как подписана колонка «Weight
        /// fract» в редакторе материалов LSRM. Но ATOM формат допускает, и
        /// прочитать атомные доли как массовые значит посчитать неверно и
        /// молча: у иодида цезия атомные 0.5/0.5 против массовых 0.488/0.512,
        /// а у чего-нибудь вроде Bi4Ge3O12 разница уже в разы. Поэтому ATOM
        /// пересчитывается в массовые, а незнакомое значение — повод сказать.
        /// </summary>
        static GeometryMaterial Material(Dictionary<string, string> kv, string prefix,
                                         string part, string nameKey, string fractionTypeKey,
                                         List<string> warnings)
        {
            GeometryMaterial m = new GeometryMaterial();
            m.Name = Get(kv, nameKey).Trim();
            m.Density = Num(kv, prefix + "Ro" + part);
            for (int i = 0; i < 24; i++)
            {
                string zKey = prefix + "Z" + part + "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                string fKey = prefix + "Fractions" + part + "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                if (!kv.ContainsKey(zKey))
                {
                    continue;
                }

                int z = (int)Num(kv, zKey);
                double fraction = Num(kv, fKey);
                if (z > 0 && fraction > 0.0)
                {
                    double have;
                    m.Fractions.TryGetValue(z, out have);
                    m.Fractions[z] = have + fraction;
                }
            }

            string type = Get(kv, fractionTypeKey).Trim().ToUpperInvariant();
            if (type.StartsWith("ATOM"))
            {
                ToMassFractions(m);
                if (warnings != null)
                {
                    warnings.Add(string.Format(CultureInfo.InvariantCulture,
                        Properties.Resources.GeometryWarningAtomFractions,
                        m.Name.Length > 0 ? m.Name : part));
                }
            }
            else if (type.Length > 0 && !type.StartsWith("MASS") && warnings != null)
            {
                warnings.Add(string.Format(CultureInfo.InvariantCulture,
                    Properties.Resources.GeometryWarningFractionType,
                    m.Name.Length > 0 ? m.Name : part, type));
            }

            return m;
        }

        /// <summary>
        /// Атомные доли -> массовые: доля умножается на атомную массу и всё
        /// нормируется заново. Обратное преобразование делать нечем и незачем —
        /// весь расчёт ослабления стоит на массовых долях (правило Брэгга).
        /// </summary>
        static void ToMassFractions(GeometryMaterial m)
        {
            Dictionary<int, double> mass = new Dictionary<int, double>();
            double total = 0.0;
            foreach (KeyValuePair<int, double> pair in m.Fractions)
            {
                double atomic;
                if (!AttenuationData.AtomicMass.TryGetValue(pair.Key, out atomic) || !(atomic > 0.0))
                {
                    // Элемента нет в таблице масс — пересчитать нечем; оставляем
                    // состав как есть, о самом элементе скажет IsKnown.
                    return;
                }

                double weight = pair.Value * atomic;
                mass[pair.Key] = weight;
                total += weight;
            }

            if (!(total > 0.0))
            {
                return;
            }

            m.Fractions.Clear();
            foreach (KeyValuePair<int, double> pair in mass)
            {
                m.Fractions[pair.Key] = pair.Value / total;
            }
        }

        /// <summary>
        /// Разбор геометрии одной строкой. Строка попадает в журнал прогона в
        /// окне конструктора кривой, поэтому она переводится: раньше была
        /// жёстко по-русски и в английском интерфейсе выглядела чужой.
        /// </summary>
        public string Describe()
        {
            string source;
            switch (this.SourceType)
            {
                case GeometrySourceType.Point:
                    source = string.Format(CultureInfo.InvariantCulture,
                        Resources.GeometrySourcePoint, this.PointDistance);
                    break;
                case GeometrySourceType.Cylinder:
                    source = string.Format(CultureInfo.InvariantCulture,
                        Resources.GeometrySourceCylinder, this.BeakerDiameter,
                        this.SourceHeight, this.BeakerToDetectorDistance);
                    break;
                case GeometrySourceType.Box:
                    source = string.Format(CultureInfo.InvariantCulture,
                        Resources.GeometrySourceBox, this.BoxSourceX, this.BoxSourceY,
                        this.BoxSourceHeight, this.BoxToDetectorDistance);
                    break;
                default:
                    source = string.Format(CultureInfo.InvariantCulture,
                        Resources.GeometrySourceMarinelli,
                        this.MarinelliBeakerDiameter, this.MarinelliHoleDiameter,
                        this.MarinelliSourceHeight, this.MarinelliToDetectorDistance);
                    break;
            }

            string crystal = this.Shape == CrystalShape.Box
                ? string.Format(CultureInfo.InvariantCulture, Resources.GeometryCrystalBox,
                                this.CrystalBoxX, this.CrystalBoxY, this.CrystalBoxZ)
                : string.Format(CultureInfo.InvariantCulture, Resources.GeometryCrystalCylinder,
                                this.CrystalDiameter, this.CrystalHeight);

            return string.Format(CultureInfo.InvariantCulture, Resources.GeometryDescription,
                this.Name, this.Crystal.Name, crystal, this.Crystal.Density, source, this.Source.Name);
        }
    }
}
