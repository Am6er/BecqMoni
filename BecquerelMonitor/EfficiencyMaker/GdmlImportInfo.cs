using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// (`AMBER322`, решение Amber 07.11.2026 «Поля + копия GDML», 11.11.2026 «Файлом рядом с конфигом») Из какого
    /// файла GDML импортирована геометрия кривой: имя, sha256 копии (<see cref="GdmlCopyStore"/>), какие части взяты
    /// из файла и какие детали выбраны кристаллом, пробой и началом защиты. Сами числа геометрии лежат в полях
    /// (<see cref="GeometryModel"/>), как у любой кривой: расчёт идёт по полям, копия нужна показу и сверке.
    /// У кривой без импорта блока нет вовсе — её XML побайтно прежний.
    /// </summary>
    public sealed class GdmlImportInfo
    {
        /// <summary>Имя исходного файла без пути (путь — личное, в конфигурацию не идёт).</summary>
        [XmlAttribute]
        public string FileName = "";

        /// <summary>sha256 содержимого файла, строчными шестнадцатеричными: имя копии на складе.</summary>
        [XmlAttribute]
        public string Sha256 = "";

        /// <summary>Образец взят из файла: вкладки «Источник» нет.</summary>
        [XmlAttribute]
        public bool Sample;

        /// <summary>Защита взята из файла: вкладки «Защита» нет.</summary>
        [XmlAttribute]
        public bool Shield;

        /// <summary>Деталь-кристалл, выбранная человеком; пусто — узнанная программой.</summary>
        [XmlAttribute]
        public string CrystalPart = "";

        /// <summary>Деталь-проба, выбранная человеком; пусто — узнанная программой.</summary>
        [XmlAttribute]
        public string SamplePart = "";

        /// <summary>Деталь, с которой начинается защита, выбранная человеком; пусто — найдено лучами.</summary>
        [XmlAttribute]
        public string ShieldPart = "";

        public bool ShouldSerializeCrystalPart()
        {
            return !string.IsNullOrEmpty(this.CrystalPart);
        }

        public bool ShouldSerializeSamplePart()
        {
            return !string.IsNullOrEmpty(this.SamplePart);
        }

        public bool ShouldSerializeShieldPart()
        {
            return !string.IsNullOrEmpty(this.ShieldPart);
        }

        [XmlIgnore]
        public GdmlParts Parts
        {
            get { return GdmlParts.Detector | (this.Sample ? GdmlParts.Sample : 0) | (this.Shield ? GdmlParts.Shield : 0); }
        }

        public GdmlImportInfo Clone()
        {
            return (GdmlImportInfo)this.MemberwiseClone();
        }
    }

    /// <summary>
    /// (`AMBER322`) Склад копий импортированных файлов GDML — каталог <c>gdml</c> рядом с конфигурациями приборов.
    /// Имя копии — sha256 содержимого: две кривые из одного файла делят одну копию, дубликат кривой ничего не теряет,
    /// а правка исходного файла копию не меняет. Файл спектра копию не несёт: у получателя поля работают, картинок
    /// импорта нет.
    /// </summary>
    public static class GdmlCopyStore
    {
        /// <summary>Подмена каталога для проб: настоящий каталог конфигурации пробы не трогают.</summary>
        public static string DirectoryOverride;

        public static string Directory
        {
            get
            {
                return string.IsNullOrEmpty(DirectoryOverride)
                    ? Path.Combine(Package.GetInstance().DeviceDir, "gdml")
                    : DirectoryOverride;
            }
        }

        public static string Sha256Of(byte[] content)
        {
            using (SHA256 sha = SHA256.Create())
            {
                StringBuilder text = new StringBuilder();
                foreach (byte b in sha.ComputeHash(content))
                {
                    text.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                }

                return text.ToString();
            }
        }

        public static string PathOf(string sha256)
        {
            return Path.Combine(Directory, sha256 + ".gdml");
        }

        /// <summary>Положить копию; уже лежит такая же — не переписывается. Возвращает sha256.</summary>
        public static string Put(byte[] content)
        {
            string sha = Sha256Of(content);
            string path = PathOf(sha);
            if (!File.Exists(path))
            {
                System.IO.Directory.CreateDirectory(Directory);
                string temp = path + ".tmp";
                File.WriteAllBytes(temp, content);
                File.Move(temp, path);
            }

            return sha;
        }

        /// <summary>Путь копии, если она лежит на складе и её содержимое то самое; иначе null.</summary>
        public static string Find(string sha256)
        {
            if (string.IsNullOrEmpty(sha256) || sha256.Length != 64)
            {
                return null;
            }

            foreach (char c in sha256)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                {
                    return null;
                }
            }

            string path = PathOf(sha256);
            try
            {
                return File.Exists(path) && string.Equals(Sha256Of(File.ReadAllBytes(path)), sha256, StringComparison.Ordinal) ? path : null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
