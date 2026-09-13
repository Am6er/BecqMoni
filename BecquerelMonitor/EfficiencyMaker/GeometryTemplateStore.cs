using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// Свой шаблон детектора (`AMBER24`, задача Amber 13.09.2026): то, что
    /// человек сохранил из редактора геометрий кнопкой «Клонировать» и правит
    /// кнопкой «Сохранить». Доступен любому прибору — шаблон не привязан к
    /// <c>DeviceConfig</c>, он лежит в общем файле конфигурации.
    ///
    /// ⛔ СОСТАВ — «Только детектор, как вшитые» (решение Amber 13.09.2026,
    /// вопросником). Шаблон переносит РОВНО те поля <see cref="GeometryModel"/>,
    /// которые задаёт вшитый пресет <see cref="GeometryPresets"/> — ни одним
    /// больше, ни одним меньше; список выписан по коду <c>GeometryPresets.Build</c>
    /// (помощники <c>Box</c>/<c>Cylinder</c>, <c>Wrapping</c>, <c>Crystal</c>,
    /// <c>Reflector</c>, <c>Fwhm</c> и прямое <c>FrontGapThickness</c> у 80x80):
    /// <list type="bullet">
    /// <item>форма и размеры кристалла: <c>Shape</c>, <c>CrystalDiameter</c>,
    /// <c>CrystalHeight</c>, <c>CrystalBoxX/Y/Z</c>;</item>
    /// <item>обвязка: <c>FrontReflectorThickness</c>, <c>SideReflectorThickness</c>,
    /// <c>FrontGapThickness</c>, <c>SideGapThickness</c>,
    /// <c>FrontCladdingThickness</c>, <c>SideCladdingThickness</c>,
    /// <c>MountingThickness</c>;</item>
    /// <item>вещества детектора: <c>Crystal</c>, <c>Reflector</c>, <c>Gap</c>,
    /// <c>Cladding</c> — с плотностью и составом, как они стоят в геометрии
    /// (у порошка плотность своя, не библиотечная);</item>
    /// <item>разрешение: <c>FwhmAt662Percent</c>.</item>
    /// </list>
    /// Источник, сосуд, проба, сцена, сторона к пробе (<c>Facing</c>), защита
    /// (<c>InShield</c>) и имя геометрии НЕ переносятся — их пресеты не трогают,
    /// и шаблон не трогает тоже: один и тот же кристалл меряют и в маринелли, и
    /// точечным источником.
    /// </summary>
    public sealed class GeometryTemplate
    {
        [XmlAttribute]
        public string Name = "";

        public CrystalShape Shape = CrystalShape.Cylinder;

        public double CrystalDiameter;
        public double CrystalHeight;
        public double CrystalBoxX;
        public double CrystalBoxY;
        public double CrystalBoxZ;

        public double FrontReflectorThickness;
        public double SideReflectorThickness;
        public double FrontGapThickness;
        public double SideGapThickness;
        public double FrontCladdingThickness;
        public double SideCladdingThickness;
        public double MountingThickness;

        public double FwhmAt662Percent;

        public GeometryMaterial Crystal = new GeometryMaterial();
        public GeometryMaterial Reflector = new GeometryMaterial();
        public GeometryMaterial Gap = new GeometryMaterial();
        public GeometryMaterial Cladding = new GeometryMaterial();

        public override string ToString()
        {
            return this.Name;
        }

        /// <summary>Снять детекторную часть с геометрии — новым шаблоном.</summary>
        public static GeometryTemplate FromModel(string name, GeometryModel g)
        {
            GeometryTemplate t = new GeometryTemplate { Name = name ?? "" };
            t.CaptureFrom(g);
            return t;
        }

        /// <summary>
        /// Переписать детекторную часть шаблона полями геометрии. Вещества —
        /// КОПИЯМИ: шаблон живёт дольше окна, и общая ссылка правилась бы за
        /// одно с геометрией.
        /// </summary>
        public void CaptureFrom(GeometryModel g)
        {
            this.Shape = g.Shape;
            this.CrystalDiameter = g.CrystalDiameter;
            this.CrystalHeight = g.CrystalHeight;
            this.CrystalBoxX = g.CrystalBoxX;
            this.CrystalBoxY = g.CrystalBoxY;
            this.CrystalBoxZ = g.CrystalBoxZ;

            this.FrontReflectorThickness = g.FrontReflectorThickness;
            this.SideReflectorThickness = g.SideReflectorThickness;
            this.FrontGapThickness = g.FrontGapThickness;
            this.SideGapThickness = g.SideGapThickness;
            this.FrontCladdingThickness = g.FrontCladdingThickness;
            this.SideCladdingThickness = g.SideCladdingThickness;
            this.MountingThickness = g.MountingThickness;

            this.FwhmAt662Percent = g.FwhmAt662Percent;

            this.Crystal = Copy(g.Crystal);
            this.Reflector = Copy(g.Reflector);
            this.Gap = Copy(g.Gap);
            this.Cladding = Copy(g.Cladding);
        }

        /// <summary>
        /// Наложить шаблон на геометрию — тем же ходом, что
        /// <c>GeometryPresets.Preset.Apply</c>: только детектор, всё остальное
        /// в геометрии остаётся как было.
        /// </summary>
        public void Apply(GeometryModel g)
        {
            g.Shape = this.Shape;
            g.CrystalDiameter = this.CrystalDiameter;
            g.CrystalHeight = this.CrystalHeight;
            g.CrystalBoxX = this.CrystalBoxX;
            g.CrystalBoxY = this.CrystalBoxY;
            g.CrystalBoxZ = this.CrystalBoxZ;
            // Размеры чужой формы — нули, как у вшитых (`A94`): поле, которое
            // можно задать и нельзя увидеть, — ловушка, а не запас.
            g.DropDeadCrystalSize();

            g.FrontReflectorThickness = this.FrontReflectorThickness;
            g.SideReflectorThickness = this.SideReflectorThickness;
            g.FrontGapThickness = this.FrontGapThickness;
            g.SideGapThickness = this.SideGapThickness;
            g.FrontCladdingThickness = this.FrontCladdingThickness;
            g.SideCladdingThickness = this.SideCladdingThickness;
            g.MountingThickness = this.MountingThickness;

            g.FwhmAt662Percent = this.FwhmAt662Percent;

            g.Crystal = Copy(this.Crystal);
            g.Reflector = Copy(this.Reflector);
            g.Gap = Copy(this.Gap);
            g.Cladding = Copy(this.Cladding);
        }

        public GeometryTemplate Clone()
        {
            GeometryTemplate copy = (GeometryTemplate)this.MemberwiseClone();
            copy.Crystal = Copy(this.Crystal);
            copy.Reflector = Copy(this.Reflector);
            copy.Gap = Copy(this.Gap);
            copy.Cladding = Copy(this.Cladding);
            return copy;
        }

        /// <summary>Перенять поля другого шаблона (имя тоже) — откат после неудачной записи.</summary>
        public void CopyFrom(GeometryTemplate other)
        {
            this.Name = other.Name;
            this.Shape = other.Shape;
            this.CrystalDiameter = other.CrystalDiameter;
            this.CrystalHeight = other.CrystalHeight;
            this.CrystalBoxX = other.CrystalBoxX;
            this.CrystalBoxY = other.CrystalBoxY;
            this.CrystalBoxZ = other.CrystalBoxZ;
            this.FrontReflectorThickness = other.FrontReflectorThickness;
            this.SideReflectorThickness = other.SideReflectorThickness;
            this.FrontGapThickness = other.FrontGapThickness;
            this.SideGapThickness = other.SideGapThickness;
            this.FrontCladdingThickness = other.FrontCladdingThickness;
            this.SideCladdingThickness = other.SideCladdingThickness;
            this.MountingThickness = other.MountingThickness;
            this.FwhmAt662Percent = other.FwhmAt662Percent;
            this.Crystal = Copy(other.Crystal);
            this.Reflector = Copy(other.Reflector);
            this.Gap = Copy(other.Gap);
            this.Cladding = Copy(other.Cladding);
        }

        /// <summary>
        /// Детекторная часть одной строкой, инвариантной культурой — для
        /// сравнения «поля X = поля A» в пробах и для проверки, что шаблон,
        /// снятый с геометрии и наложенный обратно, ничего не потерял. Имя
        /// в строку НЕ входит: сравниваются поля, а не подпись.
        /// </summary>
        public string Fingerprint()
        {
            StringBuilder sb = new StringBuilder();
            CultureInfo ic = CultureInfo.InvariantCulture;
            sb.Append("shape=").Append(this.Shape);
            sb.Append(";d=").Append(this.CrystalDiameter.ToString("R", ic));
            sb.Append(";h=").Append(this.CrystalHeight.ToString("R", ic));
            sb.Append(";bx=").Append(this.CrystalBoxX.ToString("R", ic));
            sb.Append(";by=").Append(this.CrystalBoxY.ToString("R", ic));
            sb.Append(";bz=").Append(this.CrystalBoxZ.ToString("R", ic));
            sb.Append(";fr=").Append(this.FrontReflectorThickness.ToString("R", ic));
            sb.Append(";sr=").Append(this.SideReflectorThickness.ToString("R", ic));
            sb.Append(";fg=").Append(this.FrontGapThickness.ToString("R", ic));
            sb.Append(";sg=").Append(this.SideGapThickness.ToString("R", ic));
            sb.Append(";fc=").Append(this.FrontCladdingThickness.ToString("R", ic));
            sb.Append(";sc=").Append(this.SideCladdingThickness.ToString("R", ic));
            sb.Append(";m=").Append(this.MountingThickness.ToString("R", ic));
            sb.Append(";fwhm=").Append(this.FwhmAt662Percent.ToString("R", ic));
            Describe(sb, "crystal", this.Crystal);
            Describe(sb, "reflector", this.Reflector);
            Describe(sb, "gap", this.Gap);
            Describe(sb, "cladding", this.Cladding);
            return sb.ToString();
        }

        static void Describe(StringBuilder sb, string key, GeometryMaterial m)
        {
            CultureInfo ic = CultureInfo.InvariantCulture;
            sb.Append(';').Append(key).Append('=');
            if (m == null)
            {
                sb.Append("<null>");
                return;
            }

            sb.Append(m.Name ?? "").Append('@').Append(m.Density.ToString("R", ic));
            List<int> order = new List<int>(m.Fractions.Keys);
            order.Sort();
            foreach (int z in order)
            {
                sb.Append('/').Append(z.ToString(ic)).Append(':')
                  .Append(m.Fractions[z].ToString("R", ic));
            }
        }

        static GeometryMaterial Copy(GeometryMaterial m)
        {
            return m != null ? m.Clone() : new GeometryMaterial();
        }
    }

    /// <summary>Файл своих шаблонов целиком.</summary>
    [XmlRoot("GeometryTemplates")]
    public sealed class GeometryTemplateConfig
    {
        [XmlArray("Templates")]
        [XmlArrayItem("Template")]
        public GeometryTemplate[] Templates;
    }

    /// <summary>
    /// Хранилище своих шаблонов детекторов: `config\GeometryTemplates.xml`,
    /// рядом с библиотекой веществ и по её же образцу
    /// (<see cref="GeometryMaterialStore"/>): путь через <see cref="Package"/>,
    /// <see cref="LoadError"/> при битом файле, <see cref="Reload"/> после
    /// правки снаружи, XML-сериализация.
    ///
    /// Вшитые детекторы (<see cref="GeometryPresets"/>, код) здесь НЕ лежат и
    /// отсюда не правятся — Amber: «Существующий список геометрий изменить
    /// нельзя. Можно только сохранить/удалить свою.» Засева и поколений, как у
    /// веществ, поэтому нет: файл целиком свой, и пустой файл — законное
    /// состояние.
    ///
    /// ⛔ Имя шаблона, совпадающее со вшитым или с другим своим (без учёта
    /// регистра), — отказ (<see cref="NameConflict"/>): в одном списке два
    /// одинаковых имени не различить, а «свой поверх вшитого» и был бы правкой
    /// вшитого списка.
    /// </summary>
    public static class GeometryTemplateStore
    {
        static List<GeometryTemplate> items;

        /// <summary>
        /// Подмена пути для ПРОБ: безоконный прогон пишет в свой временный
        /// каталог, а не в живой `%AppData%\BecqMoni\config` (он — только
        /// чтение). Пусто — путь штатный.
        /// </summary>
        public static string PathOverride;

        /// <summary>
        /// Чем кончилась загрузка файла. Пусто — всё в порядке (и когда файла
        /// нет вовсе: первое открытие). Непусто — своих шаблонов сейчас НЕТ,
        /// а запись заменила бы непрочитанный файл; поэтому редактор
        /// отказывает в записи и называет причину.
        /// </summary>
        public static string LoadError { get; private set; }

        public static string FilePath
        {
            get
            {
                return string.IsNullOrEmpty(PathOverride)
                    ? Package.GetInstance().GeometryTemplates
                    : PathOverride;
            }
        }

        public static List<GeometryTemplate> Items
        {
            get
            {
                EnsureLoaded();
                return items;
            }
        }

        /// <summary>Перечитать с диска — после правки файла снаружи.</summary>
        public static void Reload()
        {
            items = null;
            LoadError = null;
        }

        static void EnsureLoaded()
        {
            if (items != null)
            {
                return;
            }

            GeometryTemplateConfig config = null;
            try
            {
                string path = FilePath;
                if (File.Exists(path))
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(GeometryTemplateConfig));
                    using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                    {
                        config = (GeometryTemplateConfig)serializer.Deserialize(stream);
                    }
                }
            }
            catch (Exception e)
            {
                // Список остаётся пустым, а причина — названной: молча показать
                // человеку список без его шаблонов значит сказать ему, что они
                // пропали.
                LoadError = e.Message;
                config = null;
            }

            List<GeometryTemplate> list = new List<GeometryTemplate>();
            if (config != null && config.Templates != null)
            {
                foreach (GeometryTemplate t in config.Templates)
                {
                    if (t != null && !string.IsNullOrEmpty(t.Name))
                    {
                        list.Add(t);
                    }
                }
            }

            items = list;
        }

        /// <summary>Свой шаблон по имени (без учёта регистра); null — нет такого.</summary>
        public static GeometryTemplate Find(string name)
        {
            foreach (GeometryTemplate t in Items)
            {
                if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return t;
                }
            }

            return null;
        }

        /// <summary>Есть ли ВШИТЫЙ детектор с таким именем (без учёта регистра).</summary>
        public static bool IsBuiltinName(string name)
        {
            foreach (GeometryPresets.Preset preset in GeometryPresets.Items)
            {
                if (string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Чем имя не годится: см. <see cref="NameConflict"/>.</summary>
        public enum NameProblem
        {
            None,
            Empty,
            Builtin,
            Taken,
        }

        /// <summary>
        /// Годится ли имя новому или переименованному шаблону. <paramref name="except"/>
        /// — шаблон, чьё собственное имя не считается занятым (сам себя не занимает).
        /// </summary>
        public static NameProblem NameConflict(string name, GeometryTemplate except)
        {
            if (name == null || name.Trim().Length == 0)
            {
                return NameProblem.Empty;
            }

            if (IsBuiltinName(name))
            {
                return NameProblem.Builtin;
            }

            GeometryTemplate other = Find(name);
            if (other != null && !ReferenceEquals(other, except))
            {
                return NameProblem.Taken;
            }

            return NameProblem.None;
        }

        /// <summary>
        /// Свободное имя по образцу: само имя, если свободно, иначе «имя 2»,
        /// «имя 3», … — подсказка диалогу «Клонировать», чтобы предложенное
        /// имя не отказывалось с первого же «ОК».
        /// </summary>
        public static string FreeName(string sample)
        {
            string stem = (sample ?? "").Trim();
            if (stem.Length == 0)
            {
                stem = "detector";
            }

            if (NameConflict(stem, null) == NameProblem.None)
            {
                return stem;
            }

            for (int i = 2; i < 1000; i++)
            {
                string candidate = stem + " " + i.ToString(CultureInfo.InvariantCulture);
                if (NameConflict(candidate, null) == NameProblem.None)
                {
                    return candidate;
                }
            }

            return stem;
        }

        /// <summary>
        /// Новый свой шаблон из детекторной части геометрии — и сразу в файл.
        /// Имя проверяет вызывающий (<see cref="NameConflict"/>); здесь оно
        /// принимается как есть, обрезанное по краям.
        /// </summary>
        public static GeometryTemplate Add(string name, GeometryModel g)
        {
            GeometryTemplate t = GeometryTemplate.FromModel((name ?? "").Trim(), g);
            List<GeometryTemplate> next = new List<GeometryTemplate>(Items) { t };
            Write(next);
            return t;
        }

        /// <summary>
        /// Переписать свой шаблон детекторной частью геометрии — и в файл.
        /// Отказ записи возвращает шаблон в памяти к прежним полям: окно не
        /// должно показывать то, чего на диске нет.
        /// </summary>
        public static void Replace(GeometryTemplate t, GeometryModel g)
        {
            GeometryTemplate before = t.Clone();
            t.CaptureFrom(g);
            try
            {
                // ПО ИМЕНИ, а не по ссылке. Панель держит объект из списка,
                // каким он был при её постройке; после `Reload` (или из второго
                // открытого редактора) в хранилище лежит уже другой объект с
                // тем же именем, и правка по ссылке ушла бы в файл прежними
                // полями — измерено пробой `GeometryTemplateProbe`, плечо 2.
                List<GeometryTemplate> next = new List<GeometryTemplate>(Items);
                int i = IndexOf(next, t.Name);
                if (i >= 0)
                {
                    next[i] = t;
                }
                else
                {
                    next.Add(t);
                }

                Write(next);
            }
            catch (Exception)
            {
                t.CopyFrom(before);
                throw;
            }
        }

        /// <summary>Убрать свой шаблон — и из файла тоже. Тоже по имени (см. <see cref="Replace"/>).</summary>
        public static void Remove(GeometryTemplate t)
        {
            List<GeometryTemplate> next = new List<GeometryTemplate>(Items);
            int i = IndexOf(next, t.Name);
            if (i >= 0)
            {
                next.RemoveAt(i);
            }

            Write(next);
        }

        static int IndexOf(List<GeometryTemplate> list, string name)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Записать список целиком. Зовёт ТОЛЬКО редактор: конфигурация
        /// пользователя правится человеком, а не расчётом. Отказ записи —
        /// исключение наружу: список в памяти при этом НЕ меняется, чтобы
        /// окно не показывало шаблон, которого на диске нет.
        /// </summary>
        static void Write(List<GeometryTemplate> next)
        {
            GeometryTemplateConfig config = new GeometryTemplateConfig
            {
                Templates = next.ToArray(),
            };

            string path = FilePath;
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            XmlSerializer serializer = new XmlSerializer(typeof(GeometryTemplateConfig));
            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                serializer.Serialize(stream, config);
            }

            items = next;
            LoadError = null;
        }
    }
}
