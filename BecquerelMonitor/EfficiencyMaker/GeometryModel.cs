using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
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

    /// <summary>
    /// Одна пара «ключ = значение» разобранного файла `.in` — форма записи для
    /// XML (см. <see cref="GeometryModel.RawList"/>). Значение хранится
    /// СТРОКОЙ, ровно как в файле: там есть и числа с единицей («7.4 cm»), и
    /// имена веществ, а привести их к одному типу значило бы разобрать чужой
    /// блок, который мы нарочно не разбираем.
    /// </summary>
    public sealed class GeometryRawEntry
    {
        [XmlAttribute("k")]
        public string Name;

        [XmlAttribute("v")]
        public string Value;
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

        /// <summary>
        /// Переложить состав ПО ВОЗРАСТАНИЮ Z (`A131`).
        ///
        /// ⛔ Порядок элементов в <see cref="Fractions"/> — не украшение, а
        /// ВХОД РАСЧЁТА. Словарь без удалений перечисляется в порядке вставки,
        /// и по этому порядку `EfficiencySimulator` строит массивы, из которых
        /// РОЗЫГРЫШЕМ выбирает элемент (`PickAtom`, `SampleFluorescence`).
        /// Переставь два элемента — то же самое случайное число попадёт в
        /// другой элемент, поток разойдётся, и кривая уедет на величину шума.
        ///
        /// ⚠ Мимо отпечатка. `ComputeStamp` берёт текст `GeometryWriter.Render`,
        /// а тот состав СОРТИРУЕТ, — значит порядок в клеймо не входит вовсе, и
        /// две матрицы одной сцены с разным порядком неразличимы по
        /// происхождению. Разряд `A104`/`A121`, и цена ему известна.
        ///
        /// Измерено 05.09.2026: `ASN16_Lu176_jar.in` хранит источник как
        /// `SC_ZSource = 71, 8`, наш писатель кладёт `8, 71`, и круг
        /// «прочитать → записать → прочитать» менял кривую до **2.95 %** при
        /// совпадающих полях, тексте и клейме. Оба формата хранения — и `.in`
        /// (<see cref="GeometryWriter"/>), и XML конфигурации
        /// (<see cref="FractionList"/>) — состав уже сортируют; несогласным
        /// оставался ОДИН читатель `.in`, его и приводим к общему правилу.
        /// </summary>
        public void SortFractions()
        {
            if (this.Fractions.Count < 2)
            {
                return;
            }

            List<int> order = new List<int>(this.Fractions.Keys);
            order.Sort();
            List<double> values = new List<double>(order.Count);
            foreach (int z in order)
            {
                values.Add(this.Fractions[z]);
            }

            this.Fractions.Clear();
            for (int i = 0; i < order.Count; i++)
            {
                this.Fractions[order[i]] = values[i];
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
            if (!(energyKev > 0.0))
            {
                // Как и прежде: ниже нуля шкалы обе части нулевые, сумма нулевая.
                return 0.0;
            }

            // ⚡ (`A43`) Полное ослабление и когерентное — с ОДНОГО прохода по
            // сетке элемента: она у них общая, энергия одна, и логарифм от неё
            // берётся один на всё вещество. Было два поиска и два логарифма на
            // каждый элемент.
            double logEnergyKev = Math.Log(energyKev);
            double massAttenuation = 0.0;
            foreach (KeyValuePair<int, double> pair in this.Fractions)
            {
                double value = 0.0;
                MaterialDatabase.Element element;
                int lo, hi;
                if (MaterialDatabase.TryGet(pair.Key, out element)
                    && MaterialDatabase.Bracket(element.EnergyKev, energyKev, out lo, out hi))
                {
                    value = MaterialDatabase.Interpolate(
                        element.EnergyKev, element.LogEnergyKev,
                        element.Total, element.LogTotal, lo, hi, energyKev, logEnergyKev);
                    value -= PartialCrossSections.MassCrossSection(
                        element, lo, hi, energyKev, logEnergyKev, PhotonProcess.Coherent);
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
            return this.LinearChannel(energyKev, PhotonProcess.Incoherent);
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
            return this.LinearChannel(energyKev, PhotonProcess.Coherent);
        }

        /// <summary>
        /// Только РОЖДЕНИЕ ПАР, 1/см (`A52`, 02.09.2026). Нужно вне кристалла:
        /// там этот канал прежде числился фотопоглощением, то есть квант просто
        /// умирал — и два аннигиляционных кванта по 511 кэВ, которые обязаны
        /// были из обвязки полететь, не рождались вовсе.
        ///
        /// <paramref name="thresholdPair"/> — тот же ключ
        /// <see cref="EfficiencySimulator.XcomPairThreshold"/>, что у кристалла:
        /// у канала СВОЙ порог, и линейная по логарифму сетка XCOM около него
        /// завышает сечение (`S121`). Разводить кристалл и обвязку по разным
        /// правилам нельзя — это одно и то же сечение.
        /// </summary>
        public double LinearPair(double energyKev, bool thresholdPair)
        {
            if (!thresholdPair)
            {
                return this.LinearChannel(energyKev, PhotonProcess.PairProduction);
            }

            if (!(energyKev > 0.0))
            {
                return 0.0;
            }

            double logEnergyKev = Math.Log(energyKev);
            double massAttenuation = 0.0;
            foreach (KeyValuePair<int, double> part in this.Fractions)
            {
                MaterialDatabase.Element element;
                int lo, hi;
                if (MaterialDatabase.TryGet(part.Key, out element)
                    && MaterialDatabase.Bracket(element.EnergyKev, energyKev, out lo, out hi))
                {
                    massAttenuation += part.Value * PartialCrossSections.MassCrossSection(
                        element, lo, hi, energyKev, logEnergyKev,
                        PhotonProcess.PairProduction, true);
                }
            }

            return massAttenuation * this.Density;
        }

        /// <summary>
        /// ⚡ (`A43`) Один канал взаимодействия по всему веществу, 1/см. Общее
        /// тело <see cref="LinearIncoherent"/> и <see cref="LinearCoherent"/>:
        /// они отличались только буквой канала, а платили каждый за свой поиск
        /// элемента в словаре (`Has`, потом `TryGet` внутри сечения) и за свой
        /// логарифм энергии.
        ///
        /// Элемента нет в поставке — его вклад нулевой, как и раньше: лучше не
        /// разыграть рассеяние, чем разыграть выдуманное.
        /// </summary>
        double LinearChannel(double energyKev, PhotonProcess process)
        {
            if (!(energyKev > 0.0))
            {
                return 0.0;
            }

            double logEnergyKev = Math.Log(energyKev);
            double massAttenuation = 0.0;
            foreach (KeyValuePair<int, double> pair in this.Fractions)
            {
                MaterialDatabase.Element element;
                int lo, hi;
                if (MaterialDatabase.TryGet(pair.Key, out element)
                    && MaterialDatabase.Bracket(element.EnergyKev, energyKev, out lo, out hi))
                {
                    massAttenuation += pair.Value * PartialCrossSections.MassCrossSection(
                        element, lo, hi, energyKev, logEnergyKev, process);
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

    /// <summary>
    /// Съёмка в поле — НАШЕ расширение формата (E27). Две геометрии, которые
    /// стоят в списке рядом с точкой, цилиндром и маринелли, но отличаются от
    /// них не формой, а тем, ОТКУДА берутся размеры: их считает формула из
    /// свободного пробега в пробе и из выбранного детектора.
    ///
    /// Форма при этом остаётся штатной, и это не уловка, а физика:
    /// полупространства в формате нет, прибор на земле — это цилиндр грунта под
    /// ним, а прибор в лунке — это в точности маринелли (колодец = лунка, проба
    /// вокруг и снизу, стенок сосуда нет). Поэтому <see cref="GeometryModel.SourceType"/>
    /// у них настоящий (<see cref="GeometrySourceType.Cylinder"/> и
    /// <see cref="GeometrySourceType.Marinelli"/>), весь расчёт идёт прежним
    /// кодом, а здесь хранится только то, чем сцена НАЗЫВАЕТСЯ и по какому
    /// правилу пересчитываются её размеры.
    ///
    /// В файлах ЛСРМ такого ключа нет; пишется своим `DS_Scene`, которого их
    /// программа не читает, — тем же приёмом, что <see cref="CrystalShape.Box"/>
    /// с ключами `SB_*` и <see cref="GeometryDetectorFacing"/> с `DS_Facing`.
    /// </summary>
    public enum GeometrySceneKind
    {
        /// <summary>Обычная сцена: размеры задал человек.</summary>
        None,

        /// <summary>Прибор лежит на земле; грунт — цилиндр под ним.</summary>
        Ground,

        /// <summary>Прибор опущен в лунку; грунт вокруг и снизу.</summary>
        Borehole
    }

    /// <summary>Форма кристалла.</summary>
    public enum CrystalShape
    {
        Cylinder,
        /// <summary>Прямоугольный параллелепипед: длинная сторона вдоль оси.</summary>
        Box
    }

    /// <summary>
    /// Какой стороной детектор обращён к пробе — НАШЕ расширение формата (E21).
    ///
    /// Зачем. До 15.08.2026 проба всегда лежала на оси, лицом к переднему торцу.
    /// Измерением показано, чего это стоит: у спектра Lu₂O₃ на Nano 16 Pro
    /// (кристалл-брусок 15 × 18 × 60 мм) постановка «с торца» даёт отношение
    /// сумм-пика к одиночному 0.0112, а «сбоку» — 0.0339, ВТРОЕ больше, и
    /// измерение назвало именно второе. Ошибка в постановке шла в разы, а
    /// разбор списывал её на несуществующую аннигиляционную линию (S46, §13и).
    ///
    /// В файлах ЛСРМ такого ключа нет; пишется своим `DS_Facing`, которого их
    /// программа не читает, — тем же приёмом, что <see cref="CrystalShape.Box"/>
    /// с ключами `SB_*`. Отсутствие ключа означает <see cref="Front"/>, то есть
    /// прежнее поведение: старые файлы читаются как раньше.
    /// </summary>
    public enum GeometryDetectorFacing
    {
        /// <summary>Проба перед передним торцом — как было всегда.</summary>
        Front,

        /// <summary>
        /// Проба у БОКОВОЙ грани. Осмысленно только для бруска
        /// (<see cref="CrystalShape.Box"/>): к пробе разворачивается самая
        /// широкая грань, а глубина кристалла вдоль оси становится наименьшим
        /// его размером. У цилиндра боковая постановка ломает осевую симметрию
        /// и здесь НЕ поддержана — см. <see cref="GeometryModel.FacingError"/>.
        /// </summary>
        Side
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

        /// <summary>
        /// Съёмка в поле (E27) — см. <see cref="GeometrySceneKind"/>. Умолчание
        /// <see cref="GeometrySceneKind.None"/>: так читаются все геометрии,
        /// снятые до 16.08.2026, и все файлы ЛСРМ.
        /// </summary>
        public GeometrySceneKind Scene = GeometrySceneKind.None;

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
        /// Какой стороной детектор обращён к пробе (E21). Умолчание — передний
        /// торец, как было до 15.08.2026 и как читаются файлы без ключа.
        /// </summary>
        public GeometryDetectorFacing Facing = GeometryDetectorFacing.Front;

        /// <summary>
        /// Почему выбранная сторона не годится этой сцене; пусто — годится.
        /// Проверяется до счёта: боковая постановка у ЦИЛИНДРИЧЕСКОГО кристалла
        /// не осесимметрична, а вся сцена симулятора построена вдоль оси, и
        /// молча посчитать её «как-нибудь» — верный способ получить число, за
        /// которым ничего не стоит.
        /// </summary>
        public string FacingError
        {
            get
            {
                if (this.Facing != GeometryDetectorFacing.Side)
                {
                    return "";
                }

                return this.Shape == CrystalShape.Box
                    ? ""
                    : "Боковая постановка пробы задана только для кристалла-бруска:"
                      + " у цилиндра она не осесимметрична и сценой не выражается.";
            }
        }

        /// <summary>
        /// Снять размеры, которых у ЭТОЙ формы кристалла не существует
        /// (`A94`, решение Amber 04.09.2026).
        ///
        /// Зачем. У бруска <see cref="CrystalDiameter"/> и
        /// <see cref="CrystalHeight"/> не читает НИКТО: в файл писатель кладёт
        /// производный цилиндр равной площади торца, сцену симулятор строит по
        /// <see cref="CrystalBoxInScene"/>, чертёж и разбор ветвятся по
        /// <see cref="Shape"/>. Поле, которое можно задать и нельзя увидеть, —
        /// это ловушка, а не запас: `CrystalHeight += 1` мм оставлял текст
        /// геометрии ПОБАЙТНО тем же и отпечаток тем же, и сторож `A47` на
        /// этом полгода выглядел как дыра в отпечатке.
        ///
        /// Зовётся на обеих границах модели: при чтении файла
        /// (<see cref="Load"/>) и при сборке модели из редактора. Симметрично
        /// снимаются и габариты бруска у цилиндра — по той же причине и без
        /// последствий: писатель их у цилиндра не печатает вовсе.
        /// </summary>
        public void DropDeadCrystalSize()
        {
            if (this.Shape == CrystalShape.Box)
            {
                this.CrystalDiameter = 0.0;
                this.CrystalHeight = 0.0;
            }
            else
            {
                this.CrystalBoxX = 0.0;
                this.CrystalBoxY = 0.0;
                this.CrystalBoxZ = 0.0;
            }
        }

        /// <summary>
        /// Размеры кристалла в системе СЦЕНЫ: полуширины грани, обращённой к
        /// пробе, и глубина вдоль оси. Для <see cref="GeometryDetectorFacing.Side"/>
        /// брусок разворачивается так, чтобы к пробе смотрела САМАЯ ШИРОКАЯ
        /// грань, а глубиной стал наименьший размер: именно это и означают
        /// слова «проба стоит сбоку, где широкая часть кристалла». Объём при
        /// этом сохраняется точно — кристалл тот же, повёрнут только он.
        /// </summary>
        public void CrystalBoxInScene(out double halfX, out double halfY, out double depth)
        {
            string kx, ky, kd;
            this.CrystalBoxInScene(out halfX, out halfY, out depth, out kx, out ky, out kd);
        }

        /// <summary>
        /// То же, но вдобавок ИМЕНА полей, попавших на каждую ось сцены. Нужны
        /// чертежу: он подписывает размеры, и после разворота подпись
        /// «CrystalBoxX» рядом с высотой, взятой из Y, была бы ложью. Выбор оси
        /// живёт здесь в единственном месте — иначе модель и чертёж разойдутся
        /// при первой же правке.
        /// </summary>
        public void CrystalBoxInScene(out double halfX, out double halfY, out double depth,
                                      out string keyX, out string keyY, out string keyDepth)
        {
            double x = this.CrystalBoxX, y = this.CrystalBoxY, z = this.CrystalBoxZ;
            if (this.Facing == GeometryDetectorFacing.Side)
            {
                // Наименьший размер уходит в глубину, два оставшихся образуют грань.
                double min = Math.Min(x, Math.Min(y, z));
                if (min == x)
                {
                    depth = x; halfX = 0.5 * y; halfY = 0.5 * z;
                    keyDepth = "CrystalBoxX"; keyX = "CrystalBoxY"; keyY = "CrystalBoxZ";
                }
                else if (min == y)
                {
                    depth = y; halfX = 0.5 * x; halfY = 0.5 * z;
                    keyDepth = "CrystalBoxY"; keyX = "CrystalBoxX"; keyY = "CrystalBoxZ";
                }
                else
                {
                    depth = z; halfX = 0.5 * x; halfY = 0.5 * y;
                    keyDepth = "CrystalBoxZ"; keyX = "CrystalBoxX"; keyY = "CrystalBoxY";
                }

                return;
            }

            halfX = 0.5 * x;
            halfY = 0.5 * y;
            depth = z;
            keyX = "CrystalBoxX";
            keyY = "CrystalBoxY";
            keyDepth = "CrystalBoxZ";
        }

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

        /// <summary>
        /// Зазор между отражателем и корпусом, мм — торцевой (`AMBER1`,
        /// задача Amber 07.09.2026).
        ///
        /// ⛔ ЭТО НЕ «ЕЩЁ НЕМНОГО ВОЗДУХА». Расстояния сцены отсчитываются от
        /// ПЕРЕДНЕГО ТОРЦА КОРПУСА, поэтому зазор УГЛУБЛЯЕТ КРИСТАЛЛ под
        /// торцом: у `Atom Spectra Pro 80x80` глубина растёт с 0.5 см
        /// (0.3 корпус + 0.2 отражатель) до 2.67 см, и геометрический фактор
        /// точечной сцены на торце падает с 0.438 до 0.222 — почти вдвое.
        /// Ослабление в воздухе при этом ничтожно; работает именно вынос.
        /// </summary>
        public double FrontGapThickness;

        /// <summary>
        /// Тот же зазор с БОКА. ⚠ У ЦИЛИНДРА бока у зазора НЕТ (решение Amber
        /// 07.09.2026, вопросником): поле остаётся нулём и в окне не
        /// показывается. Заведено ради видов, у которых торец и бок
        /// различаются, — там у каждого своё число.
        ///
        /// ⚠ Правило «бока нет» сказано ПРО ЗАЗОР и только про него: обвязка
        /// (отражатель и корпус) у цилиндра по-прежнему несёт оба размера.
        /// </summary>
        public double SideGapThickness;

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

        /// <summary>
        /// Вещество зазора между отражателем и корпусом (`AMBER1`). По
        /// умолчанию ВОЗДУХ — по слову Amber; выбирается из той же библиотеки
        /// веществ, что отражатель и корпус.
        /// </summary>
        public GeometryMaterial Gap = new GeometryMaterial();
        public GeometryMaterial Cladding = new GeometryMaterial();
        public GeometryMaterial BeakerWall = new GeometryMaterial();
        public GeometryMaterial Source = new GeometryMaterial();

        /// <summary>
        /// Наибольшая плотность, при которой проба считается ВОЗДУХОМ, г/см³
        /// (`E19`, решение Amber 01.09.2026).
        ///
        /// Порог по ПЛОТНОСТИ, а не по имени вещества, нарочно: имя человек
        /// меняет, а самопоглощения от этого не появляется, — и наоборот,
        /// «воздух» с плотностью названного вещества это уже названное
        /// вещество. Сотая доля грамма на кубический сантиметр — это восемь
        /// сухих воздухов (0.001205) и вдесятеро меньше самой рыхлой засыпки:
        /// слой в 20 мм при такой плотности даёт 0.02 г/см², то есть ослабление
        /// в 1.3 % на 202 кэВ — против ×0.3, которые даёт там настоящий оксид
        /// лютеция. Ниже этого порога проба не поглощает НИЧЕГО, как её ни зови.
        /// </summary>
        public const double AirSampleDensity = 0.01;

        /// <summary>
        /// Высота пробы, мм — тот её размер, вдоль которого квант идёт наружу.
        /// Ноль у точечного источника: вещества у него нет по построению.
        /// </summary>
        [XmlIgnore]
        public double SampleHeightMm
        {
            get
            {
                switch (this.SourceType)
                {
                    case GeometrySourceType.Marinelli:
                        return this.MarinelliSourceHeight;
                    case GeometrySourceType.Box:
                        return this.BoxSourceHeight;
                    case GeometrySourceType.Cylinder:
                        return this.SourceHeight;
                    default:
                        return 0.0;                 // точечный источник
                }
            }
        }

        /// <summary>
        /// Есть ли у пробы объём: и высота, и поперечник положительны. Число
        /// объёма НЕ считается нарочно — у маринелли это разность двух тел, и
        /// вторая формула того же счёта разъехалась бы с первой молча (`S37`);
        /// а для вопроса «проба вообще есть?» довольно того, что все её размеры
        /// не нули.
        /// </summary>
        [XmlIgnore]
        public bool HasSampleVolume
        {
            get
            {
                switch (this.SourceType)
                {
                    case GeometrySourceType.Marinelli:
                        return this.MarinelliSourceHeight > 0.0
                               && this.MarinelliBeakerDiameter > 0.0;
                    case GeometrySourceType.Box:
                        return this.BoxSourceHeight > 0.0 && this.BoxSourceX > 0.0
                               && this.BoxSourceY > 0.0;
                    case GeometrySourceType.Cylinder:
                        return this.SourceHeight > 0.0 && this.BeakerDiameter > 0.0;
                    default:
                        return false;               // точечный источник
                }
            }
        }

        /// <summary>
        /// Вещество пробы осталось ВОЗДУХОМ, хотя сосуд не пуст (`E19`).
        ///
        /// Зачем это отдельным признаком. Заготовка редактора открывается с
        /// сосудом Ø40×20 мм и воздухом в нём — воздух там стоит потому, что
        /// вещества пробы у человека ЕЩЁ НЕТ (см. `GeometryEditorPanel.Blank`).
        /// Не назвав вещество, он получает кривую без самопоглощения, а это не
        /// «нет данных», а систематическая ошибка В РАЗЫ: у оксида лютеция в
        /// слое 20 мм μ/ρ = 0.630 см²/г на 202 кэВ, то есть множитель ×0.3 при
        /// ρ = 2.5. Молчать об этом нельзя; мешать считать — тоже, точечный
        /// источник в воздухе законен (и сюда не попадает: объёма у него нет).
        /// </summary>
        [XmlIgnore]
        public bool SampleIsAir
        {
            get
            {
                return this.HasSampleVolume
                       && !(this.Source != null && this.Source.Density > AirSampleDensity);
            }
        }

        /// <summary>
        /// Полная копия. Нужна там, где геометрию РАЗМНОЖАЮТ: дублирование
        /// конфигурации эффективности и копия её в файл спектра. Копируются и
        /// вещества — иначе две конфигурации правились бы за одно.
        ///
        /// ⛔ РАЗБОР ФАЙЛА (<see cref="Raw"/>) КОПИРУЕТСЯ ТОЖЕ — `A139`,
        /// 04.09.2026. До этого он намеренно терялся («к сохранённой геометрии
        /// не относится»), и посылка была неверна: писатель берёт из разбора
        /// ЧУЖИЕ БЛОКИ файла — коаксиальный детектор (16 ключей `DC_*` и пять
        /// его веществ) и «пустое место» сосуда с маринелли. Нет разбора —
        /// пишутся нули и умолчания, то есть одна и та же геометрия даёт ДВА
        /// разных текста `.in`, а с ним и два разных `ResponseMatrix.ComputeStamp`.
        /// Измерено на складе: у 17 ввезённых из ЛСРМ файлов из 66 копия
        /// сдвигала отпечаток на 26 строк, ничего в геометрии не изменив, —
        /// матрица объявлялась устаревшей, человек получал часы пересчёта.
        /// У 44 корпусных геометрий, написанных нашим же писателем, разница
        /// была нулевой: там чужие блоки и так пусты. Отсюда и разряд ошибки
        /// «посылка верна на одной сцене и врёт на другой» (см. `A94`).
        ///
        /// <see cref="Scaled"/> переносил разбор руками — теперь не нужно.
        /// </summary>
        public GeometryModel Clone()
        {
            // MemberwiseClone — чтобы не забыть ни одного из сорока полей при
            // следующей правке модели. Всё, что ссылка, перекрывается ниже.
            GeometryModel copy = (GeometryModel)this.MemberwiseClone();
            copy.Crystal = this.Crystal.Clone();
            copy.Reflector = this.Reflector.Clone();
            copy.Gap = this.Gap.Clone();
            copy.Cladding = this.Cladding.Clone();
            copy.BeakerWall = this.BeakerWall.Clone();
            copy.Source = this.Source.Clone();
            // Свои словарь и список, а не общие с исходником: копия для того и
            // делается, чтобы две геометрии не правились за одну.
            copy.Raw = new Dictionary<string, string>(this.Raw, StringComparer.OrdinalIgnoreCase);
            copy.Warnings = new List<string>(this.Warnings);
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
            // Разбор файла и предупреждения переносит сам `Clone` (`A139`);
            // здесь они нужны ровно затем же, зачем и везде: пересчёт в
            // сантиметры делается перед записью `.in`, а писатель берёт из
            // разбора чужие блоки файла, которых мы не показываем.
            GeometryModel g = this.Clone();

            g.CrystalDiameter *= factor;
            g.CrystalHeight *= factor;
            g.CrystalBoxX *= factor;
            g.CrystalBoxY *= factor;
            g.CrystalBoxZ *= factor;
            g.FrontReflectorThickness *= factor;
            g.SideReflectorThickness *= factor;
            g.FrontGapThickness *= factor;
            g.SideGapThickness *= factor;
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
        /// ⛔ ХРАНЯТСЯ (`A139`, 04.09.2026). Прежде здесь стояло обратное —
        /// «живут ровно столько, сколько длится сеанс разбора файла», — и это
        /// давало ту же беду, что потеря разбора на <see cref="Clone"/>:
        /// геометрия, приехавшая из конфигурации прибора, писала чужие блоки
        /// нулями и умолчаниями, а та же геометрия прямо из файла — настоящими
        /// числами. Два текста `.in` — два `ResponseMatrix.ComputeStamp`, и
        /// посчитанная матрица объявлялась устаревшей после перезапуска
        /// приложения. Измерено: 17 ввезённых из ЛСРМ файлов склада из 66;
        /// у 44 корпусных разницы нет, там чужие блоки и так пусты.
        ///
        /// Цена — размер конфигурации, и она урезана: в XML едет НЕ весь
        /// разбор, а только то, что писатель действительно переносит
        /// (<see cref="GeometryWriter.CarriedFrom"/>) — на `Nano16Pro.in` это
        /// 55 ключей из 191. У геометрии, собранной в редакторе с нуля, разбор
        /// пуст и в XML не появляется вовсе, то есть конфигурации таких
        /// приборов не меняются ни на знак.
        /// </summary>
        [XmlIgnore]
        public Dictionary<string, string> Raw =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Тот же разбор списком — форма записи для XML, как у состава
        /// вещества (<see cref="GeometryMaterial.FractionList"/>):
        /// `XmlSerializer` не умеет `Dictionary`.
        ///
        /// Порядок ключей ЗАКРЕПЛЁН сортировкой. Словарь порядка не обещает, а
        /// конфигурация прибора сравнивается людьми и програмами построчно;
        /// «поменялось всё» после каждого сохранения читалось бы как правка.
        /// </summary>
        [XmlArray("Raw")]
        [XmlArrayItem("Entry")]
        public GeometryRawEntry[] RawList
        {
            get
            {
                // Пустой разбор — НЕТ ЭЛЕМЕНТА, а не пустой элемент: у
                // геометрии, собранной в редакторе, разбора нет никогда, и
                // конфигурации таких приборов обязаны остаться прежними до
                // знака. Иначе первое же сохранение переписало бы их все.
                if (this.Raw.Count == 0)
                {
                    return null;
                }

                // Хранится не весь разбор, а ровно то, что ПЕРЕНОСИТ писатель:
                // остальное — те же числа, что уже лежат в полях модели.
                Dictionary<string, string> carried = GeometryWriter.CarriedFrom(this);
                if (carried.Count == 0)
                {
                    return null;
                }

                List<GeometryRawEntry> list = new List<GeometryRawEntry>();
                foreach (KeyValuePair<string, string> pair in carried)
                {
                    list.Add(new GeometryRawEntry { Name = pair.Key, Value = pair.Value });
                }

                list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
                return list.ToArray();
            }

            set
            {
                this.Raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (value == null)
                {
                    return;
                }

                foreach (GeometryRawEntry entry in value)
                {
                    if (entry != null && !string.IsNullOrEmpty(entry.Name))
                    {
                        this.Raw[entry.Name] = entry.Value ?? "";
                    }
                }
            }
        }

        static readonly Regex Line = new Regex(@"^\s*([A-Za-z_][A-Za-z0-9_\[\]\.]*)\s*=\s*(.+?)\s*$",
                                               RegexOptions.Compiled);

        /// <summary>
        /// Однобайтная кириллица — та же кодировка, какой файл ПИШЕТСЯ
        /// (<see cref="GeometryWriter.Save"/>). Держится готовой: `Load`
        /// зовётся на каждую сцену корпуса, а `GetEncoding` ходит в таблицу
        /// кодовых страниц.
        /// </summary>
        static readonly Encoding Ansi = Encoding.GetEncoding(1251);

        /// <summary>
        /// Прочитать файл геометрии ТОЙ ЖЕ кодировкой, какой он пишется
        /// (`A161`, решение Amber 05.09.2026).
        ///
        /// ⛔ **Чем это было.** `File.ReadAllLines` без кодировки — это UTF-8, а
        /// <see cref="GeometryWriter.Save"/> пишет `Encoding.GetEncoding(1251)`.
        /// Круг «открыл — сохранил» поэтому терял кириллицу дважды: при чтении
        /// однобайтные байты становились `U+FFFD`, при записи `U+FFFD` не
        /// ложился в 1251 и становился `?`. Имя вещества входит в
        /// <see cref="ResponseMatrix.ComputeStamp"/> (через `GeometryWriter.Render`),
        /// значит один такой круг объявлял матрицу устаревшей при НЕИЗМЕННОЙ
        /// сцене. Измерено 05.09.2026 на 66 геометриях дерева: имя терялось у
        /// **36**, и ровно у тех же 36 круг двигал отпечаток.
        ///
        /// ⚠ **Почему распознавание, а не «всегда 1251».** Сплошной разбор всех
        /// 614 `.in` дерева: 606 однобайтных, 7 чистого ASCII (кодировка на них
        /// не влияет) и **один настоящий UTF-8** —
        /// `tools/effmaker/models/Nano16Pro_box.in`, где 97 `U+FFFD` уже
        /// записаны в файл ранним кругом этого самого дефекта. Слепое
        /// переключение прочло бы его байты как 1251 и выдало третье, новое
        /// написание. Распознавание держит такой файл прежним и переживает
        /// правку `.in` в редакторе, сохраняющем UTF-8.
        ///
        /// ⚠ Ложное «это UTF-8» на однобайтном файле измерено и равно НУЛЮ:
        /// строгий разбор UTF-8 отверг все 606 однобайтных до одного. Русский
        /// текст в 1251 почти не бывает годным UTF-8 — заглавные буквы лежат в
        /// `C0..DF` (ведущий байт пары), а следом обязан идти `80..BF`, куда
        /// попадают лишь редкие знаки, но не буквы.
        /// </summary>
        static string[] ReadAllLines(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            List<string> lines = new List<string>();

            // Разбиение на строки отдано `StreamReader`: у него ровно та же
            // разметка концов, что была у `File.ReadAllLines` (одиночные
            // `\r`, `\n` и пара `\r\n`), и своя копия этого правила разъехалась
            // бы с ней молча. Признак порядка байтов оставлен включённым —
            // файл с BOM прочтётся своим, а не угаданным.
            using (MemoryStream stream = new MemoryStream(data, false))
            using (StreamReader reader = new StreamReader(stream, Detect(data), true))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    lines.Add(line);
                }
            }

            return lines.ToArray();
        }

        /// <summary>
        /// Кодировка файла: UTF-8, если он ЦЕЛИКОМ годен как UTF-8, иначе 1251.
        ///
        /// ⚠ Порядок проверок обратить нельзя: 1251 принимает ЛЮБЫЕ байты, и
        /// вопрос «а не 1251 ли это» ответа не имеет вовсе. Отличить может
        /// только строгий разбор UTF-8.
        /// </summary>
        static Encoding Detect(byte[] data)
        {
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            {
                return new UTF8Encoding(false);
            }

            bool high = false;
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] >= 0x80)
                {
                    high = true;
                    break;
                }
            }

            if (!high)
            {
                // Ни одного байта выше 0x7F — обе кодировки дают побуквенно
                // одно и то же, и гадать не о чем.
                return Ansi;
            }

            try
            {
                new UTF8Encoding(false, true).GetString(data);
                return new UTF8Encoding(false);
            }
            catch (ArgumentException)
            {
                // `DecoderFallbackException` — наследник `ArgumentException`.
                return Ansi;
            }
        }

        public static GeometryModel Load(string path)
        {
            Dictionary<string, string> kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in ReadAllLines(path))
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
            //
            // ⛔ ФОРМА КРИСТАЛЛА РЕШАЕТСЯ ПЕРВОЙ (`A94`, решение Amber
            // 04.09.2026). У бруска полей цилиндра НЕ СУЩЕСТВУЕТ, и прочитать
            // их «на всякий случай» значит завести мёртвое состояние: оно
            // сохранится в модели, но не попадёт ни в файл, ни в отпечаток, ни
            // в сцену — а править его при этом можно молча. Ровно об это
            // сломался сторож `A47` и полгода выглядел как дыра в отпечатке.
            g.CrystalBoxX = Len(kv, "DS_CrystalBoxX");
            g.CrystalBoxY = Len(kv, "DS_CrystalBoxY");
            g.CrystalBoxZ = Len(kv, "DS_CrystalBoxZ");
            if (g.CrystalBoxX > 0.0 && g.CrystalBoxY > 0.0 && g.CrystalBoxZ > 0.0)
            {
                g.Shape = CrystalShape.Box;
            }

            // ⚠ ОБРАТНАЯ СОВМЕСТИМОСТЬ ЧТЕНИЯ. `DS_CrystalDiameter` и
            // `DS_CrystalHeight` в файле бруска ЕСТЬ и остаются: их кладёт туда
            // наш же писатель, ПРОИЗВОДНЫМИ от `DS_CrystalBoxX/Y/Z` (равная
            // площадь торца — правило самого LSRM), чтобы файл оставался
            // осмысленным для GMaster, который бруска не знает. Обратно они не
            // читаются — просто пропускаются; ни один старый файл от этого
            // читаться не перестаёт.
            if (g.Shape != CrystalShape.Box)
            {
                g.CrystalDiameter = Len(kv, "DS_CrystalDiameter");
                g.CrystalHeight = Len(kv, "DS_CrystalHeight");
            }

            g.FrontReflectorThickness = Len(kv, "DS_CrystalFrontReflectorThickness");
            g.SideReflectorThickness = Len(kv, "DS_CrystalSideReflectorThickness");
            // (`AMBER1`) Ключей зазора у формата ЛСРМ нет — это наше
            // расширение. Отсутствие ключа читается нулём, то есть прежние
            // файлы понимаются как раньше, знак в знак.
            g.FrontGapThickness = Len(kv, "DS_CrystalFrontGapThickness");
            g.SideGapThickness = Len(kv, "DS_CrystalSideGapThickness");
            g.FrontCladdingThickness = Len(kv, "DS_CrystalFrontCladdingThickness");
            g.SideCladdingThickness = Len(kv, "DS_CrystalSideCladdingThickness");
            g.MountingThickness = Len(kv, "DS_DetectorMountingThickness");

            // E21: сторона, обращённая к пробе. Ключа нет — передний торец, то
            // есть прежнее поведение; так читаются все файлы до 15.08.2026 и
            // все файлы ЛСРМ, которые этого ключа не знают вовсе.
            string facing;
            if (kv.TryGetValue("DS_Facing", out facing)
                && facing.Trim().Equals("SIDE", StringComparison.OrdinalIgnoreCase))
            {
                g.Facing = GeometryDetectorFacing.Side;
            }

            // E27: съёмка в поле. Ключа нет — обычная сцена, то есть прежнее
            // поведение и все файлы ЛСРМ.
            string scene;
            if (kv.TryGetValue("DS_Scene", out scene))
            {
                scene = scene.Trim();
                if (scene.Equals("GROUND", StringComparison.OrdinalIgnoreCase))
                {
                    g.Scene = GeometrySceneKind.Ground;
                }
                else if (scene.Equals("BOREHOLE", StringComparison.OrdinalIgnoreCase))
                {
                    g.Scene = GeometrySceneKind.Borehole;
                }
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
            // (`AMBER1`) Вещество зазора. У файла БЕЗ этих ключей — а таковы
            // все файлы до 08.09.2026 и все чужие — выйдет пустое вещество, и
            // это верно: толщина зазора у них тоже ноль, слоя нет вовсе.
            // ⛔ Подставлять здесь воздух «по умолчанию» НЕЛЬЗЯ: умолчание
            // ставят пресет и заготовка редактора, то есть места, где человек
            // геометрию СОЗДАЁТ. Разбор чужого файла ничего не создаёт и обязан
            // прочесть ровно то, что в файле написано.
            g.Gap = Material(kv, "DS_", "CrystalGap", "M_DS_Gap.MName",
                             "DS_FractionTypeGap", g.Warnings);

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
            check(Math.Max(this.FrontGapThickness, this.SideGapThickness),
                  this.Gap, Properties.Resources.GeometryEditorGapMaterial);
            check(Math.Max(this.FrontCladdingThickness, this.SideCladdingThickness),
                  this.Cladding, Properties.Resources.GeometryEditorCladdingMaterial);

            double wall;
            // Высота пробы берётся ОДНИМ местом (`SampleHeightMm`): её же
            // читает признак «проба осталась воздухом» (`E19`), и вторая копия
            // того же разбора разъехалась бы с первой молча (`S37`).
            double sample = this.SampleHeightMm;
            switch (this.SourceType)
            {
                case GeometrySourceType.Marinelli:
                    wall = Math.Max(this.MarinelliSideThickness, this.MarinelliHoleSideThickness);
                    break;
                case GeometrySourceType.Box:
                    wall = Math.Max(this.BoxSideWallThickness, this.BoxEndWallThickness);
                    break;
                case GeometrySourceType.Cylinder:
                    wall = Math.Max(this.BeakerSideWallThickness, this.BeakerEndWallThickness);
                    break;
                default:
                    wall = 0.0;        // точечный источник вещества не имеет
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

            // ⛔ (`A131`) Порядок элементов приводится к тому же, каким его
            // кладут ОБА наших формата хранения — писатель `.in` и XML
            // конфигурации. Без этого чтение файла с иным порядком давало
            // модель, которая после сохранения считается ИНАЧЕ при том же
            // клейме; довод и число — в <see cref="GeometryMaterial.SortFractions"/>.
            m.SortFractions();
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
