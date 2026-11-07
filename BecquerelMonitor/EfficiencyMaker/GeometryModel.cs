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
        /// Вещества НЕТ: ни имени, ни состава, ни плотности (`AMBER47`). Ровно
        /// то, что оставляет чтение файла `.in` без блока слоя, пустой блок
        /// `&lt;Gap /&gt;` конфигурации прибора и заготовка
        /// <c>new GeometryMaterial()</c>. Все три признака разом, а не любой из
        /// них: вещество с именем и без состава — это ЗАДАННОЕ вещество, у
        /// которого не хватает данных, и подменять его умолчанием нельзя —
        /// о нём говорит <see cref="GeometryModel.CheckLayers"/>.
        /// </summary>
        [XmlIgnore]
        public bool IsEmpty
        {
            get
            {
                return (this.Name == null || this.Name.Trim().Length == 0)
                       && this.Fractions.Count == 0
                       && !(this.Density > 0.0);
            }
        }

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
        /// ⛔ (`AMBER74`, П132 22.09.2026) Остаток считается СУММОЙ ТРЁХ
        /// КАНАЛОВ, а не разностью «прямая по сумме узлов минус канал
        /// когерентного»: разность складывала две интерполяции по разным
        /// правилам и несла ошибку первой целиком. См.
        /// <see cref="PartialCrossSections.MassTotalWithoutCoherent"/>.
        /// Оттого же отпала и прежняя оговорка «нет парциальных — берём полное»:
        /// каналы и сумма приходят из ОДНОЙ таблицы `xcom_cross_sections`, и
        /// «полное есть, каналов нет» не бывает.
        /// </summary>
        public double LinearAttenuationWithoutCoherent(double energyKev)
        {
            if (!(energyKev > 0.0))
            {
                // Как и прежде: ниже нуля шкалы обе части нулевые, сумма нулевая.
                return 0.0;
            }

            // ⚡ (`A43`) Все каналы — с ОДНОГО прохода по сетке элемента: она у
            // них общая, энергия одна, и логарифм от неё берётся один на всё
            // вещество. Было два поиска и два логарифма на каждый элемент.
            double logEnergyKev = Math.Log(energyKev);
            double massAttenuation = 0.0;
            foreach (KeyValuePair<int, double> pair in this.Fractions)
            {
                double value = 0.0;
                MaterialDatabase.Element element;
                int lo, hi;
                if (MaterialDatabase.TryGet(pair.Key, out element)
                    && element.EnergyKev != null
                    && MaterialDatabase.Bracket(element.EnergyKev, energyKev, out lo, out hi))
                {
                    value = PartialCrossSections.MassTotalWithoutCoherent(
                        element, lo, hi, energyKev, logEnergyKev);
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
        Borehole,

        /// <summary>
        /// ИЗОТРОПНОЕ ВНЕШНЕЕ ПОЛЕ (`AMBER13` (б), решение Amber 12.09.2026
        /// «Оставь только ISO»): прибор стоит в однородном изотропном поле
        /// квантов из 4π — геометрия облучения ICRP 74 «ISO». Пробы нет
        /// вовсе, и это единственный вид сцены, у которого ИСТОЧНИКА в
        /// сцене нет: сцена — это детектор в поле.
        ///
        /// Как считается. Кванты разыгрываются с поверхности сферы радиуса
        /// <see cref="GeometryModel.FieldRadius"/> вокруг детектора, ВНУТРЬ
        /// по закону косинуса — так внутри сферы получается однородное
        /// изотропное поле (ламбертова сфера; проверено розыгрышем 12.09.2026:
        /// флюенс в точках 0…0.9 R одинаков до 0.1 %, у изотропного розыгрыша
        /// с поверхности он растёт к стенке в 1.63 раза). Радиус ответа не
        /// меняет — это и есть даровой контроль правильности (посчитать на
        /// 50 и 100 см, числа обязаны совпасть).
        ///
        /// ⛔ НОРМИРОВКА ДРУГАЯ. У всех прочих сцен строка матрицы и кривая
        /// нормированы «на квант, испущенный источником в 4π», а для внешнего
        /// поля такой нормировки не существует — поле задаётся ФЛЮЕНСОМ. У
        /// сцены ISO отклик нормирован на ЕДИНИЧНЫЙ ФЛЮЕНС (1 квант/см²), то
        /// есть строка матрицы и кривая — ЭФФЕКТИВНАЯ ПЛОЩАДЬ в см², а не
        /// доля (<see cref="ResponseMatrixNormalization.PerUnitFluence"/>).
        /// Перевод: флюенс на квант, испущенный ламбертовой сферой радиуса R,
        /// равен 1/(πR²), значит A_эфф = π·R²·ε_R — множитель сидит в весе
        /// истории (<c>EfficiencySimulator.IsoFieldSampler</c>).
        ///
        /// Форма источника в файле — <see cref="GeometrySourceType.Point"/>:
        /// у поля, как у точки, нет ни сосуда, ни пробы, а программа ЛСРМ,
        /// не знающая ключа <c>DS_Scene</c>, прочитает файл как точечный
        /// источник на расстоянии радиуса поля — осмысленная подмена, а не
        /// мусор. Потребитель — мощность дозы (`AMBER18`): она делит скорость
        /// счёта на A_эфф(E) и получает флюенс, а из флюенса — H*(10).
        /// </summary>
        Iso
    }

    /// <summary>
    /// (`AMBER282`) Форма колодца маринелли В ПЛАНЕ — НАШЕ расширение формата. У ЛСРМ колодец
    /// только круглый (`SM_BeakerHoleDiameter`); трапеция пишется ключами `SM_BeakerHole*`,
    /// которых их программа не читает, и прочтёт такой файл как круглый колодец своего диаметра.
    /// </summary>
    public enum GeometryHoleShape
    {
        /// <summary>Круг диаметром <see cref="GeometryModel.MarinelliHoleDiameter"/> — как было всегда.</summary>
        Round,

        /// <summary>
        /// Равнобокая трапеция: основания <see cref="GeometryModel.MarinelliHoleBase1"/> и
        /// <see cref="GeometryModel.MarinelliHoleBase2"/> вдоль x, расстояние между ними
        /// <see cref="GeometryModel.MarinelliHoleWidth"/> вдоль y, середина высоты — на оси.
        /// Прямоугольник — частный случай (основания равны).
        /// </summary>
        Trapezoid
    }

    /// <summary>
    /// (`AMBER282`) Один слой ЗАЩИТЫ, которая входит в перенос: вещество и толщины — боковой
    /// стенки, дна (сторона за пробой, дальняя от детектора) и крышки (за детектором). Слои
    /// идут ИЗНУТРИ наружу: первый облегает полость, каждый следующий — предыдущий. Решение
    /// Amber 02.11.2026 вопросником, дословно: «Ответ номер 1: "Стальной оболочкой в расчёте
    /// (Рекомендую)". При включении галки "в защите" добавлять новые поля сцены с материалом.»;
    /// следом — «Т.е. защита из слоёв состоит».
    /// </summary>
    public sealed class GeometryShieldLayer
    {
        public GeometryMaterial Material = new GeometryMaterial();

        /// <summary>Толщина боковой стенки слоя, мм.</summary>
        public double SideThickness;

        /// <summary>Толщина дна слоя (за пробой, со стороны, дальней от детектора), мм.</summary>
        public double FloorThickness;

        /// <summary>Толщина крышки слоя (за детектором), мм.</summary>
        public double LidThickness;

        public GeometryShieldLayer Clone()
        {
            return new GeometryShieldLayer
            {
                Material = this.Material != null ? this.Material.Clone() : new GeometryMaterial(),
                SideThickness = this.SideThickness,
                FloorThickness = this.FloorThickness,
                LidThickness = this.LidThickness,
            };
        }
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

        /// <summary>
        /// Измерение в ЗАЩИТЕ: детектор со спектром стоит в свинцовом
        /// домике (`AMBER12`, задача Amber 09.09.2026).
        ///
        /// ⛔ Признак НЕ участвует в переносе и НЕ входит в клеймо матрицы
        /// — см. <see cref="ResponseMatrix"/>.StampView. Сцена расчёта
        /// осталась прежней: кристалл, обвязка и проба, а домик в неё не
        /// кладётся. Он говорит другое — что вокруг детектора ЕСТЬ
        /// обстановка, в которой квант, ушедший мимо кристалла,
        /// рассеивается назад и возвращается. Внутриприборное рассеяние
        /// матрица считает сама (`A83`, `A55`), рассеяние в обстановке
        /// не считает никто, и при этом признаке разбор возвращает поверх
        /// матрицы аналитический образ обратного рассеяния
        /// (<c>FsaAnalyzer.BackscatterWithMatrix</c>).
        ///
        /// Умолчание — защиты нет: так читаются все геометрии, снятые до
        /// 09.09.2026, все файлы ЛСРМ и весь корпус, и чисел им это не
        /// двигает.
        /// </summary>
        public bool InShield;

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

                if (this.Shape == CrystalShape.Box)
                {
                    return "";
                }

                // (`AMBER201`, Р4, мелочь 9.2) Строкой ресурсов, а не русским
                // литералом: свойство теперь читают — журнал, вкладка
                // эффективности, отказ счёта кривой и матрицы.
                string text = Properties.Resources.ResourceManager.GetString(
                    "GeometryFacingSideNeedsBox", Properties.Resources.Culture);
                return string.IsNullOrEmpty(text)
                    ? "The sample at the side of the detector is defined for a box crystal only:"
                      + " for a cylinder it is not axially symmetric and the scene cannot express it."
                    : text;
            }
        }

        /// <summary>
        /// (`AMBER201`, Р4, мелочь G.5) Есть ли у модели размеры кристалла-
        /// сцинтиллятора его формы — то, без чего геометрия не геометрия.
        /// Читатель `.in` на чужом тексте не падает (любой файл даёт модель с
        /// нулевым кристаллом), поэтому «прочиталось» само по себе ничего не
        /// значит; признак тот же, что был у снятого импорта ЛСРМ вкладки
        /// прибора (`AMBER227`). Метод, а не свойство: в
        /// XML модели он не попадает и попасть не должен.
        /// </summary>
        public bool HasScintillatorCrystal()
        {
            bool crystal = this.Shape == CrystalShape.Box
                ? this.CrystalBoxX > 0.0 && this.CrystalBoxY > 0.0 && this.CrystalBoxZ > 0.0
                : this.CrystalDiameter > 0.0 && this.CrystalHeight > 0.0;
            return crystal && (this.Raw.Count > 0 || this.IsScintillator);
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
        /// энергией — степенной, ПШПВ(E) = ПШПВ(662)·(E/662)^p с показателем
        /// <see cref="FwhmPowerLaw"/> (`AMBER135`, П164 28.09.2026; до того —
        /// корневой, p = ½). Своей ПШПВ-калибровки у геометрии нет.
        /// </summary>
        public double FwhmAt662Percent;

        /// <summary>
        /// ⛔ (`AMBER135`, П164 28.09.2026, физика 24) Показатель хода ПШПВ с
        /// энергией для допуска пика кривой: ПШПВ(E) = ПШПВ(662)·(E/662)^p.
        ///
        /// Корень (p = ½, «статистика света») внизу шкалы у сцинтиллятора ШИРЕ
        /// настоящего пика: у ПШПВ есть и не статистический член (ПШПВ² =
        /// a + bE + cE², GEB; Knoll гл. 10), и ширина растёт быстрее корня.
        /// Замер закрытой `V2` по корпусу — степень у сцинтилляторов 0.569…0.775
        /// (ASN16 0.569, RC103 0.585, G1S16 0.614, G1S24 0.616, AS80x80 0.656);
        /// степенные ПШПВ-калибровки самих спектров корпуса дают на 46…122 кэВ
        /// действующий показатель 0.57…0.64. Корневой допуск на 46…122 кэВ был
        /// в 1.15…1.45 раза шире ПШПВ спектров, и в «пик» кривой попадало
        /// однократное рассеяние с потерей, которой в пике спектра нет: кривая
        /// маринелли RC-103 завышена на +1.9…+3.9 %, маринелли G1S на
        /// +1.7…+2.3 %, точка впритык AS80 на +0.9…+1.6 % (`PeakTolProbeP164`,
        /// одно зерно, 2 млн историй). Показатель 0.6 — один на все приборы:
        /// у RC-103 при её `DS_Fwhm662` 8.49 % он даёт на 59.5 кэВ 13.3 кэВ при
        /// 13.2 по калибровке спектра, у G1S (6.44 %) — 10.0 при 9.5…10.9.
        /// Матрицу склада не задевает: её допуск — полубин
        /// (`ResponseMatrixOptions.PeakToleranceHalfBin`), геометрия его не
        /// даёт; κ из геометрии берёт допуск только под
        /// `PeakToleranceFromGeometry` (выкл умолчанием).
        /// </summary>
        public const double FwhmPowerLaw = 0.6;

        /// <summary>
        /// Допуск пика для энергии: половина ПШПВ(E), кэВ, ход — степенью
        /// <see cref="FwhmPowerLaw"/> от 662 кэВ. Ноль, если разрешение не
        /// задано, — тогда счёт прежний, строгий.
        /// </summary>
        public double PeakHalfWidthKev(double energyKev)
        {
            if (!(this.FwhmAt662Percent > 0.0) || !(energyKev > 0.0))
            {
                return 0.0;
            }

            // ПШПВ(E) = ПШПВ%(662)/100 · 662 · (E/662)^p
            return 0.5 * this.FwhmAt662Percent / 100.0 * 662.0 * Math.Pow(energyKev / 662.0, FwhmPowerLaw);
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

        /// <summary>
        /// Радиус сферы, с которой разыгрывается изотропное поле
        /// (<see cref="GeometrySceneKind.Iso"/>), мм. Читается только у сцены
        /// ISO; у остальных — ноль и в файл не пишется (правило «нет отличия
        /// — нет строки», клеймо прежних сцен не меняется).
        ///
        /// Ответ от радиуса НЕ ЗАВИСИТ, пока сфера накрывает весь детектор с
        /// обвязкой — это свойство ламбертовой сферы, а не допущение, и оно
        /// же служит проверкой (`IsoFieldProbe`: 50 и 100 см обязаны сойтись).
        /// Умолчание и нижнюю границу ставит <c>GeometryScenes.Iso</c>.
        /// </summary>
        public double FieldRadius;

        public double BeakerToDetectorDistance;
        public double BeakerDiameter;
        public double BeakerHeight;
        public double BeakerSideWallThickness;
        public double BeakerEndWallThickness;
        public double SourceHeight;

        /// <summary>
        /// (`AMBER274`, полоса `f8cyl` 02.11.2026) ДАЛЬНЕЕ ДНО ЦИЛИНДРА, мм — донышко сосуда со
        /// стороны, ДАЛЬНЕЙ от детектора, того же вещества, что стенка (<see cref="BeakerWall"/>).
        /// Решение Amber 30.10.2026 вопросником, дословно: «Завести поле цилиндру
        /// (Рекомендую)»; повод — критик бета-спектрометра 30.10.2026: металлическое дно
        /// чашки 0.8 мм (решение Amber «Сталь (магнит липнет)») в сцену не входило, а бета пробы
        /// обратно рассеивается и тормозит в нём (+8.9 % у Sr-90 5 г). Поле — по образцу
        /// <see cref="MarinelliEndWallThickness"/> (`AMBER205`): сосуд высотой
        /// <see cref="BeakerHeight"/> (снаружи) кончается этим дном; запас стенки над пробой —
        /// <c>BeakerHeight − BeakerEndWallThickness − SourceHeight − BeakerFarEndWallThickness</c>;
        /// сосуд короче — дно вплотную к пробе. Раскладка — <c>EfficiencySimulator.CylinderVessel</c>
        /// (сцена счёта, клеймо и эскиз — одна). Дно видят и фотон, и электрон (это область сцены).
        /// ⛔ Ноль — умолчание: дна нет, сцена, клеймо, отпечаток геометрии, текст `.in` и XML
        /// побайтно прежние (в `.in` ключ `SC_BeakerFarEndWallThickness` и в XML элемент — только
        /// при ненулевом значении).
        /// </summary>
        public double BeakerFarEndWallThickness;

        /// <summary>(`f8cyl`) XML — только при ненулевом дальнем дне: прежние конфигурации прежними до знака.</summary>
        public bool ShouldSerializeBeakerFarEndWallThickness()
        {
            return this.BeakerFarEndWallThickness > 0.0;
        }

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

        /// <summary>
        /// (`AMBER282`, задача Amber 02.11.2026, дословно: «нужно добавить геометрию на базе сосуда
        /// маринелли, но колодец не круглый, а трапецевидной формы») ФОРМА КОЛОДЦА В ПЛАНЕ. Умолчание —
        /// круг: так читаются все прежние файлы, конфигурации и файлы ЛСРМ, и сцена, клеймо, текст
        /// `.in` и XML у них побайтно прежние (ключи и элементы трапеции — только у трапеции).
        /// У трапеции <see cref="MarinelliHoleDiameter"/> не читает никто; размеры — три поля ниже,
        /// ВНУТРЕННИЕ (просвет колодца), как и диаметр круглого: стенка
        /// <see cref="MarinelliHoleSideThickness"/> ложится НАРУЖУ параллельным сдвигом каждой грани.
        /// </summary>
        public GeometryHoleShape MarinelliHoleShape = GeometryHoleShape.Round;

        /// <summary>(`AMBER282`) Основание трапеции у грани y = −w/2 (просвет), мм.</summary>
        public double MarinelliHoleBase1;

        /// <summary>(`AMBER282`) Основание трапеции у грани y = +w/2 (просвет), мм.</summary>
        public double MarinelliHoleBase2;

        /// <summary>(`AMBER282`) Расстояние между основаниями — высота трапеции в плане (просвет), мм.</summary>
        public double MarinelliHoleWidth;

        /// <summary>(`AMBER282`) Колодец этой модели — трапеция (у прочих сцен поля спят).</summary>
        [XmlIgnore]
        public bool HasTrapezoidHole
        {
            get
            {
                // Полевая лунка (`Borehole`) — круг по построению: её размеры ставит формула.
                return this.SourceType == GeometrySourceType.Marinelli && this.Scene == GeometrySceneKind.None
                       && this.MarinelliHoleShape == GeometryHoleShape.Trapezoid;
            }
        }

        public bool ShouldSerializeMarinelliHoleShape()
        {
            return this.MarinelliHoleShape != GeometryHoleShape.Round;
        }

        public bool ShouldSerializeMarinelliHoleBase1()
        {
            return this.MarinelliHoleShape != GeometryHoleShape.Round;
        }

        public bool ShouldSerializeMarinelliHoleBase2()
        {
            return this.MarinelliHoleShape != GeometryHoleShape.Round;
        }

        public bool ShouldSerializeMarinelliHoleWidth()
        {
            return this.MarinelliHoleShape != GeometryHoleShape.Round;
        }

        /// <summary>
        /// (`AMBER282`) Трапеция колодца в плане, сдвинутая НАРУЖУ на <paramref name="offset"/>
        /// (0 — просвет; толщина стенки — наружный контур гильзы): четыре вершины против часовой
        /// стрелки (x, y), в единицах модели. Грани сдвигаются параллельно — стенка одной толщины
        /// по нормали к каждой грани, как у прямоугольной кюветы; наклон боковых граней сохраняется.
        /// Ложь — трапеции нет (не та форма или размеры не положительны).
        /// </summary>
        public bool TrapezoidHoleVertices(double offset, double[] xs, double[] ys)
        {
            double a = this.MarinelliHoleBase1, b = this.MarinelliHoleBase2, w = this.MarinelliHoleWidth;
            if (!this.HasTrapezoidHole || !(a > 0.0) || !(b > 0.0) || !(w > 0.0))
            {
                return false;
            }

            // Боковая грань: x = c + k·y, c = (a + b)/4 — полуширина на середине, k = (b − a)/(2w).
            // Параллельный сдвиг грани на t по нормали сдвигает её по x на t·√(1 + k²).
            double k = (b - a) / (2.0 * w);
            double c = 0.25 * (a + b) + offset * Math.Sqrt(1.0 + k * k);
            double h = 0.5 * w + offset;
            xs[0] = -(c - k * h); ys[0] = -h;
            xs[1] = c - k * h;    ys[1] = -h;
            xs[2] = c + k * h;    ys[2] = h;
            xs[3] = -(c + k * h); ys[3] = h;
            return true;
        }

        /// <summary>(`AMBER282`) Площадь трапеции колодца, сдвинутой наружу на <paramref name="offset"/>, в квадрате единиц модели; 0 — трапеции нет.</summary>
        public double TrapezoidHoleArea(double offset)
        {
            double[] xs = new double[4], ys = new double[4];
            if (!this.TrapezoidHoleVertices(offset, xs, ys))
            {
                return 0.0;
            }

            return 0.5 * ((xs[1] - xs[0]) + (xs[2] - xs[3])) * (ys[2] - ys[1]);
        }

        /// <summary>
        /// (`AMBER282`) ЗАЩИТА В РАСЧЁТЕ — полость и слои. Полость — цилиндр на оси сцены
        /// диаметром <see cref="ShieldInnerDiameter"/> и высотой <see cref="ShieldInnerHeight"/>
        /// (от внутренней грани дна до внутренней грани крышки); дно лежит на
        /// <see cref="ShieldFloorGap"/> дальше дальнего края сцены (проба/сосуд стоят на дне при 0).
        /// Слои <see cref="ShieldLayers"/> — изнутри наружу. Строится, только когда поднят признак
        /// <see cref="InShield"/> и слой есть (<see cref="ShieldInScene"/>): тогда защиту видят
        /// фотон и электрон, она входит в клеймо матрицы, а разбор формулу обратного рассеяния НЕ
        /// добавляет — рассеяние уже в матрице. Без слоёв признак значит прежнее (`AMBER12`).
        /// ⛔ Пустые слои — умолчание: сцена, клеймо, текст `.in` и XML побайтно прежние.
        /// </summary>
        public double ShieldInnerDiameter;

        /// <summary>(`AMBER282`) Высота полости защиты (дно — крышка, внутренние грани), мм.</summary>
        public double ShieldInnerHeight;

        /// <summary>(`AMBER282`) Зазор от дальнего края сцены (за пробой) до внутренней грани дна, мм.</summary>
        public double ShieldFloorGap;

        /// <summary>(`AMBER282`) Слои защиты изнутри наружу.</summary>
        [XmlArray("ShieldLayers")]
        [XmlArrayItem("Layer")]
        public List<GeometryShieldLayer> ShieldLayers = new List<GeometryShieldLayer>();

        bool ShieldToXml
        {
            get { return this.ShieldLayers != null && this.ShieldLayers.Count > 0; }
        }

        public bool ShouldSerializeShieldLayers()
        {
            return this.ShieldToXml;
        }

        public bool ShouldSerializeShieldInnerDiameter()
        {
            return this.ShieldToXml;
        }

        public bool ShouldSerializeShieldInnerHeight()
        {
            return this.ShieldToXml;
        }

        public bool ShouldSerializeShieldFloorGap()
        {
            return this.ShieldToXml;
        }

        /// <summary>
        /// (`AMBER282`) Защита ВХОДИТ в сцену переноса: признак поднят, сцена обычная (не поле —
        /// у земли, лунки и ISO своя постановка), есть слой и полость положительных размеров.
        /// </summary>
        [XmlIgnore]
        public bool ShieldInScene
        {
            get
            {
                return this.InShield && this.Scene == GeometrySceneKind.None && this.ShieldToXml
                       && this.ShieldInnerDiameter > 0.0 && this.ShieldInnerHeight > 0.0;
            }
        }

        /// <summary>(`AMBER282`) Наружный диаметр защиты: полость плюс боковые стенки всех слоёв, мм.</summary>
        [XmlIgnore]
        public double ShieldOuterDiameter
        {
            get
            {
                double d = this.ShieldInnerDiameter;
                if (this.ShieldLayers != null)
                {
                    foreach (GeometryShieldLayer layer in this.ShieldLayers)
                    {
                        d += 2.0 * Math.Max(0.0, layer.SideThickness);
                    }
                }

                return d;
            }
        }

        /// <summary>(`AMBER282`) Наружная высота защиты: полость, дно и крышка всех слоёв, мм.</summary>
        [XmlIgnore]
        public double ShieldOuterHeight
        {
            get
            {
                double h = this.ShieldInnerHeight;
                if (this.ShieldLayers != null)
                {
                    foreach (GeometryShieldLayer layer in this.ShieldLayers)
                    {
                        h += Math.Max(0.0, layer.FloorThickness) + Math.Max(0.0, layer.LidThickness);
                    }
                }

                return h;
            }
        }

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
        /// веществ, что отражатель и корпус. Умолчание ставит
        /// <see cref="ApplyGapDefault"/> — на КАЖДОМ входе геометрии в
        /// приложение (`AMBER47`), а не только в пресете и заготовке.
        /// </summary>
        public GeometryMaterial Gap = new GeometryMaterial();
        public GeometryMaterial Cladding = new GeometryMaterial();

        /// <summary>Имя вещества зазора по умолчанию — как в библиотеке и в файлах LSRM.</summary>
        public const string DefaultGapMaterialName = "Air, dry";

        /// <summary>
        /// Вещество зазора ПО УМОЛЧАНИЮ — сухой воздух (`AMBER47`, слово Amber
        /// 17.09.2026 дословно: «Занеси в шаблоны приложения - что там GAP по
        /// умолчанию - воздух (Air, dry). Если это влияет на корпус -
        /// перезапускай.»).
        ///
        /// Берётся из ДЕЙСТВУЮЩЕЙ библиотеки веществ (у человека она своя и
        /// правится), а если `Air, dry` там переименован или удалён — из
        /// засева <see cref="GeometryMaterialLibrary.Seed"/> тем же именем:
        /// умолчание не может зависеть от того, что человек сделал со своим
        /// списком. Плотность — библиотечная (0.001205 г/см³).
        ///
        /// ⚠ Состав считается из формулы через атомные массы, то есть первый
        /// вызов поднимает базу веществ (`matdb.sqlite`) — как любое
        /// обращение к библиотеке. Зовётся он только у геометрии, у которой
        /// вещества зазора НЕТ (см. <see cref="ApplyGapDefault"/>); у
        /// геометрии с заданным веществом до базы дело не доходит.
        /// </summary>
        public static GeometryMaterial DefaultGapMaterial()
        {
            GeometryMaterialLibrary.Entry entry = GeometryMaterialLibrary.ByName(DefaultGapMaterialName);
            if (entry == null)
            {
                foreach (GeometryMaterialLibrary.Entry seeded in GeometryMaterialLibrary.Seed())
                {
                    if (string.Equals(seeded.Name, DefaultGapMaterialName, StringComparison.OrdinalIgnoreCase))
                    {
                        entry = seeded;
                        break;
                    }
                }
            }

            return entry != null
                ? GeometryMaterialLibrary.Make(entry, entry.Density)
                : new GeometryMaterial();
        }

        /// <summary>
        /// Поставить зазору воздух, если вещества у него НЕТ (`AMBER47`).
        ///
        /// ⛔ ЗАДАННОЕ вещество не подменяется НИКОГДА — ни вода, ни вещество с
        /// именем без состава. Иначе это был бы `A303` наоборот: там редактор
        /// молча ставил пустому зазору первую строку списка (воду), и след
        /// этого до сих пор стоит в живом конфиге у «Точки» и «Точки 10 см»
        /// прибора Atom Spectra 80x80. Умолчание — для ПУСТОГО, явное — чьё бы
        /// оно ни было — остаётся явным; воду в зазоре человек чинит руками.
        ///
        /// Зовётся на трёх входах геометрии в приложение: чтение файла `.in`
        /// (<see cref="Load"/>), чтение конфигурации прибора и файла спектра
        /// (сеттер <c>EfficiencyConfigData.Geometry</c>) и наложение своего
        /// шаблона (<c>GeometryTemplate.Apply</c>). Пресеты и заготовка
        /// редактора ставят воздух сами с 08.09.2026.
        ///
        /// Клеймо матриц от этого не двигается: у зазора нулевой толщины
        /// вещество в клеймо не входит (<c>ResponseMatrix.StampView</c>,
        /// `dropGap`), а у шести корпусных сцен с ненулевым зазором воздух
        /// записан в файле явно. Измерено пробой `GapDefaultProbe` на всех 61
        /// файле `.in` корпуса и моделей.
        /// </summary>
        public void ApplyGapDefault()
        {
            if (this.Gap == null || this.Gap.IsEmpty)
            {
                this.Gap = DefaultGapMaterial();
            }
        }
        public GeometryMaterial BeakerWall = new GeometryMaterial();
        public GeometryMaterial Source = new GeometryMaterial();

        /// <summary>
        /// (`AMBER274`, Э1 29.10.2026) КАПСУЛА ТОЧЕЧНОГО ИСТОЧНИКА — вещество плоской
        /// оболочки активного пятна (плёнка или диск по обе стороны точки) и её
        /// толщина <see cref="SourceCapsuleThickness"/>, мм. Вещество плёнки и кольца
        /// читает ТОЛЬКО перенос первичной частицы пробы (строки электрона матрицы, слой
        /// «Бета»): потеря энергии бетой на пути t/|cos θ| до входа в сцену. Фотонный
        /// перенос вещества капсулы не видит, и в клеймо фотонной части матрицы оно НЕ
        /// входит (<c>ResponseMatrix.StampView</c>) — его ослабление фотонов при
        /// толщинах 0.1…1 мм пластика меньше 0.5 % выше 60 кэВ. ⛔ Видит фотонный
        /// перенос СДВИГ точки в капсуле (<see cref="SourceCapsuleOffset"/>, `Ф6`), и он в
        /// клейме есть. В XML — только у точечной сцены с капсулой не ОСГИ (`Д4`).
        ///
        /// ⛔ Умолчание — КАПСУЛА ОСГИ (решение Amber 29.10.2026 вопросником, дословно:
        /// «Паспорт ОСГИ»): полиимидная плёнка 0.05 мм к детектору и алюминиевое кольцо
        /// Ø25 × 3 мм вокруг активного пятна (<see cref="OsgiCapsule"/>). Так читается
        /// всякая точечная сцена БЕЗ ключа `DS_SourceCapsuleThickness` (все прежние файлы и
        /// файлы ЛСРМ); голая точка — ключ с нулём. Фотонной части матрицы умолчание не
        /// касается (клеймо его не видит).
        /// </summary>
        public GeometryMaterial SourceCapsule = OsgiFilm();

        /// <summary>(`AMBER274`) Толщина плёнки капсулы к детектору, мм; 0 — голая точка.</summary>
        public double SourceCapsuleThickness = OsgiFilmMm;

        /// <summary>
        /// (`AMBER274`) Кольцо-держатель капсулы (алюминий): высота вдоль оси, мм (0 —
        /// кольца нет), внутренний и внешний диаметры, мм. Частица, вылетевшая ближе к
        /// плоскости капсулы, чем край кольца, проходит его стенку радиально.
        /// </summary>
        public double SourceCapsuleRingHeight = OsgiRingHeightMm;

        public double SourceCapsuleRingInnerDiameter = OsgiRingInnerMm;

        public double SourceCapsuleRingOuterDiameter = OsgiRingOuterMm;

        /// <summary>
        /// (`AMBER274`) Устройство ОСГИ по описаниям типа (журнал `AMBER274` Э1, шаг 4):
        /// активная часть Ø ≤ 3 мм запаяна между двумя полиимидными плёнками не тоньше 50 мкм
        /// каждая, держатель — алюминиевое кольцо Ø25 × 3 мм. ⚠ Внутренний диаметр кольца в
        /// описаниях не назван — 20 мм допущением.
        /// </summary>
        public const double OsgiFilmMm = 0.05, OsgiRingHeightMm = 3.0, OsgiRingInnerMm = 20.0, OsgiRingOuterMm = 25.0;

        /// <summary>(`AMBER274`) Полиимид (NIST «Kapton polyimide film»): 1.42 г/см³.</summary>
        public static GeometryMaterial OsgiFilm()
        {
            var m = new GeometryMaterial { Name = "Kapton polyimide film", Density = 1.42 };
            m.Fractions[1] = 0.026362;
            m.Fractions[6] = 0.691133;
            m.Fractions[7] = 0.073270;
            m.Fractions[8] = 0.209235;
            return m;
        }

        /// <summary>(`AMBER274`) Алюминий кольца капсулы.</summary>
        public static GeometryMaterial OsgiRingMaterial()
        {
            var m = new GeometryMaterial { Name = "Aluminum", Density = 2.699 };
            m.Fractions[13] = 1.0;
            return m;
        }

        /// <summary>
        /// (`AMBER274`) Сцена, у которой капсула ЕСТЬ: точечный источник обычной сцены
        /// (у изотропного поля форма тоже «точка», но источника в сцене нет).
        /// </summary>
        [XmlIgnore]
        public bool HasSourceCapsule
        {
            get { return this.SourceType == GeometrySourceType.Point && this.Scene == GeometrySceneKind.None; }
        }

        /// <summary>
        /// (`AMBER274` Ф6, решение Amber 30.10.2026 вопросником, дословно: «На 1.5 мм и для
        /// фотонов (Рекомендую)») СДВИГ ТОЧКИ ОТ ПЛОСКОСТИ КАПСУЛЫ, в единицах модели (мм; у
        /// копии в сантиметрах — см): расстояние точечной сцены <see cref="PointDistance"/>
        /// — от плоскости капсулы, а сама точка внутри неё. С кольцом — середина кольца
        /// (h/2; у ОСГИ 1.5 мм), но не ближе плёнки; без кольца — за плёнкой (t); голая точка
        /// и не точечная сцена — 0, и тогда сцена прежняя до бита. Сдвиг видят И ФОТОНЫ
        /// (точка сцены, <c>EfficiencySimulator</c>), поэтому он входит в фотонное клеймо
        /// (<c>ResponseMatrix.GeometryText</c>); вещество плёнки и кольца фотоны не видят.
        ///
        /// ⛔ ОТКАТ Ф6 (критик `critpt30`, 02.11.2026; правило Amber 02.11.2026, дословно: «корпус у
        /// нас сейчас СТАБИЛЕН. Если появляется новый прибор - он должен доказать, что нужно менять
        /// весь корпус.», «Если есть сомнения у критиков - пусть отменяют свои решения»). Сдвиг
        /// НУЛЕВОЙ: точка сцены стоит на <see cref="PointDistance"/>, как до 30.10.2026, у фотона и у
        /// электрона. Почему: (1) положение пятна ОСГИ по высоте кольца в описаниях типа не названо —
        /// 1.5 мм было допущением «середина кольца», не замером; (2) расстояния точечных сцен корпуса
        /// и глубина кристалла шаблона «RadiaCode-103» (5.5 мм, П73) ИЗМЕРЕНЫ или подобраны по
        /// паспорту голой точкой (П71 +7 мм, П73, П219, П228) — положение пятна в них уже сидит, и
        /// сдвиг клал его второй раз; (3) неподогнанная контактная мерка `AS80_Cs137_0cm` (зазор
        /// 21.7 мм — слово Amber, не подбор) — 0.995 паспорта без сдвига и 1.076 со сдвигом; контакт
        /// ASN16 и без сдвига 1.17 (П99), со сдвигом 1.52. Перенос сдвиг считал верно (Geant4 `g4cf`:
        /// ОСГИ/голая 0.762 против нашей 0.758 на 662 кэВ у `RC103_point0`) — неверным было само
        /// положение точки. Вещество плёнки и кольца (раздел электрона) — как было; ветвь строки
        /// `srcoff` в <c>ResponseMatrix.GeometryText</c> и признак «кривая до сдвига точки» при
        /// нулевом сдвиге не срабатывают, и фотонное клеймо точечной сцены — прежнее, как у голой точки.
        /// </summary>
        [XmlIgnore]
        public double SourceCapsuleOffset
        {
            get
            {
                return 0.0;
            }
        }

        /// <summary>
        /// (`AMBER274`, Д4) Капсула уходит в XML конфигурации и спектра ТОЛЬКО у точечной
        /// сцены и только отличной от умолчания ОСГИ — иначе каждое «открыть — сохранить»
        /// дописало бы пять полей во все прежние файлы (правило «конфигурации прежними до
        /// знака», как у <see cref="RawList"/>). Нет элементов — читается умолчание ОСГИ.
        /// </summary>
        bool CapsuleToXml
        {
            get { return this.HasSourceCapsule && !this.IsOsgiCapsule; }
        }

        /// <summary>(`AMBER274`, Д4) Образец `XmlSerializer` «ShouldSerialize&lt;имя&gt;».</summary>
        public bool ShouldSerializeSourceCapsule()
        {
            return this.CapsuleToXml;
        }

        public bool ShouldSerializeSourceCapsuleThickness()
        {
            return this.CapsuleToXml;
        }

        public bool ShouldSerializeSourceCapsuleRingHeight()
        {
            return this.CapsuleToXml;
        }

        public bool ShouldSerializeSourceCapsuleRingInnerDiameter()
        {
            return this.CapsuleToXml;
        }

        public bool ShouldSerializeSourceCapsuleRingOuterDiameter()
        {
            return this.CapsuleToXml;
        }

        /// <summary>(`AMBER274`) Капсула — ровно умолчание ОСГИ (тогда в `.in` её строк нет).</summary>
        [XmlIgnore]
        public bool IsOsgiCapsule
        {
            get
            {
                if (this.SourceCapsuleThickness != OsgiFilmMm || this.SourceCapsuleRingHeight != OsgiRingHeightMm
                    || this.SourceCapsuleRingInnerDiameter != OsgiRingInnerMm
                    || this.SourceCapsuleRingOuterDiameter != OsgiRingOuterMm || this.SourceCapsule == null)
                {
                    return false;
                }

                GeometryMaterial f = OsgiFilm();
                if (this.SourceCapsule.Density != f.Density || this.SourceCapsule.Fractions.Count != f.Fractions.Count)
                {
                    return false;
                }

                foreach (KeyValuePair<int, double> kv in f.Fractions)
                {
                    double v;
                    if (!this.SourceCapsule.Fractions.TryGetValue(kv.Key, out v) || v != kv.Value)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

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
        /// Измерено на складе: у 17 импортированных из ЛСРМ файлов из 66 копия
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
            copy.SourceCapsule = this.SourceCapsule != null ? this.SourceCapsule.Clone() : new GeometryMaterial();
            // (`AMBER282`) Слои защиты — свои у копии, вместе с веществами.
            copy.ShieldLayers = new List<GeometryShieldLayer>();
            if (this.ShieldLayers != null)
            {
                foreach (GeometryShieldLayer layer in this.ShieldLayers)
                {
                    copy.ShieldLayers.Add(layer != null ? layer.Clone() : new GeometryShieldLayer());
                }
            }
            // Свои словарь и список, а не общие с исходником: копия для того и
            // делается, чтобы две геометрии не правились за одну.
            copy.Raw = new Dictionary<string, string>(this.Raw, StringComparer.OrdinalIgnoreCase);
            // (`AMBER200`) Копируются только предупреждения РАЗБОРА файла:
            // предупреждения слоёв не хранятся вовсе, а считаются по самой
            // модели при каждом чтении <see cref="Warnings"/> — у копии с
            // исправленным веществом старое предупреждение не переживёт правку.
            copy.ParseWarnings = new List<string>(this.ParseWarnings);
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
            g.FieldRadius *= factor;

            g.BeakerToDetectorDistance *= factor;
            g.BeakerDiameter *= factor;
            g.BeakerHeight *= factor;
            g.BeakerSideWallThickness *= factor;
            g.BeakerEndWallThickness *= factor;
            g.BeakerFarEndWallThickness *= factor;    // (`f8cyl`)
            g.SourceHeight *= factor;
            g.SourceCapsuleThickness *= factor;    // (`AMBER274`)
            g.SourceCapsuleRingHeight *= factor;
            g.SourceCapsuleRingInnerDiameter *= factor;
            g.SourceCapsuleRingOuterDiameter *= factor;

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
            g.MarinelliHoleBase1 *= factor;          // (`AMBER282`)
            g.MarinelliHoleBase2 *= factor;
            g.MarinelliHoleWidth *= factor;

            // (`AMBER282`) Защита в расчёте: полость и толщины слоёв (слои уже свои у копии).
            g.ShieldInnerDiameter *= factor;
            g.ShieldInnerHeight *= factor;
            g.ShieldFloorGap *= factor;
            foreach (GeometryShieldLayer layer in g.ShieldLayers)
            {
                layer.SideThickness *= factor;
                layer.FloorThickness *= factor;
                layer.LidThickness *= factor;
            }

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
        /// приложения. Измерено: 17 импортированных из ЛСРМ файлов склада из 66;
        /// у 44 корпусных разницы нет, там чужие блоки и так пусты.
        ///
        /// Цена — размер конфигурации, и она урезана: в XML едет НЕ весь
        /// разбор, а только то, что писатель действительно переносит
        /// (<see cref="GeometryWriter.CarriedFrom"/>) — на `Nano16Pro.in` это
        /// 55 ключей из 191. У геометрии, собранной в редакторе с нуля, разбор
        /// пуст и в XML не появляется вовсе, то есть конфигурации таких
        /// приборов не меняются ни на знак.
        ///
        /// ⛔ ЗАПАС В УРЕЗАННОМ НАБОРЕ ОСТАЁТСЯ НАВСЕГДА (`T154`, 10.09.2026).
        /// Хранится заведомо БОЛЬШЕ, чем писатель читает: замер 10.09.2026 по
        /// трём складам дерева (`tools/effmaker/models`,
        /// `tools/CORPUS/corpus/geometries`, `LSRM Geometries`, 65 файлов) —
        /// ключей в разборе 12881, хранится 3120, писателю нужно 1281.
        ///
        /// Сжимать «по нынешним числам» НЕЛЬЗЯ, и это не осторожность, а
        /// разряд: 1281 — величина, вычисленная ОДИН РАЗ при нынешних
        /// умолчаниях писателя. Умолчание, совпавшее с файлом, делает ключ
        /// «ненужным» ровно до тех пор, пока умолчание не поправят; сжатый по
        /// такому числу список перестал бы хватать МОЛЧА, а цена молчания
        /// известна — два разных `ResponseMatrix.ComputeStamp` у одной
        /// геометрии и часы пересчёта матрицы у человека, который ничего не
        /// правил (ровно то, чем была ~~`A139`~~). Тот же разряд, что `T114` и
        /// `A133`.
        ///
        /// ⛔ ЧИТАТЕЛЬ У ПРОТУХАНИЯ ЕСТЬ, И ОН МЕХАНИЧЕСКИЙ — проба
        /// `RawCarryProbe --carried=&lt;каталог с .in&gt;`. Она не читает
        /// писателя и не верит спискам: у КАЖДОГО ключа разбора по очереди
        /// убирается значение, текст `.in` собирается заново
        /// (<see cref="GeometryWriter.Render"/>), и изменившийся текст значит
        /// «ключ писателю НУЖЕН». Приёмка — «нет ни одного файла, у которого
        /// нужный писателю ключ не хранится»; 10.09.2026 таких файлов 0 из 65.
        /// То есть нужное множество вычисляется ИЗ ПИСАТЕЛЯ, как того и просила
        /// `T154`, — только не на лету при каждой записи, а проверкой, которую
        /// правка умолчания заведомо не переживёт молча.
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
                else if (scene.Equals("ISO", StringComparison.OrdinalIgnoreCase))
                {
                    // `AMBER13` (б): изотропное внешнее поле. Радиус сферы
                    // розыгрыша — своим ключом; нет ключа — ноль, и
                    // `GeometryScenes.Iso` поставит умолчание.
                    g.Scene = GeometrySceneKind.Iso;
                    g.FieldRadius = Len(kv, "DS_FieldRadius");
                }
            }

            // `AMBER12`: измерение в защите (свинцовый домик). Ключа нет —
            // защиты нет, то есть прежнее поведение и все файлы ЛСРМ.
            string shield;
            if (kv.TryGetValue("DS_Shield", out shield)
                && shield.Trim().Equals("YES", StringComparison.OrdinalIgnoreCase))
            {
                g.InShield = true;
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
            // (`f8cyl`) Дальнее дно цилиндра — наше расширение формата; нет ключа — дна нет.
            g.BeakerFarEndWallThickness = Len(kv, "SC_BeakerFarEndWallThickness");

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

            // (`AMBER282`) Трапеция колодца — наше расширение формата; ключа нет — круг, то есть
            // прежнее поведение и все файлы ЛСРМ.
            string holeShape;
            if (kv.TryGetValue("SM_BeakerHoleShape", out holeShape)
                && holeShape.Trim().Equals("TRAPEZOID", StringComparison.OrdinalIgnoreCase))
            {
                g.MarinelliHoleShape = GeometryHoleShape.Trapezoid;
                g.MarinelliHoleBase1 = Len(kv, "SM_BeakerHoleBase1");
                g.MarinelliHoleBase2 = Len(kv, "SM_BeakerHoleBase2");
                g.MarinelliHoleWidth = Len(kv, "SM_BeakerHoleWidth");
            }

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
                                 "DS_FractionTypeCrystal", g.ParseWarnings);
            g.Reflector = Material(kv, "DS_", "CrystalReflector", "M_DS_Reflector.MName",
                                   "DS_FractionTypeReflector", g.ParseWarnings);
            g.Cladding = Material(kv, "DS_", "CrystalCladding", "M_DS_Crystal_Cladding.MName",
                                  "DS_FractionTypeCrystalCladding", g.ParseWarnings);
            // (`AMBER1`) Вещество зазора. У файла БЕЗ этих ключей — а таковы
            // все файлы до 08.09.2026 и все чужие — разбор даёт пустое
            // вещество; толщина зазора у них тоже ноль, слоя нет вовсе.
            //
            // (`AMBER47`) Пустому зазору ставится ВОЗДУХ — решение Amber
            // 17.09.2026 дословно: «Занеси в шаблоны приложения - что там GAP
            // по умолчанию - воздух (Air, dry). Если это влияет на корпус -
            // перезапускай.» До этого здесь стоял запрет полосы П14 («разбор
            // чужого файла обязан прочесть ровно то, что в файле написано»),
            // и файл без блока зазора приезжал в редактор с ПУСТЫМ веществом:
            // человек, набрав толщину, получал предупреждение «нет вещества»
            // вместо воздуха. Запрет снят словом Amber.
            //
            // ⛔ Явно заданное вещество НЕ подменяется (`ApplyGapDefault`
            // трогает только пустое): файл с `M_DS_Gap.MName = Water, liquid`
            // читается водой — иначе это был бы `A303` наоборот.
            //
            // Клеймо матриц от подстановки не двигается: при нулевой толщине
            // вещество зазора в клеймо не входит (`ResponseMatrix.StampView`,
            // `dropGap` — блок «0 см, воздух» и файл без блока дают ОДНО клеймо
            // с 08.09.2026), а у шести корпусных сцен с ненулевым зазором
            // (`AS80_*`, `RC103_*`) воздух записан в файле явно и читается из
            // файла. Измерено пробой `GapDefaultProbe` на 61 файле `.in`.
            g.Gap = Material(kv, "DS_", "CrystalGap", "M_DS_Gap.MName",
                             "DS_FractionTypeGap", g.ParseWarnings);
            // (`AMBER274`, Э1) Капсула точечного источника — наше расширение формата.
            // ⛔ Ключа нет — умолчание ОСГИ (поля уже стоят им): так читаются все прежние
            // файлы и файлы ЛСРМ; ключ есть — капсула из файла, ноль — голая точка;
            // кольцо — своими ключами, нет ключа — кольца нет.
            if (kv.ContainsKey("DS_SourceCapsuleThickness"))
            {
                g.SourceCapsuleThickness = Len(kv, "DS_SourceCapsuleThickness");
                g.SourceCapsuleRingHeight = Len(kv, "DS_SourceCapsuleRingHeight");
                g.SourceCapsuleRingInnerDiameter = Len(kv, "DS_SourceCapsuleRingInnerDiameter");
                g.SourceCapsuleRingOuterDiameter = Len(kv, "DS_SourceCapsuleRingOuterDiameter");
                g.SourceCapsule = g.SourceCapsuleThickness > 0.0
                    ? Material(kv, "DS_", "SourceCapsule", "M_DS_SourceCapsule.MName",
                               "DS_FractionTypeSourceCapsule", g.ParseWarnings)
                    : new GeometryMaterial();
            }
            g.ApplyGapDefault();

            // (`AMBER282`) Слои защиты в расчёте — наше расширение формата; нет ключа — слоёв нет,
            // и признак `DS_Shield` значит прежнее (`AMBER12`).
            int shieldLayers = (int)Num(kv, "DS_nShieldLayers");
            if (shieldLayers > 0)
            {
                g.ShieldInnerDiameter = Len(kv, "DS_ShieldInnerDiameter");
                g.ShieldInnerHeight = Len(kv, "DS_ShieldInnerHeight");
                g.ShieldFloorGap = Len(kv, "DS_ShieldFloorGap");
                for (int i = 0; i < shieldLayers; i++)
                {
                    string part = ShieldLayerPart(i);
                    g.ShieldLayers.Add(new GeometryShieldLayer
                    {
                        Material = Material(kv, "DS_", part, "M_DS_" + part + ".MName",
                                            "DS_FractionType" + part, g.ParseWarnings),
                        SideThickness = Len(kv, "DS_" + part + "SideThickness"),
                        FloorThickness = Len(kv, "DS_" + part + "FloorThickness"),
                        LidThickness = Len(kv, "DS_" + part + "LidThickness"),
                    });
                }
            }

            string prefix = g.SourceType == GeometrySourceType.Marinelli ? "SM_" : "SC_";
            g.BeakerWall = Material(kv, prefix, "Wall", "M_" + prefix + "Beaker.MName",
                                    prefix + "FractionTypeWall", g.ParseWarnings);
            g.Source = Material(kv, prefix, "Source", "M_" + prefix + "Source.MName",
                                prefix + "FractionTypeSource", g.ParseWarnings);
            // (`AMBER200`) Проверка слоёв здесь больше НЕ зовётся: она входит в
            // <see cref="Warnings"/> при каждом чтении, у геометрии из файла,
            // из редактора и из конфигурации одинаково.
            return g;
        }

        /// <summary>
        /// Что в разобранном файле `.in` выглядит подозрительно (неизвестное
        /// вещество, чужой тип долей). Пусто, если всё ясно или геометрия не из
        /// файла.
        ///
        /// Не хранится: это итог РАЗБОРА файла, а не свойство геометрии.
        /// </summary>
        [XmlIgnore]
        public List<string> ParseWarnings = new List<string>();

        /// <summary>
        /// Всё, о чём надо сказать перед счётом и при показе: предупреждения
        /// разбора файла (<see cref="ParseWarnings"/>) и проверка слоёв
        /// (<see cref="CheckLayers"/>), посчитанная ЗАНОВО по модели как она
        /// есть сейчас. Каждое чтение — новый список.
        ///
        /// ⛔ (`AMBER200`, 05.10.2026) Прежде это было ПОЛЕ, а проверка слоёв
        /// звалась один раз — внутри чтения `.in`. У геометрии из редактора и из
        /// конфигурации прибора (поле `[XmlIgnore]`, после перезапуска пустое)
        /// слой с толщиной без вещества уходил в счёт молчаливым вакуумом
        /// (<c>EfficiencySimulator.OrVacuum</c>), а копия (<see cref="Clone"/>)
        /// переносила старое предупреждение и после того, как вещество
        /// исправили.
        ///
        /// У обеих половин есть читатель: расчёт печатает это в журнал прогона,
        /// конструктор кривой — в свой, вкладка эффективности формы прибора —
        /// под чертежом.
        /// </summary>
        [XmlIgnore]
        public List<string> Warnings
        {
            get
            {
                List<string> all = new List<string>(this.ParseWarnings);
                all.AddRange(this.CheckLayers());
                // (`AMBER201`, Р4, мелочь 9.2) Боковая постановка у ЦИЛИНДРА.
                // `FacingError` прежде не читал никто (E21 обещал отказ словами),
                // и цилиндр с `DS_Facing = SIDE` из `.in` или правленого XML
                // считался с переставленной обвязкой молча. Теперь — строкой
                // здесь (журнал, вкладка), а счёт кривой и матрицы отказывает.
                string facing = this.FacingError;
                if (!string.IsNullOrEmpty(facing))
                {
                    all.Add(facing);
                }

                return all;
            }
        }

        /// <summary>
        /// Слой с толщиной, но без вещества. Разбирать это молча нельзя: области
        /// сцены вложены и ищутся по порядку, поэтому слой без плотности не
        /// исчезает, а ЗАМЕЩАЕТСЯ слоем снаружи — забыл плотность отражателя, и
        /// на его месте оказался алюминий корпуса, который тяжелее. Расчёт при
        /// этом доводится до конца и выдаёт правдоподобную, но чужую кривую.
        ///
        /// (`AMBER200`) Открыта и ничего не хранит: каждый вызов — проверка
        /// модели как она есть сейчас.
        /// </summary>
        public List<string> CheckLayers()
        {
            List<string> warnings = new List<string>();
            Action<double, GeometryMaterial, string> check = (thickness, material, caption) =>
            {
                if (thickness > 0.0 && (material == null || !(material.Density > 0.0)
                                        || material.Fractions.Count == 0))
                {
                    warnings.Add(string.Format(CultureInfo.InvariantCulture,
                        Properties.Resources.GeometryWarningNoMaterial, caption, thickness));
                }
            };

            this.VisitLayers(check);
            return warnings;
        }

        /// <summary>
        /// (`AMBER307`) Слой или кристалл с толщиной, но без вещества, — отказ счёта словами.
        /// Вещество пустеет, когда строку библиотеки удалили: шаблон
        /// (`GeometryPresets.Material`) кладёт пустое вещество и молчит. Замер 06.11.2026 на
        /// RC-110: пустой корпус, отражатель или зазор роняли кривую и матрицу
        /// `AggregateException` «вырожденная система сплайна», а без кристалла матрица считалась.
        /// Сцен с таким слоем в корпусе нет (0 из 51).
        /// </summary>
        public string MissingMaterialProblem()
        {
            string problem = null;
            Action<double, GeometryMaterial, string> check = (thickness, material, caption) =>
            {
                if (problem == null && thickness > 0.0
                    && (material == null || !(material.Density > 0.0) || material.Fractions.Count == 0))
                {
                    problem = string.Format(CultureInfo.InvariantCulture,
                        Properties.Resources.GeometryWarningNoMaterial, caption, thickness);
                }
            };

            double crystal = this.Shape == CrystalShape.Box
                ? Math.Min(this.CrystalBoxX, Math.Min(this.CrystalBoxY, this.CrystalBoxZ))
                : Math.Min(this.CrystalDiameter, this.CrystalHeight);
            check(crystal, this.Crystal, Properties.Resources.GeometryEditorCrystalMaterial);
            this.VisitLayers(check);
            return problem;
        }

        /// <summary>
        /// (`AMBER201`, Р4, подозрение G) Элемент вещества СЛОЯ СЦЕНЫ, которого
        /// нет в таблицах ослабления; null — все известны. Прежде так
        /// проверялся только кристалл (`EfficiencyCalculation.Run`), а в
        /// матрице — ничто: элемент вне таблиц в отражателе, корпусе, стенке
        /// или пробе выпадал из слоя молча (замер 05.10.2026: отражатель
        /// с Z = 150 — матрица построена без единого слова). Слои — те же, что
        /// у <see cref="CheckLayers"/>: с толщиной, то есть те, что сцена
        /// строит.
        /// </summary>
        public string UnknownElementProblem()
        {
            string problem = null;
            Action<double, GeometryMaterial, string> check = (thickness, material, caption) =>
            {
                int z;
                if (problem == null && thickness > 0.0 && material != null && !material.IsKnown(out z))
                {
                    string text = Properties.Resources.ResourceManager.GetString(
                        "GeometryUnknownElementInLayer", Properties.Resources.Culture);
                    if (string.IsNullOrEmpty(text))
                    {
                        text = "The material of \"{0}\" contains the element Z = {1}, which is absent from the"
                               + " attenuation tables: the layer would be counted without it.";
                    }

                    problem = string.Format(CultureInfo.InvariantCulture, text, caption, z);
                }
            };

            double crystal = this.Shape == CrystalShape.Box
                ? Math.Min(this.CrystalBoxX, Math.Min(this.CrystalBoxY, this.CrystalBoxZ))
                : Math.Min(this.CrystalDiameter, this.CrystalHeight);
            check(crystal, this.Crystal, Properties.Resources.GeometryEditorCrystalMaterial);
            this.VisitLayers(check);
            return problem;
        }

        /// <summary>Слои обвязки и пробы с их толщиной — одним списком для
        /// <see cref="CheckLayers"/> и <see cref="UnknownElementProblem"/>.</summary>
        void VisitLayers(Action<double, GeometryMaterial, string> check)
        {
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
                    wall = Math.Max(Math.Max(this.BeakerSideWallThickness, this.BeakerEndWallThickness),
                                    this.BeakerFarEndWallThickness);
                    break;
                default:
                    wall = 0.0;        // точечный источник вещества не имеет
                    break;
            }

            check(wall, this.BeakerWall, Properties.Resources.GeometryEditorWallMaterial);
            check(sample, this.Source, Properties.Resources.GeometryEditorSourceMaterial);

            // (`AMBER282`) Слои защиты — только когда защита в сцене (иначе их не строит никто).
            if (this.ShieldInScene)
            {
                for (int i = 0; i < this.ShieldLayers.Count; i++)
                {
                    GeometryShieldLayer layer = this.ShieldLayers[i];
                    double thickness = Math.Max(layer.SideThickness, Math.Max(layer.FloorThickness, layer.LidThickness));
                    check(thickness, layer.Material, ShieldLayerCaption(i));
                }
            }
        }

        /// <summary>(`AMBER282`) Подпись слоя защиты в предупреждениях: «Shield layer 1» / «Слой защиты 1».</summary>
        internal static string ShieldLayerCaption(int index)
        {
            return string.Format(CultureInfo.InvariantCulture,
                                 Res("GeometryEditorShieldLayerCaption", "Shield layer {0}"), index + 1);
        }

        /// <summary>(`AMBER282`) Строка ресурсов с английским запасом — тем же приёмом, что `GeometryFacingSideNeedsBox`.</summary>
        internal static string Res(string name, string fallback)
        {
            string text = Properties.Resources.ResourceManager.GetString(name, Properties.Resources.Culture);
            return string.IsNullOrEmpty(text) ? fallback : text;
        }

        /// <summary>(`AMBER282`) Имя слоя защиты в ключах `.in`: `ShieldLayer0`, `ShieldLayer1`, … — одно правило писателю и читателю.</summary>
        internal static string ShieldLayerPart(int index)
        {
            return "ShieldLayer" + index.ToString(CultureInfo.InvariantCulture);
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

            return LeadingNumber(v);
        }

        /// <summary>
        /// Число в начале значения `.in`: «5.03 cm» -> 5.03.
        ///
        /// (`AMBER201`, Р4, мелочь 9.3) Запятая как разделитель дробной части
        /// принимается наравне с точкой: «5,03 cm» прежде читалось как 5 —
        /// разбор брал `[0-9.]+` и обрывался на запятой, молча. Файл ЛСРМ,
        /// сохранённый на машине с русской культурой, такие числа и несёт; в
        /// дереве их нет (244 файла `.in`, 0 с запятой — замер 05.10.2026),
        /// поэтому клеймо ни одной матрицы правкой не задето. Разделителей
        /// разрядов у `.in` нет, поэтому одиночная запятая между цифрами
        /// однозначна. Зовёт и писатель (<c>GeometryWriter.Carried</c>) — одно
        /// правило на чтение и перенос.
        /// </summary>
        internal static double LeadingNumber(string v)
        {
            Match m = Regex.Match(v ?? "", @"^\s*(-?(?:[0-9]+(?:[.,][0-9]*)?|[.,][0-9]+)(?:[eE][-+]?[0-9]+)?)");
            double value;
            return m.Success && double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float,
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
                    // `AMBER13` (б): у поля форма источника точечная, но
                    // источника в сцене нет — называется поле и его радиус.
                    source = this.Scene == GeometrySceneKind.Iso
                        ? string.Format(CultureInfo.InvariantCulture,
                            Resources.GeometrySourceIso, this.FieldRadius)
                        : string.Format(CultureInfo.InvariantCulture,
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
                    if (this.HasTrapezoidHole)
                    {
                        // (`AMBER282`) У трапеции диаметра колодца нет — называются её размеры.
                        source += string.Format(CultureInfo.InvariantCulture,
                            Res("GeometrySourceTrapezoidHole", ", hole trapezoid {0:F1}/{1:F1}x{2:F1} mm"),
                            this.MarinelliHoleBase1, this.MarinelliHoleBase2, this.MarinelliHoleWidth);
                    }

                    break;
            }

            if (this.ShieldInScene)
            {
                // (`AMBER282`) Защита в расчёте — полость и наружный габарит.
                source += string.Format(CultureInfo.InvariantCulture,
                    Res("GeometrySourceShieldLayers", "; shield {0} layer(s) D={1:F1}/{2:F1} H={3:F1}/{4:F1} mm"),
                    this.ShieldLayers.Count, this.ShieldInnerDiameter, this.ShieldOuterDiameter,
                    this.ShieldInnerHeight, this.ShieldOuterHeight);
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
