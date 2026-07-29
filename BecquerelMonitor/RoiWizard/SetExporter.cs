using System;
using System.Collections.Generic;
using System.Drawing;

namespace BecquerelMonitor.RoiWizard
{
    // Сборка результата в объекты BecqMoni. Файлы не пишутся: конфигурация уходит в
    // ROIConfigManager, а записи набора — в NuclideDefinitionManager, то есть инструмент
    // работает как часть приложения, а не как генератор XML на диск.
    public class SetExporter
    {
        readonly ResolutionModel resolution;

        public SetExporter(ResolutionModel resolution) : this(resolution, null)
        {
        }

        public SetExporter(ResolutionModel resolution, ZoneCalculator zones)
        {
            this.resolution = resolution;
            this.Zones = zones ?? new ZoneCalculator(resolution);
        }

        // границы зоны считает отдельный калькулятор — он же уходит в проверки
        public ZoneCalculator Zones { get; private set; }


        public ROIConfigData BuildRoiConfig(IEnumerable<SpectralLine> lines, string name,
                                            Func<SpectralLine, Color> colorOf)
        {
            ROIConfigData config = new ROIConfigData();
            // ту же версию формата проставляет ROIConfigManager.CreateConfig; без неё
            // предпросмотр показывал бы пустой FormatVersion вместо того, что ляжет в файл
            config.InitFormatVersion();
            config.Guid = System.Guid.NewGuid().ToString();
            config.Name = string.IsNullOrEmpty(name) ? "IAEA lines" : name;
            config.LastUpdated = DateTime.Now;

            List<SpectralLine> ordered = Selected(lines);
            ordered.Sort(delegate(SpectralLine a, SpectralLine b)
            {
                int byLabel = string.CompareOrdinal(a.Label, b.Label);
                return byLabel != 0 ? byLabel : a.Energy.CompareTo(b.Energy);
            });

            foreach (SpectralLine line in ordered)
            {
                double lower, upper;
                this.Zones.LimitsFor(line, out lower, out upper);

                ROIDefinitionData roi = new ROIDefinitionData();
                roi.Name = line.Label;
                roi.Enabled = true;
                roi.PeakEnergy = line.Energy;
                roi.LowerLimit = lower;
                roi.UpperLimit = upper;
                roi.Color = colorOf != null ? colorOf(line) : Color.Red;
                // период полураспада в конфигурации ROI хранится в годах
                roi.HalfLife = line.HalfLifeYears >= 1e9 ? 0 : line.HalfLifeYears;
                roi.Intencity = line.Intensity;
                // Без примитива область не измеряет НИЧЕГО: MeasurementResultManager
                // .CalculateROI обходит только roi.ROIPrimitives, и на пустом списке цикл
                // не выполняется — метод возвращает true с count = 0, а панель результата
                // показывает «0.00 ± 0.00 (0.00 %), MDA 0, ND» по всем областям. Границы
                // на графике при этом рисуются, поэтому со стороны это выглядит как
                // «нет сигнала», а не как «не задан способ подсчёта».
                AddDifferencePrimitive(roi);
                config.ROIDefinitions.Add(roi);
            }
            return config;
        }

        // Примитив собирается тем же способом, что в ROIConfigForm.button3_Click: тип
        // «BG difference» (сумма отсчётов в границах минус приведённый фон — считает
        // ROISimpleDifferenceData) с операцией «Addition». Границы примитив берёт из
        // самой области через InitFromDefinition, своих чисел здесь не появляется.
        //
        // Поиск по ИМЕНИ, а не по индексу списка: индекс — деталь порядка регистрации в
        // InitializeROIPrimitiveDefinitions, тогда как имя уходит в файл полем
        // PrimitiveType и по нему же читается обратно ROIConfigManager.
        static void AddDifferencePrimitive(ROIDefinitionData roi)
        {
            // В режиме МАРКЕРОВ границы области равны −10 (признак «зоны нет», запись
            // рисуется штрихом высотой по Intencity — см. ZoneCalculator.LimitsFor).
            // Примитив с таким интервалом не измеряет ничего: канал нижней границы равен
            // каналу верхней, и сумма отсчётов пуста, а EnergyToChannel(−10) вообще
            // способен выбросить OutofChannelException. Создать его — значит заменить
            // честное «примитива нет» на «примитив есть, но пустой», то есть спрятать
            // отсутствие настройки под видом настроенного. Маркерная разметка изначально
            // не предназначена для подсчёта площади, поэтому примитива здесь и не должно
            // быть; измеряют режимы «зоны» и «зоны с маркерами».
            if (!(roi.UpperLimit > roi.LowerLimit) || roi.LowerLimit < 0.0)
            {
                return;
            }
            ROIPrimitiveDefinition definition = FindPrimitive("BG difference");
            ROIPrimitiveOperation operation = FindOperation("Addition");
            if (definition == null || operation == null || definition.TypeOfData == null)
            {
                // таблицы примитивов не инициализированы (вне приложения — например в
                // консольной проверке каталога): область остаётся без примитива, как
                // было раньше, но мастер не падает на ровном месте
                return;
            }
            ROIPrimitiveData primitive =
                (ROIPrimitiveData)Activator.CreateInstance(definition.TypeOfData);
            primitive.Primitive = definition;
            primitive.PrimitiveType = definition.Name;
            primitive.Operation = operation;
            primitive.OperationType = operation.Name;
            primitive.InitFromDefinition(roi);
            roi.ROIPrimitives.Add(primitive);
        }

        static ROIPrimitiveDefinition FindPrimitive(string name)
        {
            List<ROIPrimitiveDefinition> all = ROIPrimitiveDefinition.Definitions;
            if (all == null)
            {
                return null;
            }
            foreach (ROIPrimitiveDefinition candidate in all)
            {
                if (string.Equals(candidate.Name, name, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
            return null;
        }

        static ROIPrimitiveOperation FindOperation(string name)
        {
            List<ROIPrimitiveOperation> all = ROIPrimitiveOperation.Operations;
            if (all == null)
            {
                return null;
            }
            foreach (ROIPrimitiveOperation candidate in all)
            {
                if (string.Equals(candidate.Name, name, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
            return null;
        }

        // Возвращает записи набора и сам NuclideSet. Вызывающая сторона добавляет их в
        // NuclideDefinitionManager — существующие нуклиды и наборы при этом не трогаются.
        public NuclideSet BuildNuclideSet(IEnumerable<SpectralLine> lines, string setName,
                                          Func<SpectralLine, Color> colorOf,
                                          SpectralLine anchorOverride,
                                          out List<NuclideDefinition> definitions)
        {
            List<SpectralLine> anchors = null;
            if (anchorOverride != null)
            {
                anchors = new List<SpectralLine>();
                anchors.Add(anchorOverride);
            }
            return this.BuildNuclideSet(lines, setName, colorOf, anchors,
                                        AnchorPicker.DefaultCount, out definitions);
        }

        // anchorOverride — якоря, выбранные руками (null = выбрать автоматически).
        // anchorCount действует только при автовыборе: сколько линий пометить IsAnchor.
        public NuclideSet BuildNuclideSet(IEnumerable<SpectralLine> lines, string setName,
                                          Func<SpectralLine, Color> colorOf,
                                          IList<SpectralLine> anchorOverride, int anchorCount,
                                          out List<NuclideDefinition> definitions)
        {
            List<SpectralLine> ordered = Selected(lines);
            ordered.Sort(delegate(SpectralLine a, SpectralLine b) { return a.Energy.CompareTo(b.Energy); });

            NuclideSet set = new NuclideSet();
            set.Id = System.Guid.NewGuid();
            set.Name = string.IsNullOrEmpty(setName) ? "IAEA set" : setName;
            set.HideUnknownPeaks = false;

            List<SpectralLine> anchors = anchorOverride != null && anchorOverride.Count > 0
                ? new List<SpectralLine>(anchorOverride)
                : AnchorPicker.PickMany(ordered, this.resolution, anchorCount);
            definitions = new List<NuclideDefinition>();

            foreach (SpectralLine line in ordered)
            {
                NuclideDefinition definition = new NuclideDefinition();
                definition.Name = line.LibraryName;
                definition.Energy = line.Energy;
                // у нераспадных записей (ХРИ, вторичные) период полураспада не заполняется —
                // конвенция файла-образца BecqMoni
                definition.HalfLife = line.Type == LineType.Xrf || line.Type == LineType.Secondary
                    ? 0
                    : (line.HalfLifeYears >= 1e9 ? 0 : line.HalfLifeYears);
                definition.NuclideColor = colorOf != null ? colorOf(line) : Color.Gray;
                definition.Visible = true;
                // Аппаратным записям интенсивность не выставляется, и это не потеря
                // данных, а требование. Обратное рассеяние, комптоновский край, вылеты,
                // сумм-пики и ХРИ защиты в спектре ЕСТЬ и ловятся finder'ом — держать их
                // в наборе полезно: лучше именованный аппаратный пик, чем фантомная
                // линия нуклида на том же месте. Но выхода на распад у них нет: доля от
                // родителя — эмпирическая оценка, интенсивность ХРИ условна (Kα1 = 100).
                //
                // Нулевая Intencity гарантирует, что такая запись не попадёт в BR-связку:
                // LibraryPeakFitter собирает bound-группу только из линий с Intencity > 0
                // (members.All(m => m.Intensity > 0)), поэтому запись остаётся одиночным
                // компонентом со свободной амплитудой. Заодно EnergySpectrumView не
                // посчитает по комптоновскому краю активность — там гейт Intencity > 0.
                definition.Intencity = line.Type == LineType.Xrf || line.Type == LineType.Secondary
                    ? 0.0
                    : line.Intensity;
                definition.Sets.Add(set.Id);
                definition.IsAnchor = Contains(anchors, line);
                definitions.Add(definition);
            }
            return set;
        }

        // Внесение построенных записей в библиотеку С ПЕРЕИСПОЛЬЗОВАНИЕМ существующих.
        // Прежде форма делала NuclideDefinitions.AddRange(definitions), то есть добавляла
        // новую NuclideDefinition на каждую линию каждого прогона, не проверяя, нет ли уже
        // записи с тем же именем и энергией: четыре прогона мастера превращали 118 записей
        // в 230, из них 36 групп дублей до четырёх копий одной линии («Tl-208 (Th-232)
        // 2614.511» четырежды). Дубли участвуют в отождествлении при «--- All Nuclides ---»,
        // засоряют комбобокс ROI-редактора и список в «Edit Nuclide Sets», а убрать их можно
        // только руками — это единственный след мастера, остающийся в пользовательском файле
        // после закрытия программы.
        //
        // Принадлежность записи набору хранится в Sets (HashSet<Guid>): одна запись законно
        // принадлежит нескольким наборам, именно это здесь и используется.
        //
        // Работает со СПИСКОМ, а не с NuclideDefinitionManager: так функция проверяется
        // харнессом (tools/RoiWizardCheck) без запуска приложения.
        public static void MergeIntoLibrary(List<NuclideDefinition> library, Guid setId,
                                            List<NuclideDefinition> built)
        {
            foreach (NuclideDefinition definition in built)
            {
                NuclideDefinition existing = FindSameLine(library, definition);
                if (existing == null)
                {
                    library.Add(definition);
                    continue;
                }
                existing.Sets.Add(setId);
                // Якорь — свойство ЗАПИСИ, а не пары «запись + набор»: LibraryPeakFitter
                // перебирает записи с IsAnchor по всей библиотеке. Поэтому флаг только
                // ПОДНИМАЕТСЯ: без него новый набор не запустит фит вовсе, а снятие
                // сломало бы чужой набор, которому та же запись служит якорем.
                if (definition.IsAnchor)
                {
                    existing.IsAnchor = true;
                }
                // цвет, видимость и интенсивность существующей записи не трогаются: их мог
                // настроить пользователь, а линия та же самая
            }
        }

        // Совпадение по имени и энергии. Энергия сравнивается с допуском: записи одной линии
        // приходят из одного каталога и совпадают побитно, но запись могла быть введена
        // руками или прочитана из чужого файла, а 0,001 кэВ заведомо меньше любого
        // физического различия двух линий.
        public const double SameEnergyKeV = 0.001;

        static NuclideDefinition FindSameLine(List<NuclideDefinition> library,
                                              NuclideDefinition built)
        {
            foreach (NuclideDefinition existing in library)
            {
                if (string.Equals(existing.Name, built.Name, StringComparison.Ordinal) &&
                    Math.Abs(existing.Energy - built.Energy) < SameEnergyKeV)
                {
                    return existing;
                }
            }
            return null;
        }

        // сравнение по ссылке: линия из набора и линия из списка якорей — один объект
        static bool Contains(List<SpectralLine> anchors, SpectralLine line)
        {
            foreach (SpectralLine anchor in anchors)
            {
                if (ReferenceEquals(anchor, line))
                {
                    return true;
                }
            }
            return false;
        }

        static List<SpectralLine> Selected(IEnumerable<SpectralLine> lines)
        {
            List<SpectralLine> result = new List<SpectralLine>();
            foreach (SpectralLine line in lines)
            {
                if (line.Selected)
                {
                    result.Add(line);
                }
            }
            return result;
        }
    }

}
