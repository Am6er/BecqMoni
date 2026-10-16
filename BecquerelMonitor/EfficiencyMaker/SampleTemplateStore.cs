using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// Свой шаблон образца (`AMBER230`, задача Amber 16.10.2026): то же, что
    /// свой шаблон детектора (<see cref="GeometryTemplate"/>, `AMBER24`), но для
    /// вкладки «Источник» редактора геометрий. Не привязан к <c>DeviceConfig</c>
    /// и лежит в общем файле конфигурации — доступен любому прибору.
    ///
    /// ⛔ СОСТАВ — «Вся вкладка «Источник»» (решение Amber 16.10.2026,
    /// вопросником). Шаблон переносит ровно то, что задаётся на этой вкладке:
    /// <list type="bullet">
    /// <item>тип источника — форма и вид съёмки: <c>SourceType</c>, <c>Scene</c>;</item>
    /// <item>точка и поле: <c>PointDistance</c>, <c>FieldRadius</c>;</item>
    /// <item>цилиндрический сосуд — шесть полей <c>Beaker*</c> и <c>SourceHeight</c>;</item>
    /// <item>маринелли — десять полей <c>Marinelli*</c>;</item>
    /// <item>прямоугольная кювета — шесть полей <c>Box*</c>;</item>
    /// <item>вещества стенки и пробы: <c>BeakerWall</c>, <c>Source</c> — с
    /// плотностью и составом, как они стоят в геометрии;</item>
    /// <item>измерение в защите: <c>InShield</c>.</item>
    /// </list>
    /// Размеры ВСЕХ форм, а не только выбранной: так же их держит сама
    /// геометрия, и чужую форму редактор снимает сам (`A134`), когда собирает
    /// модель. Детектор, сторона к пробе (<c>Facing</c>, это пара «кристалл и
    /// проба», но поле стоит на вкладке детектора) и имя геометрии НЕ
    /// переносятся — один и тот же стакан ставят на разные приборы.
    /// </summary>
    public sealed class SampleTemplate
    {
        [XmlAttribute]
        public string Name = "";

        public GeometrySourceType SourceType = GeometrySourceType.Point;
        public GeometrySceneKind Scene = GeometrySceneKind.None;
        public bool InShield;

        public double PointDistance;
        public double FieldRadius;

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

        public double BoxSourceX;
        public double BoxSourceY;
        public double BoxSourceHeight;
        public double BoxToDetectorDistance;
        public double BoxSideWallThickness;
        public double BoxEndWallThickness;

        public GeometryMaterial BeakerWall = new GeometryMaterial();
        public GeometryMaterial Source = new GeometryMaterial();

        public override string ToString()
        {
            return this.Name;
        }

        /// <summary>Снять часть образца с геометрии — новым шаблоном.</summary>
        public static SampleTemplate FromModel(string name, GeometryModel g)
        {
            SampleTemplate t = new SampleTemplate { Name = name ?? "" };
            t.CaptureFrom(g);
            return t;
        }

        /// <summary>
        /// Переписать шаблон полями образца геометрии. Вещества — КОПИЯМИ, по
        /// тому же доводу, что у шаблона детектора: шаблон живёт дольше окна.
        /// </summary>
        public void CaptureFrom(GeometryModel g)
        {
            this.SourceType = g.SourceType;
            this.Scene = g.Scene;
            this.InShield = g.InShield;

            this.PointDistance = g.PointDistance;
            this.FieldRadius = g.FieldRadius;

            this.BeakerToDetectorDistance = g.BeakerToDetectorDistance;
            this.BeakerDiameter = g.BeakerDiameter;
            this.BeakerHeight = g.BeakerHeight;
            this.BeakerSideWallThickness = g.BeakerSideWallThickness;
            this.BeakerEndWallThickness = g.BeakerEndWallThickness;
            this.SourceHeight = g.SourceHeight;

            this.MarinelliBeakerDiameter = g.MarinelliBeakerDiameter;
            this.MarinelliBeakerHeight = g.MarinelliBeakerHeight;
            this.MarinelliHoleDiameter = g.MarinelliHoleDiameter;
            this.MarinelliHoleHeight = g.MarinelliHoleHeight;
            this.MarinelliSideThickness = g.MarinelliSideThickness;
            this.MarinelliEndWallThickness = g.MarinelliEndWallThickness;
            this.MarinelliHoleSideThickness = g.MarinelliHoleSideThickness;
            this.MarinelliHoleEndWallThickness = g.MarinelliHoleEndWallThickness;
            this.MarinelliSourceHeight = g.MarinelliSourceHeight;
            this.MarinelliToDetectorDistance = g.MarinelliToDetectorDistance;

            this.BoxSourceX = g.BoxSourceX;
            this.BoxSourceY = g.BoxSourceY;
            this.BoxSourceHeight = g.BoxSourceHeight;
            this.BoxToDetectorDistance = g.BoxToDetectorDistance;
            this.BoxSideWallThickness = g.BoxSideWallThickness;
            this.BoxEndWallThickness = g.BoxEndWallThickness;

            this.BeakerWall = Copy(g.BeakerWall);
            this.Source = Copy(g.Source);
        }

        /// <summary>
        /// Наложить шаблон на геометрию: только образец, детектор и всё
        /// остальное остаются как были. Обратный ход <see cref="CaptureFrom"/>
        /// по тому же списку полей — на нём держится и <see cref="CopyFrom"/>.
        /// </summary>
        public void Apply(GeometryModel g)
        {
            g.SourceType = this.SourceType;
            g.Scene = this.Scene;
            g.InShield = this.InShield;

            g.PointDistance = this.PointDistance;
            g.FieldRadius = this.FieldRadius;

            g.BeakerToDetectorDistance = this.BeakerToDetectorDistance;
            g.BeakerDiameter = this.BeakerDiameter;
            g.BeakerHeight = this.BeakerHeight;
            g.BeakerSideWallThickness = this.BeakerSideWallThickness;
            g.BeakerEndWallThickness = this.BeakerEndWallThickness;
            g.SourceHeight = this.SourceHeight;

            g.MarinelliBeakerDiameter = this.MarinelliBeakerDiameter;
            g.MarinelliBeakerHeight = this.MarinelliBeakerHeight;
            g.MarinelliHoleDiameter = this.MarinelliHoleDiameter;
            g.MarinelliHoleHeight = this.MarinelliHoleHeight;
            g.MarinelliSideThickness = this.MarinelliSideThickness;
            g.MarinelliEndWallThickness = this.MarinelliEndWallThickness;
            g.MarinelliHoleSideThickness = this.MarinelliHoleSideThickness;
            g.MarinelliHoleEndWallThickness = this.MarinelliHoleEndWallThickness;
            g.MarinelliSourceHeight = this.MarinelliSourceHeight;
            g.MarinelliToDetectorDistance = this.MarinelliToDetectorDistance;

            g.BoxSourceX = this.BoxSourceX;
            g.BoxSourceY = this.BoxSourceY;
            g.BoxSourceHeight = this.BoxSourceHeight;
            g.BoxToDetectorDistance = this.BoxToDetectorDistance;
            g.BoxSideWallThickness = this.BoxSideWallThickness;
            g.BoxEndWallThickness = this.BoxEndWallThickness;

            g.BeakerWall = Copy(this.BeakerWall);
            g.Source = Copy(this.Source);
        }

        public SampleTemplate Clone()
        {
            SampleTemplate copy = (SampleTemplate)this.MemberwiseClone();
            copy.BeakerWall = Copy(this.BeakerWall);
            copy.Source = Copy(this.Source);
            return copy;
        }

        /// <summary>
        /// Перенять поля другого шаблона (имя тоже) — откат после неудачной
        /// записи. Через геометрию-посредника: третьего списка полей, который
        /// разошёлся бы с первыми двумя при следующей правке, здесь нет.
        /// </summary>
        public void CopyFrom(SampleTemplate other)
        {
            GeometryModel carrier = new GeometryModel();
            other.Apply(carrier);
            this.CaptureFrom(carrier);
            this.Name = other.Name;
        }

        /// <summary>
        /// Часть образца одной строкой, инвариантной культурой — для сравнения
        /// в пробах. Имя в строку НЕ входит. Собирается из геометрии-посредника
        /// по <see cref="Apply"/>, так что поле, забытое здесь, не может быть
        /// перенесено там.
        /// </summary>
        public string Fingerprint()
        {
            GeometryModel g = new GeometryModel();
            this.Apply(g);
            return SampleFingerprint(g);
        }

        /// <summary>Та же строка — прямо по геометрии (для сравнения «до/после» в пробах).</summary>
        public static string SampleFingerprint(GeometryModel g)
        {
            StringBuilder sb = new StringBuilder();
            CultureInfo ic = CultureInfo.InvariantCulture;
            sb.Append("type=").Append(g.SourceType);
            sb.Append(";scene=").Append(g.Scene);
            sb.Append(";shield=").Append(g.InShield ? "1" : "0");
            double[] values =
            {
                g.PointDistance, g.FieldRadius,
                g.BeakerToDetectorDistance, g.BeakerDiameter, g.BeakerHeight,
                g.BeakerSideWallThickness, g.BeakerEndWallThickness, g.SourceHeight,
                g.MarinelliBeakerDiameter, g.MarinelliBeakerHeight, g.MarinelliHoleDiameter,
                g.MarinelliHoleHeight, g.MarinelliSideThickness, g.MarinelliEndWallThickness,
                g.MarinelliHoleSideThickness, g.MarinelliHoleEndWallThickness,
                g.MarinelliSourceHeight, g.MarinelliToDetectorDistance,
                g.BoxSourceX, g.BoxSourceY, g.BoxSourceHeight, g.BoxToDetectorDistance,
                g.BoxSideWallThickness, g.BoxEndWallThickness,
            };
            foreach (double v in values)
            {
                sb.Append(';').Append(v.ToString("R", ic));
            }

            Describe(sb, "wall", g.BeakerWall);
            Describe(sb, "source", g.Source);
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

    /// <summary>Файл своих шаблонов образцов целиком.</summary>
    [XmlRoot("SampleTemplates")]
    public sealed class SampleTemplateConfig
    {
        [XmlArray("Templates")]
        [XmlArrayItem("Template")]
        public SampleTemplate[] Templates;
    }

    /// <summary>
    /// Хранилище своих шаблонов образцов: `config\SampleTemplates.xml`, СВОИМ
    /// файлом рядом с `GeometryTemplates.xml` (решение Amber 16.10.2026) и по
    /// образцу <see cref="GeometryTemplateStore"/> во всём: путь через
    /// <see cref="Package"/>, <see cref="LoadError"/> при битом файле,
    /// <see cref="Reload"/>, <see cref="PathOverride"/> для проб, запись через
    /// временный файл, правка и удаление ПО ИМЕНИ.
    ///
    /// Вшитых образцов нет (решение Amber 16.10.2026 «Только свои»): список —
    /// подсказка и свои, и пустой файл — законное состояние. Отказ по имени
    /// поэтому один — занято своим (без учёта регистра).
    /// </summary>
    public static class SampleTemplateStore
    {
        static List<SampleTemplate> items;

        /// <summary>Подмена пути для ПРОБ — см. <see cref="GeometryTemplateStore.PathOverride"/>.</summary>
        public static string PathOverride;

        /// <summary>
        /// Чем кончилась загрузка файла: пусто — в порядке (и когда файла нет
        /// вовсе), непусто — своих шаблонов сейчас НЕТ, и запись отказывается.
        /// </summary>
        public static string LoadError { get; private set; }

        public static string FilePath
        {
            get
            {
                return string.IsNullOrEmpty(PathOverride)
                    ? Package.GetInstance().SampleTemplates
                    : PathOverride;
            }
        }

        public static List<SampleTemplate> Items
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

        static readonly object Gate = new object();

        static void EnsureLoaded()
        {
            if (items != null)
            {
                return;
            }

            lock (Gate)
            {
                if (items == null)
                {
                    LoadLocked();
                }
            }
        }

        static void LoadLocked()
        {
            SampleTemplateConfig config = null;
            try
            {
                string path = FilePath;
                if (File.Exists(path))
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(SampleTemplateConfig));
                    using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                    {
                        config = (SampleTemplateConfig)serializer.Deserialize(stream);
                    }
                }
            }
            catch (Exception e)
            {
                // Пустой список с НАЗВАННОЙ причиной: молча показать список без
                // шаблонов человека значит сказать ему, что они пропали.
                LoadError = e.Message;
                config = null;
            }

            List<SampleTemplate> list = new List<SampleTemplate>();
            if (config != null && config.Templates != null)
            {
                foreach (SampleTemplate t in config.Templates)
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
        public static SampleTemplate Find(string name)
        {
            foreach (SampleTemplate t in Items)
            {
                if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return t;
                }
            }

            return null;
        }

        /// <summary>
        /// Годится ли имя новому шаблону. Разряды отказа — те же, что у
        /// шаблонов детектора; <see cref="GeometryTemplateStore.NameProblem.Builtin"/>
        /// здесь не бывает: вшитых образцов нет.
        /// </summary>
        public static GeometryTemplateStore.NameProblem NameConflict(string name, SampleTemplate except)
        {
            if (name == null || name.Trim().Length == 0)
            {
                return GeometryTemplateStore.NameProblem.Empty;
            }

            SampleTemplate other = Find(name);
            if (other != null && !ReferenceEquals(other, except))
            {
                return GeometryTemplateStore.NameProblem.Taken;
            }

            return GeometryTemplateStore.NameProblem.None;
        }

        /// <summary>Свободное имя по образцу: само имя или «имя 2», «имя 3», …</summary>
        public static string FreeName(string sample)
        {
            string stem = (sample ?? "").Trim();
            if (stem.Length == 0)
            {
                stem = "sample";
            }

            if (NameConflict(stem, null) == GeometryTemplateStore.NameProblem.None)
            {
                return stem;
            }

            for (int i = 2; i < 1000; i++)
            {
                string candidate = stem + " " + i.ToString(CultureInfo.InvariantCulture);
                if (NameConflict(candidate, null) == GeometryTemplateStore.NameProblem.None)
                {
                    return candidate;
                }
            }

            return stem;
        }

        /// <summary>Новый свой шаблон из части образца геометрии — и сразу в файл.</summary>
        public static SampleTemplate Add(string name, GeometryModel g)
        {
            SampleTemplate t = SampleTemplate.FromModel((name ?? "").Trim(), g);
            List<SampleTemplate> next = new List<SampleTemplate>(Items) { t };
            Write(next);
            return t;
        }

        /// <summary>
        /// Переписать свой шаблон частью образца геометрии — и в файл, ПО ИМЕНИ
        /// (довод — у <see cref="GeometryTemplateStore.Replace"/>). Отказ записи
        /// возвращает шаблон в памяти к прежним полям.
        /// </summary>
        public static void Replace(SampleTemplate t, GeometryModel g)
        {
            SampleTemplate before = t.Clone();
            t.CaptureFrom(g);
            try
            {
                List<SampleTemplate> next = new List<SampleTemplate>(Items);
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

        /// <summary>Убрать свой шаблон — и из файла тоже, по имени.</summary>
        public static void Remove(SampleTemplate t)
        {
            List<SampleTemplate> next = new List<SampleTemplate>(Items);
            int i = IndexOf(next, t.Name);
            if (i >= 0)
            {
                next.RemoveAt(i);
            }

            Write(next);
        }

        static int IndexOf(List<SampleTemplate> list, string name)
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
        /// Записать список целиком (зовёт ТОЛЬКО редактор). Через временный
        /// файл и замену; отказ — исключение наружу, список в памяти при этом
        /// не меняется.
        /// </summary>
        static void Write(List<SampleTemplate> next)
        {
            SampleTemplateConfig config = new SampleTemplateConfig
            {
                Templates = next.ToArray(),
            };

            string path = FilePath;
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            XmlSerializer serializer = new XmlSerializer(typeof(SampleTemplateConfig));
            Utils.AtomicFileWriter.Write(path, stream => serializer.Serialize(stream, config));

            lock (Gate)
            {
                items = next;
                LoadError = null;
            }
        }
    }
}
